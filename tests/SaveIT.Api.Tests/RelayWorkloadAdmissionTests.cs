using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SaveIT.Api.Operations;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class RelayWorkloadAdmissionTests(ApiClientAuthenticationTests.Fixture fixture) : IDisposable, IClassFixture<ApiClientAuthenticationTests.Fixture>
{
    private const string Pool = "us-east-1:10000000-0000-0000-0000-000000000001";
    private const string Provider = "saveit.pilot.staging.web";
    private const string Issuer = "https://cognito-identity.amazonaws.com";
    private readonly RSA key = RSA.Create(2048);
    private byte[] Jwks()
    {
        var parameters = key.ExportParameters(false);
        return JsonSerializer.SerializeToUtf8Bytes(new { keys = new[] { new { kty = "RSA", kid = "fixture", use = "sig", alg = "RS512", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } });
    }
    private RelayWorkloadAdmission Admission() => new(Pool, Issuer, Provider, Jwks());
    private static Dictionary<string, object> Claims()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new() { ["iss"] = Issuer, ["aud"] = Pool, ["sub"] = "us-east-1:20000000-0000-0000-0000-000000000001", ["iat"] = now - 1, ["exp"] = now + 59, ["amr"] = new[] { "authenticated", Provider } };
    }
    private string Sign(string payload, string? header = null)
    {
        var content = Base64UrlEncoder.Encode(header ?? "{\"alg\":\"RS512\",\"kid\":\"fixture\",\"typ\":\"JWS\"}") + "." + Base64UrlEncoder.Encode(payload);
        return content + "." + Base64UrlEncoder.Encode(key.SignData(Encoding.ASCII.GetBytes(content), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task Real_standard_RSA_signature_and_dedicated_workload_claims_pass()
    {
        Assert.True(await Admission().AcceptAsync(Sign(JsonSerializer.Serialize(Claims()))));
    }

    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("forged")] [InlineData("valid")]
    public async Task Bound_native_TLS_intake_refuses_unauthorized_requests_and_strips_token_before_mTLS_API(string mode)
    {
        using var clientAdmission = ApiClientAuthentication.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Client:Enabled"] = "true", ["Client:CaSha256"] = fixture.Hash,
            ["Client:LeafSha256:0"] = fixture.Clients["valid"].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant()
        }).Build().GetSection("Client"), fixture.Path)!;
        var upstreamBuilder = WebApplication.CreateSlimBuilder(); upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback,0,e => e.UseHttps(h => { h.ServerCertificate = fixture.Server; clientAdmission.Configure(h); })));
        await using var upstream = upstreamBuilder.Build(); var seen = 0;
        upstream.Run(async context => {
            Interlocked.Increment(ref seen);
            Assert.False(context.Request.Headers.ContainsKey(RelayWorkloadAdmission.Header));
            Assert.Equal("synthetic=session", context.Request.Headers.Cookie);
            Assert.Equal("synthetic-csrf", context.Request.Headers["X-CSRF-TOKEN"]);
            context.Response.Cookies.Append("synthetic", "updated", new CookieOptions { Secure = true, HttpOnly = true });
            await context.Response.WriteAsync("ok");
        });
        await upstream.StartAsync();
        var configuration = new ApiRelayConfiguration(new Uri(upstream.Urls.Single().Replace("127.0.0.1","localhost",StringComparison.Ordinal)),new Uri("https://localhost:8443"),fixture.Server,fixture.Clients["valid"],new X509Certificate2Collection(fixture.Root));
        using var relay = new ApiRelay(configuration, admission: Admission());
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback,0,e => e.UseHttps(fixture.Server)));
        await using var host = builder.Build(); host.Run(relay.HandleAsync); await host.StartAsync();
        var chain = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true }; chain.CustomTrustStore.Add(fixture.Root);
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false, SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = chain } };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Get,host.Urls.Single().Replace("127.0.0.1","localhost",StringComparison.Ordinal) + "/api/v1/session");
        request.Headers.Host = "localhost:8443"; request.Headers.Add("Cookie", "synthetic=session"); request.Headers.Add("X-CSRF-TOKEN", "synthetic-csrf");
        var token = Sign(JsonSerializer.Serialize(Claims()));
        if (mode == "forged") token = token[..^8] + "AAAAAAAA";
        if (mode != "missing") request.Headers.TryAddWithoutValidation(RelayWorkloadAdmission.Header,mode == "duplicate" ? new[] { token, token } : new[] { token });
        try
        {
            using var response = await client.SendAsync(request);
            Assert.Equal(mode == "valid" ? 200 : 401,(int)response.StatusCode);
            Assert.False(response.Headers.Contains(RelayWorkloadAdmission.Header));
            Assert.Equal(mode == "valid" ? 1 : 0,seen);
            if (mode == "valid") { Assert.Equal("ok",await response.Content.ReadAsStringAsync()); Assert.Contains("synthetic=updated",response.Headers.GetValues("Set-Cookie").Single()); }
        }
        finally { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(4)); await host.StopAsync(stop.Token); await upstream.StopAsync(stop.Token); }
    }

    [Theory]
    [InlineData("iss", "\"https://customer.okta.invalid\"")]
    [InlineData("aud", "\"us-east-1:10000000-0000-0000-0000-000000000002\"")]
    [InlineData("aud", "[\"us-east-1:10000000-0000-0000-0000-000000000001\"]")]
    [InlineData("sub", "\"other\"")]
    [InlineData("amr", "[\"unauthenticated\"]")]
    [InlineData("amr", "[\"authenticated\",\"saveit.other.staging.web\"]")]
    [InlineData("amr", "[\"authenticated\",\"saveit.pilot.staging.web\",\"unauthenticated\"]")]
    [InlineData("amr", "[\"authenticated\",\"saveit.pilot.staging.web\",\"authenticated\"]")]
    [InlineData("amr", "[\"authenticated\"]")]
    public async Task Signed_wrong_pool_provider_issuer_or_public_flow_claims_fail(string name, string json)
    {
        var claims = Claims(); claims[name] = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(await Admission().AcceptAsync(Sign(JsonSerializer.Serialize(claims))));
    }

    [Theory]
    [InlineData(-1, 600)] [InlineData(-1, 60)] [InlineData(-61, 1)] [InlineData(1, 59)] [InlineData(-60, -1)]
    public async Task Lifetime_age_future_and_expiry_limits_fail(int issuedOffset, int expiresOffset)
    {
        var claims = Claims(); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); claims["iat"] = now + issuedOffset; claims["exp"] = now + expiresOffset;
        Assert.False(await Admission().AcceptAsync(Sign(JsonSerializer.Serialize(claims))));
    }

    [Theory]
    [InlineData("iss")] [InlineData("aud")] [InlineData("sub")] [InlineData("iat")] [InlineData("exp")] [InlineData("amr")]
    public async Task Missing_required_claim_fails(string name)
    {
        var claims = Claims(); claims.Remove(name); Assert.False(await Admission().AcceptAsync(Sign(JsonSerializer.Serialize(claims))));
    }

    [Fact]
    public async Task Forgery_duplicate_escaped_alias_unknown_header_and_unknown_claim_fail()
    {
        var json = JsonSerializer.Serialize(Claims()); var admission = Admission();
        Assert.False(await admission.AcceptAsync(Sign(json)[..^8] + "AAAAAAAA"));
        Assert.False(await admission.AcceptAsync(Sign(json[..^1] + ",\"a\\u0075d\":\"" + Pool + "\"}")));
        Assert.False(await admission.AcceptAsync(Sign(json, "{\"alg\":\"RS512\",\"kid\":\"fixture\",\"typ\":\"JWS\",\"jku\":\"https://foreign.invalid\"}")));
        Assert.False(await admission.AcceptAsync(Sign(json, "{\"alg\":\"RS512\",\"kid\":\"unknown\",\"typ\":\"JWS\"}")));
        Assert.False(await admission.AcceptAsync(Sign(json[..^1] + ",\"tenant\":\"synthetic\"}")));
        Assert.False(await admission.AcceptAsync(new string('x', 8193)));
        Assert.False(await admission.AcceptAsync(""));
    }

    [Fact]
    public void Private_duplicate_and_empty_JWKS_fields_fail()
    {
        var json = Encoding.UTF8.GetString(Jwks());
        Assert.Throws<InvalidOperationException>(() => new RelayWorkloadAdmission(Pool, Issuer, Provider, Encoding.UTF8.GetBytes(json.Replace("\"kty\":", "\"d\":\"synthetic-private-marker\",\"kty\":", StringComparison.Ordinal))));
        Assert.Throws<InvalidOperationException>(() => new RelayWorkloadAdmission(Pool, Issuer, Provider, Encoding.UTF8.GetBytes(json.Replace("\"kid\":\"fixture\"", "\"kid\":\"other\",\"kid\":\"fixture\"", StringComparison.Ordinal))));
        Assert.Throws<InvalidOperationException>(() => new RelayWorkloadAdmission(Pool, Issuer, Provider, "{\"keys\":[]}"u8.ToArray()));
    }
    public void Dispose() => key.Dispose();
}
