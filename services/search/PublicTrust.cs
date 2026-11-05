using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace SaveIT.Search;

// Public release configuration, never a private-key or secret-store loader.
public static partial class PublicTrust
{
    public const string DatabasePath = "/opt/saveit/trust/database-ca.crt";
    public const int MaximumBytes = 128 * 1024;
    private const string Invalid = "Public CA release configuration is invalid.";

    public static string DatabaseConnection(string connection, string? expectedHash, string path = DatabasePath)
    {
        if (expectedHash is null) return connection; // Existing opt-in/local paths.
        Validate(path, expectedHash);
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            if (parsed.SslMode != SslMode.VerifyFull) throw new InvalidOperationException(Invalid);
            parsed.RootCertificate = path;
            return parsed.ConnectionString;
        }
        catch (Exception error) when (error is ArgumentException or FormatException) { throw new InvalidOperationException(Invalid); }
    }

    public static void Validate(string path, string expectedHash)
    {
        var certificates = new X509Certificate2Collection();
        try
        {
            if (!HashPattern().IsMatch(expectedHash)) throw new InvalidOperationException(Invalid);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > MaximumBytes) throw new InvalidOperationException(Invalid);
            var bytes = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = file.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count == 0 || count > MaximumBytes || Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, count))) != expectedHash)
                throw new InvalidOperationException(Invalid);
            var pem = Encoding.ASCII.GetString(bytes, 0, count);
            if (!PemPattern().IsMatch(pem)) throw new InvalidOperationException(Invalid);
            foreach (Match block in CertificateBlockPattern().Matches(pem))
            {
                var compact = block.Groups[1].Value.Replace("\r", "").Replace("\n", "");
                var der = Convert.FromBase64String(compact);
                if (Convert.ToBase64String(der) != compact) throw new InvalidOperationException(Invalid);
                var certificate = X509CertificateLoader.LoadCertificate(der);
                certificates.Add(certificate);
                if (!certificate.RawData.AsSpan().SequenceEqual(der)) throw new InvalidOperationException(Invalid);
            }
            if (certificates.Count is < 1 or > 16) throw new InvalidOperationException(Invalid);
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var certificate in certificates)
            {
                if (!fingerprints.Add(certificate.GetCertHashString(HashAlgorithmName.SHA256)) || certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
                    !certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(x => x.CertificateAuthority) ||
                    certificate.Extensions.OfType<X509KeyUsageExtension>().Any(x => !x.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign)))
                    throw new InvalidOperationException(Invalid);
                using var rsa = certificate.GetRSAPublicKey();
                using var ec = certificate.GetECDsaPublicKey();
                if (!(rsa is not null && rsa.KeySize >= 2048) && !(ec is not null && ec.KeySize >= 256 &&
                    ec.ExportParameters(false).Curve.Oid.Value is "1.2.840.10045.3.1.7" or "1.3.132.0.34" or "1.3.132.0.35")) throw new InvalidOperationException(Invalid);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException or RegexMatchTimeoutException or FormatException)
        {
            throw new InvalidOperationException(Invalid);
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    [GeneratedRegex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
    [GeneratedRegex(@"\A(?:\s*-----BEGIN CERTIFICATE-----\r?\n[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*){1,16}\z", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PemPattern();
    [GeneratedRegex(@"-----BEGIN CERTIFICATE-----\r?\n([A-Za-z0-9+/=\r\n]+)-----END CERTIFICATE-----", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CertificateBlockPattern();
}
