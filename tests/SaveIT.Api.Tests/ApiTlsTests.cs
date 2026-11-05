using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaveIT.Api.Operations;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ApiTlsTests
{
    private const string Password = "synthetic-transport-only";

    private static byte[] Pfx(bool server = true, bool san = true, bool ca = false, bool expired = false, string? invalidSan = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        if (san)
        {
            var names = new SubjectAlternativeNameBuilder();
            if (invalidSan == "uri") names.AddUri(new Uri("https://localhost"));
            else if (invalidSan is null) names.AddDnsName("localhost");
            request.CertificateExtensions.Add(invalidSan switch {
                "empty" => new X509Extension("2.5.29.17", new byte[] { 0x30, 0x00 }, false),
                "malformed" => new X509Extension("2.5.29.17", new byte[] { 0x30, 0x03, 0x82, 0x02, 0x61 }, false),
                _ => names.Build()
            });
        }
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 2));
        return cert.Export(X509ContentType.Pfx, Password);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void Invalid_server_purpose_names_authority_or_validity_is_rejected(bool server, bool san, bool ca, bool expired)
    {
        var pfx = Pfx(server, san, ca, expired);
        try { AssertSanitized(() => ApiTls.Load(null, Convert.ToBase64String(pfx), Password)); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    [Theory]
    [InlineData("uri")]
    [InlineData("empty")]
    [InlineData("malformed")]
    public void Common_name_cannot_replace_a_parsed_dns_san(string invalidSan)
    {
        var pfx = Pfx(invalidSan: invalidSan);
        try { AssertSanitized(() => ApiTls.Load(null, Convert.ToBase64String(pfx), Password)); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    [Fact]
    public void File_and_memory_sources_preserve_the_actual_server_key()
    {
        var pfx = Pfx(); var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, pfx);
            using var file = ApiTls.Load(path, null, Password);
            using var memory = ApiTls.Load(null, Convert.ToBase64String(pfx), Password);
            Assert.Equal(file.Thumbprint, memory.Thumbprint);
            Assert.True(memory.HasPrivateKey);
            Assert.Equal(pfx, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); CryptographicOperations.ZeroMemory(pfx); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("missing-private-path", "secret-encoded-value")]
    [InlineData(null, "secret-encoded-value")]
    [InlineData("missing-private-path", null)]
    public void Invalid_sources_are_sanitized(string? path, string? encoded) => AssertSanitized(() => ApiTls.Load(path, encoded, Password));

    [Theory]
    [InlineData("Kestrel:Endpoints:Plain:Url", "http://0.0.0.0:9999")]
    [InlineData("Kestrel:EndpointDefaults:Protocols", "Http1")]
    [InlineData("Deployment:TrustedProxies:0", "127.0.0.1")]
    [InlineData("Deployment:TrustedNetworks:0", "127.0.0.0/8")]
    public void Conflicting_listener_and_forwarding_configuration_fails_before_loading_secrets(string key, string value)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["Deployment:ApiTls:Enabled"] = "true";
        builder.Configuration[key] = value;
        AssertSanitized(() => ApiTls.Configure(builder));
    }

    [Fact]
    public void Disabled_mode_cannot_silently_ignore_a_configured_private_source()
    {
        var builder = WebApplication.CreateSlimBuilder();
        Assert.Null(ApiTls.Configure(builder));
        builder.Configuration["Deployment:ApiTls:CertificateBase64"] = "secret-encoded-value";
        AssertSanitized(() => ApiTls.Configure(builder));
    }

    [Theory]
    [InlineData("GET", "/health/ready", 8080, false, "127.0.0.1", 200)]
    [InlineData("GET", "/health/live", 8080, false, "127.0.0.1", 200)]
    [InlineData("POST", "/health/ready", 8080, false, "127.0.0.1", 404)]
    [InlineData("GET", "/api/v1/csrf", 8080, false, "127.0.0.1", 404)]
    [InlineData("GET", "/api/v1/auth/options", 8080, false, "127.0.0.1", 404)]
    [InlineData("GET", "/health/ready", 8081, false, "127.0.0.1", 404)]
    [InlineData("GET", "/health/ready", 8080, false, "10.0.0.1", 404)]
    [InlineData("GET", "/api/v1/csrf", 8443, true, "10.0.0.1", 200)]
    public async Task Plaintext_health_boundary_uses_actual_transport_before_forwarding(string method, string path, int port, bool https, string localAddress, int expected)
    {
        var builder = WebApplication.CreateSlimBuilder();
        await using var app = builder.Build();
        ApiTls.RestrictPlaintext(app);
        app.Run(context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
        var pipeline = ((IApplicationBuilder)app).Build();
        var context = new DefaultHttpContext();
        context.Request.Method = method; context.Request.Path = path; context.Request.Scheme = https ? "https" : "http";
        context.Connection.LocalIpAddress = IPAddress.Parse(localAddress); context.Connection.LocalPort = port;
        if (https) context.Features.Set<ITlsConnectionFeature>(new SyntheticTlsFeature());
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        await pipeline(context);
        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Fact]
    public async Task Real_tls_requires_trusted_chain_and_correct_hostname()
    {
        var pfx = Pfx();
        try
        {
            using var certificate = ApiTls.Load(null, Convert.ToBase64String(pfx), Password);
            // Schannel cannot serve EphemeralKeySet keys. This synthetic Windows
            // test host loads its own fixture with the default temporary key store;
            // Linux tests and packaged canaries serve the actual ephemeral loader.
            using var serverCertificate = OperatingSystem.IsWindows()
                ? X509CertificateLoader.LoadPkcs12(pfx, Password, X509KeyStorageFlags.DefaultKeySet) : null;
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, e => e.UseHttps(serverCertificate ?? certificate)));
            await using var app = builder.Build();
            app.Run(context => context.Response.WriteAsync("synthetic TLS"));
            await app.StartAsync();
            var url = new Uri(app.Urls.Single());
            using var untrusted = new HttpClient(new HttpClientHandler { UseProxy = false });
            await Assert.ThrowsAsync<HttpRequestException>(() => untrusted.GetAsync($"https://localhost:{url.Port}/"));
            // Custom root chain is confined to this synthetic test; hostname errors
            // remain mandatory. Production Node uses ordinary platform validation.
            using var handler = new HttpClientHandler { UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, leaf, _, errors) =>
            {
                if ((errors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0 || leaf is null) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(certificate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(leaf);
            };
            using var trusted = new HttpClient(handler);
            Assert.Equal("synthetic TLS", await trusted.GetStringAsync($"https://localhost:{url.Port}/"));
            await Assert.ThrowsAsync<HttpRequestException>(() => trusted.GetAsync(url));
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    [Fact]
    public async Task Scheme_or_forwarded_metadata_cannot_replace_the_actual_tls_feature()
    {
        var builder = WebApplication.CreateSlimBuilder();
        await using var app = builder.Build();
        ApiTls.RestrictPlaintext(app);
        app.Run(context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "GET"; context.Request.Path = "/api/v1/csrf";
        context.Request.Scheme = "https"; context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Connection.LocalPort = 8080; context.Connection.LocalIpAddress = IPAddress.Loopback;
        await ((IApplicationBuilder)app).Build()(context);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private static void AssertSanitized(Action action)
    {
        var error = Assert.Throws<InvalidOperationException>(action);
        Assert.StartsWith("API TLS requires", error.Message);
        Assert.DoesNotContain(Password, error.ToString());
        Assert.DoesNotContain("secret-encoded-value", error.ToString());
        Assert.DoesNotContain("missing-private-path", error.ToString());
        Assert.Null(error.InnerException);
    }

    private sealed class SyntheticTlsFeature : ITlsConnectionFeature
    {
        public X509Certificate2? ClientCertificate { get; set; }
        public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate);
    }
}
