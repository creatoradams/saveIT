using System.Collections.Immutable;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Optional only for backward-compatible signature inspection. Network acquisition
// and future provider effects must require a complete independently signed binding.
internal sealed record ComputeNetworkBindings(ImmutableArray<string> DatabaseSubnetIds,
    ImmutableDictionary<string,string> RouteTables,ImmutableDictionary<string,string> SecurityGroups,
    ImmutableDictionary<string,string> Endpoints,ImmutableDictionary<string,string> EndpointPolicySha256,string S3PrefixListId)
{
    internal static readonly string[] AdditionalGroups=["deployment","database","session-keys","ecr-api","ecr-dkr","logs","configuration","web-token"];
    internal static readonly string[] EndpointNames=["ecr-api","ecr-dkr","logs","configuration","web-token","layers"];
    internal static ComputeNetworkBindings Require(ComputeRelease release)=>release.Data.Network.Bindings??throw new InvalidOperationException();
    internal void Validate(ComputeNetwork network)
    {
        if(DatabaseSubnetIds.IsDefaultOrEmpty||DatabaseSubnetIds.Length!=2||DatabaseSubnetIds.Concat(network.SubnetIds).Distinct(StringComparer.Ordinal).Count()!=4)throw new InvalidOperationException();
        foreach(var id in DatabaseSubnetIds)Match(id,"subnet-[a-f0-9]{17}");
        Map(RouteTables,["application","database"],"rtb-[a-f0-9]{17}");Map(SecurityGroups,AdditionalGroups,"sg-[a-f0-9]{17}");
        if(SecurityGroups.Values.Concat(network.SecurityGroups.Values).Distinct(StringComparer.Ordinal).Count()!=12)throw new InvalidOperationException();
        Map(Endpoints,EndpointNames,"vpce-[a-f0-9]{17}");
        if(EndpointPolicySha256.Count!=EndpointNames.Length||EndpointNames.Any(k=>!EndpointPolicySha256.ContainsKey(k)))throw new InvalidOperationException();
        foreach(var hash in EndpointPolicySha256.Values)Match(hash,"[a-f0-9]{64}");
        Match(S3PrefixListId,"pl-([a-f0-9]{8}|[a-f0-9]{17})");
    }
    private static void Map(ImmutableDictionary<string,string> values,string[] keys,string pattern)
    {
        if(values.Count!=keys.Length||keys.Any(k=>!values.ContainsKey(k))||values.Values.Distinct(StringComparer.Ordinal).Count()!=keys.Length)throw new InvalidOperationException();
        foreach(var value in values.Values)Match(value,pattern);
    }
}
