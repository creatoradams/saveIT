using Amazon.ECS;
using Amazon.ECS.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

internal sealed class AcquiredParkedComputeCell
{
    internal AcquiredComputeTasks Tasks {get;}
    private AcquiredParkedComputeCell(AcquiredComputeTasks tasks)=>Tasks=tasks;
    internal static async Task<AcquiredParkedComputeCell> ReadAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,CancellationToken ct)
    {
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var tasks=await AcquiredComputeTasks.ReadAsync(release,session,transport,deadline.Token);
            foreach(var read in new[]{ComputeSdkRead.Cluster(release.Scope),ComputeSdkRead.Services(release.Scope)})
            {
                session.RequireFresh();
                using var wire=new ComputeAwsTransport(transport("ecs"),release.Scope,read,deadline.Token);
                using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
                using var sdk=new AmazonECSClient(session.Credentials,ComputeSdkConfiguration.Ecs(release.Scope.Region,http));
                if(read.Action=="DescribeClusters") {
                    _=await sdk.DescribeClustersAsync(new DescribeClustersRequest{Clusters=[ComputeSdkRead.ClusterArn(release.Scope)],Include=["ATTACHMENTS","CONFIGURATIONS","SETTINGS","TAGS"]},deadline.Token);
                    ComputeParkedContract.Cluster(release,wire.ResponseJson);
                } else {
                    _=await sdk.DescribeServicesAsync(new DescribeServicesRequest{Cluster=ComputeSdkRead.ClusterArn(release.Scope),Services=ComputeRelease.Processes.Select(p=>ComputeSdkRead.ServiceArn(release.Scope,p)).ToList(),Include=["TAGS"]},deadline.Token);
                    ComputeParkedContract.Services(release,wire.ResponseJson);
                }
                deadline.Token.ThrowIfCancellationRequested();session.RequireFresh();if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();
            }
            deadline.Token.ThrowIfCancellationRequested();session.RequireFresh();if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();
            return new(tasks);
        }
        catch(Exception){throw new InvalidOperationException(ComputeCellAcquisition.Failure);}
    }
}
public static class ComputeCellAcquisition
{
    public const string Success="SaveIT acquired parked cell metadata passed (no ownership handoff, launch or fencing).";
    public const string Failure="SaveIT compute cell acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {try{await ComputeTaskAcquisition.AcquireAsync(input,ComputeAcquisitionKind.ParkedCell,ct);}catch(Exception){throw new InvalidOperationException(Failure);}}
}
