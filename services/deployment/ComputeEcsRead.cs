using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveIT.Deployment;

// Only fixed read operations. This is not a generic SDK operation/endpoint escape hatch.
internal sealed class ComputeEcsRead
{
    internal string Action {get;}
    internal JsonObject Request {get;}
    internal string Root {get;}
    internal int MaximumBytes {get;}
    private ComputeEcsRead(string action,JsonObject request,string root,int maximumBytes)
    {Action=action;Request=request;Root=root;MaximumBytes=maximumBytes;}
    internal static string ClusterArn(ComputeScope s)=>$"arn:aws:ecs:{s.Region}:{s.AccountId}:cluster/{s.Name}";
    internal static string ServiceArn(ComputeScope s,string process)=>$"arn:aws:ecs:{s.Region}:{s.AccountId}:service/{s.Name}/{s.Name}-{process}";
    internal static ComputeEcsRead Cluster(ComputeScope s)=>new("DescribeClusters",new JsonObject{
        ["clusters"]=new JsonArray(ClusterArn(s)),["include"]=new JsonArray("ATTACHMENTS","CONFIGURATIONS","SETTINGS","TAGS")},"clusters",65536);
    internal static ComputeEcsRead Services(ComputeScope s)=>new("DescribeServices",new JsonObject{
        ["cluster"]=ClusterArn(s),["services"]=new JsonArray(ComputeRelease.Processes.Select(p=>(JsonNode?)JsonValue.Create(ServiceArn(s,p))).ToArray()),["include"]=new JsonArray("TAGS")},"services",524288);
    internal void ValidateRequest(JsonElement value)
    {if(!JsonNode.DeepEquals(JsonNode.Parse(value.GetRawText()),Request))throw new InvalidOperationException();}
}
