using System.Xml.Linq;
using static SaveIT.Deployment.ComputeQueryXml;

namespace SaveIT.Deployment;

// Complete initial authenticated topology only. This establishes no attachment,
// route, resolver, exclusive process ownership or drift-prevention authority.
internal static class ComputeSecurityGroups
{
    internal static void Validate(ComputeRelease release,XElement response)
    {
        var binding=ComputeNetworkBindings.Require(release);var s=release.Scope;var network=release.Data.Network;
        var root=Fields(response,["requestId","securityGroupInfo"],"nextToken");_=Text(root["requestId"],1,128);
        if(root.TryGetValue("nextToken",out var next)&&Text(next)!="")throw new InvalidOperationException();
        var groups=Items(root["securityGroupInfo"],13);if(groups.Length!=13)throw new InvalidOperationException();
        var ids=network.SecurityGroups.Concat(binding.SecurityGroups).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
        var expected=Edges(ids,binding.S3PrefixListId);var seen=new HashSet<string>(StringComparer.Ordinal);var defaults=0;
        foreach(var group in groups) {
            var fields=Fields(group,["ownerId","groupId","groupName","groupDescription","vpcId","ipPermissions","ipPermissionsEgress"],"tagSet","securityGroupArn");
            var id=Text(fields["groupId"],20,20);TaskMetadata.Match(id,"sg-[a-f0-9]{17}");if(!seen.Add(id)||Text(fields["ownerId"])!=s.AccountId||Text(fields["vpcId"])!=network.VpcId)throw new InvalidOperationException();
            if(fields.TryGetValue("securityGroupArn",out var arn)&&Text(arn)!=$"arn:aws:ec2:{s.Region}:{s.AccountId}:security-group/{id}")throw new InvalidOperationException();
            _=Text(fields["groupDescription"],0,255);var name=Text(fields["groupName"],1,255);string role;
            if(name=="default") {role="default";defaults++;if(ids.Values.Contains(id,StringComparer.Ordinal))throw new InvalidOperationException();}
            else {
                role=ids.Single(x=>x.Value==id).Key;var suffix=role switch{"session-keys"=>"session-keys","ecr-api" or "ecr-dkr" or "logs" or "configuration" or "web-token"=>role+"-endpoint",_=>role};
                if(!name.StartsWith(s.Name+"-"+suffix+"-",StringComparison.Ordinal))throw new InvalidOperationException();
            }
            Tags(fields.GetValueOrDefault("tagSet"),s,role);
            var observed=new HashSet<string>(StringComparer.Ordinal);
            Permissions(fields["ipPermissions"],"in",s,network,observed);Permissions(fields["ipPermissionsEgress"],"out",s,network,observed);
            if(!observed.SetEquals(role=="default"?[]:expected[role]))throw new InvalidOperationException();
        }
        if(defaults!=1||ids.Values.Any(id=>!seen.Contains(id)))throw new InvalidOperationException();
    }
    private static Dictionary<string,HashSet<string>> Edges(Dictionary<string,string> ids,string prefix)
    {
        var edges=ids.ToDictionary(x=>x.Key,_=>new HashSet<string>(StringComparer.Ordinal),StringComparer.Ordinal);
        void Pair(string from,string to,int port){edges[from].Add($"out:{port}:group:{ids[to]}");edges[to].Add($"in:{port}:group:{ids[from]}");}
        foreach(var role in new[]{"api","worker","deployment"})Pair(role,"database",5432);
        Pair("api","session-keys",2049);Pair("web","relay",8443);Pair("relay","api",8443);Pair("web","web-token",443);
        foreach(var role in new[]{"api","web","worker","deployment","relay"}) {
            foreach(var endpoint in new[]{"ecr-api","ecr-dkr","logs","configuration"})if(endpoint!="configuration"||role!="web")Pair(role,endpoint,443);
            edges[role].Add($"out:443:prefix:{prefix}");
        }
        return edges;
    }
    private static void Tags(XElement? element,ComputeScope scope,string role)
    {
        var tags=new Dictionary<string,string>(StringComparer.Ordinal);
        if(element is not null)foreach(var item in Items(element,50)){var f=Fields(item,["key","value"]);if(!tags.TryAdd(Text(f["key"],1,128),Text(f["value"],0,256)))throw new InvalidOperationException();}
        if(role=="default"&&tags.Count==0)return;
        if(tags.GetValueOrDefault("Project")!="SaveIT"||tags.GetValueOrDefault("Cell")!=scope.Cell||tags.GetValueOrDefault("Environment")!=scope.Environment)throw new InvalidOperationException();
        if(role is "api" or "web" or "worker" or "deployment" or "relay" or "web-token")if(tags.GetValueOrDefault("Process")!=role)throw new InvalidOperationException();
    }
    private static void Permissions(XElement element,string direction,ComputeScope scope,ComputeNetwork network,HashSet<string> edges)
    {
        foreach(var permission in Items(element,64)) {
            var p=Fields(permission,["ipProtocol","fromPort","toPort"],"groups","ipRanges","ipv6Ranges","prefixListIds");
            var port=Text(p["fromPort"]);if(Text(p["ipProtocol"])!="tcp"||Text(p["toPort"])!=port||port is not("443" or "5432" or "8443" or "2049"))throw new InvalidOperationException();
            foreach(var name in new[]{"ipRanges","ipv6Ranges"})if(p.TryGetValue(name,out var ranges)&&Items(ranges).Length!=0)throw new InvalidOperationException();
            var count=0;
            if(p.TryGetValue("groups",out var groups))foreach(var group in Items(groups,13)) {
                var g=Fields(group,["userId","groupId"],"vpcId","groupName","description","securityGroupRuleId");
                if(Text(g["userId"])!=scope.AccountId||g.TryGetValue("vpcId",out var vpc)&&Text(vpc)!=network.VpcId)throw new InvalidOperationException();
                if(g.TryGetValue("groupName",out var name))_=Text(name,1,255);if(g.TryGetValue("description",out var description))_=Text(description,0,255);
                if(g.TryGetValue("securityGroupRuleId",out var rule))TaskMetadata.Match(Text(rule),"sgr-[a-f0-9]{17}");
                TaskMetadata.Match(Text(g["groupId"]),"sg-[a-f0-9]{17}");if(!edges.Add($"{direction}:{port}:group:{Text(g["groupId"])}"))throw new InvalidOperationException();count++;
            }
            if(p.TryGetValue("prefixListIds",out var prefixes))foreach(var prefix in Items(prefixes,1)) {
                var f=Fields(prefix,["prefixListId"],"description","securityGroupRuleId");
                if(f.TryGetValue("description",out var description))_=Text(description,0,255);if(f.TryGetValue("securityGroupRuleId",out var rule))TaskMetadata.Match(Text(rule),"sgr-[a-f0-9]{17}");
                if(!edges.Add($"{direction}:{port}:prefix:{Text(f["prefixListId"])}"))throw new InvalidOperationException();count++;
            }
            if(count==0)throw new InvalidOperationException();
        }
    }
}
