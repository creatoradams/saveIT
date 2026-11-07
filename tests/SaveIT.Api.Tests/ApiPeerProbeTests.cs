using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaveIT.Api.Operations;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ApiPeerProbeTests : IClassFixture<ApiClientAuthenticationTests.Fixture>
{
    private readonly ApiClientAuthenticationTests.Fixture fixture;
    public ApiPeerProbeTests(ApiClientAuthenticationTests.Fixture fixture) => this.fixture = fixture;
    private const string Password = "synthetic-probe-only";
    private Dictionary<string, string> Payload(string name = "valid")
    {
        var certificate = fixture.Clients[name]; var bytes = certificate.Export(X509ContentType.Pfx, Password);
        try { return new() {
            ["CertificateBase64"] = Convert.ToBase64String(bytes), ["CertificatePassword"] = Password,
            ["ClientLeafSha256"] = certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(),
            ["ApiCaSha256"] = fixture.Hash
        }; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Theory]
    [InlineData("http://api:8443")] [InlineData("https://user:secret@api:8443")]
    [InlineData("https://api:8443/path")] [InlineData("https://api:8443?marker")]
    [InlineData("https://api:8443#marker")] [InlineData("https://127.0.0.1:8443")]
    [InlineData("https://api:0")] [InlineData("https://api.")] [InlineData("https://-api")]
    [InlineData("https://api//")] [InlineData("https://api/../")] [InlineData("https://api\\path")]
    public void Unreviewable_or_noncanonical_origins_are_refused(string origin)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ApiPeerProbe.Origin(origin));
        Assert.Equal(ApiPeerProbe.Failure, error.Message); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("https://api:8443")] [InlineData("https://api:8443/")] [InlineData("https://api.pilot.staging.saveit.internal:8443")]
    public void Canonical_public_origin_metadata_is_accepted(string origin) => Assert.Equal("https", ApiPeerProbe.Origin(origin).Scheme);

    [Theory]
    [InlineData("valid", true)] [InlineData("spare", true)] [InlineData("expired", false)] [InlineData("future", false)]
    [InlineData("server", false)] [InlineData("weak", false)] [InlineData("ca", false)] [InlineData("dual-eku", false)]
    [InlineData("no-ku", false)] [InlineData("wrong-ku", false)] [InlineData("no-constraints", false)] [InlineData("no-eku", false)]
    public void Protected_private_import_preserves_exact_pin_and_shared_client_constraints(string name, bool valid)
    {
        var values = Payload(name);
        if (valid)
        {
            using var certificate = ApiPeerProbe.ReadCertificate(values["CertificateBase64"], Password, values["ClientLeafSha256"]);
            Assert.True(certificate.HasPrivateKey);
        }
        else Assert.Equal(ApiPeerProbe.Failure, Assert.Throws<InvalidOperationException>(() => ApiPeerProbe.ReadCertificate(values["CertificateBase64"], Password, values["ClientLeafSha256"])).Message);
        Assert.Throws<InvalidOperationException>(() => ApiPeerProbe.ReadCertificate(values["CertificateBase64"], "invalid-marker", values["ClientLeafSha256"]));
        Assert.Throws<InvalidOperationException>(() => ApiPeerProbe.ReadCertificate(values["CertificateBase64"], Password, new string('0', 64)));
    }

    [Theory]
    [InlineData("{}")] [InlineData("[]")]
    [InlineData("{\"CertificateBase64\":\"marker\",\"CertificateBase64\":\"marker\"}")]
    [InlineData("{\"CertificateBase64\":null}")]
    [InlineData("{\"CertificatePath\":\"marker\"}")]
    public async Task Input_contract_fails_closed_and_never_echoes_payload(string payload)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiPeerProbe.RunAsync("https://api:8443", input, trustPath: "/must-not-open"));
        Assert.Equal(ApiPeerProbe.Failure, error.Message); Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Missing_public_trust_fails_after_owned_private_import_without_network()
    {
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(Payload()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiPeerProbe.RunAsync("https://api:8443", input, trustPath: "/missing-public-ca"));
        Assert.Equal(ApiPeerProbe.Failure, error.Message); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("ok", true)] [InlineData("redirect", false)] [InlineData("status", false)]
    [InlineData("oversized", false)] [InlineData("chunked-overflow", false)] [InlineData("exact-body", true)] [InlineData("encoded", false)] [InlineData("unapproved", false)]
    [InlineData("untrusted", false)] [InlineData("wrong-name", false)] [InlineData("no-client-auth", false)] [InlineData("stalled-body", false)] [InlineData("slow-body", false)]
    public async Task Actual_native_tls_probe_is_mutual_bounded_and_has_no_user_credentials(string mode, bool success)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Peer:Enabled"] = "true", ["Peer:CaSha256"] = fixture.Hash,
            ["Peer:LeafSha256:0"] = fixture.Clients["valid"].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant()
        }).Build().GetSection("Peer");
        using var admission = ApiClientAuthentication.Read(config, fixture.Path)!;
        using var wrongName = fixture.ServerForName("other.synthetic.invalid");
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(https => {
            https.ServerCertificate = mode == "wrong-name" ? wrongName : fixture.Server;
            if (mode != "no-client-auth") admission.Configure(https);
        })));
        await using var app = builder.Build();
        var seen = 0; var redirected = 0;
        app.Run(async context => {
            if (context.Request.Path == "/redirected") { Interlocked.Increment(ref redirected); return; }
            Assert.Equal("/health/ready", context.Request.Path);
            Assert.Equal("GET", context.Request.Method);
            Assert.False(context.Request.Headers.ContainsKey("Cookie")); Assert.False(context.Request.Headers.ContainsKey("Authorization"));
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            Interlocked.Increment(ref seen);
            if (mode == "redirect") { context.Response.Redirect("/redirected"); return; }
            if (mode == "status") { context.Response.StatusCode = 503; return; }
            if (mode == "oversized") { context.Response.ContentLength = 4097; await context.Response.WriteAsync(new string('x',4097)); return; }
            if (mode is "chunked-overflow" or "exact-body")
            {
                await context.Response.StartAsync();
                Assert.Null(context.Response.ContentLength);
                await context.Response.WriteAsync(new string('x', mode == "exact-body" ? 4096 : 4097));
                return;
            }
            if (mode == "encoded") { context.Response.Headers.ContentEncoding = "gzip"; return; }
            if (mode is "stalled-body" or "slow-body")
            {
                try
                {
                    await context.Response.StartAsync();
                    while (true)
                    {
                        if (mode == "slow-body") { await context.Response.WriteAsync("x"); await context.Response.Body.FlushAsync(context.RequestAborted); }
                        await Task.Delay(100, context.RequestAborted);
                    }
                }
                catch (OperationCanceledException) { return; }
            }
            await context.Response.WriteAsync("ready");
        });
        await app.StartAsync();
        var origin = new Uri($"https://localhost:{new Uri(app.Urls.Single()).Port}");
        var roots = new X509Certificate2Collection(mode == "untrusted" ? fixture.Clients["foreign"] : fixture.Root);
        var elapsed = Stopwatch.StartNew();
        try
        {
            var operation = ApiPeerProbe.SendAsync(origin, fixture.Clients[mode == "unapproved" ? "spare" : "valid"], roots);
            if (success) await operation;
            else
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
                Assert.Equal(ApiPeerProbe.Failure, error.Message); Assert.Null(error.InnerException);
            }
            Assert.Equal(0, redirected); Assert.Equal(mode is "unapproved" or "untrusted" or "wrong-name" or "no-client-auth" ? 0 : 1, seen);
            if (mode is "stalled-body" or "slow-body") Assert.InRange(elapsed.Elapsed.TotalSeconds, 4, 9);
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4)); await app.StopAsync(stop.Token); }
    }

    [Theory]
    [InlineData("https://api:8443", "{}")]
    [InlineData("https://user:sensitive-marker@api:8443", "{}")]
    public async Task Actual_cli_has_fixed_failure_and_ignores_database_environment(string origin, string payload)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(ApiPeerProbe).Assembly.Location);
        start.ArgumentList.Add("probe-api-peer"); start.ArgumentList.Add(origin);
        start.Environment["SAVEIT_DEPLOYMENT_CONNECTION"] = "sensitive-database-marker";
        start.Environment["Deployment__Trust__DatabaseCaSha256"] = "sensitive-database-marker";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try
        {
            try { await process.StandardInput.WriteAsync(payload); process.StandardInput.Close(); } catch (IOException) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, process.ExitCode); Assert.Equal("", (await output).Trim());
            Assert.Equal(ApiPeerProbe.Failure, (await errors).Trim());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
    }
}
