using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace SaveIT.Deployment;

// Own each bounded response before the SDK can deserialize it. One wire attempt,
// fixed destination, and the shared startup deadline apply to success and errors.
internal sealed class RelayStartupTransport(HttpMessageHandler inner, Uri destination, int limit, CancellationToken startup)
    : DelegatingHandler(inner)
{
    private int attempts;
    private byte[]? retained;

    internal static SocketsHttpHandler Native() => new() {
        UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None, ActivityHeadersPropagator = null,
        ConnectTimeout = TimeSpan.FromSeconds(2), MaxConnectionsPerServer = 1, MaxResponseHeadersLength = 16,
        SslOptions = new SslClientAuthenticationOptions {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, AllowTlsResume = false, AllowRenegotiation = false,
            CertificateChainPolicy = new X509ChainPolicy {
                TrustMode = X509ChainTrustMode.System, VerificationFlags = X509VerificationFlags.NoFlag,
                RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true,
                UrlRetrievalTimeout = TimeSpan.Zero, ApplicationPolicy = { new("1.3.6.1.5.5.7.3.1") }
            }
        }
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The SDK deliberately disables URI path canonicalization for signing;
        // compare the exact serialized destination, never Uri object equality.
        if (Interlocked.Increment(ref attempts) != 1 || request.RequestUri?.AbsoluteUri != destination.AbsoluteUri ||
            request.Method.Method != (destination.Scheme == "http" ? "GET" : "POST"))
            throw new InvalidOperationException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(startup, cancellationToken);
        request.Version = HttpVersion.Version11; request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        request.Headers.ConnectionClose = true;
        HttpResponseMessage? response = null;
        var bytes = new byte[limit + 1];
        try
        {
            response = await base.SendAsync(request, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > limit ||
                response.Content.Headers.ContentEncoding.Count != 0 || response.Headers.Contains("Trailer"))
                throw new InvalidOperationException();
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await body.ReadAsync(bytes.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            if (count == 0 || count > limit || response.TrailingHeaders.Any()) throw new InvalidOperationException();
            _ = new UTF8Encoding(false, true).GetCharCount(bytes.AsSpan(0, count));
            using (var json = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 }))
            {
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in json.RootElement.EnumerateObject())
                    if (!names.Add(property.Name) || (destination.Scheme == "https" && property.Name == "SecretBinary"))
                        throw new InvalidOperationException();
            }
            var content = new ClearingContent(bytes, count);
            content.Headers.ContentType = response.Content.Headers.ContentType;
            response.Content.Dispose(); response.Content = content;
            retained = bytes; bytes = [];
            deadline.Token.ThrowIfCancellationRequested();
            var result = response; response = null; return result;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); response?.Dispose(); }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && retained is not null) CryptographicOperations.ZeroMemory(retained);
    }

    internal sealed class ClearingContent : ByteArrayContent
    {
        private readonly byte[] owned;
        private readonly int length;
        internal ClearingContent(byte[] bytes, int count) : base(bytes, 0, count) { owned = bytes; length = count; }
        internal ReadOnlyMemory<byte> Bytes => owned.AsMemory(0, length);
        protected override void Dispose(bool disposing) { if (disposing) CryptographicOperations.ZeroMemory(owned); base.Dispose(disposing); }
    }
}
