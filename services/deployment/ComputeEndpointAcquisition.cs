using Amazon.EC2.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

public static class ComputeEndpointAcquisition
{
    public const string Success = "SaveIT acquired private endpoints and S3 prefix passed (no effective policy, attachment, launch or fencing verification).";
    public const string Failure = "SaveIT compute endpoint acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Endpoints, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct)
    {
        try
        {
            var binding = ComputeNetworkBindings.Require(release);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(165));
            var token = deadline.Token;
            _ = await AcquiredComputeRoutes.ReadAsync(release, session, transport, trust, token);
            _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.Endpoints(release),
                (sdk, read) => sdk.DescribeVpcEndpointsAsync(new DescribeVpcEndpointsRequest
                {
                    Filters = [new Filter { Name = "vpc-id", Values = [release.Data.Network.VpcId] }],
                    MaxResults = 100
                }, read),
                xml => { ComputeEndpointContract.Endpoints(release, xml); return true; }, token);
            _ = await ComputeEc2Acquisition.ReadAsync(release, session, transport, ComputeSdkRead.S3Prefix(release),
                (sdk, read) => sdk.DescribeManagedPrefixListsAsync(new DescribeManagedPrefixListsRequest
                {
                    PrefixListIds = [binding.S3PrefixListId]
                }, read),
                xml => { ComputeEndpointContract.S3Prefix(release, xml); return true; }, token);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }
}
