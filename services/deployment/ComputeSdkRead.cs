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
    internal bool Query=>Endpoint is "ec2" or "rds";
    internal static string DatabaseArn(ComputeScope s)=>$"arn:aws:rds:{s.Region}:{s.AccountId}:db:{s.Name}";
    internal static ComputeSdkRead Database(ComputeScope scope)=>new("rds","DescribeDBInstances",new JsonObject{
        ["Action"]="DescribeDBInstances",["Version"]="2014-10-31",["DBInstanceIdentifier"]=DatabaseArn(scope)},"",null,65536);
    internal string? GetPath {get;private init;}
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
    internal static ComputeSdkRead ParkedTasks(ComputeScope s)=>new("ecs","ListTasks",new JsonObject{
        ["cluster"]=ClusterArn(s),["desiredStatus"]="RUNNING",["maxResults"]=100},"taskArns","nextToken",32768);
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
    internal static ComputeSdkRead ResolverRules(string vpc)=>new("route53resolver","ListResolverRuleAssociations",new JsonObject{
        ["Filters"]=new JsonArray(new JsonObject{["Name"]="VPCId",["Values"]=new JsonArray(vpc)}),["MaxResults"]=100},"ResolverRuleAssociations","NextToken",32768);
    internal static ComputeSdkRead FirewallGroups(string vpc)=>new("route53resolver","ListFirewallRuleGroupAssociations",new JsonObject{
        ["VpcId"]=vpc,["MaxResults"]=100},"FirewallRuleGroupAssociations","NextToken",32768);
    internal static ComputeSdkRead FirewallConfig(string vpc)=>new("route53resolver","GetFirewallConfig",new JsonObject{
        ["ResourceId"]=vpc},"FirewallConfig",null,16384);
    internal static ComputeSdkRead DiscoveryNamespace(string arn)=>new("servicediscovery","GetNamespace",new JsonObject{["Id"]=arn},"Namespace",null,32768);
    internal static ComputeSdkRead DiscoveryService(string arn)=>new("servicediscovery","GetService",new JsonObject{["Id"]=arn},"Service",null,32768);
    internal static ComputeSdkRead DiscoveryInstances(string arn)=>new("servicediscovery","ListInstances",new JsonObject{["ServiceId"]=arn,["MaxResults"]=100},"Instances","NextToken",32768);
    internal static ComputeSdkRead HostedZone(string id,bool records)=>new("route53",records?"ListResourceRecordSets":"GetHostedZone",
        new JsonObject(),"",null,32768) { GetPath=$"/2013-04-01/hostedzone/{id}"+(records?"/rrset?maxitems=10":"") };
    internal static ComputeSdkRead EfsAccessPoint(string id)=>new("elasticfilesystem","DescribeAccessPoints",
        new JsonObject(),"AccessPoints","NextToken",32768) { GetPath=$"/2015-02-01/access-points?AccessPointId={id}" };
    internal static ComputeSdkRead EfsMountTargets(string id)=>new("elasticfilesystem","DescribeMountTargets",
        new JsonObject(),"MountTargets","NextMarker",32768) { GetPath=$"/2015-02-01/mount-targets?FileSystemId={id}" };
    internal static ComputeSdkRead EfsMountGroups(string id)=>new("elasticfilesystem","DescribeMountTargetSecurityGroups",
        new JsonObject(),"SecurityGroups",null,16384) { GetPath=$"/2015-02-01/mount-targets/{id}/security-groups" };
    internal string TargetPrefix=>Endpoint switch {"ecs"=>"AmazonEC2ContainerServiceV20141113.","api.ecr"=>"AmazonEC2ContainerRegistry_V20150921.","secretsmanager"=>"secretsmanager.","route53resolver"=>"Route53Resolver.","servicediscovery"=>"Route53AutoNaming_v20170314.",_=>throw new InvalidOperationException()};
    internal void ValidateResponse(JsonElement root)
    {
        RejectResponse(root);
        if(Endpoint=="secretsmanager") {
            var names=new[]{"ARN","Name","VersionId","SecretString","CreatedDate","VersionStages"};
            if(root.EnumerateObject().Any(x=>!names.Contains(x.Name,StringComparer.Ordinal)))throw new InvalidOperationException();
        }
        else if(root.EnumerateObject().Any(x=>x.Name!=Root&&x.Name!=OptionalRoot&&
            !(Action=="ListResolverRuleAssociations"&&x.Name=="MaxResults")))throw new InvalidOperationException();
    }
    private void RejectResponse(JsonElement root)
    {TaskMetadata.RejectDuplicates(root);if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(Root,out _))throw new InvalidOperationException();}
    internal static string Repository(ComputeScope s,string process)=>$"{s.Path}/{(process=="relay"?"deployment":process)}";
    internal static ComputeSdkRead SecurityGroups(ComputeRelease release)=>VpcInventory(release,"DescribeSecurityGroups","1000",524288);
    internal static ComputeSdkRead Subnets(ComputeRelease release)=>VpcInventory(release,"DescribeSubnets","1000",65536);
    internal static ComputeSdkRead RouteTables(ComputeRelease release)=>VpcInventory(release,"DescribeRouteTables","100",131072);
    internal static ComputeSdkRead Endpoints(ComputeRelease release)=>VpcInventory(release,"DescribeVpcEndpoints","100",524288);
    internal static ComputeSdkRead S3Prefix(ComputeRelease release)=>Ec2("DescribeManagedPrefixLists",new JsonObject{
        ["PrefixListId.1"]=ComputeNetworkBindings.Require(release).S3PrefixListId},16384);
    internal static ComputeSdkRead Dhcp(ComputeVpcObservation vpc)=>Ec2("DescribeDhcpOptions",new JsonObject{
        ["DhcpOptionsId.1"]=vpc.DhcpOptionsId},16384);
    internal static ComputeSdkRead EndpointInterfaces(ComputeRelease release)=>Ec2("DescribeNetworkInterfaces",new JsonObject{
        ["Filter.1.Name"]="vpc-id",["Filter.1.Value.1"]=release.Data.Network.VpcId,
        ["Filter.2.Name"]="interface-type",["Filter.2.Value.1"]="vpc_endpoint",["MaxResults"]="100"},131072);
    internal static ComputeSdkRead Vpc(ComputeRelease release)=>Ec2("DescribeVpcs",new JsonObject{["VpcId.1"]=release.Data.Network.VpcId},65536);
    internal static string DnsAttributeName(bool hostnames)=>hostnames?"enableDnsHostnames":"enableDnsSupport";
    internal static ComputeSdkRead VpcDns(ComputeRelease release,bool hostnames)=>Ec2("DescribeVpcAttribute",new JsonObject{
        ["VpcId"]=release.Data.Network.VpcId,["Attribute"]=DnsAttributeName(hostnames)},16384);
    private static ComputeSdkRead VpcInventory(ComputeRelease release,string action,string count,int maximumBytes)
    {
        _=ComputeNetworkBindings.Require(release);
        return Ec2(action,new JsonObject{["Filter.1.Name"]="vpc-id",["Filter.1.Value.1"]=release.Data.Network.VpcId,["MaxResults"]=count},maximumBytes);
    }
    private static ComputeSdkRead Ec2(string action,JsonObject fields,int maximumBytes)
    {fields.Add("Action",action);fields.Add("Version","2016-11-15");return new("ec2",action,fields,"",null,maximumBytes);}
    internal void ValidateRequest(string body)
    {
        if(Identity) {
            if(body is not("Action=GetCallerIdentity&Version=2011-06-15" or "Version=2011-06-15&Action=GetCallerIdentity"))throw new InvalidOperationException();
        } else if(Query) {
            var fields=new JsonObject();
            foreach(var pair in body.Split('&')) {
                var parts=pair.Split('=');if(parts.Length!=2||parts.Any(x=>System.Text.RegularExpressions.Regex.IsMatch(x,"%(?![0-9a-fA-F]{2})")))throw new InvalidOperationException();
                var key=Uri.UnescapeDataString(parts[0].Replace('+',' '));var value=Uri.UnescapeDataString(parts[1].Replace('+',' '));
                if(fields.ContainsKey(key))throw new InvalidOperationException();fields.Add(key,value);
            }
            if(!JsonNode.DeepEquals(fields,JsonNode.Parse(request)))throw new InvalidOperationException();
        } else {
            using var json=JsonDocument.Parse(body,new JsonDocumentOptions{MaxDepth=4});TaskMetadata.RejectDuplicates(json.RootElement);
            if(!JsonNode.DeepEquals(JsonNode.Parse(json.RootElement.GetRawText()),JsonNode.Parse(request)))throw new InvalidOperationException();
        }
    }
}
