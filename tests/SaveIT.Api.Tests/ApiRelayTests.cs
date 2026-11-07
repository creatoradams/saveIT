using System.Net;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaveIT.Api.Operations;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ApiRelayTests(ApiClientAuthenticationTests.Fixture fixture) : IClassFixture<ApiClientAuthenticationTests.Fixture>
{
    [Theory]
    [InlineData("/api/v1/session", "GET")]
    [InlineData("/api/v1/auth/callback?code=a%2Bb&state=a%2Fb%3D", "GET")]
    [InlineData("/health/ready", "GET")]
    public void Fixed_target_retains_exact_raw_query(string path, string method) => Assert.Equal("https://api:8443" + path, ApiRelay.Target(new Uri("https://api:8443"), path, method).AbsoluteUri);

    [Theory]
    [InlineData("https://foreign/api/v1/session", "GET")] [InlineData("//foreign/api/v1/session", "GET")]
    [InlineData("/api/v10/session", "GET")] [InlineData("/api/v1/../session", "GET")]
    [InlineData("/api/v1/%2e%2e/session", "GET")] [InlineData("/api/v1/%2fsecret", "GET")]
    [InlineData("/api/v1/a\\b", "GET")] [InlineData("/api/v1/session?bad=%", "GET")]
    [InlineData("/api/v1/session#fragment", "GET")] [InlineData("/api/v1/session", "CONNECT")]
    [InlineData("/api/v1/session", "TRACE")] [InlineData("/health/ready?caller=1", "GET")]
    [InlineData("/health/live", "GET")] [InlineData("/health/ready", "POST")]
    public void Ambiguous_or_unsupported_targets_are_refused(string path, string method) => Assert.Throws<InvalidOperationException>(() => ApiRelay.Target(new Uri("https://api:8443"), path, method));

    private Dictionary<string,string> Payload()
    {
        const string password = "synthetic-relay-only";
        string Encoded(X509Certificate2 cert) { var bytes = cert.Export(X509ContentType.Pfx, password); try { return Convert.ToBase64String(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); } }
        return new() {
            ["CertificateBase64"] = Encoded(fixture.Clients["valid"]), ["CertificatePassword"] = password,
            ["ClientLeafSha256"] = fixture.Clients["valid"].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(),
            ["ApiCaSha256"] = fixture.Hash, ["ServerCertificateBase64"] = Encoded(fixture.Server), ["ServerCertificatePassword"] = password,
            ["ServerLeafSha256"] = fixture.Server.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant()
        };
    }

    [Theory]
    [InlineData("valid", true)] [InlineData("extra", false)] [InlineData("hash", false)]
    [InlineData("password", false)] [InlineData("name", false)] [InlineData("origin", false)]
    public async Task Exact_protected_configuration_and_owned_keys(string mode, bool valid)
    {
        var values = Payload();
        if (mode == "extra") values["ServerCertificatePath"] = "private-marker";
        if (mode == "hash") values["ServerLeafSha256"] = new string('0',64);
        if (mode == "password") values["ServerCertificatePassword"] = "wrong-marker";
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(values));
        var operation = ApiRelayConfiguration.ReadAsync("https://api:8443", mode == "name" ? "https://wrong:8443" : mode == "origin" ? "https://localhost:9443" : "https://localhost:8443", input, fixture.Path);
        if (valid) { using var result = await operation; Assert.True(result.Client.HasPrivateKey); Assert.True(result.Server.HasPrivateKey); }
        else { var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operation); Assert.Equal(ApiRelayConfiguration.Failure, error.Message); Assert.Null(error.InnerException); }
    }

    [Fact]
    public async Task Different_certificates_cannot_reuse_the_same_server_client_RSA_key()
    {
        using var rsa = fixture.Clients["valid"].GetRSAPrivateKey()!;
        var request = new CertificateRequest("CN=Synthetic shared key refusal",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") },true));
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); request.CertificateExtensions.Add(names.Build());
        using var leaf = request.Create(fixture.Root,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),RandomNumberGenerator.GetBytes(16));
        using var keyed = leaf.CopyWithPrivateKey(rsa); var bytes = keyed.Export(X509ContentType.Pfx,"synthetic-shared-only");
        try
        {
            var values = Payload(); values["ServerCertificateBase64"] = Convert.ToBase64String(bytes); values["ServerCertificatePassword"] = "synthetic-shared-only";
            values["ServerLeafSha256"] = leaf.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
            using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(values));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiRelayConfiguration.ReadAsync("https://api:8443","https://localhost:8443",input,fixture.Path));
            Assert.Equal(ApiRelayConfiguration.Failure,error.Message); Assert.Null(error.InnerException);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static DefaultHttpContext Context(CancellationToken token = default)
    {
        var context = new DefaultHttpContext(); context.RequestAborted = token;
        context.Features.Set<ITlsConnectionFeature>(new Tls());
        context.Features.Set<IHttpRequestFeature>(new HttpRequestFeature { Method = "GET", Path = "/api/v1/session", RawTarget = "/api/v1/session", Headers = new HeaderDictionary { ["Host"] = "localhost:8443" }, Body = Stream.Null });
        context.Response.Body = new MemoryStream(); return context;
    }
    private sealed class Tls : ITlsConnectionFeature { public X509Certificate2? ClientCertificate { get; set; } public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken token) => Task.FromResult(ClientCertificate); }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Sixteen_slots_have_no_queue_and_cancellation_drains_without_replaying(bool hostShutdown)
    {
        using var admission = Admission(); var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback,0,e => e.UseHttps(h => { h.ServerCertificate = fixture.Server; admission.Configure(h); })));
        await using var upstream = builder.Build(); var seen = 0; var block = true;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        upstream.Run(async c => { if (Interlocked.Increment(ref seen) == 16) all.TrySetResult(); if (block) { try { await Task.Delay(Timeout.Infinite,c.RequestAborted); } catch (OperationCanceledException) { } } else await c.Response.WriteAsync("ok"); });
        await upstream.StartAsync(); using var stopping = new CancellationTokenSource();
        var configuration = new ApiRelayConfiguration(new Uri(upstream.Urls.Single().Replace("127.0.0.1","localhost",StringComparison.Ordinal)),new Uri("https://localhost:8443"),fixture.Server,fixture.Clients["valid"],new X509Certificate2Collection(fixture.Root));
        using var relay = new ApiRelay(configuration,stopping.Token);
        using var callers = new CancellationTokenSource();
        var contexts = Enumerable.Range(0,16).Select(_ => Context(callers.Token)).ToArray();
        var operations = contexts.Select(relay.HandleAsync).ToArray();
        try
        {
            await all.Task.WaitAsync(TimeSpan.FromSeconds(12));
            var overflow = Context(); await relay.HandleAsync(overflow); Assert.Equal(503,overflow.Response.StatusCode); Assert.Equal(16,seen);
            if (hostShutdown) stopping.Cancel(); else callers.Cancel();
            await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(contexts,c => Assert.Equal(502,c.Response.StatusCode)); Assert.Equal(16,seen);
            if (!hostShutdown) { block = false; var positive = Context(); await relay.HandleAsync(positive); Assert.Equal(200,positive.Response.StatusCode); Assert.Equal(17,seen); }
        }
        finally { callers.Cancel(); stopping.Cancel(); await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5)); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4)); await upstream.StopAsync(stop.Token); }
    }

    private ApiClientAuthentication Admission() => ApiClientAuthentication.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Client:Enabled"] = "true", ["Client:CaSha256"] = fixture.Hash,
        ["Client:LeafSha256:0"] = fixture.Clients["valid"].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant()
    }).Build().GetSection("Client"), fixture.Path)!;

    [Theory]
    [InlineData("redirect")] [InlineData("opaque-form")] [InlineData("no-content")]
    [InlineData("health")] [InlineData("foreign-headers")] [InlineData("connection-cookie")]
    [InlineData("exact-request")] [InlineData("oversized-request")] [InlineData("chunked-overflow")]
    [InlineData("large-history")] [InlineData("oversized-response")] [InlineData("chunked-response-overflow")]
    [InlineData("ordinary-tls")] [InlineData("unapproved-client")] [InlineData("wrong-server-name")]
    [InlineData("encoded-response")] [InlineData("upstream-reset")] [InlineData("slow-response")]
    public async Task Native_two_TLS_hops_preserve_current_contract_and_fail_closed(string mode)
    {
        using var admission = Admission(); using var wrongName = fixture.ServerForName("wrong.invalid");
        var upstreamBuilder = WebApplication.CreateSlimBuilder(); upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 2 * ApiRelay.RequestLimit; o.Listen(IPAddress.Loopback, 0, e => e.UseHttps(h => {
            h.ServerCertificate = mode == "wrong-server-name" ? wrongName : fixture.Server;
            if (mode != "ordinary-tls") admission.Configure(h);
        })); });
        await using var upstream = upstreamBuilder.Build(); var seen = 0;
        upstream.Run(async context => {
            Interlocked.Increment(ref seen);
            Assert.Equal("localhost", context.Request.Host.Host);
            foreach (var name in new[] { "X-Tenant-Id", "X-Forwarded-For", "X-Forwarded-Proto", "X-Client-Cert", "X-Forwarded-Client-Cert", "Authorization", "Traceparent" }) Assert.False(context.Request.Headers.ContainsKey(name));
            if (mode == "health") { Assert.False(context.Request.Headers.ContainsKey("Cookie")); Assert.Equal("/health/ready", context.Request.Path); }
            else if (mode == "connection-cookie") Assert.False(context.Request.Headers.ContainsKey("Cookie"));
            else { Assert.Equal("synthetic=one; other=two", context.Request.Headers.Cookie); Assert.Equal("synthetic-csrf", context.Request.Headers["X-CSRF-TOKEN"]); }
            Assert.Equal(mode == "health" ? "" : "?opaque=a%2Bb", context.Request.QueryString.Value ?? "");
            if (mode == "redirect") { context.Response.Cookies.Append("one","a", new CookieOptions { Expires = DateTimeOffset.UtcNow.AddHours(1) }); context.Response.Cookies.Append("two","b"); context.Response.Redirect("https://issuer.invalid/authorize?opaque=a%2Bb"); return; }
            if (mode == "no-content") { context.Response.StatusCode = 204; return; }
            if (mode == "opaque-form" || mode == "exact-request") { using var buffer = new MemoryStream(); await context.Request.Body.CopyToAsync(buffer); Assert.Equal(mode == "exact-request" ? ApiRelay.RequestLimit : 5, buffer.Length); if (mode == "opaque-form") { Assert.Equal(new byte[] { 0,255,13,10,38 }, buffer.ToArray()); Assert.Equal("application/x-www-form-urlencoded",context.Request.ContentType); } }
            if (mode == "oversized-response") { context.Response.ContentLength = ApiRelay.ResponseLimit + 1; await context.Response.StartAsync(); return; }
            if (mode == "chunked-response-overflow") { for (var i = 0; i < 4097; i++) await context.Response.Body.WriteAsync(new byte[8192], context.RequestAborted); return; }
            if (mode == "encoded-response") { context.Response.Headers.ContentEncoding = "gzip"; return; }
            if (mode == "upstream-reset") { context.Abort(); return; }
            if (mode == "slow-response") { try { while (true) { await context.Response.WriteAsync("x",context.RequestAborted); await context.Response.Body.FlushAsync(context.RequestAborted); await Task.Delay(100,context.RequestAborted); } } catch (OperationCanceledException) { } return; }
            if (mode == "large-history") { context.Response.ContentType = "application/json"; for (var i = 0; i < 1920; i++) await context.Response.Body.WriteAsync(new byte[8192], context.RequestAborted); return; }
            await context.Response.WriteAsync("ok");
        });
        await upstream.StartAsync();
        var configuration = new ApiRelayConfiguration(new Uri(upstream.Urls.Single().Replace("127.0.0.1","localhost",StringComparison.Ordinal)), new Uri("https://localhost:8443"), fixture.Server, fixture.Clients[mode == "unapproved-client" ? "spare" : "valid"], new X509Certificate2Collection(fixture.Root));
        using var relay = new ApiRelay(configuration);
        var relayBuilder = WebApplication.CreateSlimBuilder(); relayBuilder.Logging.ClearProviders();
        relayBuilder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = null; o.Listen(IPAddress.Loopback,0,e => e.UseHttps(fixture.Server)); });
        await using var host = relayBuilder.Build(); host.Run(relay.HandleAsync); await host.StartAsync();
        var chain = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true }; chain.CustomTrustStore.Add(fixture.Root);
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false, SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = chain } };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        using var request = new HttpRequestMessage(mode is "opaque-form" or "exact-request" or "oversized-request" or "chunked-overflow" or "upstream-reset" ? HttpMethod.Post : HttpMethod.Get, host.Urls.Single().Replace("127.0.0.1","localhost",StringComparison.Ordinal) + (mode == "health" ? "/health/ready" : "/api/v1/session?opaque=a%2Bb"));
        request.Headers.Host = "localhost:8443"; request.Headers.TryAddWithoutValidation("Cookie", "synthetic=one; other=two"); request.Headers.Add("X-CSRF-TOKEN","synthetic-csrf");
        foreach (var name in new[] { "X-Tenant-Id", "X-Forwarded-For", "X-Forwarded-Proto", "X-Client-Cert", "X-Forwarded-Client-Cert", "Authorization", "Traceparent" }) request.Headers.TryAddWithoutValidation(name,"private-marker");
        if (mode == "connection-cookie") request.Headers.Connection.Add("Cookie");
        if (request.Method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(mode == "opaque-form" ? [0,255,13,10,38] : new byte[ApiRelay.RequestLimit + (mode is "oversized-request" or "chunked-overflow" ? 1 : 0)]);
            request.Content.Headers.ContentType = new("application/x-www-form-urlencoded");
            if (mode == "chunked-overflow") request.Headers.TransferEncodingChunked = true;
        }
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (mode is "chunked-response-overflow" or "slow-response") await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
            else
            {
                using var response = await client.SendAsync(request);
                var status = mode == "redirect" ? 302 : mode == "no-content" ? 204 : mode is "oversized-request" or "chunked-overflow" ? 413 : mode is "ordinary-tls" or "unapproved-client" or "wrong-server-name" or "oversized-response" or "encoded-response" or "upstream-reset" ? 502 : 200;
                Assert.Equal(status, (int)response.StatusCode);
                if (mode == "redirect") { Assert.Equal(2,response.Headers.GetValues("Set-Cookie").Count()); Assert.Equal("https://issuer.invalid/authorize?opaque=a%2Bb",response.Headers.Location!.OriginalString); }
                if (mode == "large-history") Assert.Equal(15 * 1_048_576,(await response.Content.ReadAsByteArrayAsync()).Length);
                Assert.Equal("no-store",response.Headers.CacheControl!.ToString());
            }
            Assert.Equal(mode is "oversized-request" or "chunked-overflow" or "ordinary-tls" or "unapproved-client" or "wrong-server-name" ? 0 : 1,seen);
            if (mode == "slow-response") Assert.InRange(elapsed.Elapsed.TotalSeconds,18,24);
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4)); await host.StopAsync(stop.Token); await upstream.StopAsync(stop.Token); }
    }
}
