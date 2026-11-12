using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Npgsql;
using SaveIT.Api.Operations;
using SaveIT.Api.Security;
using SaveIT.Search;

namespace SaveIT.Deployment;

// Offline carrier validation only. Never opens a connection, writes a payload or
// establishes AWS version/IAM, database-role, certificate-chain or tenant authority.
public static class ProtectedConfiguration
{
    public const int MaximumBytes = ProtectedJsonInput.MaximumBytes;
    public const string Failure = "SaveIT protected configuration preflight failed.";
    public const string Success = "SaveIT protected configuration preflight passed (local validation only).";

    public static async Task ValidateAsync(Stream input, string workload, int retiredCount, CancellationToken cancellationToken = default)
    {
        using var validated = await ReadValidatedAsync(input, workload, retiredCount, cancellationToken);
    }

    internal sealed record DatabaseTarget(string Host, int Port, string Database);

    // Retain the checked certificate and public key hashes for cohort checks, not
    // the original password, connection string or encoded PFX fields.
    internal sealed class Validated(DatabaseTarget target, X509Certificate2? server, IReadOnlyList<string> protectorKeys) : IDisposable
    {
        internal DatabaseTarget Target { get; } = target;
        internal X509Certificate2? Server { get; } = server;
        internal IReadOnlyList<string> ProtectorKeys { get; } = protectorKeys;
        public void Dispose() => Server?.Dispose();
    }

    internal static async Task<Validated> ReadValidatedAsync(Stream input, string workload, int retiredCount, CancellationToken cancellationToken)
    {
        X509Certificate2? transport = null;
        try
        {
            var values = await ProtectedJsonInput.ReadAsync(input, Fields(workload, retiredCount), cancellationToken);
            var connectionField = workload switch
            {
                "api" => "ConnectionStrings__App", "worker" => "ConnectionStrings__SearchWorker",
                "migration" => "SAVEIT_DEPLOYMENT_CONNECTION", _ => "SAVEIT_ACCESS_CONNECTION"
            };
            var target = ValidateConnection(values[connectionField], workload);
            cancellationToken.ThrowIfCancellationRequested();
            if (workload != "api") return new(target, null, []);

            transport = ApiTls.Load(null, values["Deployment__ApiTls__CertificateBase64"], values["Deployment__ApiTls__CertificatePassword"]);
            var transportKey = PublicKeyHash(transport);
            var protectorKeys = new string[retiredCount + 1];
            for (var i = -1; i < retiredCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = i < 0 ? "Identity__KeyCertificate" : $"Identity__PreviousKeyCertificates__{i}__";
                using var protector = KeyProtectionCertificate.Load(null, values[prefix + "Base64"], values[prefix + "Password"], allowExpired: i >= 0);
                // Reissuing a certificate around the same key must not hide reuse.
                var key = PublicKeyHash(protector);
                if (key == transportKey) throw new InvalidOperationException();
                protectorKeys[i + 1] = key;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var result = new Validated(target, transport, protectorKeys);
            transport = null;
            return result;
        }
        catch (Exception)
        {
            // JSON/provider/import errors can carry credentials or input fragments.
            throw new InvalidOperationException(Failure);
        }
        finally { transport?.Dispose(); }
    }

    internal static string PublicKeyHash(X509Certificate2 certificate)
    {
        // Normalize RSA public parameters across certificate reissuance. Public
        // trust files also permit approved EC anchors.
        using var rsa = certificate.GetRSAPublicKey();
        var spki = rsa?.ExportSubjectPublicKeyInfo() ?? certificate.PublicKey.ExportSubjectPublicKeyInfo();
        return Convert.ToHexStringLower(SHA256.HashData(spki));
    }

    private static HashSet<string> Fields(string workload, int retiredCount)
    {
        if (retiredCount is < 0 or > 4 || (workload != "api" && retiredCount != 0)) throw new InvalidOperationException();
        var fields = workload switch
        {
            "api" => new HashSet<string>(["ConnectionStrings__App", "Identity__Okta__ClientSecret", "Identity__KeyCertificateBase64", "Identity__KeyCertificatePassword", "Deployment__ApiTls__CertificateBase64", "Deployment__ApiTls__CertificatePassword"], StringComparer.Ordinal),
            "worker" => new HashSet<string>(["ConnectionStrings__SearchWorker"], StringComparer.Ordinal),
            "migration" => new HashSet<string>(["SAVEIT_DEPLOYMENT_CONNECTION"], StringComparer.Ordinal),
            "access" => new HashSet<string>(["SAVEIT_ACCESS_CONNECTION"], StringComparer.Ordinal),
            _ => throw new InvalidOperationException()
        };
        for (var i = 0; i < retiredCount; i++)
        {
            fields.Add($"Identity__PreviousKeyCertificates__{i}__Base64");
            fields.Add($"Identity__PreviousKeyCertificates__{i}__Password");
        }
        return fields;
    }

    private static DatabaseTarget ValidateConnection(string value, string workload)
    {
        ConnectionPolicy.Validate(value);
        var connection = new NpgsqlConnectionStringBuilder(ConnectionPolicy.RuntimeConnection(value));
        var host = connection.Host ?? "";
        if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.TrimEnd('.').Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.TrimEnd('.').EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _) ||
            string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password) ||
            connection.IncludeErrorDetail || (connection.RootCertificate is not (null or "") && connection.RootCertificate != PublicTrust.DatabasePath))
            throw new InvalidOperationException();
        if ((workload == "api" && connection.Username != "saveit_app") || (workload == "worker" && connection.Username != "saveit_search_worker") ||
            (workload is "migration" or "access" && connection.Username is "postgres" or "saveit_app" or "saveit_search_worker" or "saveit_migration_owner" or "saveit_access_operator"))
            throw new InvalidOperationException();
        return new(host, connection.Port, connection.Database!);
    }
}
