using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SaveIT.Api.Operations;
using SaveIT.Api.Security;
using SaveIT.Search;

namespace SaveIT.Deployment;

// Explicit protected operator input only. No environment merge, DB, cookies,
// user claims, secret manager client, business request or forwarding service.
public static class ApiPeerProbe
{
    public const string Failure = "SaveIT API peer probe failed.";
    public const string Success = "SaveIT API peer probe passed (selected endpoint only).";
    public const string TrustPath = "/opt/saveit/trust/api-ca.crt";
    private static readonly HashSet<string> Fields = new(["CertificateBase64", "CertificatePassword", "ApiCaSha256", "ClientLeafSha256"], StringComparer.Ordinal);

    public static Uri Origin(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 512 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.HostNameType != UriHostNameType.Dns ||
            uri.Port is < 1 or > 65535 || uri.Host.Length > 253 ||
            uri.Host.Any(c => c > 127) || uri.Host.Split('.').Any(label => label.Length is < 1 or > 63 ||
                !char.IsAsciiLetterOrDigit(label[0]) || !char.IsAsciiLetterOrDigit(label[^1]) || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !(string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal) ||
              string.Equals(value, uri.GetLeftPart(UriPartial.Authority) + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException(Failure);
        return uri;
    }

    public static async Task RunAsync(string origin, Stream input, CancellationToken cancellationToken = default, string trustPath = TrustPath)
    {
        try
        {
            var target = Origin(origin);
            using var inputDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            inputDeadline.CancelAfter(TimeSpan.FromSeconds(10));
            var values = await ProtectedJsonInput.ReadAsync(input, Fields, inputDeadline.Token);
            using var certificate = ReadCertificate(values["CertificateBase64"], values["CertificatePassword"], values["ClientLeafSha256"]);
            inputDeadline.Token.ThrowIfCancellationRequested();
            var roots = PublicTrust.Load(trustPath, values["ApiCaSha256"]);
            try { inputDeadline.Token.ThrowIfCancellationRequested(); await SendAsync(target, certificate, roots, cancellationToken); }
            finally { foreach (var root in roots) root.Dispose(); }
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    public static X509Certificate2 ReadCertificate(string encoded, string password, string expectedHash)
    {
        X509Certificate2? certificate = null;
        try
        {
            if (expectedHash.Length != 64 || expectedHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw new InvalidOperationException();
            certificate = KeyProtectionCertificate.Load(null, encoded, password);
            if (!ApiClientAuthentication.EligibleLeaf(certificate) ||
                !CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(expectedHash)))
                throw new InvalidOperationException();
            var result = certificate; certificate = null; return result;
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
        finally { certificate?.Dispose(); }
    }

    // Caller owns certificate/anchors until completion. A fresh handler prevents
    // reuse of an old TLS session, cookie jar or ambient proxy configuration.
    public static async Task SendAsync(Uri origin, X509Certificate2 certificate, X509Certificate2Collection roots, CancellationToken cancellationToken = default)
    {
        try
        {
            _ = Origin(origin.OriginalString);
            if (!ApiClientAuthentication.EligibleLeaf(certificate) || !certificate.HasPrivateKey || roots.Count is < 1 or > 16) throw new InvalidOperationException();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
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
            // Schannel starts anonymously and must select this exact certificate
            // when challenged to retain correct LocalCertificate bookkeeping.
            // Its callback path constructs no managed issuer-matching chain.
            if (OperatingSystem.IsWindows()) tlsOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => certificate;
            else tlsOptions.ClientCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true);
            using var handler = new SocketsHttpHandler {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(3), MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 4,
                // A server-TLS-only200 cannot establish that a client credential
                // was used. Reject before HTTP bytes if no mutual TLS occurred.
                PlaintextStreamFilter = (context, token) => {
                    token.ThrowIfCancellationRequested();
                    if (context.PlaintextStream is not SslStream tls || !tls.IsMutuallyAuthenticated || tls.LocalCertificate is null ||
                        !CryptographicOperations.FixedTimeEquals(localHash, tls.LocalCertificate.GetCertHash(HashAlgorithmName.SHA256)))
                        throw new InvalidOperationException();
                    return ValueTask.FromResult(context.PlaintextStream);
                },
                SslOptions = tlsOptions
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/health/ready")) {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.ConnectionClose = true;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > 4096) throw new InvalidOperationException();
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[4097]; var count = 0;
            while (count < buffer.Length)
            {
                var read = await body.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (read == 0) return;
                count += read;
            }
            throw new InvalidOperationException();
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }
}
