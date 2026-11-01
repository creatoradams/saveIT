using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("ContainerTest")) throw new InvalidOperationException("Synthetic identity fixture requires ContainerTest environment.");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var issuer = builder.Configuration["Fixture:Issuer"] ?? throw new InvalidOperationException("Fixture issuer required.");
var origin = builder.Configuration["Fixture:PublicOrigin"] ?? throw new InvalidOperationException("Fixture app origin required.");
using var rsa = RSA.Create(2048);
var signing = new RsaSecurityKey(rsa) { KeyId = "synthetic-container-key" };
var pub = rsa.ExportParameters(false);
var codes = new ConcurrentDictionary<string, (string Nonce, string Challenge, DateTimeOffset Expires)>();
var exchanged = 0;
var app = builder.Build();
app.MapGet("/.well-known/openid-configuration", () => Results.Ok(new {
    issuer, authorization_endpoint = issuer + "/authorize", token_endpoint = issuer + "/token", jwks_uri = issuer + "/jwks",
    response_types_supported = new[] { "code" }, response_modes_supported = new[] { "query", "form_post" }, subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { "RS256" }, token_endpoint_auth_methods_supported = new[] { "client_secret_post" }, code_challenge_methods_supported = new[] { "S256" }
}));
app.MapGet("/jwks", () => Results.Ok(new { keys = new[] { new { kty = "RSA", use = "sig", kid = signing.KeyId, alg = "RS256", n = Base64UrlEncoder.Encode(pub.Modulus!), e = Base64UrlEncoder.Encode(pub.Exponent!) } } }));
app.MapGet("/authorize", (HttpRequest r) => {
    var q = r.Query;
    if (q["client_id"] != "synthetic-client" || q["redirect_uri"] != origin + "/api/v1/auth/callback" || q["response_type"] != "code"
        || q["code_challenge_method"] != "S256" || q["nonce"].ToString().Length < 16 || q["state"].ToString().Length < 16 || codes.Count >= 100)
        return Results.BadRequest();
    var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    codes[code] = (q["nonce"].ToString(), q["code_challenge"].ToString(), DateTimeOffset.UtcNow.AddMinutes(1));
    return Results.Redirect(origin + "/api/v1/auth/callback?code=" + Uri.EscapeDataString(code) + "&state=" + Uri.EscapeDataString(q["state"].ToString()));
});
app.MapPost("/token", async (HttpRequest r) => {
    var f = await r.ReadFormAsync();
    if (f["client_id"] != "synthetic-client" || f["client_secret"] != "synthetic-container-only" || f["grant_type"] != "authorization_code"
        || f["redirect_uri"] != origin + "/api/v1/auth/callback" || !codes.TryRemove(f["code"].ToString(), out var code) || code.Expires < DateTimeOffset.UtcNow
        || Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(f["code_verifier"].ToString()))) != code.Challenge)
        return Results.BadRequest();
    var token = new JwtSecurityToken(issuer, "synthetic-client", [new Claim("sub", "external-release-user"), new Claim("nonce", code.Nonce),
        new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64), new Claim("tenant", "20000000-0000-0000-0000-000000000001"), new Claim("roles", "Administrator")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(signing, SecurityAlgorithms.RsaSha256));
    Interlocked.Increment(ref exchanged);
    return Results.Ok(new { token_type = "Bearer", access_token = "synthetic-unused", expires_in = 300, id_token = new JwtSecurityTokenHandler().WriteToken(token) });
});
app.MapGet("/status", () => Results.Ok(new { exchanged, pkce = exchanged > 0 }));
await app.RunAsync();
