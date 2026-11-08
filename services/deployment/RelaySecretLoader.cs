using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.Runtime.Telemetry;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

[assembly: InternalsVisibleTo("SaveIT.Api.Tests")]

namespace SaveIT.Deployment;

// One startup read. No credential chain, profile, environment payload, cache or
// refreshing provider; native bounded metadata retrieval, SDK-owned SigV4.
internal static class RelaySecretLoader
{
    // Dedicated CLI process, before the first SDK configuration/client. Legacy
    // CSM has a separate global fallback chain from logging/OpenTelemetry and
    // can emit session-token metadata over UDP. Explicit false also stops its
    // environment/shared-profile fallback; per-client flags do not do that.
    static RelaySecretLoader() => Amazon.AWSConfigs.CSMConfig = new Amazon.Util.CSMConfig { CSMEnabled = false };

    public static Task<ApiRelayConfiguration> ReadAsync(string api, string relay, string arn, string version, CancellationToken cancellationToken = default) =>
        ReadAsync(api, relay, arn, version, Environment.GetEnvironmentVariable,
            RelayStartupTransport.Native(), RelayStartupTransport.Native(), ApiPeerProbe.TrustPath, cancellationToken);

    internal static async Task<ApiRelayConfiguration> ReadAsync(string api, string relay, string arn, string version,
        Func<string,string?> environment, HttpMessageHandler metadata, HttpMessageHandler secrets,
        string trustPath, CancellationToken cancellationToken = default)
    {
        using (metadata) using (secrets)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            ApiRelayConfiguration? configuration = null;
            byte[]? payload = null;
            try
            {
                var reference = Reference.Parse(arn, version);
                var apiUri = ApiPeerProbe.Origin(api); var relayUri = ApiPeerProbe.Origin(relay);
                if (relayUri.Port != 8443 || apiUri == relayUri) throw new InvalidOperationException();
                var relative = environment("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI");
                if (relative is null || !relative.StartsWith("/v2/credentials/", StringComparison.Ordinal) ||
                    !CanonicalUuid(relative["/v2/credentials/".Length..])) throw new InvalidOperationException();
                foreach (var name in ForbiddenEnvironment)
                    if (!string.IsNullOrEmpty(environment(name))) throw new InvalidOperationException();
                using var metadataTransport = new RelayStartupTransport(metadata, new Uri("http://169.254.170.2" + relative), 32768, deadline.Token);
                using var metadataClient = new HttpClient(metadataTransport, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
                using var response = await metadataClient.GetAsync("http://169.254.170.2" + relative, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                using var json = JsonDocument.Parse(((RelayStartupTransport.ClearingContent)response.Content).Bytes);
                var root = json.RootElement;
                string Value(string name, int max) {
                    var value = root.GetProperty(name).GetString();
                    if (value is null || value.Length is < 1 || value.Length > max || value.Any(c => c <= 32 || c >= 127)) throw new InvalidOperationException();
                    return value;
                }
                if (Value("RoleArn", 256) != reference.RoleArn ||
                    !DateTimeOffset.TryParseExact(Value("Expiration", 40),
                        ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expires) ||
                    expires <= DateTimeOffset.UtcNow.AddMinutes(1)) throw new InvalidOperationException();
                var credentials = new SessionAWSCredentials(Value("AccessKeyId", 128), Value("SecretAccessKey", 128), Value("Token", 16384));
                using var secretTransport = new RelayStartupTransport(secrets, reference.Endpoint, 512 * 1024, deadline.Token);
                using var http = new HttpClient(secretTransport, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
                using var client = new AmazonSecretsManagerClient(credentials, ClientConfiguration(reference, http));
                var result = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = arn, VersionId = version }, deadline.Token);
                try
                {
                    if (result.ARN != arn || result.VersionId != version || result.SecretBinary is not null || string.IsNullOrEmpty(result.SecretString))
                        throw new InvalidOperationException();
                    var encoding = new UTF8Encoding(false, true);
                    if (encoding.GetByteCount(result.SecretString) > ProtectedJsonInput.MaximumBytes) throw new InvalidOperationException();
                    payload = encoding.GetBytes(result.SecretString); result.SecretString = null;
                    using var input = new MemoryStream(payload, writable: false);
                    configuration = await ApiRelayConfiguration.ReadAsync(api, relay, input, trustPath, deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    var complete = configuration; configuration = null; return complete;
                }
                finally { result.SecretString = null; result.SecretBinary?.Dispose(); }
            }
            catch (Exception) { throw new InvalidOperationException(ApiRelayConfiguration.Failure); }
            finally { configuration?.Dispose(); if (payload is not null) CryptographicOperations.ZeroMemory(payload); }
        }
    }

    private static readonly string[] ForbiddenEnvironment = [
        "AWS_CONTAINER_CREDENTIALS_FULL_URI", "AWS_CONTAINER_AUTHORIZATION_TOKEN", "AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE",
        "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_PROFILE", "AWS_DEFAULT_PROFILE",
        "AWS_SHARED_CREDENTIALS_FILE", "AWS_CONFIG_FILE", "AWS_WEB_IDENTITY_TOKEN_FILE", "AWS_ROLE_ARN",
        "AWS_ENDPOINT_URL", "AWS_ENDPOINT_URL_SECRETS_MANAGER", "AWS_CA_BUNDLE",
        "SAVEIT_DEPLOYMENT_CONNECTION", "SAVEIT_ACCESS_CONNECTION", "ConnectionStrings__App", "ConnectionStrings__Migration", "ConnectionStrings__SearchWorker"
    ];

    internal static bool CanonicalUuid(string value) => Guid.TryParseExact(value, "D", out var parsed) && value == parsed.ToString("D");

    internal sealed record Reference(string Arn, string Version, string Region, string RoleArn, Uri Endpoint)
    {
        internal static Reference Parse(string arn, string version)
        {
            if (arn.Length > 256 || !CanonicalUuid(version)) throw new InvalidOperationException();
            var match = Regex.Match(arn, @"^arn:aws:secretsmanager:([a-z]{2}-[a-z]+-[1-9]):([0-9]{12}):secret:saveit/([a-z][a-z0-9]{1,11})/(staging|production)/relay/config-[A-Za-z0-9]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || match.Groups[1].Value.Length > 32 || match.Groups[1].Value.StartsWith("cn-", StringComparison.Ordinal)) throw new InvalidOperationException();
            var region = match.Groups[1].Value;
            return new(arn, version, region, $"arn:aws:iam::{match.Groups[2].Value}:role/saveit-{match.Groups[3].Value}-{match.Groups[4].Value}-relay-task",
                new Uri($"https://secretsmanager.{region}.amazonaws.com/"));
        }
    }

    internal static AmazonSecretsManagerConfig ClientConfiguration(Reference reference, HttpClient http) => new() {
        ServiceURL = reference.Endpoint.AbsoluteUri, AuthenticationRegion = reference.Region, AuthenticationServiceName = "secretsmanager",
        DefaultConfigurationMode = DefaultConfigurationMode.Standard, AuthSchemePreference = ["sigv4"], AccountIdEndpointMode = AccountIdEndpointMode.DISABLED,
        IgnoreConfiguredEndpointUrls = true, UseHttp = false, UseDualstackEndpoint = false, UseFIPSEndpoint = false,
        EndpointDiscoveryEnabled = false, DisableHostPrefixInjection = true, AllowAutoRedirect = false,
        RetryMode = RequestRetryMode.Standard, MaxErrorRetry = 0, MaxStaleConnectionRetries = 0, DisableClockSkewCorrection = true,
        DisableLogging = true, LogResponse = false, LogMetrics = false, TelemetryProvider = new DefaultTelemetryProvider(),
        ClientAppId = "SaveITRelay", JsonMaxDepth = 8, HttpClientFactory = new OwnedClientFactory(http)
    };

    private sealed class OwnedClientFactory(HttpClient http) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig config) => http;
        public override bool UseSDKHttpClientCaching(IClientConfig config) => false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig config) => false;
    }
}
