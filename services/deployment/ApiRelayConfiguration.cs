using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SaveIT.Api.Operations;
using SaveIT.Search;

namespace SaveIT.Deployment;

// Isolated startup input; never inherits migration/operator configuration.
public sealed class ApiRelayConfiguration(Uri api, Uri relay, X509Certificate2 server, X509Certificate2 client, X509Certificate2Collection roots) : IDisposable
{
    public const string Failure = "SaveIT API relay failed.";
    public const string Started = "SaveIT API relay started.";
    private static readonly HashSet<string> Fields = new(["CertificateBase64", "CertificatePassword", "ApiCaSha256", "ClientLeafSha256", "ServerCertificateBase64", "ServerCertificatePassword", "ServerLeafSha256"], StringComparer.Ordinal);
    public Uri Api { get; } = api;
    public Uri Relay { get; } = relay;
    public X509Certificate2 Server { get; } = server;
    public X509Certificate2 Client { get; } = client;
    public X509Certificate2Collection Roots { get; } = roots;

    public static async Task<ApiRelayConfiguration> ReadAsync(string apiOrigin, string relayOrigin, Stream input, string trustPath = ApiPeerProbe.TrustPath, CancellationToken cancellationToken = default)
    {
        X509Certificate2? server = null, client = null; X509Certificate2Collection? roots = null;
        try
        {
            var api = ApiPeerProbe.Origin(apiOrigin); var relay = ApiPeerProbe.Origin(relayOrigin);
            if (relay.Port != 8443 || api == relay) throw new InvalidOperationException();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var values = await ProtectedJsonInput.ReadAsync(input, Fields, deadline.Token);
            client = ApiPeerProbe.ReadCertificate(values["CertificateBase64"], values["CertificatePassword"], values["ClientLeafSha256"]);
            server = ApiTls.Load(null, values["ServerCertificateBase64"], values["ServerCertificatePassword"]);
            var expected = values["ServerLeafSha256"];
            if (expected.Length != 64 || expected.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
                !CryptographicOperations.FixedTimeEquals(server.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(expected)) ||
                !server.MatchesHostname(relay.Host, allowWildcards: false, allowCommonName: false)) throw new InvalidOperationException();
            using var serverKey = server.GetRSAPublicKey(); using var clientKey = client.GetRSAPublicKey();
            if (serverKey is null || clientKey is null || CryptographicOperations.FixedTimeEquals(serverKey.ExportSubjectPublicKeyInfo(), clientKey.ExportSubjectPublicKeyInfo())) throw new InvalidOperationException();
            roots = PublicTrust.Load(trustPath, values["ApiCaSha256"]);
            deadline.Token.ThrowIfCancellationRequested();
            var result = new ApiRelayConfiguration(api, relay, server, client, roots); server = null; client = null; roots = null; return result;
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
        finally { server?.Dispose(); client?.Dispose(); if (roots is not null) foreach (var root in roots) root.Dispose(); }
    }

    public void Dispose() { Server.Dispose(); Client.Dispose(); foreach (var root in Roots) root.Dispose(); }
}
