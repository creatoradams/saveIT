using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SaveIT.Api.Security;

public static class KeyProtectionCertificate
{
    public const int MaximumBytes = 64 * 1024;
    private const string InvalidConfiguration = "Data Protection requires exactly one bounded protected PFX source with the required private key and validity.";

    // Configuration is privileged server input, never tenant/request data. An
    // injected Base64 value avoids materializing a secret-manager PFX on disk.
    public static X509Certificate2 Load(string? path, string? base64, string? password, bool allowExpired = false)
    {
        var hasPath = !string.IsNullOrWhiteSpace(path);
        var hasBase64 = !string.IsNullOrWhiteSpace(base64);
        if (hasPath == hasBase64 || (path?.Length ?? 0) > 4096 || (base64?.Length ?? 0) > ((MaximumBytes + 2) / 3) * 4)
            throw new InvalidOperationException(InvalidConfiguration);

        byte[]? bytes = null;
        X509Certificate2? certificate = null;
        try
        {
            var count = 0;
            if (hasBase64)
            {
                bytes = Convert.FromBase64String(base64!);
                count = bytes.Length;
            }
            else
            {
                // Inspect/read one open file; cap the read even if it grows.
                using var file = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > MaximumBytes) throw new InvalidOperationException(InvalidConfiguration);
                bytes = new byte[MaximumBytes + 1];
                while (count < bytes.Length)
                {
                    var read = file.Read(bytes, count, bytes.Length - count);
                    if (read == 0) break;
                    count += read;
                }
            }
            if (count == 0 || count > MaximumBytes)
                throw new InvalidOperationException(InvalidConfiguration);
            certificate = X509CertificateLoader.LoadPkcs12(bytes.AsSpan(0, count), password, X509KeyStorageFlags.EphemeralKeySet);
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is null || rsa.KeySize < 2048 || (!allowExpired &&
                (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)))
                throw new InvalidOperationException(InvalidConfiguration);
            var result = certificate;
            certificate = null;
            return result;
        }
        catch (Exception error) when (error is CryptographicException or FormatException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Provider/format exceptions must never echo configuration or secret bytes.
            throw new InvalidOperationException(InvalidConfiguration);
        }
        finally
        {
            certificate?.Dispose();
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
