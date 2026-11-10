using Amazon.ECS;
using DescribeTaskDefinitionRequest = Amazon.ECS.Model.DescribeTaskDefinitionRequest;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using System.Text.Json;
using System.Text.RegularExpressions;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal sealed class AcquiredComputeTasks
{
    internal ComputeRelease Release {get;}
    internal DateTimeOffset ObservedAt {get;}
    internal string CallerArn {get;}
    private AcquiredComputeTasks(ComputeRelease release,string caller){Release=release;CallerArn=caller;ObservedAt=DateTimeOffset.UtcNow;}
    internal static async Task<AcquiredComputeTasks> ReadAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,CancellationToken ct)
    {
        try{return await ReadCoreAsync(release,session,transport,ct);}
        catch(Exception){throw new InvalidOperationException(ComputeTaskAcquisition.Failure);}
    }
    private static async Task<AcquiredComputeTasks> ReadCoreAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,CancellationToken ct)
    {
        var scope=release.Scope;session.RequireFresh();
        using var global=CancellationTokenSource.CreateLinkedTokenSource(ct);global.CancelAfter(TimeSpan.FromSeconds(30));
        using var identityTransport=new ComputeAwsTransport(transport("sts"),scope,null,global.Token);
        using var identityHttp=new HttpClient(identityTransport,false){Timeout=Timeout.InfiniteTimeSpan};
        using var sts=new AmazonSecurityTokenServiceClient(session.Credentials,ComputeSdkConfiguration.Sts(scope.Region,identityHttp));
        var identity=await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(),global.Token);
        if(identity.Account!=scope.AccountId)throw new InvalidOperationException();
        Match(identity.Arn,Regex.Escape($"arn:aws:sts::{scope.AccountId}:assumed-role/{scope.Name}-compute-controller/")+"[A-Za-z0-9+=,.@_-]{2,64}");
        Match(identity.UserId,"AROA[A-Z0-9]{17}:"+Regex.Escape(identity.Arn[(identity.Arn.LastIndexOf('/')+1)..]));
        foreach(var process in ComputeRelease.Processes)
        {
            session.RequireFresh();var reference=release.Data.Tasks[process];
            using var wire=new ComputeAwsTransport(transport("ecs"),scope,reference.TaskDefinitionArn,global.Token);
            using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
            using var ecs=new AmazonECSClient(session.Credentials,ComputeSdkConfiguration.Ecs(scope.Region,http));
            _=await ecs.DescribeTaskDefinitionAsync(new DescribeTaskDefinitionRequest{TaskDefinition=reference.TaskDefinitionArn,Include=[TaskDefinitionField.TAGS]},global.Token);
            ComputeTaskContract.Validate(release,process,wire.ResponseJson);
        }
        global.Token.ThrowIfCancellationRequested();session.RequireFresh();
        if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();
        return new(release,identity.Arn);
    }
}

public static class ComputeTaskAcquisition
{
    public const string Success="SaveIT acquired task metadata passed (no launch, image/configuration verification or fencing).";
    public const string Failure="SaveIT compute task acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {
        try{await AcquireAsync(input,false,ct);}
        catch(Exception){throw new InvalidOperationException(Failure);}
    }
    internal static async Task AcquireAsync(Stream input,bool parkedCell,CancellationToken ct)
    {
            if(!OperatingSystem.IsLinux())throw new InvalidOperationException();
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var trust=await ComputeReleaseTrust.ReadAsync(deadline.Token);
            using var inputDeadline=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);inputDeadline.CancelAfter(TimeSpan.FromSeconds(10));
            var token=await ProtectedJsonInput.ReadAsync(input,2,root=>{RejectDuplicates(root);return Object(root,"signedRelease")["signedRelease"].GetString()??throw new InvalidOperationException();},inputDeadline.Token);
            var release=await ComputeRelease.VerifyAsync(token,trust.Scope,trust.PublicSpki,trust.Pin,deadline.Token);
            var session=await ComputeAwsSession.ReadAsync(Environment.GetEnvironmentVariable("SAVEIT_COMPUTE_AWS_SESSION_FILE")??throw new InvalidOperationException(),Environment.GetEnvironmentVariable,deadline.Token);
            if(parkedCell)_=await AcquiredParkedComputeCell.ReadAsync(release,session,_=>RelayStartupTransport.Native(),deadline.Token);
            else _=await AcquiredComputeTasks.ReadAsync(release,session,_=>RelayStartupTransport.Native(),deadline.Token);
    }
}
