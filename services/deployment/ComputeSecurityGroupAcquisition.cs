using Amazon.EC2.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

internal sealed class AcquiredComputeSecurityGroups
{
    internal AcquiredComputeConfiguration Configuration {get;}
    private AcquiredComputeSecurityGroups(AcquiredComputeConfiguration configuration)=>Configuration=configuration;
    internal static async Task<AcquiredComputeSecurityGroups> ReadAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,ComputeConfigurationTrust trust,CancellationToken ct)
    {
        try {
            _=ComputeNetworkBindings.Require(release);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(120));
            var configuration=await AcquiredComputeConfiguration.ReadAsync(release,session,transport,trust,deadline.Token);
            _=await ComputeEc2Acquisition.ReadAsync(release,session,transport,ComputeSdkRead.SecurityGroups(release),
                (sdk,token)=>sdk.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest{Filters=[new Filter{Name="vpc-id",Values=[release.Data.Network.VpcId]}],MaxResults=1000},token),
                xml=>{ComputeSecurityGroups.Validate(release,xml);return true;},deadline.Token);
            return new(configuration);
        }
        catch(Exception){throw new InvalidOperationException(ComputeSecurityGroupAcquisition.Failure);}
    }
}
public static class ComputeSecurityGroupAcquisition
{
    public const string Success="SaveIT acquired security-group graph passed (no route, DNS, attachment, launch or fencing verification).";
    public const string Failure="SaveIT compute security-group acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {try{await ComputeTaskAcquisition.AcquireAsync(input,ComputeAcquisitionKind.SecurityGroups,ct);}catch(Exception){throw new InvalidOperationException(Failure);}}
}
