using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeImageTests(ComputeReleaseFixture signing):IClassFixture<ComputeReleaseFixture>
{
    private static JsonObject Configurations()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-image-configurations.json")))!.AsObject();
    private static string Digest(byte[] bytes)=>"sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static JsonElement Element(JsonNode value)=>JsonSerializer.SerializeToElement(value);
    private sealed record Bundle(ComputeRelease Release,Dictionary<string,byte[]> Configurations,Dictionary<string,JsonObject> Envelopes);
    private async Task<Bundle> Images(Action<JsonObject>? changeConfigurations=null,Action<JsonObject>? changeManifest=null,long? expires=null,Func<string,string>? encodeConfiguration=null,Func<string,string>? encodeManifest=null)
    {
        var configs=Configurations();changeConfigurations?.Invoke(configs);var bytes=new Dictionary<string,byte[]>();var envelopes=new Dictionary<string,JsonObject>();
        foreach(var process in ComputeRelease.Processes) {
            var configuration=configs[process]!.ToJsonString();bytes[process]=Encoding.UTF8.GetBytes(encodeConfiguration?.Invoke(configuration)??configuration);
            var manifest=new JsonObject{["schemaVersion"]=2,["mediaType"]=ComputeImageManifest.OciMediaType,
                ["config"]=new JsonObject{["mediaType"]="application/vnd.oci.image.config.v1+json",["size"]=bytes[process].Length,["digest"]=Digest(bytes[process])},
                ["layers"]=new JsonArray(new JsonObject{["mediaType"]="application/vnd.oci.image.layer.v1.tar+gzip",["size"]=100,["digest"]="sha256:"+new string('c',64)})};
            changeManifest?.Invoke(manifest);var original=manifest.ToJsonString();original=encodeManifest?.Invoke(original)??original;
            envelopes[process]=new JsonObject{["images"]=new JsonArray(new JsonObject{
                ["registryId"]="111122223333",["repositoryName"]="saveit/pilot/staging/"+(process=="relay"?"deployment":process),
                ["imageId"]=new JsonObject{["imageDigest"]=Digest(Encoding.UTF8.GetBytes(original))},["imageManifest"]=original,["imageManifestMediaType"]=ComputeImageManifest.OciMediaType}),["failures"]=new JsonArray()};
        }
        var release=await signing.CreateAsync(generation:Guid.Parse("70000000-0000-0000-0000-000000000001"),expires:expires,mutate:data=>{
            foreach(var process in ComputeRelease.Processes)data["tasks"]![process]!["image"]="111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/"+(process=="relay"?"deployment":process)+"@"+envelopes[process]["images"]![0]!["imageId"]!["imageDigest"]!.GetValue<string>();
        });return new(release,bytes,envelopes);
    }
    private static ComputeImageManifest Manifest(Bundle b,string p)=>ComputeImageManifest.Parse(b.Release,p,Element(b.Envelopes[p]));
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Original_manifest_and_configuration_decoded_duplicate_aliases_fail_despite_correct_approved_hashes(bool manifest)
    {
        var b=manifest?await Images(encodeManifest:json=>json.Replace("\"schemaVersion\":2","\"schemaVersion\":1,\"schemaV\\u0065rsion\":2",StringComparison.Ordinal)):
            await Images(encodeConfiguration:json=>json.Replace("\"User\":\"1654\"","\"User\":\"0\",\"U\\u0073er\":\"1654\"",StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(()=>{var parsed=Manifest(b,"api");if(!manifest)ComputeImageContract.Validate(b.Release,"api",parsed,b.Configurations["api"]);});
    }
    [Fact]
    public async Task Approved_platform_manifests_bind_each_original_configuration_and_known_startup_contract()
    {
        var b=await Images();foreach(var p in ComputeRelease.Processes) {
            var manifest=Manifest(b,p);ComputeImageContract.Validate(b.Release,p,manifest,b.Configurations[p]);
            var changed=b.Configurations[p].ToArray();changed[^1]=(byte)' ';Assert.ThrowsAny<Exception>(()=>ComputeImageContract.Validate(b.Release,p,manifest,changed));
            Assert.ThrowsAny<Exception>(()=>ComputeImageContract.Validate(b.Release,p,manifest,b.Configurations[p][..^1]));
        }
    }
    [Theory]
    [InlineData("User")] [InlineData("WorkingDir")] [InlineData("Entrypoint")] [InlineData("Cmd")]
    [InlineData("Volumes")] [InlineData("Labels")] [InlineData("Env")] [InlineData("OnBuild")]
    [InlineData("Shell")] [InlineData("Healthcheck")] [InlineData("unknownAuthority")]
    public async Task A_validly_signed_digest_never_substitutes_for_safe_inherited_startup_authority(string field)
    {
        foreach(var process in ComputeRelease.Processes) {
            var b=await Images(configurations=>{
                var c=configurations[process]!["config"]!;
                switch(field) {
                    case "User":c[field]="0";break;
                    case "WorkingDir":c[field]="/unreviewed";break;
                    case "Entrypoint":case "Cmd":case "OnBuild":case "Shell":c[field]=new JsonArray("synthetic-private-command");break;
                    case "Volumes":c[field]!["/unreviewed"]=new JsonObject();break;
                    case "Labels":c[field]!["org.opencontainers.image.revision"]=new string('b',40);break;
                    case "Env":c[field]!.AsArray().Add("LD_PRELOAD=synthetic-private-marker");break;
                    case "Healthcheck":c[field]=new JsonObject{["Test"]=new JsonArray("CMD","synthetic-private-command")};break;
                    default:c[field]=false;break;
                }
            });
            Assert.ThrowsAny<Exception>(()=>ComputeImageContract.Validate(b.Release,process,Manifest(b,process),b.Configurations[process]));
        }
    }
    [Theory]
    [InlineData("windows")] [InlineData("arm64")] [InlineData("unknown-root")] [InlineData("bad-rootfs")]
    [InlineData("duplicate-env")] [InlineData("missing-env")] [InlineData("bad-health-default")]
    public async Task Platform_rootfs_and_unknown_or_aliased_configuration_authority_fail_closed(string mode)
    {
        var b=await Images(c=>{
            var image=c["api"]!;var config=image["config"]!;
            if(mode=="windows")image["os"]="windows";if(mode=="arm64")image["architecture"]="arm64";
            if(mode=="unknown-root")image["variant"]="unreviewed";
            if(mode=="bad-rootfs")image["rootfs"]!["diff_ids"]=new JsonArray();
            if(mode=="duplicate-env")config["Env"]!.AsArray().Add("APP_UID=1654");
            if(mode=="missing-env")config["Env"]!.AsArray().RemoveAt(0);
            if(mode=="bad-health-default")config["Healthcheck"]!["Retries"]=9;
        });Assert.ThrowsAny<Exception>(()=>ComputeImageContract.Validate(b.Release,"api",Manifest(b,"api"),b.Configurations["api"]));
    }
    [Theory]
    [InlineData("schema")] [InlineData("index")] [InlineData("extra")] [InlineData("url")]
    [InlineData("configuration-size")] [InlineData("configuration-digest")] [InlineData("layer-size")]
    [InlineData("no-layers")] [InlineData("foreign-layer")] [InlineData("media")]
    public async Task Manifest_schema_descriptors_and_foreign_layer_authority_fail_even_when_hash_is_approved(string mode)
    {
        var b=await Images(changeManifest:m=>{
            if(mode=="schema")m["schemaVersion"]=1;if(mode=="index")m["manifests"]=new JsonArray();if(mode=="extra")m["unknownAuthority"]=false;
            if(mode=="url")m["config"]!["urls"]=new JsonArray("https://foreign.invalid/private");
            if(mode=="configuration-size")m["config"]!["size"]=65537;if(mode=="configuration-digest")m["config"]!["digest"]="sha256:"+new string('A',64);
            if(mode=="layer-size")m["layers"]![0]!["size"]=5368709121L;if(mode=="no-layers")m["layers"]=new JsonArray();
            if(mode=="foreign-layer")m["layers"]![0]!["mediaType"]="application/vnd.docker.image.rootfs.foreign.diff.tar.gzip";
            if(mode=="media")m["mediaType"]=ComputeImageManifest.DockerMediaType;
        });Assert.ThrowsAny<Exception>(()=>Manifest(b,"api"));
    }
    [Theory]
    [InlineData("manifest-bytes")] [InlineData("digest")] [InlineData("account")] [InlineData("repository")]
    [InlineData("missing")] [InlineData("extra")] [InlineData("failures")]
    public async Task Exact_original_manifest_hash_and_single_scoped_response_are_required(string mode)
    {
        var b=await Images();var e=b.Envelopes["api"];var image=e["images"]![0]!;
        if(mode=="manifest-bytes")image["imageManifest"]=image["imageManifest"]!.GetValue<string>()+" ";
        if(mode=="digest")image["imageId"]!["imageDigest"]="sha256:"+new string('f',64);
        if(mode=="account")image["registryId"]="444455556666";if(mode=="repository")image["repositoryName"]="saveit/pilot/staging/web";
        if(mode=="missing")e["images"]=new JsonArray();if(mode=="extra")e["images"]!.AsArray().Add(image.DeepClone());
        if(mode=="failures")e["failures"]=new JsonArray(new JsonObject{["failureCode"]="ImageNotFound"});
        Assert.ThrowsAny<Exception>(()=>Manifest(b,"api"));
    }
    private static JsonObject Authorization()=>new(){["authorizationData"]=new JsonArray(new JsonObject{
        ["authorizationToken"]=Convert.ToBase64String("AWS:synthetic-private-registry-password"u8),["expiresAt"]=DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),["proxyEndpoint"]="https://111122223333.dkr.ecr.us-east-1.amazonaws.com"})};
    [Theory]
    [InlineData("host")] [InlineData("path")] [InlineData("plaintext")] [InlineData("token")]
    [InlineData("whitespace")] [InlineData("username")] [InlineData("control")] [InlineData("expired")]
    [InlineData("overlong")] [InlineData("extra")] [InlineData("unknown")]
    public void Registry_bearer_response_never_controls_destinations_or_accepts_alternate_credentials(string mode)
    {
        var a=Authorization();var data=a["authorizationData"]![0]!;
        if(mode=="host")data["proxyEndpoint"]="https://foreign.invalid";if(mode=="path")data["proxyEndpoint"]="https://111122223333.dkr.ecr.us-east-1.amazonaws.com/";
        if(mode=="plaintext")data["proxyEndpoint"]="http://111122223333.dkr.ecr.us-east-1.amazonaws.com";
        if(mode=="token")data["authorizationToken"]="invalid-base64";
        if(mode=="whitespace")data["authorizationToken"]=data["authorizationToken"]!.GetValue<string>()+"\n";
        if(mode=="username")data["authorizationToken"]=Convert.ToBase64String("root:synthetic-private-registry-password"u8);
        if(mode=="control")data["authorizationToken"]=Convert.ToBase64String("AWS:synthetic\nprivate"u8);
        if(mode=="expired")data["expiresAt"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if(mode=="overlong")data["expiresAt"]=DateTimeOffset.UtcNow.AddHours(13).ToUnixTimeSeconds();
        if(mode=="extra")a["authorizationData"]!.AsArray().Add(data.DeepClone());if(mode=="unknown")data["approved"]=true;
        Assert.ThrowsAny<Exception>(()=>ComputeRegistryAuthorization.Parse(new("111122223333","us-east-1","pilot","staging"),Element(a)));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,ct);}
    private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/x-amz-json-1.1")};
    private static Task<ComputeAwsSession> Session()=>ComputeAwsSession.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(new JsonObject{
        ["accessKeyId"]="ASIA0000000000000000",["secretAccessKey"]=new string('a',40),["sessionToken"]="synthetic-temporary-session-token",["expiration"]=DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")}.ToJsonString())),CancellationToken.None);
    private static Func<string,HttpMessageHandler> Provider(Bundle b,List<string> calls,Func<string,string,HttpResponseMessage?>? change=null,Func<string,CancellationToken,Task>? wait=null)
    {
        var tasks=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-observations.json")))!;
        foreach(var p in ComputeRelease.Processes)tasks[p]!["taskDefinition"]!["containerDefinitions"]![0]!["image"]=b.Release.Data.Tasks[p].Image;
        var cell=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-parked-observations.json")))!;
        return service=>new Handler(async(request,ct)=>{
            if(service=="registry") {
                var p=ComputeRelease.Processes.Single(p=>request.RequestUri!.AbsoluteUri==$"https://111122223333.dkr.ecr.us-east-1.amazonaws.com/v2/{ComputeSdkRead.Repository(b.Release.Scope,p)}/blobs/{Manifest(b,p).ConfigurationDigest}");
                Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal("Basic",request.Headers.Authorization!.Scheme);Assert.Equal(Authorization()["authorizationData"]![0]!["authorizationToken"]!.GetValue<string>(),request.Headers.Authorization.Parameter);
                Assert.False(request.Headers.Contains("X-Amz-Security-Token"));calls.Add("blob:"+p);if(wait is not null)await wait("blob:"+p,ct);
                return change?.Invoke("blob",p)??new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(b.Configurations[p])};
            }
            Assert.Equal($"https://{(service=="ecr"?"api.ecr":service)}.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);
            Assert.Contains("AWS4-HMAC-SHA256 Credential=ASIA0000000000000000/",request.Headers.GetValues("Authorization").Single());
            if(service=="ecr")Assert.Contains("/us-east-1/ecr/aws4_request",request.Headers.GetValues("Authorization").Single());
            if(service=="sts"){calls.Add("identity");return Response("<GetCallerIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><GetCallerIdentityResult><Arn>arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-compute-controller/synthetic</Arn><UserId>AROA00000000000000000:synthetic</UserId><Account>111122223333</Account></GetCallerIdentityResult><ResponseMetadata><RequestId>synthetic</RequestId></ResponseMetadata></GetCallerIdentityResponse>");}
            var action=request.Headers.GetValues("X-Amz-Target").Single().Split('.')[1];calls.Add(action);if(wait is not null)await wait(action,ct);
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if(action=="DescribeTaskDefinition"){var p=b.Release.Data.Tasks.Single(x=>x.Value.TaskDefinitionArn==json.RootElement.GetProperty("taskDefinition").GetString()).Key;return Response(tasks[p]!.ToJsonString());}
            if(action=="DescribeClusters")return Response(cell["cluster"]!.ToJsonString());if(action=="DescribeServices")return Response(cell["services"]!.ToJsonString());
            if(action=="GetAuthorizationToken")return change?.Invoke("authorization","")??Response(Authorization().ToJsonString());
            Assert.Equal("BatchGetImage",action);var process=ComputeRelease.Processes.Single(p=>ComputeSdkRead.Repository(b.Release.Scope,p)==json.RootElement.GetProperty("repositoryName").GetString());
            Assert.Equal(b.Release.Scope.AccountId,json.RootElement.GetProperty("registryId").GetString());Assert.Equal(b.Release.Data.Tasks[process].Image.Split('@')[1],json.RootElement.GetProperty("imageIds")[0].GetProperty("imageDigest").GetString());
            return change?.Invoke("manifest",process)??Response(b.Envelopes[process].ToJsonString());
        });
    }
    [Fact]
    public async Task Official_SDK_reads_all_four_exact_manifests_before_one_authentication_and_only_their_bound_configuration_blobs()
    {
        var b=await Images();var calls=new List<string>();var result=await AcquiredComputeImages.ReadAsync(b.Release,await Session(),Provider(b,calls),CancellationToken.None);
        Assert.Same(b.Release,result.Cell.Tasks.Release);Assert.Equal(16,calls.Count);Assert.Equal(4,calls.Count(x=>x=="BatchGetImage"));Assert.Equal("GetAuthorizationToken",calls[11]);Assert.Equal(ComputeRelease.Processes.Select(p=>"blob:"+p),calls.Skip(12));
    }
    [Theory]
    [InlineData("manifest","unknown")] [InlineData("manifest","duplicate")] [InlineData("manifest","redirect")]
    [InlineData("manifest","overflow")] [InlineData("authorization","foreign")] [InlineData("authorization","unknown")]
    [InlineData("blob","digest")] [InlineData("blob","encoding")] [InlineData("blob","redirect")]
    [InlineData("blob","trailer")] [InlineData("blob","overflow")] [InlineData("blob","length")]
    public async Task Raw_provider_failures_are_content_free_and_stop_before_any_later_effect(string target,string mode)
    {
        var b=await Images();var calls=new List<string>();
        var operation=AcquiredComputeImages.ReadAsync(b.Release,await Session(),Provider(b,calls,(kind,p)=>{
            if(kind!=target)return null;
            if(kind=="authorization") {var a=Authorization();if(mode=="foreign")a["authorizationData"]![0]!["proxyEndpoint"]="https://foreign.invalid/private";else a["authorizationData"]![0]!["private"]="synthetic-private-marker";return Response(a.ToJsonString());}
            var body=kind=="manifest"?b.Envelopes[p].ToJsonString():Encoding.UTF8.GetString(b.Configurations[p]);
            if(mode=="unknown")body=body[..^1]+",\"private\":\"synthetic-private-marker\"}";
            if(mode=="duplicate")body=body.Replace("\"registryId\":","\"registryId\":\"foreign\",\"reg\\u0069stryId\":",StringComparison.Ordinal);
            if(mode=="digest")body+=" ";if(mode=="overflow")body=body.PadRight(kind=="manifest"?262145:65537);
            var response=Response(body);if(mode=="encoding")response.Content.Headers.ContentEncoding.Add("gzip");if(mode=="trailer")response.Headers.TryAddWithoutValidation("Trailer","private");
            if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new Uri("https://foreign.invalid/private");}if(mode=="length")response.Content.Headers.ContentLength=body.Length+1;
            return response;
        }),CancellationToken.None);
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeImageAcquisition.Failure,failure.Message);Assert.Null(failure.InnerException);
        Assert.Equal(target=="manifest"?8:target=="authorization"?12:13,calls.Count);
    }
    [Theory]
    [InlineData("BatchGetImage")] [InlineData("GetAuthorizationToken")] [InlineData("blob:api")]
    public async Task Cancellation_waits_for_observed_entry_and_stops_every_new_stage(string target)
    {
        var b=await Images();var calls=new List<string>();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
        var operation=AcquiredComputeImages.ReadAsync(b.Release,await Session(),Provider(b,calls,wait:async(action,ct)=>{if(action==target){entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}}),cancel.Token);
        try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));}finally{cancel.Cancel();}
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation.WaitAsync(TimeSpan.FromSeconds(5)));Assert.Equal(ComputeImageAcquisition.Failure,failure.Message);Assert.Equal(target,calls[^1]);
    }
    [Theory]
    [InlineData("BatchGetImage",8)] [InlineData("GetAuthorizationToken",12)] [InlineData("blob:api",13)]
    public async Task Real_approval_expiry_after_each_new_stage_prevents_subsequent_requests(string target,int count)
    {
        var b=await Images(expires:DateTimeOffset.UtcNow.ToUnixTimeSeconds()+4);var calls=new List<string>();var returned=false;
        var operation=AcquiredComputeImages.ReadAsync(b.Release,await Session(),Provider(b,calls,wait:async(action,ct)=>{
            if(action!=target)return;while(DateTimeOffset.UtcNow.ToUnixTimeSeconds()<b.Release.ExpiresAt)await Task.Delay(25,ct);returned=true;
        }),CancellationToken.None);
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation.WaitAsync(TimeSpan.FromSeconds(12)));
        Assert.True(returned,"An actual response after expiry is required; transport failure cannot provide the refusal.");
        Assert.Equal(ComputeImageAcquisition.Failure,failure.Message);Assert.Equal(target,calls[^1]);Assert.Equal(count,calls.Count);
    }
}
