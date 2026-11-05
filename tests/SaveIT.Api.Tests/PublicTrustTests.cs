using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Npgsql;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class PublicTrustTests
{
    private static string Pem(string variant = "valid", bool ec = false)
    {
        using var rsa = ec ? null : RSA.Create(variant == "weak" ? 1024 : 2048);
        using var ecdsa = ec ? ECDsa.Create(ECCurve.NamedCurves.nistP384) : null;
        var request = rsa is not null
            ? new CertificateRequest("CN=SaveIT synthetic public CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : new CertificateRequest("CN=SaveIT synthetic public EC CA", ecdsa!, HashAlgorithmName.SHA384);
        if (variant != "missing-constraints") request.CertificateExtensions.Add(new X509BasicConstraintsExtension(variant != "leaf", false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(variant == "wrong-usage" ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(variant == "future" ? 1 : -2), DateTimeOffset.UtcNow.AddDays(variant == "expired" ? -1 : 2));
        return certificate.ExportCertificatePem();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_runtimes_validate_actual_public_ca_bytes_and_declared_hash(bool ec)
    {
        var pem = Pem(ec: ec);
        await WithFile(pem, async (path, hash) =>
        {
            PublicTrust.Validate(path, hash);
            Assert.Equal((0, "ok", ""), await NodeAsync(path, hash));
            var connection = new NpgsqlConnectionStringBuilder(PublicTrust.DatabaseConnection("Host=db.example.invalid;SSL Mode=VerifyFull;Root Certificate=/must-not-use-original", hash, path));
            Assert.Equal(path, connection.RootCertificate);
            Assert.Equal(SslMode.VerifyFull, connection.SslMode);
        });
    }

    [Theory]
    [InlineData("leaf")]
    [InlineData("missing-constraints")]
    [InlineData("wrong-usage")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("weak")]
    [InlineData("junk")]
    [InlineData("mixed")]
    [InlineData("private")]
    [InlineData("duplicate")]
    [InlineData("too-many")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("non-ascii")]
    [InlineData("der-trailing")]
    [InlineData("der-private")]
    [InlineData("after-padding")]
    [InlineData("extra-padding")]
    public async Task Both_runtimes_reject_unsafe_or_malformed_public_trust_without_echoing_input(string variant)
    {
        var pem = Pem(variant);
        pem = variant switch {
            "junk" => "sensitive-input-marker",
            "mixed" => pem + "\nsensitive-input-marker",
            "private" => pem + "\n" + PemEncoding.WriteString("PRIVATE KEY", Encoding.ASCII.GetBytes("sensitive-input-marker")),
            "duplicate" => pem + "\n" + pem,
            "too-many" => string.Join('\n', Enumerable.Repeat(pem, 17)),
            "oversized" => new string('x', PublicTrust.MaximumBytes + 1),
            "malformed" => "-----BEGIN CERTIFICATE-----\nc2Vuc2l0aXZlLWlucHV0LW1hcmtlcg==\n-----END CERTIFICATE-----",
            "non-ascii" => pem + "\né",
            _ => pem
        };
        if (variant is "der-trailing" or "der-private" or "after-padding" or "extra-padding") pem = AddEncodedTrailing(pem, variant);
        await WithFile(pem, async (path, hash) =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => PublicTrust.Validate(path, hash));
            Assert.Equal("Public CA release configuration is invalid.", error.Message);
            Assert.Null(error.InnerException);
            var node = await NodeAsync(path, hash);
            Assert.Equal(1, node.Code); Assert.Equal("", node.Output);
            Assert.Equal(error.Message, node.Error);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad-sensitive-hash-marker")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Both_runtimes_refuse_wrong_missing_or_noncanonical_release_hashes(string hash)
    {
        await WithFile(Pem(), async (path, _) => {
            Assert.Throws<InvalidOperationException>(() => PublicTrust.Validate(path, hash));
            var node = await NodeAsync(path, hash);
            Assert.Equal(1, node.Code); Assert.Equal("Public CA release configuration is invalid.", node.Error);
        });
    }

    [Fact]
    public async Task Strict_database_trust_requires_verifyfull_and_unconfigured_legacy_path_is_explicit()
    {
        const string legacy = "Host=127.0.0.1;SSL Mode=Disable";
        Assert.Equal(legacy, PublicTrust.DatabaseConnection(legacy, null));
        await WithFile(Pem(), (path, hash) => {
            Assert.Throws<InvalidOperationException>(() => PublicTrust.DatabaseConnection(legacy, hash, path));
            var invalid = Assert.Throws<InvalidOperationException>(() => PublicTrust.DatabaseConnection("secret-invalid-key=secret-value", hash, path));
            Assert.Equal("Public CA release configuration is invalid.", invalid.Message);
            Assert.Null(invalid.InnerException);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Multiple_distinct_current_roots_are_supported()
    {
        await WithFile(Pem() + "\n" + Pem(ec: true), async (path, hash) => {
            PublicTrust.Validate(path, hash);
            Assert.Equal((0, "ok", ""), await NodeAsync(path, hash));
        });
    }

    [Fact]
    public async Task Missing_public_bundle_fails_with_fixed_diagnostics()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-sensitive-path-" + Guid.NewGuid());
        var hash = new string('a', 64);
        Assert.Throws<InvalidOperationException>(() => PublicTrust.Validate(path, hash));
        var node = await NodeAsync(path, hash);
        Assert.Equal(1, node.Code); Assert.Equal("Public CA release configuration is invalid.", node.Error);
    }

    private static async Task WithFile(string pem, Func<string, string, Task> action)
    {
        var path = Path.GetTempFileName();
        try {
            var bytes = Encoding.UTF8.GetBytes(pem); await File.WriteAllBytesAsync(path, bytes);
            await action(path, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("private")]
    [InlineData("der-private")]
    public async Task Release_preparation_copies_only_validated_public_files_and_refuses_overwrite_or_private_input(string variant)
    {
        var pem = Pem();
        if (variant == "private") pem += "\n" + PemEncoding.WriteString("PRIVATE KEY", Encoding.ASCII.GetBytes("sensitive-input-marker"));
        if (variant == "der-private") pem = AddEncodedTrailing(pem, variant);
        await WithFile(pem, async (path, hash) =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "saveit-public-release-" + Guid.NewGuid());
            try
            {
                var root = RepositoryRoot();
                var result = await RunNodeAsync([Path.Combine(root, "scripts", "prepare-public-trust.mjs"), path, path, directory]);
                if (variant != "valid")
                {
                    Assert.Equal(1, result.Code); Assert.Equal("", result.Output);
                    Assert.Equal("Public CA release preparation failed.", result.Error.Trim());
                    Assert.False(Directory.Exists(directory));
                    return;
                }
                Assert.Equal(0, result.Code); Assert.Equal("", result.Error);
                Assert.Equal(3, Directory.GetFiles(directory).Length);
                foreach (var name in new[] { "api-ca.crt", "database-ca.crt" }) Assert.Equal(await File.ReadAllBytesAsync(path), await File.ReadAllBytesAsync(Path.Combine(directory, name)));
                using var manifest = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")));
                Assert.Equal(hash, manifest.RootElement.GetProperty("apiCaSha256").GetString());
                Assert.Equal(hash, manifest.RootElement.GetProperty("databaseCaSha256").GetString());
                var duplicate = await RunNodeAsync([Path.Combine(root, "scripts", "prepare-public-trust.mjs"), path, path, directory]);
                Assert.Equal(1, duplicate.Code); Assert.Equal("", duplicate.Output);
                Assert.Equal("Public CA release preparation failed.", duplicate.Error.Trim());
            }
            finally
            {
                // Only known synthetic public files from this test's fresh directory.
                if (Directory.Exists(directory))
                {
                    foreach (var name in new[] { "api-ca.crt", "database-ca.crt", "manifest.json" }) File.Delete(Path.Combine(directory, name));
                    Directory.Delete(directory);
                }
            }
        });
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SaveIT.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }

    private static string AddEncodedTrailing(string pem, string variant)
    {
        var compact = pem.Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "").Replace("\r", "").Replace("\n", "");
        if (variant is "after-padding" or "extra-padding")
            return "-----BEGIN CERTIFICATE-----\n" + compact + (variant == "extra-padding" ? "=" : "====c2Vuc2l0aXZl") + "\n-----END CERTIFICATE-----";
        var der = Convert.FromBase64String(compact);
        using var key = RSA.Create(2048);
        var trailing = variant == "der-private" ? key.ExportPkcs8PrivateKey() : Encoding.ASCII.GetBytes("sensitive-trailing-marker");
        try { return PemEncoding.WriteString("CERTIFICATE", der.Concat(trailing).ToArray()); }
        finally { CryptographicOperations.ZeroMemory(trailing); }
    }

    private static async Task<(int Code, string Output, string Error)> NodeAsync(string path, string hash)
    {
        return await RunNodeAsync(["-e",
            "try {require(process.argv[1]).validate(process.argv[2],process.argv[3]);process.stdout.write('ok')}catch(e){process.stderr.write(e.message);process.exitCode=1}",
            Path.Combine(RepositoryRoot(), "apps", "web", "public-trust.cjs"), path, hash]); // Public metadata only.
    }

    private static async Task<(int Code, string Output, string Error)> RunNodeAsync(string[] arguments)
    {
        var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        return (process.ExitCode, await output, await error);
    }
}
