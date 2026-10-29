using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class OktaTests(DatabaseFixture fixture)
{
    private const string Issuer = "https://synthetic.okta.example/oauth2/default";
    private sealed record Csrf(string Token);
    private static readonly Guid Member = DevelopmentSeed.Profiles[0].MembershipId;
    private sealed class TokenServer(string failure, RSA signingKey) : HttpMessageHandler
    {
        public string Nonce { get; set; } = "";
        public bool VerifiedPkce { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            VerifiedPkce = form["code_verifier"].ToString().Length >= 43 && form["grant_type"] == "authorization_code";
            using var foreign = RSA.Create(2048);
            var key = new RsaSecurityKey(failure == "signature" ? foreign : signingKey) { KeyId = "synthetic-key" };
            var claims = new[] { new Claim("sub", failure == "unmapped" ? "unknown" : "external-alice"),
                new Claim("nonce", failure == "nonce" ? "wrong-nonce" : Nonce), new Claim("acr", failure == "acr" ? "weak" : "required-acr"),
                new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
                new Claim("tenant", DevelopmentSeed.TenantB.ToString()), new Claim("roles", "Administrator") };
            var token = new JwtSecurityToken(failure == "issuer" ? "https://foreign.example" : Issuer,
                failure == "audience" ? "foreign-client" : "synthetic-client", claims, DateTime.UtcNow.AddMinutes(-10),
                failure == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { token_type = "Bearer", access_token = "synthetic-unused", expires_in = 300, id_token = new JwtSecurityTokenHandler().WriteToken(token) }), System.Text.Encoding.UTF8, "application/json") };
        }
    }
    private static Dictionary<string, string?> Config() => new() {
        ["Identity:Okta:Enabled"] = "true", ["Identity:Okta:Issuer"] = Issuer,
        ["Identity:Okta:ClientId"] = "synthetic-client", ["Identity:Okta:ClientSecret"] = "synthetic-secret-only",
        ["Identity:Okta:TenantId"] = DevelopmentSeed.TenantA.ToString(), ["Identity:Okta:PublicOrigin"] = "https://localhost",
        ["Identity:Okta:RequiredAcr"] = "required-acr"
    };

    [Theory]
    [InlineData("")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("nonce")]
    [InlineData("unmapped")]
    [InlineData("acr")]
    [InlineData("state")]
    public async Task Code_flow_verifies_tokens_nonce_state_pkce_and_persisted_tenant_mapping(string failure)
    {
        using var rsa = RSA.Create(2048); using var server = new TokenServer(failure, rsa);
        using var root = new ApiFactory(fixture.AppConnection, developmentIdentity: false);
        using var factory = root.WithWebHostBuilder(builder => {
            foreach (var pair in Config()) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>(OktaIdentity.Scheme, options => {
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer, AuthorizationEndpoint = Issuer + "/authorize", TokenEndpoint = Issuer + "/token" };
                options.Configuration.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "synthetic-key" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                options.Backchannel = new HttpClient(server);
            }));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        await fixture.Execute($"UPDATE \"Memberships\" SET \"ExternalIssuer\"='{Issuer}',\"ExternalSubject\"='external-alice' WHERE \"Id\"='{Member}'");
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/dev/profiles")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/auth/login", null)).StatusCode);
            var csrf = (await client.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login"); request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            var challenge = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            var query = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
            Assert.Equal("code", query["response_type"]); Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal("https://localhost/api/v1/auth/callback", query["redirect_uri"]); server.Nonce = query["nonce"].ToString();
            var callback = await client.PostAsync("/api/v1/auth/callback", new FormUrlEncodedContent(new Dictionary<string,string> {
                ["code"] = "synthetic-code", ["state"] = failure == "state" ? "forged" : query["state"].ToString()
            }));
            if (failure.Length != 0)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, callback.StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
                Assert.DoesNotContain("external-alice", await callback.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode); Assert.True(server.VerifiedPkce);
                var session = (await client.GetFromJsonAsync<SessionDto>("/api/v1/session"))!; Assert.Equal(DevelopmentSeed.TenantA, session.TenantId);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/organizations/{DevelopmentSeed.OrgB}/assets")).StatusCode);
                var cookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), x => x.StartsWith("SaveIT.Session=", StringComparison.Ordinal));
                Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
                var value = cookie.Split(';')[0]["SaveIT.Session=".Length..];
                var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
                var ticket = options.TicketDataFormat.Unprotect(value)!; Assert.Null(ticket.Properties.GetTokenValue("id_token"));
                await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{Member}'");
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
            }
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"ExternalIssuer\"=NULL,\"ExternalSubject\"=NULL,\"Active\"=true WHERE \"Id\"='{Member}'"); }
    }

    [Fact]
    public void Okta_configuration_rejects_untrusted_origins_incomplete_setup_and_redacts_secrets()
    {
        var config = Config(); var options = OktaConnection.Read(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        Assert.DoesNotContain("synthetic-secret-only", options.ToString());
        foreach (var (key, value) in new[] { ("Issuer", "http://insecure.example"), ("Issuer", "https://idp.example/?bad=1"), ("PublicOrigin", "https://app.example/foreign"), ("TenantId", Guid.Empty.ToString()), ("ClientSecret", "") })
        {
            var invalid = Config(); invalid["Identity:Okta:" + key] = value;
            Assert.Throws<InvalidOperationException>(() => OktaConnection.Read(new ConfigurationBuilder().AddInMemoryCollection(invalid).Build()));
        }
    }
}
