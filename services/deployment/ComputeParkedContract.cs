using System.Text.Json;
using System.Text.Json.Nodes;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Initial zero-count metadata only. Counts/tags never prove process absence or ownership transfer.
internal static class ComputeParkedContract
{
    internal static void Cluster(ComputeRelease release,JsonElement observed)
    {
        var root=Envelope(observed,"clusters");var clusters=root["clusters"]!.AsArray();if(clusters.Count!=1)throw new InvalidOperationException();
        var actual=clusters[0]!.AsObject();
        actual["tags"]=Tags(actual["tags"]!.AsArray());
        RemoveDefault(actual,"attachmentsStatus",JsonValue.Create("UPDATE_COMPLETE")!);
        RemoveDefault(actual,"statistics",new JsonArray());
        if(actual["configuration"] is JsonObject configuration) {
            RemoveDefault(configuration,"executeCommandConfiguration",new JsonObject{["logging"]="DEFAULT"});
            RemoveDefault(actual,"configuration",new JsonObject());
        }
        if(!JsonNode.DeepEquals(actual,new JsonObject{
            ["clusterArn"]=ComputeSdkRead.ClusterArn(release.Scope),["clusterName"]=release.Scope.Name,["status"]="ACTIVE",
            ["registeredContainerInstancesCount"]=0,["runningTasksCount"]=0,["pendingTasksCount"]=0,["activeServicesCount"]=4,
            ["settings"]=new JsonArray(new JsonObject{["name"]="containerInsights",["value"]="disabled"}),
            ["capacityProviders"]=new JsonArray(),["defaultCapacityProviderStrategy"]=new JsonArray(),["attachments"]=new JsonArray(),["tags"]=ExpectedTags(release.Scope,null)
        }))throw new InvalidOperationException();
    }
    internal static void Services(ComputeRelease release,JsonElement observed)
    {
        var root=Envelope(observed,"services");var services=root["services"]!.AsArray();if(services.Count!=4)throw new InvalidOperationException();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in services)
        {
            var actual=item!.AsObject();var name=actual["serviceName"]!.GetValue<string>();
            var process=ComputeRelease.Processes.Single(p=>name==release.Scope.Name+"-"+p);if(!seen.Add(process))throw new InvalidOperationException();
            actual["tags"]=Tags(actual["tags"]!.AsArray());
            StripTimestamp(actual,"createdAt");
            if(actual.Remove("createdBy",out var creator)) { var value=creator!.GetValue<string>();if(value.Length is <1 or >2048)throw new InvalidOperationException(); }
            if(actual.Remove("events",out var events)) {
                var values=events!.AsArray();if(values.Count>100)throw new InvalidOperationException();
                foreach(var entry in values){var e=entry!.AsObject();StripTimestamp(e,"createdAt");var fields=Object(JsonSerializer.SerializeToElement(e),"id","message");
                    if(fields["id"].GetString() is not {Length:>0 and <=128}||fields["message"].GetString() is not {Length:<=1024})throw new InvalidOperationException();}
            }
            foreach(var field in new[]{"loadBalancers","placementConstraints","placementStrategy","capacityProviderStrategy","volumeConfigurations","vpcLatticeConfigurations","serviceConnectResources","taskSets"})RemoveDefault(actual,field,new JsonArray());
            RemoveDefault(actual,"healthCheckGracePeriodSeconds",JsonValue.Create(0)!);
            RemoveDefault(actual,"resourceManagementType",JsonValue.Create("CUSTOMER")!);
            RemoveDefault(actual,"serviceConnectConfiguration",new JsonObject{["enabled"]=false});
            var deployment=actual["deploymentConfiguration"]!.AsObject();
            RemoveDefault(deployment,"strategy",JsonValue.Create("ROLLING")!);
            RemoveDefault(deployment,"alarms",new JsonObject{["alarmNames"]=new JsonArray(),["enable"]=false,["rollback"]=false});
            SortNetwork(actual["networkConfiguration"]!.AsObject());
            var deployments=actual["deployments"]!.AsArray();if(deployments.Count!=1)throw new InvalidOperationException();
            var current=deployments[0]!.AsObject();Match(current["id"]!.GetValue<string>(),"ecs-svc/[0-9]{1,20}");current.Remove("id");
            StripTimestamp(current,"createdAt");StripTimestamp(current,"updatedAt");
            if(current.Remove("rolloutStateReason",out var reason)&&reason!.GetValue<string>().Length>1024)throw new InvalidOperationException();
            RemoveDefault(current,"capacityProviderStrategy",new JsonArray());RemoveDefault(current,"serviceConnectConfiguration",new JsonObject{["enabled"]=false});
            SortNetwork(current["networkConfiguration"]!.AsObject());
            if(!JsonNode.DeepEquals(actual,Expected(release,process)))throw new InvalidOperationException();
        }
    }
    private static JsonObject Envelope(JsonElement observed,string field)
    {
        RejectDuplicates(observed);if(observed.ValueKind!=JsonValueKind.Object||!observed.TryGetProperty(field,out _)||observed.EnumerateObject().Any(x=>x.Name!=field&&x.Name!="failures"))throw new InvalidOperationException();
        var root=JsonNode.Parse(observed.GetRawText())!.AsObject();RemoveDefault(root,"failures",new JsonArray());return root;
    }
    private static void StripTimestamp(JsonObject value,string field)
    {if(value.Remove(field,out var timestamp)&&(!timestamp!.AsValue().TryGetValue<double>(out var number)||!double.IsFinite(number)||number<=0||number>253402300799))throw new InvalidOperationException();}
    private static JsonObject Tags(JsonArray values)
    {
        var result=new JsonObject();if(values.Count>50)throw new InvalidOperationException();
        foreach(var pair in values){var fields=Object(JsonSerializer.SerializeToElement(pair),"key","value");result.Add(fields["key"].GetString()!,fields["value"].GetString());}return result;
    }
    private static JsonObject ExpectedTags(ComputeScope s,string? process)
    {var tags=new JsonObject{["Project"]="SaveIT",["Cell"]=s.Cell,["Environment"]=s.Environment,["ManagedBy"]="Terraform"};if(process is not null)tags["Process"]=process;return tags;}
    private static void SortNetwork(JsonObject network)
    {
        var vpc=network["awsvpcConfiguration"]!.AsObject();
        foreach(var field in new[]{"subnets","securityGroups"})vpc[field]=new JsonArray(vpc[field]!.AsArray().Select(x=>x!.GetValue<string>()).Order(StringComparer.Ordinal).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
    }
    private static JsonObject Network(ComputeRelease release,string process)=>new(){["awsvpcConfiguration"]=new JsonObject{
        ["subnets"]=new JsonArray(release.Data.Network.SubnetIds.Order(StringComparer.Ordinal).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),
        ["securityGroups"]=new JsonArray(release.Data.Network.SecurityGroups[process]),["assignPublicIp"]="DISABLED"}};
    private static JsonObject Expected(ComputeRelease release,string process)
    {
        var s=release.Scope;var task=release.Data.Tasks[process].TaskDefinitionArn;
        var registries=new JsonArray();if(process is "api" or "relay")registries.Add(new JsonObject{["registryArn"]=process=="api"?release.Data.Network.ApiDiscoveryServiceArn:release.Data.Network.RelayDiscoveryServiceArn});
        return new(){
            ["serviceArn"]=ComputeSdkRead.ServiceArn(s,process),["serviceName"]=s.Name+"-"+process,["clusterArn"]=ComputeSdkRead.ClusterArn(s),
            ["taskDefinition"]=task,["status"]="ACTIVE",["desiredCount"]=0,["pendingCount"]=0,["runningCount"]=0,
            ["launchType"]="FARGATE",["platformVersion"]="1.4.0",["platformFamily"]="Linux",["schedulingStrategy"]="REPLICA",
            ["enableExecuteCommand"]=false,["enableECSManagedTags"]=true,["propagateTags"]="SERVICE",["availabilityZoneRebalancing"]="ENABLED",
            ["roleArn"]=$"arn:aws:iam::{s.AccountId}:role/aws-service-role/ecs.amazonaws.com/AWSServiceRoleForECS",
            ["deploymentController"]=new JsonObject{["type"]="ECS"},["deploymentConfiguration"]=new JsonObject{
                ["minimumHealthyPercent"]=100,["maximumPercent"]=200,["deploymentCircuitBreaker"]=new JsonObject{["enable"]=true,["rollback"]=true}},
            ["networkConfiguration"]=Network(release,process),["serviceRegistries"]=registries,["tags"]=ExpectedTags(s,process),
            ["deployments"]=new JsonArray(new JsonObject{
                ["status"]="PRIMARY",["taskDefinition"]=task,["desiredCount"]=0,["pendingCount"]=0,["runningCount"]=0,["failedTasks"]=0,
                ["launchType"]="FARGATE",["platformVersion"]="1.4.0",["platformFamily"]="Linux",["networkConfiguration"]=Network(release,process),["rolloutState"]="COMPLETED"})
        };
    }
}
