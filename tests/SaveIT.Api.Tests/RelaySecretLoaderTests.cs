using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Amazon.Runtime;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class RelaySecretLoaderTests(ApiClientAuthenticationTests.Fixture fixture) : IClassFixture<ApiClientAuthenticationTests.Fixture>
{
    private const string Arn = "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/relay/config-Abcd90";
    private const string Version = "10000000-0000-0000-0000-000000000009";
    private const string Relative = "/v2/credentials/10000000-0000-0000-0000-000000000001";
    private const string Role = "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-task";
    private static string? EnvironmentValue(string name) => name == "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI" ? Relative : null;
    private static string Json(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    private static HttpResponseMessage Response(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/x-amz-json-1.1") };
    private static Dictionary<string,object?> Credentials() => new() {
        ["RoleArn"] = Role, ["AccessKeyId"] = "synthetic-access", ["SecretAccessKey"] = "synthetic-private-signing-key",
        ["Token"] = "synthetic-temporary-token", ["Expiration"] = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
    };
    private string Payload()
    {
        const string password = "synthetic-loader-é";
        string Pfx(X509Certificate2 certificate) {
            var bytes = certificate.Export(X509ContentType.Pfx, password);
            try { return Convert.ToBase64String(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        return Json(new Dictionary<string,string> {
            ["CertificateBase64"] = Pfx(fixture.Clients["valid"]), ["CertificatePassword"] = password,
            ["ClientLeafSha256"] = fixture.Clients["valid"].GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(),
            ["ApiCaSha256"] = fixture.Hash, ["ServerCertificateBase64"] = Pfx(fixture.Server), ["ServerCertificatePassword"] = password,
            ["ServerLeafSha256"] = fixture.Server.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant()
        });
    }
    private static Dictionary<string,object?> Secret(string payload) => new() { ["ARN"] = Arn, ["VersionId"] = Version, ["SecretString"] = payload };
    private Task<ApiRelayConfiguration> Load(Handler metadata, Handler secret, Func<string,string?>? environment = null,
        string arn = Arn, string version = Version, CancellationToken token = default) =>
        RelaySecretLoader.ReadAsync("https://api:8443", "https://localhost:8443", arn, version, environment ?? EnvironmentValue, metadata, secret, fixture.Path, token);
    private static async Task Refused(Task<ApiRelayConfiguration> operation)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(ApiRelayConfiguration.Failure, failure.Message); Assert.Null(failure.InnerException);
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        internal int Calls; internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return action(request, token); }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
    private static Handler Replies(string json) => new((_, _) => Task.FromResult(Response(json)));

    [Fact]
    public async Task SDK_wire_contract_uses_the_exact_regional_endpoint()
    {
        var handler = new Handler((request, _) => {
            Assert.Equal("https://secretsmanager.us-east-1.amazonaws.com/", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(Response(Json(Secret("{}"))));
        });
        using var transport = new RelayStartupTransport(handler, new Uri("https://secretsmanager.us-east-1.amazonaws.com/"), 512 * 1024, CancellationToken.None);
        using var http = new HttpClient(transport);
        using var client = new Amazon.SecretsManager.AmazonSecretsManagerClient(new SessionAWSCredentials("synthetic-access", "synthetic-private", "synthetic-token"),
            RelaySecretLoader.ClientConfiguration(RelaySecretLoader.Reference.Parse(Arn, Version), http));
        var response = await client.GetSecretValueAsync(new Amazon.SecretsManager.Model.GetSecretValueRequest { SecretId = Arn, VersionId = Version });
        Assert.Equal(Arn, response.ARN); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Metadata_wire_contract_retains_owned_bytes_until_response_disposal()
    {
        var handler = Replies(Json(Credentials()));
        using var transport = new RelayStartupTransport(handler, new Uri("http://169.254.170.2" + Relative), 32768, CancellationToken.None);
        using var client = new HttpClient(transport, false);
        var response = await client.GetAsync("http://169.254.170.2" + Relative, HttpCompletionOption.ResponseHeadersRead);
        var content = Assert.IsType<RelayStartupTransport.ClearingContent>(response.Content);
        var bytes = content.Bytes;
        using (var json = JsonDocument.Parse(bytes)) {
            Assert.Equal(Role, json.RootElement.GetProperty("RoleArn").GetString());
            Assert.Equal(Role, RelaySecretLoader.Reference.Parse(Arn, Version).RoleArn);
            var expires = json.RootElement.GetProperty("Expiration").GetString();
            Assert.True(DateTimeOffset.TryParseExact(expires, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var expiration));
            Assert.True(expiration > DateTimeOffset.UtcNow.AddMinutes(1));
        }
        response.Dispose(); Assert.All(bytes.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Real_SDK_signs_exact_version_without_stage_then_reuses_existing_certificate_checks()
    {
        var metadata = new Handler((request, _) => {
            Assert.Equal("http://169.254.170.2" + Relative, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Response(Json(Credentials())));
        });
        var secret = new Handler(async (request, token) => {
            Assert.Equal("https://secretsmanager.us-east-1.amazonaws.com/", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(token));
            using (body) {
                Assert.Equal(2, body.RootElement.EnumerateObject().Count());
                Assert.Equal(Arn, body.RootElement.GetProperty("SecretId").GetString());
                Assert.Equal(Version, body.RootElement.GetProperty("VersionId").GetString());
                Assert.False(body.RootElement.TryGetProperty("VersionStage", out _));
            }
            Assert.Contains("/us-east-1/secretsmanager/aws4_request", request.Headers.GetValues("Authorization").Single());
            Assert.Equal("synthetic-temporary-token", request.Headers.GetValues("X-Amz-Security-Token").Single());
            return Response(Json(Secret(Payload())));
        });
        using var configuration = await Load(metadata, secret);
        Assert.True(configuration.Client.HasPrivateKey); Assert.True(configuration.Server.HasPrivateKey);
        Assert.Equal(1, metadata.Calls); Assert.Equal(1, secret.Calls);
        Assert.True(metadata.Disposed); Assert.True(secret.Disposed);
    }

    [Theory]
    [InlineData("AWS_CONTAINER_CREDENTIALS_FULL_URI")] [InlineData("AWS_CONTAINER_AUTHORIZATION_TOKEN")]
    [InlineData("AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE")] [InlineData("AWS_ACCESS_KEY_ID")]
    [InlineData("AWS_SECRET_ACCESS_KEY")] [InlineData("AWS_SESSION_TOKEN")] [InlineData("AWS_PROFILE")]
    [InlineData("AWS_CONFIG_FILE")] [InlineData("AWS_WEB_IDENTITY_TOKEN_FILE")] [InlineData("AWS_ROLE_ARN")]
    [InlineData("AWS_ENDPOINT_URL")] [InlineData("AWS_ENDPOINT_URL_SECRETS_MANAGER")] [InlineData("AWS_CA_BUNDLE")]
    [InlineData("SAVEIT_DEPLOYMENT_CONNECTION")] [InlineData("SAVEIT_ACCESS_CONNECTION")] [InlineData("ConnectionStrings__App")]
    [InlineData("AWS_DEFAULT_PROFILE")] [InlineData("AWS_SHARED_CREDENTIALS_FILE")]
    [InlineData("ConnectionStrings__Migration")] [InlineData("ConnectionStrings__SearchWorker")]
    public async Task Alternate_credential_endpoint_and_operator_sources_fail_before_IO(string poisoned)
    {
        var metadata = Replies(Json(Credentials())); var secret = Replies("{}");
        await Refused(Load(metadata, secret, name => name == poisoned ? "synthetic-private-marker" : EnvironmentValue(name)));
        Assert.Equal(0, metadata.Calls); Assert.Equal(0, secret.Calls);
        Assert.True(metadata.Disposed); Assert.True(secret.Disposed);
    }

    [Theory]
    [InlineData("")] [InlineData("http://foreign/credentials")] [InlineData("/v2/credentials/../marker")]
    [InlineData("/v2/credentials/10000000-0000-0000-0000-000000000001?extra=1")] [InlineData("/v2/credentials/10000000-0000-0000-0000-00000000000A")]
    public async Task Only_canonical_fixed_relative_metadata_path_is_supported(string path)
    {
        var metadata = Replies("{}"); var secret = Replies("{}");
        await Refused(Load(metadata, secret, _ => path)); Assert.Equal(0, metadata.Calls); Assert.Equal(0, secret.Calls);
    }

    [Theory]
    [InlineData("RoleArn", "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution")]
    [InlineData("RoleArn", "arn:aws:iam::111122223333:role/saveit-pilot-staging-migration-task")]
    [InlineData("RoleArn", "arn:aws:iam::999900001111:role/saveit-pilot-staging-relay-task")]
    [InlineData("Token", "")] [InlineData("Token", null)] [InlineData("SecretAccessKey", "private\nmarker")]
    [InlineData("Expiration", "2020-01-01T00:00:00Z")] [InlineData("Expiration", "2099-01-01")]
    public async Task Wrong_role_expired_or_missing_temporary_credentials_never_read_a_secret(string field, string? value)
    {
        var credentials = Credentials(); credentials[field] = value;
        var metadata = Replies(Json(credentials)); var secret = Replies("{}");
        await Refused(Load(metadata, secret)); Assert.Equal(1, metadata.Calls); Assert.Equal(0, secret.Calls);
    }

    [Theory]
    [InlineData("RoleArn")] [InlineData("Expiration")] [InlineData("AccessKeyId")] [InlineData("SecretAccessKey")] [InlineData("Token")]
    public async Task Absent_temporary_credential_fields_never_read_a_secret(string field)
    {
        var credentials = Credentials(); credentials.Remove(field);
        var metadata = Replies(Json(credentials)); var secret = Replies("{}");
        await Refused(Load(metadata, secret)); Assert.Equal(1, metadata.Calls); Assert.Equal(0, secret.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Malformed_original_UTF8_is_rejected_before_deserialization(bool service)
    {
        var invalid = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent([123, 34, 120, 34, 58, 34, 0xc3, 0x28, 34, 125])
        }));
        var metadata = service ? Replies(Json(Credentials())) : invalid;
        var secret = service ? invalid : Replies("{}");
        await Refused(Load(metadata, secret)); Assert.Equal(service ? 1 : 0, secret.Calls);
    }

    [Theory]
    [InlineData("ARN", "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd90")]
    [InlineData("ARN", null)] [InlineData("VersionId", "10000000-0000-0000-0000-000000000008")]
    [InlineData("VersionId", null)] [InlineData("SecretString", null)] [InlineData("SecretString", "")]
    [InlineData("SecretBinary", "")] [InlineData("SecretBinary", null)]
    public async Task Returned_reference_and_string_only_contract_fail_closed(string field, string? value)
    {
        var returned = Secret(Payload()); returned[field] = value;
        var metadata = Replies(Json(Credentials())); var secret = Replies(Json(returned));
        await Refused(Load(metadata, secret)); Assert.Equal(1, secret.Calls); Assert.True(secret.Disposed);
    }

    [Theory]
    [InlineData(65536, true)] [InlineData(65537, false)]
    public async Task Original_UTF8_bytes_are_bounded_independently_from_escaped_SDK_envelope(int bytes, bool valid)
    {
        var payload = Payload(); Assert.Contains("é", payload);
        payload += new string(' ', bytes - Encoding.UTF8.GetByteCount(payload));
        var metadata = Replies(Json(Credentials())); var secret = Replies(Json(Secret(payload)));
        if (valid) { using var configuration = await Load(metadata, secret); Assert.True(configuration.Server.HasPrivateKey); }
        else await Refused(Load(metadata, secret));
    }

    [Theory]
    [InlineData(302)] [InlineData(429)] [InlineData(500)]
    public async Task Failed_service_status_never_retries_or_follows_redirects(int status)
    {
        var metadata = Replies(Json(Credentials()));
        var secret = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) {
            Content = new StringContent("synthetic-private-failure-marker"), Headers = { Location = new Uri("https://foreign.invalid/") }
        }));
        await Refused(Load(metadata, secret)); Assert.Equal(1, secret.Calls);
    }

    [Theory]
    [InlineData("declared")] [InlineData("chunked")] [InlineData("encoded")] [InlineData("duplicate")] [InlineData("deep")]
    public async Task Raw_response_is_bounded_before_the_SDK_parser(string mode)
    {
        var metadata = Replies(Json(Credentials()));
        var secret = new Handler((_, _) => {
            var response = Response(Json(Secret(Payload())));
            if (mode == "declared") response.Content.Headers.ContentLength = 512 * 1024 + 1;
            if (mode == "chunked") response.Content = new StreamContent(new MemoryStream(new byte[512 * 1024 + 1]));
            if (mode == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
            if (mode == "duplicate") { response.Dispose(); response = Response("{\"ARN\":\"private-marker\",\"ARN\":\"other-marker\"}"); }
            if (mode == "deep") { response.Dispose(); response = Response("{\"x\":" + new string('[', 9) + "0" + new string(']', 9) + "}"); }
            return Task.FromResult(response);
        });
        await Refused(Load(metadata, secret)); Assert.Equal(1, secret.Calls);
    }

    [Theory]
    [InlineData("metadata")] [InlineData("secret")]
    public async Task One_caller_deadline_cancels_each_network_leg_and_disposes_owned_clients(string leg)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var metadata = new Handler(async (_, token) => { if (leg == "metadata") await Task.Delay(Timeout.Infinite, token); return Response(Json(Credentials())); });
        var secret = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); });
        var clock = Stopwatch.StartNew();
        await Refused(Load(metadata, secret, token: deadline.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3)); Assert.True(metadata.Disposed); Assert.True(secret.Disposed);
        Assert.Equal(leg == "metadata" ? 0 : 1, secret.Calls);
    }

    [Fact]
    public void Explicit_SDK_and_native_transport_settings_disable_ambient_routing_retries_and_telemetry()
    {
        using var http = new HttpClient(); var config = RelaySecretLoader.ClientConfiguration(RelaySecretLoader.Reference.Parse(Arn, Version), http);
        Assert.False(Amazon.AWSConfigs.CSMConfig.CSMEnabled);
        Assert.Equal("https://secretsmanager.us-east-1.amazonaws.com/", config.ServiceURL);
        Assert.True(config.IgnoreConfiguredEndpointUrls); Assert.True(config.DisableClockSkewCorrection); Assert.True(config.DisableHostPrefixInjection);
        Assert.False(config.AllowAutoRedirect); Assert.False(config.UseHttp); Assert.False(config.UseDualstackEndpoint); Assert.False(config.UseFIPSEndpoint);
        Assert.True(config.DisableLogging); Assert.False(config.LogResponse); Assert.False(config.LogMetrics);
        Assert.Equal(0, config.MaxErrorRetry); Assert.Equal(0, config.MaxStaleConnectionRetries);
        Assert.False(config.HttpClientFactory.UseSDKHttpClientCaching(config)); Assert.False(config.HttpClientFactory.DisposeHttpClientsAfterUse(config));
        using var native = RelayStartupTransport.Native();
        Assert.False(native.UseProxy); Assert.False(native.UseCookies); Assert.False(native.AllowAutoRedirect); Assert.Null(native.ActivityHeadersPropagator);
        Assert.Null(native.SslOptions.RemoteCertificateValidationCallback); Assert.True(native.SslOptions.CertificateChainPolicy!.DisableCertificateDownloads);
    }

    [Theory]
    [InlineData("config", "AWSCURRENT")] [InlineData(Arn, "10000000-0000-0000-0000-00000000000A")]
    [InlineData("arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd90", Version)]
    [InlineData("arn:aws-cn:secretsmanager:cn-north-1:111122223333:secret:saveit/pilot/staging/relay/config-Abcd90", Version)]
    [InlineData("arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/Pilot/staging/relay/config-Abcd90", Version)]
    public async Task Invalid_secret_references_never_fetch_credentials(string arn, string version)
    {
        var metadata = Replies("{}"); var secret = Replies("{}");
        await Refused(Load(metadata, secret, arn: arn, version: version));
        Assert.Equal(0, metadata.Calls); Assert.Equal(0, secret.Calls);
    }

    [Theory]
    [InlineData("https://foreign.invalid/", "POST")] [InlineData("http://secretsmanager.us-east-1.amazonaws.com/", "POST")]
    [InlineData("https://secretsmanager.us-east-1.amazonaws.com/?extra=1", "POST")]
    [InlineData("https://secretsmanager.us-east-1.amazonaws.com/secret", "POST")]
    [InlineData("https://secretsmanager.us-east-1.amazonaws.com:8443/", "POST")]
    [InlineData("https://user@secretsmanager.us-east-1.amazonaws.com/", "POST")]
    [InlineData("https://secretsmanager.us-east-1.amazonaws.com/", "GET")]
    public async Task Exact_serialized_destination_and_method_reject_changes_before_wire_IO(string uri, string method)
    {
        var inner = Replies("{}");
        using var transport = new RelayStartupTransport(inner, new Uri("https://secretsmanager.us-east-1.amazonaws.com/"), 512 * 1024, CancellationToken.None);
        using var http = new HttpClient(transport);
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(new HttpRequestMessage(new HttpMethod(method), uri)));
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task Partial_body_cancellation_clears_owned_bytes_and_never_reaches_the_SDK_parser()
    {
        var stream = new HeldStream();
        var metadata = Replies(Json(Credentials()));
        var secret = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Refused(Load(metadata, secret, token: deadline.Token));
        Assert.True(stream.Disposed); Assert.True(stream.Seen.Length > 0);
        Assert.All(stream.Seen.ToArray(), value => Assert.Equal(0, value)); Assert.Equal(1, secret.Calls);
    }

    private sealed class HeldStream : Stream
    {
        internal Memory<byte> Seen { get; private set; } internal bool Disposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
            Seen = buffer; buffer.Span[0] = 65; await Task.Delay(Timeout.Infinite, token); return 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
