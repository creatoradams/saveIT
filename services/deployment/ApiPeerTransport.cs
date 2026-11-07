using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SaveIT.Api.Operations;

namespace SaveIT.Deployment;

// Caller owns immutable certificates/roots through completion. No pooled sessions,
// cookies, proxy, redirect, download or caller-selected certificate authority.
public static class ApiPeerTransport
{
    public static SocketsHttpHandler CreateHandler(X509Certificate2 certificate, X509Certificate2Collection roots)
    {
        if (!ApiClientAuthentication.EligibleLeaf(certificate) || !certificate.HasPrivateKey || roots.Count is < 1 or > 16) throw new InvalidOperationException();
        var chain = new X509ChainPolicy {
            TrustMode = X509ChainTrustMode.CustomRootTrust, VerificationFlags = X509VerificationFlags.NoFlag,
            RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true, UrlRetrievalTimeout = TimeSpan.Zero
        };
        chain.CustomTrustStore.AddRange(roots);
        chain.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        var localHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        var tlsOptions = new SslClientAuthenticationOptions {
            CertificateChainPolicy = chain, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            AllowTlsResume = false, AllowRenegotiation = false
        };
        // Schannel's challenged selection must also retain LocalCertificate.
        if (OperatingSystem.IsWindows()) tlsOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => certificate;
        else tlsOptions.ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true);
        return new SocketsHttpHandler {
            UseProxy = false, UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            ActivityHeadersPropagator = null,
            ConnectTimeout = TimeSpan.FromSeconds(3), MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 4,
            PlaintextStreamFilter = (context, token) => {
                token.ThrowIfCancellationRequested();
                if (context.PlaintextStream is not SslStream tls || !tls.IsMutuallyAuthenticated || tls.LocalCertificate is null ||
                    !CryptographicOperations.FixedTimeEquals(localHash, tls.LocalCertificate.GetCertHash(HashAlgorithmName.SHA256))) throw new InvalidOperationException();
                return ValueTask.FromResult(context.PlaintextStream);
            },
            SslOptions = tlsOptions
        };
    }
}
