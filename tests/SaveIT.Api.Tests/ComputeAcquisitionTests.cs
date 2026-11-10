using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.ECS;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeAcquisitionTests(ComputeReleaseFixture signing):IClassFixture<ComputeReleaseFixture>
{
    private const string Caller="arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-compute-controller/synthetic";
    private static string Identity(string? arn=null,string account="111122223333")=>$"<GetCallerIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><GetCallerIdentityResult><Arn>{arn??Caller}</Arn><UserId>AROA00000000000000000:synthetic</UserId><Account>{account}</Account></GetCallerIdentityResult><ResponseMetadata><RequestId>synthetic-request</RequestId></ResponseMetadata></GetCallerIdentityResponse>";
    private static JsonObject SessionJson()=>new(){["accessKeyId"]="ASIA0000000000000000",["secretAccessKey"]=new string('a',40),["sessionToken"]="synthetic-temporary-session-token",["expiration"]=DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")};
    private static Task<ComputeAwsSession> Session(JsonObject? json=null)=>ComputeAwsSession.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes((json??SessionJson()).ToJsonString())),CancellationToken.None);
    private static JsonObject Observations()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-observations.json")))!.AsObject();
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>handle(request,ct);
    }
    private static HttpResponseMessage Response(string body,bool xml=false)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,xml?"text/xml":"application/x-amz-json-1.1")};
    private Task<ComputeRelease> Release()=>signing.CreateAsync(generation:Guid.Parse("70000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task Official_SDK_signs_one_fixed_STS_identity_read_then_four_exact_ECS_revisions_and_preserves_raw_contracts()
    {
        var release=await Release();var session=await Session();var calls=new List<string>();var fixtures=Observations();
        var acquired=await AcquiredComputeTasks.ReadAsync(release,session,service=>new Handler(async(request,ct)=>{
            Assert.Equal($"https://{service}.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);Assert.Equal(HttpMethod.Post,request.Method);
            Assert.Contains("AWS4-HMAC-SHA256 Credential=ASIA0000000000000000/",request.Headers.GetValues("Authorization").Single());
            Assert.Equal("synthetic-temporary-session-token",request.Headers.GetValues("X-Amz-Security-Token").Single());
            var body=await request.Content!.ReadAsStringAsync(ct);calls.Add(service);
            if(service=="sts"){Assert.Contains("Action=GetCallerIdentity",body);return Response(Identity(),true);}
            using var json=JsonDocument.Parse(body);var arn=json.RootElement.GetProperty("taskDefinition").GetString();
            var process=release.Data.Tasks.Single(x=>x.Value.TaskDefinitionArn==arn).Key;return Response(fixtures[process]!.ToJsonString());
        }),CancellationToken.None);
        Assert.Equal(new[]{"sts","ecs","ecs","ecs","ecs"},calls);Assert.Same(release,acquired.Release);Assert.Equal(Caller,acquired.CallerArn);
    }

    private async Task Failed(Task operation)
    {
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);
        Assert.Equal(ComputeTaskAcquisition.Failure,failure.Message);Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData("foreign-role")] [InlineData("foreign-account")] [InlineData("duplicate")]
    [InlineData("dtd")] [InlineData("foreign-namespace")] [InlineData("unknown")]
    [InlineData("redirect")] [InlineData("encoding")] [InlineData("trailer")]
    [InlineData("failure")] [InlineData("oversized")] [InlineData("length-mismatch")]
    public async Task Untrusted_identity_XML_framing_and_provider_failures_stop_before_any_ECS_read(string mode)
    {
        var release=await Release();var session=await Session();var calls=0;
        await Failed(AcquiredComputeTasks.ReadAsync(release,session,service=>new Handler((_,_)=>{
            calls++;Assert.Equal("sts",service);var body=Identity();
            if(mode=="foreign-role")body=Identity(Caller.Replace("compute-controller","api-task",StringComparison.Ordinal));
            if(mode=="foreign-account")body=Identity(account:"444455556666");
            if(mode=="duplicate")body=body.Replace("</Account>","</Account><Account>111122223333</Account>",StringComparison.Ordinal);
            if(mode=="dtd")body="<!DOCTYPE x [<!ENTITY leak SYSTEM 'http://foreign.invalid/private'>]>"+body.Replace("synthetic-request","&leak;",StringComparison.Ordinal);
            if(mode=="foreign-namespace")body=body.Replace("https://sts.amazonaws.com/doc/2011-06-15/","https://foreign.invalid/",StringComparison.Ordinal);
            if(mode=="unknown")body=body.Replace("</GetCallerIdentityResult>","<Unknown>authority</Unknown></GetCallerIdentityResult>",StringComparison.Ordinal);
            if(mode=="oversized")body=body.PadRight(16385);
            var response=Response(body,true);
            if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new Uri("https://foreign.invalid/");}
            if(mode=="failure")response.StatusCode=HttpStatusCode.InternalServerError;
            if(mode=="encoding")response.Content.Headers.ContentEncoding.Add("gzip");
            if(mode=="trailer")response.Headers.TryAddWithoutValidation("Trailer","synthetic");
            if(mode=="length-mismatch")response.Content.Headers.ContentLength=body.Length+1;
            return Task.FromResult(response);
        }),CancellationToken.None));Assert.Equal(1,calls);
    }

    [Theory]
    [InlineData("api")] [InlineData("web")] [InlineData("worker")] [InlineData("relay")]
    public async Task Original_task_JSON_rejects_semantics_the_official_SDK_would_silently_drop(string process)
    {
        var release=await Release();var fixtures=Observations();var changed=fixtures[process]!["taskDefinition"]!;
        changed["unknownAuthority"]=new JsonObject{["private"]="synthetic-private-marker"};
        // Positive control: typed SDK deserialization alone loses the unknown field.
        using(var handler=new Handler((_,_)=>Task.FromResult(Response(fixtures[process]!.ToJsonString()))))
        using(var http=new HttpClient(handler))
        using(var sdk=new AmazonECSClient((await Session()).Credentials,ComputeSdkConfiguration.Ecs("us-east-1",http)))
        {
            var response=await sdk.DescribeTaskDefinitionAsync(new Amazon.ECS.Model.DescribeTaskDefinitionRequest{TaskDefinition=release.Data.Tasks[process].TaskDefinitionArn});
            Assert.DoesNotContain("unknownAuthority",JsonSerializer.Serialize(response));
        }
        var seen=0;
        await Failed(AcquiredComputeTasks.ReadAsync(release,await Session(),service=>new Handler(async(request,ct)=>{
            if(service=="sts")return Response(Identity(),true);
            seen++;using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var current=release.Data.Tasks.Single(x=>x.Value.TaskDefinitionArn==json.RootElement.GetProperty("taskDefinition").GetString()).Key;
            return Response(fixtures[current]!.ToJsonString());
        }),CancellationToken.None));
        Assert.Equal(Array.IndexOf(ComputeRelease.Processes,process)+1,seen);
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("extra-root")] [InlineData("privileged")]
    [InlineData("oversized")] [InlineData("trailing")] [InlineData("actual-trailer")]
    public async Task Acquired_raw_task_framing_duplicates_and_privileges_fail_closed(string mode)
    {
        var release=await Release();var fixtures=Observations();var calls=0;
        await Failed(AcquiredComputeTasks.ReadAsync(release,await Session(),service=>new Handler((_,_)=>{
            if(service=="sts")return Task.FromResult(Response(Identity(),true));calls++;
            var value=fixtures["api"]!.DeepClone();
            if(mode=="extra-root")value["approved"]=true;
            if(mode=="privileged")value["taskDefinition"]!["containerDefinitions"]![0]!["privileged"]=true;
            var body=value.ToJsonString();
            if(mode=="duplicate")body=body.Replace("\"status\":\"ACTIVE\"","\"status\":\"INACTIVE\",\"st\\u0061tus\":\"ACTIVE\"",StringComparison.Ordinal);
            if(mode=="oversized")body=body.PadRight(65537);
            if(mode=="trailing")body+="{}";
            var response=Response(body);if(mode=="actual-trailer")response.TrailingHeaders.TryAddWithoutValidation("synthetic","value");
            return Task.FromResult(response);
        }),CancellationToken.None));Assert.Equal(1,calls);
    }

    [Theory]
    [InlineData("accessKeyId","\"AKIA0000000000000000\"")]
    [InlineData("secretAccessKey","\"short\"")]
    [InlineData("sessionToken","\"synthetic\\nprivate-token\"")]
    [InlineData("expiration","\"2020-01-01T00:00:00Z\"")]
    [InlineData("expiration","\"2099-01-01T00:00:00Z\"")]
    [InlineData("expiration","\"2099-01-01T00:00:00+00:00\"")]
    [InlineData("roleArn","\"caller-supplied-not-authority\"")]
    public async Task Session_carrier_refuses_long_term_ambient_overlong_expired_and_unknown_authority(string field,string json)
    {
        var value=SessionJson();value[field]=JsonNode.Parse(json);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Session(value));
    }

    [Fact]
    public async Task Session_carrier_enforces_original_32KiB_UTF8_EOF_and_decoded_duplicate_names()
    {
        var value=SessionJson().ToJsonString();
        Task<ComputeAwsSession> Read(byte[] input)=>ComputeAwsSession.ReadAsync(new MemoryStream(input),CancellationToken.None);
        await Read(Encoding.UTF8.GetBytes(value.PadRight(32768)));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(Encoding.UTF8.GetBytes(value.PadRight(32769))));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Read([0xff,0xfe]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(Encoding.UTF8.GetBytes(value+"{}")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Read(Encoding.UTF8.GetBytes(value.Replace("\"accessKeyId\":","\"accessK\\u0065yId\":\"other\",\"accessKeyId\":",StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("AWS_ACCESS_KEY_ID")] [InlineData("AWS_PROFILE")] [InlineData("AWS_SHARED_CREDENTIALS_FILE")]
    [InlineData("AWS_CONFIG_FILE")] [InlineData("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI")]
    [InlineData("AWS_CONTAINER_CREDENTIALS_FULL_URI")] [InlineData("AWS_WEB_IDENTITY_TOKEN_FILE")]
    [InlineData("AWS_ENDPOINT_URL")] [InlineData("AWS_ENDPOINT_URL_ECS")] [InlineData("AWS_CA_BUNDLE")]
    [InlineData("AWS_ENDPOINT_URL_STS")] [InlineData("AWS_EC2_METADATA_SERVICE_ENDPOINT")]
    [InlineData("AWS_EC2_METADATA_SERVICE_ENDPOINT_MODE")]
    public async Task Alternate_credential_endpoint_and_CA_sources_fail_before_the_protected_file_is_opened(string name)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(()=>ComputeAwsSession.ReadAsync("does-not-exist",key=>key==name?"synthetic-private-marker":null,CancellationToken.None));
    }

    [Fact]
    public async Task Linux_session_file_requires_private_permissions_on_the_open_handle()
    {
        // Native packaged tests exercise this boundary on Linux as well; Windows has no Unix mode.
        if(!OperatingSystem.IsLinux())return;
        var path=Path.Combine(Path.GetTempPath(),"saveit-compute-session-"+Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path,SessionJson().ToJsonString());
            File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            _=await ComputeAwsSession.ReadAsync(path,_=>null,CancellationToken.None);
            foreach(var permission in new[]{UnixFileMode.GroupRead,UnixFileMode.GroupWrite,UnixFileMode.GroupExecute,UnixFileMode.OtherRead,UnixFileMode.OtherWrite,UnixFileMode.OtherExecute})
            {
                File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|permission);
                await Assert.ThrowsAsync<InvalidOperationException>(()=>ComputeAwsSession.ReadAsync(path,_=>null,CancellationToken.None));
            }
        }
        finally{File.Delete(path);}
    }

    [Fact]
    public async Task Cancellation_stops_a_stalled_provider_before_any_additional_attempt()
    {
        var release=await Release();var session=await Session();var calls=0;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Failed(AcquiredComputeTasks.ReadAsync(release,session,_=>new Handler(async(_,ct)=>{calls++;await Task.Delay(Timeout.Infinite,ct);throw new InvalidOperationException();}),timeout.Token));
        Assert.Equal(1,calls);
    }
    [Fact]
    public void SDK_configuration_pins_signing_endpoint_mode_no_retries_compression_and_no_ambient_monitoring()
    {
        using var http=new HttpClient();
        var configs=new Amazon.Runtime.ClientConfig[]{ComputeSdkConfiguration.Ecs("us-east-1",http),ComputeSdkConfiguration.Sts("us-east-1",http)};
        foreach(var c in configs)
        {
            Assert.Equal(Amazon.Runtime.DefaultConfigurationMode.Standard,c.DefaultConfigurationMode);
            Assert.Equal(new[]{"sigv4"},c.AuthSchemePreference);Assert.True(c.IgnoreConfiguredEndpointUrls);Assert.Equal(0,c.MaxErrorRetry);Assert.Equal(0,c.MaxStaleConnectionRetries);
            Assert.True(c.DisableRequestCompression);Assert.True(c.DisableClockSkewCorrection);Assert.False(c.AllowAutoRedirect);Assert.False(c.UseDualstackEndpoint);Assert.False(c.UseFIPSEndpoint);Assert.False(c.EndpointDiscoveryEnabled);
            Assert.True(c.DisableLogging);Assert.False(c.LogResponse);Assert.False(c.LogMetrics);Assert.Equal(24,c.JsonMaxDepth);
            Assert.False(c.HttpClientFactory.UseSDKHttpClientCaching(c));Assert.False(c.HttpClientFactory.DisposeHttpClientsAfterUse(c));
        }
        Assert.False(Amazon.AWSConfigs.CSMConfig.CSMEnabled);
    }
}
