using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveIT.Deployment;

// Fixed SDK reads only; no generic endpoint, action, response template or provider write.
internal sealed class ComputeSdkRead
{
    internal string Action {get;}
    internal string Endpoint {get;}
    internal string Root {get;}
    internal string? OptionalRoot {get;}
    internal int MaximumBytes {get;}
    internal bool Identity=>Endpoint=="sts";
    private readonly string request;
    private ComputeSdkRead(string endpoint,string action,JsonObject body,string root,string? optional,int maximumBytes)
    {Endpoint=endpoint;Action=action;request=body.ToJsonString();Root=root;OptionalRoot=optional;MaximumBytes=maximumBytes;}
    internal static string ClusterArn(ComputeScope s)=>$"arn:aws:ecs:{s.Region}:{s.AccountId}:cluster/{s.Name}";
    internal static string ServiceArn(ComputeScope s,string process)=>$"arn:aws:ecs:{s.Region}:{s.AccountId}:service/{s.Name}/{s.Name}-{process}";
    internal static ComputeSdkRead Sts()=>new("sts","GetCallerIdentity",new JsonObject(),"",null,16384);
    internal static ComputeSdkRead Task(string arn)=>new("ecs","DescribeTaskDefinition",new JsonObject{["taskDefinition"]=arn,["include"]=new JsonArray("TAGS")},"taskDefinition","tags",65536);
    internal static ComputeSdkRead Cluster(ComputeScope s)=>new("ecs","DescribeClusters",new JsonObject{
        ["clusters"]=new JsonArray(ClusterArn(s)),["include"]=new JsonArray("ATTACHMENTS","CONFIGURATIONS","SETTINGS","TAGS")},"clusters","failures",65536);
    internal static ComputeSdkRead Services(ComputeScope s)=>new("ecs","DescribeServices",new JsonObject{
        ["cluster"]=ClusterArn(s),["services"]=new JsonArray(ComputeRelease.Processes.Select(p=>(JsonNode?)JsonValue.Create(ServiceArn(s,p))).ToArray()),["include"]=new JsonArray("TAGS")},"services","failures",524288);
    internal static ComputeSdkRead Manifest(ComputeRelease release,string process)=>new("api.ecr","BatchGetImage",new JsonObject{
        ["registryId"]=release.Scope.AccountId,["repositoryName"]=Repository(release.Scope,process),
        ["imageIds"]=new JsonArray(new JsonObject{["imageDigest"]=release.Data.Tasks[process].Image.Split('@')[1]}),
        ["acceptedMediaTypes"]=new JsonArray(ComputeImageManifest.DockerMediaType,ComputeImageManifest.OciMediaType)},"images","failures",262144);
    internal static ComputeSdkRead RegistryAuthorization()=>new("api.ecr","GetAuthorizationToken",new JsonObject(),"authorizationData",null,32768);
    internal static ComputeSdkRead Configuration(ComputeRelease release,string process)
    {
        if(process is not("api" or "worker" or "relay"))throw new InvalidOperationException();
        var reference=release.Data.Tasks[process];
        return new("secretsmanager","GetSecretValue",new JsonObject{["SecretId"]=reference.ConfigurationArn,["VersionId"]=reference.ConfigurationVersionId},"SecretString",null,524288);
    }
    internal string TargetPrefix=>Endpoint switch {"ecs"=>"AmazonEC2ContainerServiceV20141113.","api.ecr"=>"AmazonEC2ContainerRegistry_V20150921.","secretsmanager"=>"secretsmanager.",_=>throw new InvalidOperationException()};
    internal void ValidateResponse(JsonElement root)
    {
        RejectResponse(root);
        if(Endpoint=="secretsmanager") {
            var names=new[]{"ARN","Name","VersionId","SecretString","CreatedDate","VersionStages"};
            if(root.EnumerateObject().Any(x=>!names.Contains(x.Name,StringComparer.Ordinal)))throw new InvalidOperationException();
        }
        else if(root.EnumerateObject().Any(x=>x.Name!=Root&&x.Name!=OptionalRoot))throw new InvalidOperationException();
    }
    private void RejectResponse(JsonElement root)
    {TaskMetadata.RejectDuplicates(root);if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(Root,out _))throw new InvalidOperationException();}
    internal static string Repository(ComputeScope s,string process)=>$"{s.Path}/{(process=="relay"?"deployment":process)}";
    internal void ValidateRequest(string body)
    {
        if(Identity) {
            if(body is not("Action=GetCallerIdentity&Version=2011-06-15" or "Version=2011-06-15&Action=GetCallerIdentity"))throw new InvalidOperationException();
        } else {
            using var json=JsonDocument.Parse(body,new JsonDocumentOptions{MaxDepth=4});TaskMetadata.RejectDuplicates(json.RootElement);
            if(!JsonNode.DeepEquals(JsonNode.Parse(json.RootElement.GetRawText()),JsonNode.Parse(request)))throw new InvalidOperationException();
        }
    }
}
