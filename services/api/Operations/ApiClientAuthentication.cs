using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SaveIT.Search;

namespace SaveIT.Api.Operations;

// TLS peer admission only: never maps a certificate/header into user or tenant claims.
public sealed class ApiClientAuthentication : IDisposable
{
    public const string TrustPath = "/opt/saveit/trust/client-ca.crt";
    public const string Invalid = "API client TLS authentication requires bounded public trust and exact client pins.";
    private readonly X509Certificate2Collection roots;
    private readonly byte[][] pins;
    private ApiClientAuthentication(X509Certificate2Collection roots, byte[][] pins) { this.roots = roots; this.pins = pins; }

    public static ApiClientAuthentication? Read(IConfigurationSection section, string path = TrustPath)
    {
        X509Certificate2Collection? roots = null;
        try
        {
            if (section.Value is not null || section.GetSection("Enabled").GetChildren().Any() ||
                section.GetSection("CaSha256").GetChildren().Any() || section.GetSection("LeafSha256").Value is not null)
                throw new InvalidOperationException();
            var enabled = section["Enabled"];
            if (enabled is not null && !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException();
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                if (section.GetChildren().Any(x => x.Key != "Enabled")) throw new InvalidOperationException();
                return null;
            }
            if (section.GetChildren().Any(x => x.Key is not ("Enabled" or "CaSha256" or "LeafSha256"))) throw new InvalidOperationException();
            var children = section.GetSection("LeafSha256").GetChildren().ToArray();
            if (children.Length is < 1 or > 4 || children.Where((x, i) => x.Key != i.ToString(System.Globalization.CultureInfo.InvariantCulture) || x.GetChildren().Any()).Any())
                throw new InvalidOperationException();
            var hashes = children.Select(x => x.Value ?? "").ToArray();
            if (hashes.Any(x => x.Length != 64 || x.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) || hashes.Distinct(StringComparer.Ordinal).Count() != hashes.Length)
                throw new InvalidOperationException();
            roots = PublicTrust.Load(path, section["CaSha256"] ?? "");
            var policy = new ApiClientAuthentication(roots, hashes.Select(Convert.FromHexString).ToArray());
            roots = null;
            return policy;
        }
        catch (Exception) { throw new InvalidOperationException(Invalid); }
        finally { if (roots is not null) foreach (var root in roots) root.Dispose(); }
    }

    public X509ChainPolicy CreateChainPolicy()
    {
        var policy = new X509ChainPolicy {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.NoFlag,
            DisableCertificateDownloads = true,
            UrlRetrievalTimeout = TimeSpan.Zero
        };
        policy.CustomTrustStore.AddRange(roots);
        policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        return policy;
    }

    public bool AcceptLeaf(X509Certificate2? certificate)
    {
        try
        {
            if (!EligibleLeaf(certificate)) return false;
            var hash = certificate!.GetCertHash(HashAlgorithmName.SHA256);
            return pins.Any(pin => CryptographicOperations.FixedTimeEquals(hash, pin));
        }
        catch (Exception) { return false; }
    }

    // Shared public-leaf policy; possession is established separately by TLS.
    public static bool EligibleLeaf(X509Certificate2? certificate)
    {
        try
        {
            if (certificate is null || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) return false;
            var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().ToArray();
            var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
            var purpose = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
            if (constraints.Length != 1 || constraints[0].CertificateAuthority || usage.Length != 1 ||
                !usage[0].KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature) || usage[0].KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign) ||
                purpose.Length != 1 || purpose[0].EnhancedKeyUsages.Count != 1 || purpose[0].EnhancedKeyUsages[0].Value != "1.3.6.1.5.5.7.3.2") return false;
            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null || rsa.KeySize < 2048) return false;
            return true;
        }
        catch (Exception) { return false; }
    }

    public bool Validate(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors errors)
        => errors == SslPolicyErrors.None && chain is not null && chain.ChainStatus.All(x => x.Status == X509ChainStatusFlags.NoError) && AcceptLeaf(certificate);

    public void Configure(HttpsConnectionAdapterOptions https)
    {
        https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        https.ClientCertificateValidation = Validate;
        https.HandshakeTimeout = TimeSpan.FromSeconds(5);
        // Set the native handshake policy before its first chain build. Merely
        // overriding the callback would leave .NET10's default AIA fetch possible.
        https.OnAuthenticate = (_, options) => {
            options.CertificateChainPolicy = CreateChainPolicy();
            options.AllowTlsResume = false;
            options.AllowRenegotiation = false;
        };
    }

    public void Dispose() { foreach (var root in roots) root.Dispose(); }
}
