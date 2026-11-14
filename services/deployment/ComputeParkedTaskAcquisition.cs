using System.Text.Json;
using Amazon.ECS;
using Amazon.ECS.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

public static class ComputeParkedTaskAcquisition
{
    public const string Success = "SaveIT parked task inventory passed (no task termination or fencing verification).";
    public const string Failure = "SaveIT parked task inventory failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.ParkedTasks, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, CancellationToken ct)
    {
        try
        {
            _ = await AcquiredParkedComputeCell.ReadAsync(release, session, transport, ct);
            ComputeEc2Acquisition.Fresh(release, session, ct);
            using var wire = new ComputeAwsTransport(transport("ecs"), release.Scope, ComputeSdkRead.ParkedTasks(release.Scope), ct);
            using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
            using var sdk = new AmazonECSClient(session.Credentials, ComputeSdkConfiguration.Ecs(release.Scope.Region, http));
            _ = await sdk.ListTasksAsync(new ListTasksRequest
            {
                Cluster = ComputeSdkRead.ClusterArn(release.Scope),
                DesiredStatus = DesiredStatus.RUNNING,
                MaxResults = 100
            }, ct);
            var response = wire.ResponseJson;
            if (response.GetProperty("taskArns").GetArrayLength() != 0 ||
                (response.TryGetProperty("nextToken", out var next) &&
                    next.ValueKind != JsonValueKind.Null && (next.ValueKind != JsonValueKind.String || next.GetString() != "")))
                throw new InvalidOperationException();
            ComputeEc2Acquisition.Fresh(release, session, ct);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }
}
