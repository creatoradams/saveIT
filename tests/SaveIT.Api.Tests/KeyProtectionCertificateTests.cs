using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using SaveIT.Api.Security;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class KeyProtectionCertificateTests
{
    private const string Password = "synthetic-certificate-only";
    private static byte[] Pfx(bool expired = false, bool future = false, bool privateKey = true, int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        var request = new CertificateRequest("CN=SaveIT synthetic protector", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(future ? now.AddDays(1) : now.AddDays(-2), expired ? now.AddDays(-1) : now.AddDays(2));
        if (privateKey) return certificate.Export(X509ContentType.Pfx, Password);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, Password);
    }

    [Fact]
    public void File_and_base64_sources_load_the_same_private_certificate_without_rewriting_the_file()
    {
        var bytes = Pfx(); var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            using var file = KeyProtectionCertificate.Load(path, null, Password);
            using var memory = KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password);
            Assert.Equal(file.Thumbprint, memory.Thumbprint);
            Assert.True(memory.HasPrivateKey);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            using var privateKey = memory.GetRSAPrivateKey(); using var publicKey = file.GetRSAPublicKey();
            var data = RandomNumberGenerator.GetBytes(32);
            Assert.True(publicKey!.VerifyData(data, privateKey!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        finally { File.Delete(path); CryptographicOperations.ZeroMemory(bytes); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", " ")]
    [InlineData("unused-path", "not-a-certificate")]
    [InlineData(null, "secret-value-must-not-be-echoed")]
    public void Missing_conflicting_and_malformed_sources_fail_without_echoing_configuration(string? path, string? base64)
    {
        var error = Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(path, base64, "secret-password-must-not-be-echoed"));
        Assert.DoesNotContain("secret-value", error.ToString());
        Assert.DoesNotContain("secret-password", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Oversized_memory_and_file_inputs_are_rejected_before_certificate_import()
    {
        var bytes = new byte[KeyProtectionCertificate.MaximumBytes + 1];
        Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password));
        var path = Path.GetTempFileName();
        try { File.WriteAllBytes(path, bytes); Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(path, null, Password)); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    public void Current_validity_and_all_private_key_requirements_fail_closed(bool expired, bool future, bool privateKey, bool retired)
    {
        var bytes = Pfx(expired, future, privateKey);
        try { Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password, allowExpired: retired)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public void Weak_RSA_protector_fails_before_key_ring_encryption()
    {
        var bytes = Pfx(bits:1024);
        try { Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public void Unsupported_ECDSA_protector_fails_before_key_ring_encryption()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=SaveIT synthetic unsupported protector", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var bytes = certificate.Export(X509ContentType.Pfx, Password);
        try { Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public void Wrong_password_is_sanitized_and_expired_retired_private_key_remains_decryptable()
    {
        var bytes = Pfx(expired:true);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(()=>KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), "wrong-secret-password"));
            Assert.DoesNotContain("wrong-secret-password", error.ToString()); Assert.Null(error.InnerException);
            using var retired = KeyProtectionCertificate.Load(null, Convert.ToBase64String(bytes), Password, allowExpired:true);
            Assert.True(retired.HasPrivateKey);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public void Real_persisted_key_ring_decrypts_after_reloading_base64_and_replacing_current_protector()
    {
        var directory = Path.Combine(Path.GetTempPath(), "saveit-protector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Pfx(); var second = Pfx();
        try
        {
            using var original = KeyProtectionCertificate.Load(null, Convert.ToBase64String(first), Password);
            var writer = DataProtectionProvider.Create(new DirectoryInfo(directory), b=>b.SetApplicationName("SaveIT protector synthetic test").ProtectKeysWithCertificate(original));
            var protectedValue = writer.CreateProtector("session-test").Protect("synthetic session test value");
            var files = Directory.GetFiles(directory, "key-*.xml"); Assert.NotEmpty(files);
            Assert.All(files, path=>Assert.Contains("encryptedSecret", File.ReadAllText(path)));
            using var current = KeyProtectionCertificate.Load(null, Convert.ToBase64String(second), Password);
            using var retired = KeyProtectionCertificate.Load(null, Convert.ToBase64String(first), Password, allowExpired:true);
            var reader = DataProtectionProvider.Create(new DirectoryInfo(directory), b=>b.SetApplicationName("SaveIT protector synthetic test").ProtectKeysWithCertificate(current).UnprotectKeysWithAnyCertificate(retired));
            Assert.Equal("synthetic session test value", reader.CreateProtector("session-test").Unprotect(protectedValue));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first); CryptographicOperations.ZeroMemory(second);
            foreach (var path in Directory.GetFiles(directory, "key-*.xml")) File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
