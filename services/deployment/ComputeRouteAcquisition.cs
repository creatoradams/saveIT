using Amazon.EC2.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

internal sealed class AcquiredComputeRoutes
{
    internal AcquiredComputeSecurityGroups SecurityGroups { get; }
    internal ComputeVpcObservation Vpc { get; }
    internal IReadOnlyDictionary<string, ComputeSubnetObservation> Subnets { get; }
    private AcquiredComputeRoutes(AcquiredComputeSecurityGroups groups, ComputeVpcObservation vpc,
        IReadOnlyDictionary<string, ComputeSubnetObservation> subnets) =>
        (SecurityGroups, Vpc, Subnets) = (groups, vpc, subnets);

    internal static async Task<AcquiredComputeRoutes> ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct, TimeProvider? lastReadClock = null)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(150));
            var token = deadline.Token;
            var groups = await AcquiredComputeSecurityGroups.ReadAsync(release, session, transport, trust, token);
            var id = release.Data.Network.VpcId;
            List<Filter> VpcFilter() => [new Filter { Name = "vpc-id", Values = [id] }];
            var vpc = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.Vpc(release),
                (sdk, read) => sdk.DescribeVpcsAsync(new DescribeVpcsRequest { VpcIds = [id] }, read),
                xml => ComputeRouteContract.Vpc(release, xml), token);
            foreach (var hostnames in new[] { false, true })
            {
                _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.VpcDns(release, hostnames),
                    (sdk, read) => sdk.DescribeVpcAttributeAsync(new DescribeVpcAttributeRequest { VpcId = id, Attribute = ComputeSdkRead.DnsAttributeName(hostnames) }, read),
                    xml => { ComputeRouteContract.DnsAttribute(release, hostnames, xml); return true; }, token);
            }
            var subnets = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.Subnets(release),
                (sdk, read) => sdk.DescribeSubnetsAsync(new DescribeSubnetsRequest { Filters = VpcFilter(), MaxResults = 1000 }, read),
                xml => ComputeRouteContract.Subnets(release, vpc.Cidr, xml), token);
            _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.RouteTables(release),
                (sdk, read) => sdk.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = VpcFilter(), MaxResults = 100 }, read),
                xml => { ComputeRouteContract.Routes(release, vpc.Cidr, xml); return true; }, token, lastReadClock);
            return new(groups, vpc, subnets);
        }
        catch (Exception) { throw new InvalidOperationException(ComputeRouteAcquisition.Failure); }
    }
}

public static class ComputeRouteAcquisition
{
    public const string Success = "SaveIT acquired private route topology passed (no endpoint policy, attachment, launch or fencing verification).";
    public const string Failure = "SaveIT compute route acquisition failed.";
    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Routes, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }
}
