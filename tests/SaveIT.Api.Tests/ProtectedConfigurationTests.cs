using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ProtectedConfigurationTests
{
    private const string Password = "synthetic-preflight-only";
    private const string Marker = "synthetic-value-must-not-appear";
    private static readonly Lazy<(string Session, string Transport, string Expired, string Reused)> Certificates = new(() =>
    {
        using var sessionKey = RSA.Create(2048);
        using var transportKey = RSA.Create(2048);
        return (Pfx(sessionKey), Pfx(transportKey, server: true), Pfx(sessionKey, expired: true), Pfx(sessionKey, server: true));
    });

    private static string Pfx(RSA key, bool server = false, bool expired = false)
    {
        var request = new CertificateRequest("CN=SaveIT synthetic preflight", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (server)
        {
            var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("api.synthetic.invalid");
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        }
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 2));
        var bytes = certificate.Export(X509ContentType.Pfx, Password);
        try { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string Connection(string workload) => "Host=db.synthetic.invalid;Database=saveit;SSL Mode=VerifyFull;Username=" +
        (workload switch { "api" => "saveit_app", "worker" => "saveit_search_worker", "migration" => "synthetic_deployer", _ => "synthetic_access" }) + ";Password=" + Marker;

    private static Dictionary<string, string> Payload(string workload = "api", int retired = 0)
    {
        if (workload != "api") return new() { [workload switch { "worker" => "ConnectionStrings__SearchWorker", "migration" => "SAVEIT_DEPLOYMENT_CONNECTION", _ => "SAVEIT_ACCESS_CONNECTION" }] = Connection(workload) };
        var values = new Dictionary<string, string>
        {
            ["ConnectionStrings__App"] = Connection(workload), ["Identity__Okta__ClientSecret"] = Marker,
            ["Identity__KeyCertificateBase64"] = Certificates.Value.Session, ["Identity__KeyCertificatePassword"] = Password,
            ["Deployment__ApiTls__CertificateBase64"] = Certificates.Value.Transport, ["Deployment__ApiTls__CertificatePassword"] = Password
        };
        for (var i = 0; i < retired; i++)
        {
            values[$"Identity__PreviousKeyCertificates__{i}__Base64"] = Certificates.Value.Expired;
            values[$"Identity__PreviousKeyCertificates__{i}__Password"] = Password;
        }
        return values;
    }

    private static byte[] Json(Dictionary<string, string> values) => JsonSerializer.SerializeToUtf8Bytes(values);
    private static Task Validate(byte[] bytes, string workload = "api", int retired = 0) => ProtectedConfiguration.ValidateAsync(new MemoryStream(bytes), workload, retired);
    private static async Task Rejected(byte[] bytes, string workload = "api", int retired = 0)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Validate(bytes, workload, retired));
        Assert.Equal(ProtectedConfiguration.Failure, error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(Marker, error.ToString());
    }

    [Theory]
    [InlineData("api", 0)] [InlineData("api", 4)] [InlineData("worker", 0)] [InlineData("migration", 0)] [InlineData("access", 0)]
    public async Task Exact_workload_contracts_accept_real_private_keys_and_expired_retired_decryption_keys(string workload, int retired)
        => await Validate(Json(Payload(workload, retired)), workload, retired);

    [Theory]
    [InlineData("web", 0)] [InlineData("API", 0)] [InlineData("unknown", 0)] [InlineData("api", -1)] [InlineData("api", 5)] [InlineData("worker", 1)]
    public async Task Unsupported_workloads_and_retired_counts_are_rejected(string workload, int retired)
        => await Rejected(Json(Payload()), workload, retired);

    [Fact]
    public async Task Original_utf8_byte_limit_includes_whitespace_escaping_and_multibyte_text()
    {
        var values = Payload("worker");
        values["ConnectionStrings__SearchWorker"] += ";Application Name=é";
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values).Replace("\\u00E9", "é", StringComparison.OrdinalIgnoreCase));
        var boundary = Enumerable.Repeat((byte)' ', ProtectedConfiguration.MaximumBytes).ToArray();
        json.CopyTo(boundary, 0);
        await Validate(boundary, "worker");
        await Rejected([.. boundary, (byte)' '], "worker");
        var aggregate = Payload(); aggregate["Identity__Okta__ClientSecret"] = new string('x', ProtectedConfiguration.MaximumBytes);
        await Rejected(Json(aggregate));
    }

    [Theory]
    [InlineData("")] [InlineData("null")] [InlineData("[]")] [InlineData("{}")]
    [InlineData("{\"SAVEIT_ACCESS_CONNECTION\":null}")]
    [InlineData("{\"SAVEIT_ACCESS_CONNECTION\":42}")]
    [InlineData("{\"SAVEIT_ACCESS_CONNECTION\":{\"nested\":\"value\"}}")]
    [InlineData("{\"SAVEIT_ACCESS_CONNECTION\":\"x\",}")]
    [InlineData("{/*comment*/\"SAVEIT_ACCESS_CONNECTION\":\"x\"}")]
    public async Task Invalid_json_shapes_are_sanitized(string json) => await Rejected(Encoding.UTF8.GetBytes(json), "access");

    [Theory]
    [InlineData("duplicate")] [InlineData("escaped-duplicate")] [InlineData("case")] [InlineData("extra")] [InlineData("missing")]
    [InlineData("path")] [InlineData("gap")] [InlineData("empty")] [InlineData("null-byte")]
    public async Task Exact_fields_reject_duplicates_aliases_paths_gaps_and_empty_values(string variant)
    {
        var values = Payload();
        if (variant is "duplicate" or "escaped-duplicate")
        {
            var json = JsonSerializer.Serialize(values);
            var key = variant == "duplicate" ? "Identity__Okta__ClientSecret" : "Identity__Okta__ClientSecr\\u0065t";
            await Rejected(Encoding.UTF8.GetBytes(json[..^1] + ",\"" + key + "\":\"" + Marker + "\"}"));
            return;
        }
        if (variant == "case") { values["identity__Okta__ClientSecret"] = values["Identity__Okta__ClientSecret"]; values.Remove("Identity__Okta__ClientSecret"); }
        if (variant == "extra") values["Kestrel__Endpoints__Plain__Url"] = "http://localhost:9999";
        if (variant == "missing") values.Remove("Identity__Okta__ClientSecret");
        if (variant == "path") values["Identity__KeyCertificatePath"] = "/must-not-read";
        if (variant == "gap") { values["Identity__PreviousKeyCertificates__1__Base64"] = Certificates.Value.Session; values["Identity__PreviousKeyCertificates__1__Password"] = Password; }
        if (variant == "empty") values["Identity__Okta__ClientSecret"] = " ";
        if (variant == "null-byte") values["Identity__Okta__ClientSecret"] = "private\0value";
        await Rejected(Json(values), retired: variant == "gap" ? 1 : 0);
    }

    [Theory]
    [InlineData("Identity__KeyCertificatePassword", "wrong")]
    [InlineData("Deployment__ApiTls__CertificatePassword", "wrong")]
    [InlineData("Identity__KeyCertificateBase64", "malformed-private-marker")]
    [InlineData("Deployment__ApiTls__CertificateBase64", "malformed-private-marker")]
    public async Task Real_loader_failures_do_not_expose_credentials(string key, string value)
    {
        var values = Payload(); values[key] = value;
        await Rejected(Json(values));
    }

    [Fact]
    public async Task Expired_current_wrong_server_purpose_and_reissued_transport_key_reuse_fail()
    {
        var values = Payload(); values["Identity__KeyCertificateBase64"] = Certificates.Value.Expired;
        await Rejected(Json(values));
        values = Payload(); values["Deployment__ApiTls__CertificateBase64"] = Certificates.Value.Session;
        await Rejected(Json(values));
        values = Payload(); values["Deployment__ApiTls__CertificateBase64"] = Certificates.Value.Reused;
        await Rejected(Json(values));
        values = Payload(retired: 1); values["Identity__PreviousKeyCertificates__0__Base64"] = Certificates.Value.Transport;
        await Rejected(Json(values), retired: 1);
    }

    [Theory]
    [InlineData("SSL Mode=Disable")] [InlineData("GSS Encryption Mode=Require")] [InlineData("Options=-c role=postgres")]
    [InlineData("Root Certificate=/unapproved/path")] [InlineData("Include Error Detail=true")]
    [InlineData("Host=127.0.0.1")] [InlineData("Host=localhost")] [InlineData("Host=db1,db2")]
    [InlineData("Username=postgres")] [InlineData("Password=")] [InlineData("Database=")]
    public async Task Unsafe_or_incomplete_connection_syntax_fails_offline(string setting)
    {
        var values = Payload("worker"); values["ConnectionStrings__SearchWorker"] += ";" + setting;
        await Rejected(Json(values), "worker");
    }

    [Theory]
    [InlineData("api", "saveit_search_worker")] [InlineData("worker", "saveit_app")]
    [InlineData("migration", "saveit_migration_owner")] [InlineData("migration", "saveit_app")]
    [InlineData("access", "saveit_access_operator")] [InlineData("access", "postgres")]
    public async Task Known_wrong_or_nonlogin_roles_cannot_stand_in_for_separate_logins(string workload, string username)
    {
        var values = Payload(workload);
        var field = values.Keys.First(key => key is "ConnectionStrings__App" or "ConnectionStrings__SearchWorker" or "SAVEIT_DEPLOYMENT_CONNECTION" or "SAVEIT_ACCESS_CONNECTION");
        values[field] = new Npgsql.NpgsqlConnectionStringBuilder(values[field]) { Username = username }.ConnectionString;
        await Rejected(Json(values), workload);
    }

    [Fact]
    public async Task Reader_caps_partial_reads_clears_input_buffer_and_honors_cancellation()
    {
        var valid = Json(Payload("worker"));
        using var partial = new ObservedStream(valid, 7);
        await ProtectedConfiguration.ValidateAsync(partial, "worker", 0);
        Assert.All(partial.Buffer.ToArray(), b => Assert.Equal(0, b));
        using var large = new ObservedStream(new byte[ProtectedConfiguration.MaximumBytes + 500], 13);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProtectedConfiguration.ValidateAsync(large, "worker", 0));
        Assert.Equal(ProtectedConfiguration.MaximumBytes + 1, large.BytesRead);
        Assert.All(large.Buffer.ToArray(), b => Assert.Equal(0, b));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        using var stalled = new ObservedStream([], 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProtectedConfiguration.ValidateAsync(stalled, "worker", 0, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)));
        await Rejected([0x7b, 0x22, 0xff, 0x22, 0x3a, 0x22, 0x78, 0x22, 0x7d], "worker");
    }

    private sealed class ObservedStream(byte[] content, int chunk) : MemoryStream(content)
    {
        public Memory<byte> Buffer { get; private set; }
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Buffer.IsEmpty) Buffer = buffer;
            if (chunk == 0) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
            var read = await base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    [Fact]
    public async Task Noncooperative_late_reads_are_cleared_after_caller_timeout()
    {
        using var input = new LateStream();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProtectedConfiguration.ValidateAsync(input, "worker", 0, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(2)));
        input.Release.SetResult();
        await input.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Wait for the asynchronous clearing condition under a fixed deadline.
        using var cleared = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (input.Buffer.Span.IndexOfAnyExcept((byte)0) >= 0) await Task.Delay(5, cleared.Token);
        Assert.All(input.Buffer.ToArray(), b => Assert.Equal(0, b));
    }

    private sealed class LateStream : MemoryStream
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Memory<byte> Buffer { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Buffer = buffer;
            await Release.Task;
            buffer.Span[0] = 42;
            Finished.SetResult();
            return 1;
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_cli_exits_at_input_deadline_without_producer_eof(bool validBytes)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(ProtectedConfiguration).Assembly.Location);
        foreach (var argument in new[] { "check-configuration", "worker", "0" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        try
        {
            if (validBytes) { await process.StandardInput.WriteAsync(JsonSerializer.Serialize(Payload("worker"))); await process.StandardInput.FlushAsync(); }
            // Deliberately do not close stdin until the child has exited.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(17));
            Assert.InRange(elapsed.Elapsed.TotalSeconds, 8, 17);
            Assert.Equal(1, process.ExitCode);
            Assert.Equal("", (await output).Trim());
            Assert.Equal(ProtectedConfiguration.Failure, (await errors).Trim());
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }

    [Theory]
    [InlineData("worker", "0", true)] [InlineData("api", "0", false)] [InlineData("worker", "1", false)] [InlineData("web", "0", false)] [InlineData("worker", "00", false)]
    public async Task Actual_cli_uses_only_redirected_stdin_and_fixed_output(string workload, string count, bool succeeds)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(ProtectedConfiguration).Assembly.Location);
        start.ArgumentList.Add("check-configuration"); start.ArgumentList.Add(workload); start.ArgumentList.Add(count);
        // These must not be read in the offline branch, even when malformed.
        start.Environment["SAVEIT_DEPLOYMENT_CONNECTION"] = Marker;
        start.Environment["Deployment__Trust__DatabaseCaSha256"] = Marker;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try
        {
            if (count == "0" && workload is "api" or "worker") await process.StandardInput.WriteAsync(JsonSerializer.Serialize(Payload("worker")));
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(succeeds ? 0 : 1, process.ExitCode);
            Assert.Equal(succeeds ? ProtectedConfiguration.Success : "", (await output).Trim());
            Assert.Equal(succeeds ? "" : ProtectedConfiguration.Failure, (await errors).Trim());
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
    }
}
