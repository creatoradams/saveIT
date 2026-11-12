using Amazon.EC2.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

public static class ComputeNetworkAcquisition
{
    public const string Success = "SaveIT acquired DHCP and endpoint interfaces passed (no Resolver policy, packet, launch or fencing verification).";
    public const string Failure = "SaveIT compute network acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Network, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(180));
            var token = deadline.Token;
            var endpoints = await ComputeEndpointAcquisition.ReadAsync(release, session, transport, trust, token);
            var vpc = endpoints.Routes.Vpc;
            _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.Dhcp(vpc),
                (sdk, read) => sdk.DescribeDhcpOptionsAsync(new DescribeDhcpOptionsRequest { DhcpOptionsIds = [vpc.DhcpOptionsId] }, read),
                xml => { ComputeNetworkObservation.Dhcp(release.Scope, vpc, xml); return true; }, token);
            _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.EndpointInterfaces(release),
                (sdk, read) => sdk.DescribeNetworkInterfacesAsync(new DescribeNetworkInterfacesRequest
                {
                    Filters = [
                        new Filter { Name = "vpc-id", Values = [release.Data.Network.VpcId] },
                        new Filter { Name = "interface-type", Values = ["vpc_endpoint"] }
                    ],
                    MaxResults = 100
                }, read),
                xml => { ComputeNetworkObservation.Interfaces(release, endpoints.Routes.Subnets, endpoints.Interfaces, xml); return true; }, token);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }
}
