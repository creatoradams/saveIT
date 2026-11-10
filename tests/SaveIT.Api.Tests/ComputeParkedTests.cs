using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeParkedTests(ComputeReleaseFixture signing):IClassFixture<ComputeReleaseFixture>
{
    private static JsonObject Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-parked-observations.json")))!.AsObject();
    private static JsonObject Tasks()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-observations.json")))!.AsObject();
    private Task<ComputeRelease> Release()=>signing.CreateAsync(generation:Guid.Parse("70000000-0000-0000-0000-000000000001"));
    private static Task<ComputeAwsSession> Session()=>ComputeAwsSession.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(new JsonObject{
        ["accessKeyId"]="ASIA0000000000000000",["secretAccessKey"]=new string('a',40),["sessionToken"]="synthetic-temporary-session-token",["expiration"]=DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")}.ToJsonString())),CancellationToken.None);
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>handle(request,ct);}
    private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/x-amz-json-1.1")};
    private static JsonElement Element(JsonNode value)=>JsonSerializer.SerializeToElement(value);
    [Fact]
    public async Task Initial_parked_cluster_and_all_four_services_match_independent_frozen_producer_contract()
    {
        var release=await Release();var f=Fixture();ComputeParkedContract.Cluster(release,Element(f["cluster"]!));ComputeParkedContract.Services(release,Element(f["services"]!));
        var services=f["services"]!["services"]!.AsArray();
        f["services"]!["services"]=new JsonArray(services.Reverse().Select(x=>x!.DeepClone()).ToArray());
        foreach(var s in f["services"]!["services"]!.AsArray()) {
            var value=s!.AsObject();value["events"]=new JsonArray(new JsonObject{["id"]="synthetic",["createdAt"]=1700000000,["message"]="private diagnostic is never returned"});
            value["taskSets"]=new JsonArray();value["serviceConnectConfiguration"]=new JsonObject{["enabled"]=false};value["resourceManagementType"]="CUSTOMER";
            var subnet=value["networkConfiguration"]!["awsvpcConfiguration"]!["subnets"]!.AsArray();value["networkConfiguration"]!["awsvpcConfiguration"]!["subnets"]=new JsonArray(subnet.Reverse().Select(x=>x!.DeepClone()).ToArray());
        }
        ComputeParkedContract.Services(release,Element(f["services"]!));
    }
    [Theory]
    [InlineData("serviceArn")] [InlineData("clusterArn")] [InlineData("taskDefinition")] [InlineData("status")]
    [InlineData("desiredCount")] [InlineData("runningCount")] [InlineData("pendingCount")] [InlineData("launchType")]
    [InlineData("platformVersion")] [InlineData("platformFamily")] [InlineData("schedulingStrategy")]
    [InlineData("enableExecuteCommand")] [InlineData("enableECSManagedTags")] [InlineData("propagateTags")]
    [InlineData("availabilityZoneRebalancing")] [InlineData("roleArn")] [InlineData("deploymentController")]
    [InlineData("deploymentConfiguration")] [InlineData("networkConfiguration")] [InlineData("serviceRegistries")]
    [InlineData("tags")] [InlineData("deployments")]
    public async Task Every_service_authority_field_is_required_and_drift_fails_for_each_process(string field)
    {
        var release=await Release();
        for(var i=0;i<4;i++) {
            var f=Fixture()["services"]!;var s=f["services"]![i]!.AsObject();s.Remove(field);
            Assert.ThrowsAny<Exception>(()=>ComputeParkedContract.Services(release,Element(f)));
            f=Fixture()["services"]!;s=f["services"]![i]!.AsObject();var original=s[field]!;
            s[field]=original.GetValueKind() switch {JsonValueKind.True or JsonValueKind.False=>!original.GetValue<bool>(),JsonValueKind.Number=>1,JsonValueKind.String=>"foreign-authority",JsonValueKind.Array=>new JsonArray(),_=>new JsonObject()};
            // Web/worker intentionally have an empty registry set; replacing it by empty preserves it.
            if(field=="serviceRegistries"&&i is 1 or 2)s[field]=new JsonArray(new JsonObject{["registryArn"]="foreign"});
            Assert.ThrowsAny<Exception>(()=>ComputeParkedContract.Services(release,Element(f)));
        }
    }
    [Theory]
    [InlineData("extra-group")] [InlineData("public-ip")] [InlineData("foreign-subnet")]
    [InlineData("extra-deployment")] [InlineData("old-task")] [InlineData("deployment-public-ip")]
    [InlineData("running-deployment")] [InlineData("failed-deployment")] [InlineData("hook")]
    [InlineData("load-balancer")] [InlineData("lattice")] [InlineData("volume")]
    [InlineData("capacity-provider")] [InlineData("service-connect")] [InlineData("unknown")]
    [InlineData("duplicate-service")] [InlineData("failures")] [InlineData("duplicate-tag")]
    public async Task Nested_or_additional_service_authority_is_never_normalized_away(string mode)
    {
        var release=await Release();var f=Fixture()["services"]!;var s=f["services"]![0]!;var d=s["deployments"]![0]!;
        switch(mode) {
            case "extra-group":s["networkConfiguration"]!["awsvpcConfiguration"]!["securityGroups"]!.AsArray().Add("sg-00000000000000004");break;
            case "public-ip":s["networkConfiguration"]!["awsvpcConfiguration"]!["assignPublicIp"]="ENABLED";break;
            case "foreign-subnet":s["networkConfiguration"]!["awsvpcConfiguration"]!["subnets"]![0]="subnet-00000000000000009";break;
            case "extra-deployment":s["deployments"]!.AsArray().Add(d.DeepClone());break;
            case "old-task":d["taskDefinition"]=d["taskDefinition"]!.GetValue<string>().Replace(":1",":2",StringComparison.Ordinal);break;
            case "deployment-public-ip":d["networkConfiguration"]!["awsvpcConfiguration"]!["assignPublicIp"]="ENABLED";break;
            case "running-deployment":d["runningCount"]=1;break;
            case "failed-deployment":d["rolloutState"]="FAILED";break;
            case "hook":s["deploymentConfiguration"]!["lifecycleHooks"]=new JsonArray(new JsonObject{["roleArn"]="foreign"});break;
            case "load-balancer":s["loadBalancers"]=new JsonArray(new JsonObject{["targetGroupArn"]="foreign"});break;
            case "lattice":s["vpcLatticeConfigurations"]=new JsonArray(new JsonObject{["roleArn"]="foreign"});break;
            case "volume":s["volumeConfigurations"]=new JsonArray(new JsonObject{["name"]="foreign"});break;
            case "capacity-provider":s["capacityProviderStrategy"]=new JsonArray(new JsonObject{["capacityProvider"]="FARGATE_SPOT"});break;
            case "service-connect":s["serviceConnectConfiguration"]=new JsonObject{["enabled"]=true};break;
            case "unknown":s["unknownAuthority"]=false;break;
            case "duplicate-service":f["services"]![1]=s.DeepClone();break;
            case "failures":f["failures"]=new JsonArray(new JsonObject{["reason"]="MISSING"});break;
            case "duplicate-tag":s["tags"]!.AsArray().Add(s["tags"]![0]!.DeepClone());break;
        }
        Assert.ThrowsAny<Exception>(()=>ComputeParkedContract.Services(release,Element(f)));
    }
    [Theory]
    [InlineData("clusterArn")] [InlineData("clusterName")] [InlineData("status")] [InlineData("registeredContainerInstancesCount")]
    [InlineData("runningTasksCount")] [InlineData("pendingTasksCount")] [InlineData("activeServicesCount")]
    [InlineData("settings")] [InlineData("capacityProviders")] [InlineData("defaultCapacityProviderStrategy")]
    [InlineData("attachments")] [InlineData("configuration")] [InlineData("serviceConnectDefaults")]
    [InlineData("tags")] [InlineData("unknownAuthority")]
    public async Task Cluster_identity_capacity_configuration_and_counts_are_exact(string field)
    {
        var release=await Release();var f=Fixture()["cluster"]!;var cluster=f["clusters"]![0]!.AsObject();cluster[field]=new JsonObject{["foreign"]=true};
        Assert.ThrowsAny<Exception>(()=>ComputeParkedContract.Cluster(release,Element(f)));
    }
    [Theory]
    [InlineData("cluster","failure")] [InlineData("cluster","missing")] [InlineData("cluster","extra")]
    [InlineData("services","missing")]
    public async Task Provider_result_failures_and_missing_or_extra_resources_fail_closed(string kind,string mode)
    {
        var release=await Release();var f=Fixture()[kind]!;var field=kind=="cluster"?"clusters":"services";
        if(mode=="failure")f["failures"]=new JsonArray(new JsonObject{["reason"]="MISSING"});
        else if(mode=="missing")f[field]!.AsArray().RemoveAt(0);
        else f[field]!.AsArray().Add(f[field]![0]!.DeepClone());
        Assert.ThrowsAny<Exception>(()=>{if(kind=="cluster")ComputeParkedContract.Cluster(release,Element(f));else ComputeParkedContract.Services(release,Element(f));});
    }
    [Theory]
    [InlineData("DescribeClusters",false)] [InlineData("DescribeServices",false)]
    [InlineData("DescribeClusters",true)] [InlineData("DescribeServices",true)]
    public async Task Cancellation_or_approval_expiry_during_new_provider_reads_never_returns_an_acquired_cell(string target,bool expire)
    {
        var release=await signing.CreateAsync(generation:Guid.Parse("70000000-0000-0000-0000-000000000001"),expires:expire?DateTimeOffset.UtcNow.ToUnixTimeSeconds()+4:null);
        var tasks=Tasks();var f=Fixture();var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=new List<string>();var targetReturned=false;
        using var cancel=new CancellationTokenSource();
        var operation=AcquiredParkedComputeCell.ReadAsync(release,await Session(),service=>new Handler(async(request,ct)=>{
            if(service=="sts")return Response("<GetCallerIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><GetCallerIdentityResult><Arn>arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-compute-controller/synthetic</Arn><UserId>AROA00000000000000000:synthetic</UserId><Account>111122223333</Account></GetCallerIdentityResult><ResponseMetadata><RequestId>synthetic</RequestId></ResponseMetadata></GetCallerIdentityResponse>");
            var action=request.Headers.GetValues("X-Amz-Target").Single().Split('.')[1];calls.Add(action);
            if(action=="DescribeTaskDefinition") {using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var process=release.Data.Tasks.Single(x=>x.Value.TaskDefinitionArn==json.RootElement.GetProperty("taskDefinition").GetString()).Key;return Response(tasks[process]!.ToJsonString());}
            if(action==target) {
                reached.TrySetResult();
                if(expire){while(DateTimeOffset.UtcNow.ToUnixTimeSeconds()<release.ExpiresAt)await Task.Delay(25,ct);targetReturned=true;}
                else await Task.Delay(Timeout.Infinite,ct);
            }
            return Response(f[action=="DescribeClusters"?"cluster":"services"]!.ToJsonString());
        }),cancel.Token);
        try {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));if(!expire)cancel.Cancel();
            var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(ComputeCellAcquisition.Failure,failure.Message);
            if(expire)Assert.True(targetReturned,"The provider must return after expiry; a transport timeout cannot supply the passing refusal.");
            // Expiry is checked after every complete new read; no later provider read follows it.
            Assert.Equal(target,calls[^1]);
        } finally {cancel.Cancel();}
    }
    [Theory]
    [InlineData("valid")] [InlineData("raw-unknown")] [InlineData("duplicate")]
    [InlineData("root-unknown")] [InlineData("overflow")] [InlineData("private-drift")]
    public async Task Official_SDK_acquires_exact_cluster_and_four_services_after_identity_and_tasks(string mode)
    {
        var release=await Release();var tasks=Tasks();var f=Fixture();var calls=new List<string>();
        var operation=AcquiredParkedComputeCell.ReadAsync(release,await Session(),service=>new Handler(async(request,ct)=>{
            if(service=="sts")return Response("<GetCallerIdentityResponse xmlns=\"https://sts.amazonaws.com/doc/2011-06-15/\"><GetCallerIdentityResult><Arn>arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-compute-controller/synthetic</Arn><UserId>AROA00000000000000000:synthetic</UserId><Account>111122223333</Account></GetCallerIdentityResult><ResponseMetadata><RequestId>synthetic</RequestId></ResponseMetadata></GetCallerIdentityResponse>");
            var action=request.Headers.GetValues("X-Amz-Target").Single().Split('.')[1];calls.Add(action);using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if(action=="DescribeTaskDefinition"){var p=release.Data.Tasks.Single(x=>x.Value.TaskDefinitionArn==json.RootElement.GetProperty("taskDefinition").GetString()).Key;return Response(tasks[p]!.ToJsonString());}
            if(action=="DescribeClusters")return Response(f["cluster"]!.ToJsonString());
            Assert.Equal(ComputeEcsRead.ClusterArn(release.Scope),json.RootElement.GetProperty("cluster").GetString());Assert.Equal(4,json.RootElement.GetProperty("services").GetArrayLength());
            var value=f["services"]!;
            if(mode=="raw-unknown")value["services"]![0]!["unknownAuthority"]=false;
            if(mode=="root-unknown")value["approved"]=true;
            if(mode=="private-drift")value["services"]![0]!["deploymentConfiguration"]!["private"]="synthetic-private-marker";
            var body=value.ToJsonString();if(mode=="duplicate")body=body.Replace("\"desiredCount\":0","\"desiredCount\":1,\"desiredC\\u006funt\":0",StringComparison.Ordinal);if(mode=="overflow")body=body.PadRight(524289);
            return Response(body);
        }),CancellationToken.None);
        if(mode=="valid")Assert.Same(release,(await operation).Tasks.Release);
        else {var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeCellAcquisition.Failure,error.Message);Assert.Null(error.InnerException);}
        Assert.Equal(new[]{"DescribeTaskDefinition","DescribeTaskDefinition","DescribeTaskDefinition","DescribeTaskDefinition","DescribeClusters","DescribeServices"},calls);
    }
}
