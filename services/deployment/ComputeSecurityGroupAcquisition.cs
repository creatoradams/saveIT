using Amazon.EC2;
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
            Fresh(release,session,deadline.Token);
            using var wire=new ComputeAwsTransport(transport("ec2"),release.Scope,ComputeSdkRead.SecurityGroups(release),deadline.Token);
            using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
            using var sdk=new AmazonEC2Client(session.Credentials,ComputeSdkConfiguration.Ec2(release.Scope.Region,http));
            _=await sdk.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest{Filters=[new Filter{Name="vpc-id",Values=[release.Data.Network.VpcId]}],MaxResults=1000},deadline.Token);
            ComputeSecurityGroups.Validate(release,wire.ResponseXml??throw new InvalidOperationException());Fresh(release,session,deadline.Token);
            return new(configuration);
        }
        catch(Exception){throw new InvalidOperationException(ComputeSecurityGroupAcquisition.Failure);}
    }
    private static void Fresh(ComputeRelease release,ComputeAwsSession session,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();session.RequireFresh();if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();}
}
public static class ComputeSecurityGroupAcquisition
{
    public const string Success="SaveIT acquired security-group graph passed (no route, DNS, attachment, launch or fencing verification).";
    public const string Failure="SaveIT compute security-group acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {try{await ComputeTaskAcquisition.AcquireAsync(input,ComputeAcquisitionKind.SecurityGroups,ct);}catch(Exception){throw new InvalidOperationException(Failure);}}
}
