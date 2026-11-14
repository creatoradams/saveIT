using System.Text.Json;
using Amazon.Route53Resolver;
using Amazon.Route53Resolver.Model;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

public static class ComputeResolverAcquisition
{
    public const string Success = "SaveIT acquired direct Resolver baseline passed (DNS filtering, Profiles, live answers and launch unverified).";
    public const string Failure = "SaveIT compute Resolver acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Resolver, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(200));
            var token = deadline.Token;
            await ComputeNetworkAcquisition.ReadAsync(release, session, transport, trust, token);
            var vpc = release.Data.Network.VpcId;
            await Read(ComputeSdkRead.ResolverRules(vpc), (sdk, read) => sdk.ListResolverRuleAssociationsAsync(
                new ListResolverRuleAssociationsRequest { Filters = [new Filter { Name = "VPCId", Values = [vpc] }], MaxResults = 100 }, read),
                root => CheckRules(root, vpc));
            await Read(ComputeSdkRead.FirewallGroups(vpc), (sdk, read) => sdk.ListFirewallRuleGroupAssociationsAsync(
                new ListFirewallRuleGroupAssociationsRequest { VpcId = vpc, MaxResults = 100 }, read), CheckGroups);
            await Read(ComputeSdkRead.FirewallConfig(vpc), (sdk, read) => sdk.GetFirewallConfigAsync(
                new GetFirewallConfigRequest { ResourceId = vpc }, read),
                root => CheckConfig(root, vpc, release.Scope.AccountId));

            async Task Read(ComputeSdkRead descriptor, Func<AmazonRoute53ResolverClient, CancellationToken, Task> request,
                Action<JsonElement> check)
            {
                ComputeEc2Acquisition.Fresh(release, session, token);
                using var wire = new ComputeAwsTransport(transport(descriptor.Endpoint), release.Scope, descriptor, token);
                using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = new AmazonRoute53ResolverClient(session.Credentials, ComputeSdkConfiguration.Resolver(release.Scope.Region, http));
                await request(sdk, token);
                check(wire.ResponseJson);
                ComputeEc2Acquisition.Fresh(release, session, token);
            }
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static void CheckRules(JsonElement root, string vpc)
    {
        CompletePage(root);
        if (root.TryGetProperty("MaxResults", out var maximum) && maximum.GetInt32() != 100)
            throw new InvalidOperationException();
        var associations = root.GetProperty("ResolverRuleAssociations");
        if (associations.GetArrayLength() != 1) throw new InvalidOperationException();
        var rule = associations[0];
        if (rule.EnumerateObject().Any(p => p.Name is not ("Id" or "Name" or "ResolverRuleId" or "Status" or "StatusMessage" or "VPCId")) ||
            rule.GetProperty("Id").GetString() != $"rslvr-autodefined-assoc-{vpc}-internet-resolver" ||
            rule.GetProperty("ResolverRuleId").GetString() != "rslvr-autodefined-rr-internet-resolver" ||
            rule.GetProperty("Name").GetString() != "System Rule Association" ||
            rule.GetProperty("VPCId").GetString() != vpc || rule.GetProperty("Status").GetString() != "COMPLETE" ||
            (rule.TryGetProperty("StatusMessage", out var message) && message.GetString() != ""))
            throw new InvalidOperationException();
    }

    internal static void CheckGroups(JsonElement root)
    {
        CompletePage(root);
        if (root.GetProperty("FirewallRuleGroupAssociations").GetArrayLength() != 0)
            throw new InvalidOperationException();
    }

    internal static void CheckConfig(JsonElement root, string vpc, string account)
    {
        var fields = Object(root.GetProperty("FirewallConfig"), "Id", "ResourceId", "OwnerId", "FirewallFailOpen");
        Match(fields["Id"].GetString() ?? "", "rslvr-fc-[a-z0-9]{1,55}");
        if (fields["ResourceId"].GetString() != vpc || fields["OwnerId"].GetString() != account ||
            fields["FirewallFailOpen"].GetString() != "DISABLED") throw new InvalidOperationException();
    }

    private static void CompletePage(JsonElement root)
    {
        if (root.TryGetProperty("NextToken", out var token) && token.GetString() != "")
            throw new InvalidOperationException();
    }
}
