using Amazon.ECS;
using Amazon.Runtime;
using Amazon.Runtime.Telemetry;
using Amazon.SecurityToken;
using Amazon.ECR;

namespace SaveIT.Deployment;

internal static class ComputeSdkConfiguration
{
    static ComputeSdkConfiguration()=>Amazon.AWSConfigs.CSMConfig=new Amazon.Util.CSMConfig{CSMEnabled=false};
    internal static AmazonECSConfig Ecs(string region,HttpClient http)=>Configure(new AmazonECSConfig(),region,"ecs",http);
    internal static AmazonSecurityTokenServiceConfig Sts(string region,HttpClient http)=>Configure(new AmazonSecurityTokenServiceConfig(),region,"sts",http);
    internal static AmazonECRConfig Ecr(string region,HttpClient http)=>Configure(new AmazonECRConfig(),region,"api.ecr",http);
    private static T Configure<T>(T config,string region,string service,HttpClient http) where T:ClientConfig
    {
        config.ServiceURL=$"https://{service}.{region}.amazonaws.com/";config.AuthenticationRegion=region;config.AuthenticationServiceName=service=="api.ecr"?"ecr":service;
        config.DefaultConfigurationMode=DefaultConfigurationMode.Standard;config.AuthSchemePreference=["sigv4"];config.AccountIdEndpointMode=AccountIdEndpointMode.DISABLED;
        config.IgnoreConfiguredEndpointUrls=true;config.UseHttp=false;config.UseDualstackEndpoint=false;config.UseFIPSEndpoint=false;config.EndpointDiscoveryEnabled=false;
        config.DisableHostPrefixInjection=true;config.AllowAutoRedirect=false;config.RetryMode=RequestRetryMode.Standard;config.MaxErrorRetry=0;config.MaxStaleConnectionRetries=0;
        config.DisableClockSkewCorrection=true;config.DisableLogging=true;config.LogResponse=false;config.LogMetrics=false;config.TelemetryProvider=new DefaultTelemetryProvider();
        config.DisableRequestCompression=true;config.ClientAppId="SaveITCompute";config.JsonMaxDepth=24;config.HttpClientFactory=new OwnedClient(http);
        return config;
    }
    private sealed class OwnedClient(HttpClient http):HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig config)=>http;
        public override bool UseSDKHttpClientCaching(IClientConfig config)=>false;
        public override bool DisposeHttpClientsAfterUse(IClientConfig config)=>false;
    }
}
