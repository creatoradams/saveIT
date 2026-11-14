using System.Xml.Linq;
using Amazon.EC2;

namespace SaveIT.Deployment;

// Both the SDK's signed query and the original XML are bound by a fixed descriptor.
internal static class ComputeEc2Acquisition
{
    internal static async Task<T> ReadAsync<T>(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,
        ComputeSdkRead read,Func<AmazonEC2Client,CancellationToken,Task> call,Func<XElement,T> validate,CancellationToken ct,TimeProvider? timeProvider=null)
    {
        if(!read.Query)throw new InvalidOperationException();
        Fresh(release,session,ct,timeProvider);
        using var wire=new ComputeAwsTransport(transport("ec2"),release.Scope,read,ct);
        using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
        using var sdk=new AmazonEC2Client(session.Credentials,ComputeSdkConfiguration.Ec2(release.Scope.Region,http));
        await call(sdk,ct);
        var result=validate(wire.ResponseXml??throw new InvalidOperationException());
        Fresh(release,session,ct,timeProvider);return result;
    }
    internal static void Fresh(ComputeRelease release,ComputeAwsSession session,CancellationToken ct,TimeProvider? testClock=null)
    {
        ct.ThrowIfCancellationRequested();session.RequireFresh();
        // The internal deterministic test clock can only tighten the real UTC check.
        if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()||testClock is not null&&release.ExpiresAt<=testClock.GetUtcNow().ToUnixTimeSeconds())throw new InvalidOperationException();
    }
}
