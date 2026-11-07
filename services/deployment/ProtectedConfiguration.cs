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
        try
        {
            var values = await ProtectedJsonInput.ReadAsync(input, Fields(workload, retiredCount), cancellationToken);
            var connectionField = workload switch
            {
                "api" => "ConnectionStrings__App", "worker" => "ConnectionStrings__SearchWorker",
                "migration" => "SAVEIT_DEPLOYMENT_CONNECTION", _ => "SAVEIT_ACCESS_CONNECTION"
            };
            ValidateConnection(values[connectionField], workload);
            if (workload != "api") return;

            cancellationToken.ThrowIfCancellationRequested();
            using var transport = ApiTls.Load(null, values["Deployment__ApiTls__CertificateBase64"], values["Deployment__ApiTls__CertificatePassword"]);
            using var transportKey = transport.GetRSAPublicKey() ?? throw new InvalidOperationException();
            var transportPublicKey = transportKey.ExportSubjectPublicKeyInfo();
            for (var i = -1; i < retiredCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prefix = i < 0 ? "Identity__KeyCertificate" : $"Identity__PreviousKeyCertificates__{i}__";
                using var protector = KeyProtectionCertificate.Load(null, values[prefix + "Base64"], values[prefix + "Password"], allowExpired: i >= 0);
                using var protectorKey = protector.GetRSAPublicKey() ?? throw new InvalidOperationException();
                // Reissuing a certificate around the same key must not hide reuse.
                if (transportPublicKey.AsSpan().SequenceEqual(protectorKey.ExportSubjectPublicKeyInfo())) throw new InvalidOperationException();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception)
        {
            // JSON/provider/import errors can carry credentials or input fragments.
            throw new InvalidOperationException(Failure);
        }
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

    private static void ValidateConnection(string value, string workload)
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
    }
}
