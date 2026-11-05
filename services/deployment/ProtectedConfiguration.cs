using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Npgsql;
using SaveIT.Api.Operations;
using SaveIT.Api.Security;
using SaveIT.Search;

namespace SaveIT.Deployment;

// Offline carrier validation only. Never opens a connection, writes a payload or
// establishes AWS version/IAM, database-role, certificate-chain or tenant authority.
public static class ProtectedConfiguration
{
    public const int MaximumBytes = 64 * 1024;
    public const string Failure = "SaveIT protected configuration preflight failed.";
    public const string Success = "SaveIT protected configuration preflight passed (local validation only).";

    public static async Task ValidateAsync(Stream input, string workload, int retiredCount, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[MaximumBytes + 1];
        try
        {
            var fields = Fields(workload, retiredCount);
            var count = 0;
            while (count < buffer.Length)
            {
                var pendingRead = input.ReadAsync(buffer.AsMemory(count), cancellationToken).AsTask();
                int read;
                try { read = await pendingRead.WaitAsync(cancellationToken); }
                catch
                {
                    // Console/foreign streams may finish a non-cancellable native
                    // read after WaitAsync expires. Clear again after that write.
                    if (!pendingRead.IsCompleted) _ = ClearAfterReadAsync(pendingRead, buffer);
                    throw;
                }
                if (read == 0) break;
                count += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (count is 0 or > MaximumBytes) throw new InvalidOperationException();
            _ = new UTF8Encoding(false, true).GetCharCount(buffer.AsSpan(0, count));
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!fields.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String ||
                    !values.TryAdd(property.Name, property.Value.GetString()!)) throw new InvalidOperationException();
                var value = values[property.Name];
                if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) throw new InvalidOperationException();
            }
            if (values.Count != fields.Count) throw new InvalidOperationException();
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
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static async Task ClearAfterReadAsync(Task<int> read, byte[] buffer)
    {
        try { await read.ConfigureAwait(false); }
        catch { /* Observe late input failures without exposing their details. */ }
        finally { CryptographicOperations.ZeroMemory(buffer); }
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
