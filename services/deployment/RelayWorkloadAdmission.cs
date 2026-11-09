using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SaveIT.Deployment;

// A workload capability only. Never creates an end-user/tenant principal.
public sealed class RelayWorkloadAdmission
{
    public const string Header = "X-SaveIT-Workload";
    public const string TrustPath = "/opt/saveit/trust/workload-jwks.json";
    private readonly string pool, issuer, provider;
    private readonly SecurityKey[] keys;

    public static RelayWorkloadAdmission? FromEnvironment()
    {
        var pool = Environment.GetEnvironmentVariable("SAVEIT_WORKLOAD_POOL_ID");
        var issuer = Environment.GetEnvironmentVariable("SAVEIT_WORKLOAD_ISSUER");
        var provider = Environment.GetEnvironmentVariable("SAVEIT_WORKLOAD_PROVIDER");
        var hash = Environment.GetEnvironmentVariable("SAVEIT_WORKLOAD_JWKS_SHA256");
        if (pool is null && issuer is null && provider is null && hash is null) return null;
        try
        {
            if (hash is null || !Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z")) throw new InvalidOperationException();
            using var file = new FileStream(TrustPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > 65536) throw new InvalidOperationException();
            var bytes = new byte[65537]; var count = 0;
            while (count < bytes.Length) { var read = file.Read(bytes, count, bytes.Length - count); if (read == 0) break; count += read; }
            if (count is 0 or > 65536 || Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, count))) != hash) throw new InvalidOperationException();
            return new(pool!, issuer!, provider!, bytes.AsMemory(0, count));
        }
        catch { throw new InvalidOperationException(ApiRelayConfiguration.Failure); }
    }

    internal RelayWorkloadAdmission(string pool, string issuer, string provider, ReadOnlyMemory<byte> jwks)
    {
        if (!Regex.IsMatch(pool, "\\A[a-z]{2}-[a-z]+-[1-9]:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z") || pool.StartsWith("cn-", StringComparison.Ordinal) ||
            !Regex.IsMatch(provider, "\\Asaveit[.][a-z][a-z0-9]{1,11}[.](staging|production)[.]web\\z") ||
            issuer != "https://cognito-identity.amazonaws.com" && issuer != $"https://cognito-identity.{pool.Split(':')[0]}.amazonaws.com" || jwks.Length is 0 or > 65536) throw new InvalidOperationException();
        this.pool = pool; this.issuer = issuer; this.provider = provider;
        using var document = Parse(jwks);
        var root = document.RootElement;
        Exact(root, "keys");
        var list = root.GetProperty("keys");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 16) throw new InvalidOperationException();
        var ids = new HashSet<string>(StringComparer.Ordinal); var result = new List<SecurityKey>();
        foreach (var value in list.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(x => x.Name is not ("kty" or "kid" or "use" or "alg" or "n" or "e")) ||
                value.GetProperty("kty").GetString() != "RSA" || value.GetProperty("use").GetString() != "sig" ||
                value.TryGetProperty("alg", out var algorithm) && algorithm.GetString() != "RS512") throw new InvalidOperationException();
            var kid = value.GetProperty("kid").GetString()!;
            if (kid.Length is < 1 or > 128 || kid.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') || !ids.Add(kid)) throw new InvalidOperationException();
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Decode(value.GetProperty("n").GetString()!), Exponent = Decode(value.GetProperty("e").GetString()!) });
            if (rsa.KeySize is < 2048 or > 4096) throw new InvalidOperationException();
            result.Add(new JsonWebKey(value.GetRawText()));
        }
        keys = result.ToArray();
    }

    public async Task<bool> AcceptAsync(string token)
    {
        try
        {
            if (token.Length is < 1 or > 8192) return false;
            var parts = token.Split('.'); if (parts.Length != 3) return false;
            using var header = Parse(Decode(parts[0])); using var payload = Parse(Decode(parts[1]));
            var signature = Decode(parts[2]); CryptographicOperations.ZeroMemory(signature);
            Exact(header.RootElement, "alg", "kid", "typ");
            if (header.RootElement.GetProperty("alg").GetString() != "RS512" || header.RootElement.GetProperty("typ").GetString() is not ("JWS" or "JWT") ||
                !keys.Any(k => k.KeyId == header.RootElement.GetProperty("kid").GetString())) return false;
            var claims = payload.RootElement;
            Exact(claims, "iss", "aud", "sub", "amr", "iat", "exp");
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var issued = claims.GetProperty("iat").GetInt64(); var expires = claims.GetProperty("exp").GetInt64();
            if (issued > now || issued < now - 60 || expires <= now || expires <= issued || expires - issued > 60 ||
                claims.GetProperty("iss").GetString() != issuer || claims.GetProperty("aud").GetString() != pool ||
                !Regex.IsMatch(claims.GetProperty("sub").GetString()!, "\\A" + Regex.Escape(pool.Split(':')[0]) + ":[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z")) return false;
            var amr = claims.GetProperty("amr");
            if (amr.ValueKind != JsonValueKind.Array || amr.GetArrayLength() is < 2 or > 4) return false;
            var methods = amr.EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (methods.Distinct(StringComparer.Ordinal).Count() != methods.Length || !methods.Contains("authenticated", StringComparer.Ordinal) || !methods.Contains(provider, StringComparer.Ordinal) ||
                methods.Any(x => x != "authenticated" && x != provider && x != "cognito-identity.amazonaws.com" && x != new Uri(issuer).Host)) return false;
            var result = await new JsonWebTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 }.ValidateTokenAsync(token, new TokenValidationParameters
            {
                RequireSignedTokens = true, RequireExpirationTime = true, RequireAudience = true,
                ValidateIssuerSigningKey = true, IssuerSigningKeys = keys, ValidAlgorithms = [SecurityAlgorithms.RsaSha512],
                ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = pool,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero, ValidTypes = ["JWS", "JWT"], TryAllIssuerSigningKeys = false,
                IncludeTokenOnFailedValidation = false, LogValidationExceptions = false
            });
            return result.IsValid;
        }
        catch { return false; }
    }

    private static byte[] Decode(string value)
    {
        if (value.Length is < 1 or > 8192 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new InvalidOperationException();
        var bytes = Base64UrlEncoder.DecodeBytes(value);
        if (Base64UrlEncoder.Encode(bytes) != value) throw new InvalidOperationException();
        return bytes;
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        try { Duplicates(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != fields.Length || fields.Any(x => !value.TryGetProperty(x, out _))) throw new InvalidOperationException();
    }
    private static void Duplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidOperationException(); Duplicates(property.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Duplicates(item);
    }
}
