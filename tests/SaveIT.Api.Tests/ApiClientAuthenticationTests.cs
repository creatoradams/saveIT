using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaveIT.Api.Operations;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ApiClientAuthenticationTests : IClassFixture<ApiClientAuthenticationTests.Fixture>
{
    private readonly Fixture fixture;
    public ApiClientAuthenticationTests(Fixture fixture) => this.fixture = fixture;
    private ApiClientAuthentication Read(params string[] names)
        => ApiClientAuthentication.Read(Config(names.Select(name => fixture.Clients[name]).ToArray()), fixture.Path)!;
    private IConfigurationSection Config(params X509Certificate2[] certificates)
    {
        var values = new Dictionary<string, string?> {
            ["Client:Enabled"] = "true", ["Client:CaSha256"] = fixture.Hash
        };
        for (var i = 0; i < certificates.Length; i++) values[$"Client:LeafSha256:{i}"] = certificates[i].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Client");
    }

    [Theory]
    [InlineData("valid", true)] [InlineData("spare", true)] [InlineData("expired", false)] [InlineData("future", false)]
    [InlineData("server", false)] [InlineData("ca", false)] [InlineData("weak", false)] [InlineData("no-ku", false)]
    [InlineData("wrong-ku", false)] [InlineData("no-constraints", false)] [InlineData("dual-eku", false)] [InlineData("no-eku", false)]
    public void Exact_pins_do_not_override_leaf_validity_strength_constraints_usage_or_purpose(string name, bool accepted)
    {
        using var policy = Read(name);
        Assert.Equal(accepted, policy.AcceptLeaf(fixture.Clients[name]));
        Assert.False(policy.AcceptLeaf(null));
    }

    [Fact]
    public void Native_chain_policy_is_exclusive_offline_and_still_rejects_a_pinned_foreign_issuer()
    {
        using var policy = Read("valid", "foreign", "spare", "server");
        var chainPolicy = policy.CreateChainPolicy();
        Assert.True(chainPolicy.DisableCertificateDownloads);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, chainPolicy.TrustMode);
        Assert.Equal(X509RevocationMode.NoCheck, chainPolicy.RevocationMode);
        Assert.Equal(X509VerificationFlags.NoFlag, chainPolicy.VerificationFlags);
        Assert.Single(chainPolicy.CustomTrustStore.Cast<X509Certificate2>());
        using var trusted = new X509Chain { ChainPolicy = chainPolicy };
        Assert.True(trusted.Build(fixture.Clients["valid"]));
        Assert.True(policy.Validate(fixture.Clients["valid"], trusted, SslPolicyErrors.None));
        using var foreign = new X509Chain { ChainPolicy = policy.CreateChainPolicy() };
        Assert.False(foreign.Build(fixture.Clients["foreign"]));
        Assert.False(policy.Validate(fixture.Clients["foreign"], foreign, SslPolicyErrors.RemoteCertificateChainErrors));
        using var wrongPurpose = new X509Chain { ChainPolicy = policy.CreateChainPolicy() };
        Assert.False(wrongPurpose.Build(fixture.Clients["server"]));
        Assert.Contains(wrongPurpose.ChainStatus, status => (status.Status & X509ChainStatusFlags.NotValidForUsage) != 0);
        Assert.False(policy.Validate(fixture.Clients["server"], wrongPurpose, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(policy.AcceptLeaf(fixture.Clients["unapproved"]));
    }

    [Theory]
    [InlineData("Client:Enabled", "sensitive-invalid-marker")]
    [InlineData("Client:CaSha256", "sensitive-invalid-marker")]
    [InlineData("Client:LeafSha256:0", "ABCDEF")]
    [InlineData("Client:LeafSha256:00", "hash")]
    [InlineData("Client:LeafSha256:2", "hash")]
    [InlineData("Client:LeafSha256:4", "hash")]
    [InlineData("Client:CertificatePath", "/sensitive-marker")]
    [InlineData("Client:Enabled", " true")]
    [InlineData("Client:CaSha256:nested", "unexpected")]
    [InlineData("Client:LeafSha256", "unexpected")]
    public void Invalid_configuration_is_sanitized(string key, string value)
    {
        var section = Config(fixture.Clients["valid"]);
        section[key["Client:".Length..]] = value == "hash" ? new string('a', 64) : value;
        var error = Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(section, fixture.Path));
        Assert.Equal(ApiClientAuthentication.Invalid, error.Message); Assert.Null(error.InnerException);
    }

    [Fact]
    public void Missing_duplicate_noncanonical_or_disabled_pins_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(Config(), fixture.Path));
        Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(Config(fixture.Clients["valid"], fixture.Clients["valid"]), fixture.Path));
        var section = Config(fixture.Clients["valid"]); section["LeafSha256:0"] = new string('A', 64);
        Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(section, fixture.Path));
        section = Config(fixture.Clients["valid"]); section["Enabled"] = "false";
        Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(section, fixture.Path));
        var empty = new ConfigurationBuilder().AddInMemoryCollection().Build().GetSection("Client");
        Assert.Null(ApiClientAuthentication.Read(empty, "/must-not-open"));
        empty["Enabled"] = "false"; Assert.Null(ApiClientAuthentication.Read(empty, "/must-not-open"));
        empty.Value = "sensitive-marker";
        Assert.Throws<InvalidOperationException>(() => ApiClientAuthentication.Read(empty, "/must-not-open"));
    }

    [Fact]
    public void Kestrel_configuration_uses_native_policy_before_callback_and_prevents_resumption()
    {
        using var policy = Read("valid");
        var https = new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions(); policy.Configure(https);
        Assert.Equal(Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate, https.ClientCertificateMode);
        Assert.Equal(TimeSpan.FromSeconds(5), https.HandshakeTimeout);
        var native = new SslServerAuthenticationOptions();
        https.OnAuthenticate!(null!, native);
        Assert.NotNull(native.CertificateChainPolicy);
        Assert.True(native.CertificateChainPolicy.DisableCertificateDownloads);
        Assert.False(native.AllowTlsResume); Assert.False(native.AllowRenegotiation);
    }

    [Theory]
    [InlineData("valid", true)] [InlineData("spare", true)] [InlineData(null, false)]
    [InlineData("unapproved", false)] [InlineData("foreign", false)] [InlineData("expired", false)]
    public async Task Actual_kestrel_requires_possession_trust_and_exact_peer_pin(string? client, bool accepted)
    {
        using var policy = Read("valid", "spare", "foreign", "expired");
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(https => {
            https.ServerCertificate = fixture.Server; policy.Configure(https);
        })));
        await using var app = builder.Build(); ApiTls.RestrictPlaintext(app, policy);
        app.Run(context => context.Response.WriteAsync(context.User.Identity?.IsAuthenticated == true ? "wrong-principal" : "anonymous"));
        await app.StartAsync();
        using var http = Client(client);
        var address = new Uri(app.Urls.Single());
        var url = $"https://localhost:{address.Port}/api/synthetic";
        try
        {
            if (accepted) Assert.Equal("anonymous", await http.GetStringAsync(url));
            else await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));
        }
        finally { await app.StopAsync(); }
    }

    private HttpClient Client(string? name)
        => ClientForCertificate(name is null ? null : fixture.Clients[name]);
    private HttpClient ClientForCertificate(X509Certificate2? certificate)
    {
        var handler = new HttpClientHandler { UseProxy = false, ClientCertificateOptions = ClientCertificateOption.Manual };
        if (certificate is not null) handler.ClientCertificates.Add(certificate);
        handler.ServerCertificateCustomValidationCallback = (_, leaf, _, errors) => {
            if (leaf is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(fixture.Root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            return chain.Build(leaf);
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    [Fact]
    public async Task Expiry_is_enforced_on_the_same_actual_keepalive_connection()
    {
        using var leaf = fixture.ShortLived();
        using var policy = ApiClientAuthentication.Read(Config(leaf), fixture.Path)!;
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(https => { https.ServerCertificate = fixture.Server; policy.Configure(https); })));
        await using var app = builder.Build();
        app.Use((context, next) => { context.Response.Headers["X-Fixture-Connection"] = context.Connection.Id; return next(); });
        ApiTls.RestrictPlaintext(app, policy);
        app.Run(context => context.Response.WriteAsync("anonymous"));
        await app.StartAsync();
        using var http = ClientForCertificate(leaf);
        var url = $"https://localhost:{new Uri(app.Urls.Single()).Port}/api/synthetic";
        try
        {
            using var first = await http.GetAsync(url); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var connection = first.Headers.GetValues("X-Fixture-Connection").Single();
            var remaining = leaf.NotAfter.ToUniversalTime() - DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            using var second = await http.GetAsync(url); Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
            Assert.Equal(connection, second.Headers.GetValues("X-Fixture-Connection").Single());
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public void Public_anchor_snapshot_survives_input_removal_without_reopening()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            File.Copy(fixture.Path, path, true);
            using var policy = ApiClientAuthentication.Read(Config(fixture.Clients["valid"]), path)!;
            File.Delete(path);
            using var chain = new X509Chain { ChainPolicy = policy.CreateChainPolicy() };
            Assert.True(chain.Build(fixture.Clients["valid"]));
            Assert.True(policy.Validate(fixture.Clients["valid"], chain, SslPolicyErrors.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Headers_cannot_supply_a_peer_and_plaintext_is_only_loopback_health()
    {
        using var policy = Read("valid");
        var builder = WebApplication.CreateSlimBuilder(); await using var app = builder.Build();
        ApiTls.RestrictPlaintext(app, policy); app.Run(context => context.Response.WriteAsync("ok"));
        var pipeline = ((IApplicationBuilder)app).Build();
        foreach (var tls in new[] { false, true })
        {
            var context = new DefaultHttpContext(); context.Request.Path = "/api/synthetic";
            context.Request.Method = "GET"; context.Connection.LocalIpAddress = IPAddress.Loopback; context.Connection.LocalPort = tls ? 8443 : 8080;
            if (tls) context.Features.Set<ITlsConnectionFeature>(new Tls());
            context.Request.Headers["X-Client-Cert"] = Convert.ToBase64String(fixture.Clients["valid"].RawData);
            context.Request.Headers["X-Forwarded-Client-Cert"] = "sensitive-marker";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            await pipeline(context); Assert.Equal(tls ? 403 : 404, context.Response.StatusCode);
        }
        var health = new DefaultHttpContext(); health.Request.Path = "/health/ready"; health.Request.Method = "GET";
        health.Connection.LocalIpAddress = IPAddress.Loopback; health.Connection.LocalPort = 8080;
        await pipeline(health); Assert.Equal(200, health.Response.StatusCode);
        var expired = new DefaultHttpContext(); expired.Request.Path = "/api/synthetic"; expired.Request.Method = "GET";
        expired.Features.Set<ITlsConnectionFeature>(new Tls { ClientCertificate = fixture.Clients["expired"] });
        await pipeline(expired); Assert.Equal(403, expired.Response.StatusCode);
    }
    private sealed class Tls : ITlsConnectionFeature { public X509Certificate2? ClientCertificate { get; set; } public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) => Task.FromResult(ClientCertificate); }

    public sealed class Fixture : IDisposable
    {
        public X509Certificate2 Root { get; }
        public X509Certificate2 Server { get; }
        public Dictionary<string, X509Certificate2> Clients { get; } = new();
        public string Path { get; } = System.IO.Path.GetTempFileName();
        public string Hash { get; }
        public Fixture()
        {
            Root = Ca();
            foreach (var name in new[] { "valid", "spare", "unapproved", "expired", "future", "server", "ca", "weak", "no-ku", "wrong-ku", "no-constraints", "dual-eku", "no-eku" }) Clients[name] = Issue(Root, name);
            using var foreign = Ca(); Clients["foreign"] = Issue(foreign, "valid");
            Server = Issue(Root, "server");
            var pem = Encoding.ASCII.GetBytes(Root.ExportCertificatePem()); File.WriteAllBytes(Path, pem);
            Hash = Convert.ToHexStringLower(SHA256.HashData(pem));
        }
        private static X509Certificate2 Ca()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=SaveIT synthetic mTLS CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(3));
        }
        public X509Certificate2 ShortLived() => Issue(Root, "valid", DateTimeOffset.UtcNow.AddSeconds(7));
        public X509Certificate2 ServerForName(string name) => Issue(Root, "server", serverName: name);
        private static X509Certificate2 Issue(X509Certificate2 root, string variant, DateTimeOffset? expiry = null, string serverName = "localhost")
        {
            using var rsa = RSA.Create(variant == "weak" ? 1024 : 2048);
            var request = new CertificateRequest("CN=SaveIT synthetic peer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (variant != "no-constraints") request.CertificateExtensions.Add(new X509BasicConstraintsExtension(variant == "ca", false, 0, true));
            if (variant != "no-ku") request.CertificateExtensions.Add(new X509KeyUsageExtension(variant == "wrong-ku" ? X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature, true));
            if (variant != "no-eku")
            {
                var purpose = new OidCollection { new(variant == "server" ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") };
                if (variant == "dual-eku") purpose.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purpose, true));
            }
            if (variant == "server") { var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(serverName); request.CertificateExtensions.Add(san.Build()); }
            using var issued = request.Create(root, DateTimeOffset.UtcNow.AddDays(variant == "future" ? 1 : -2), expiry ?? DateTimeOffset.UtcNow.AddDays(variant == "expired" ? -1 : 2), RandomNumberGenerator.GetBytes(16));
            using var keyed = issued.CopyWithPrivateKey(rsa);
            var bytes = keyed.Export(X509ContentType.Pfx);
            try { return X509CertificateLoader.LoadPkcs12(bytes, null, OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable : X509KeyStorageFlags.EphemeralKeySet); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        public void Dispose() { Root.Dispose(); Server.Dispose(); foreach (var certificate in Clients.Values) certificate.Dispose(); File.Delete(Path); }
    }
}
