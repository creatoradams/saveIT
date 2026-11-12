using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Amazon.ECR;
using Amazon.ECR.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

internal sealed class AcquiredComputeImages
{
    internal AcquiredParkedComputeCell Cell {get;}
    private AcquiredComputeImages(AcquiredParkedComputeCell cell)=>Cell=cell;
    internal static async Task<AcquiredComputeImages> ReadAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,CancellationToken ct)
    {
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var cell=await AcquiredParkedComputeCell.ReadAsync(release,session,transport,deadline.Token);
            var manifests=new Dictionary<string,ComputeImageManifest>(StringComparer.Ordinal);
            foreach(var process in ComputeRelease.Processes) {
                Fresh(release,session,deadline.Token);
                using var wire=new ComputeAwsTransport(transport("ecr"),release.Scope,ComputeSdkRead.Manifest(release,process),deadline.Token);
                using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
                using var sdk=new AmazonECRClient(session.Credentials,ComputeSdkConfiguration.Ecr(release.Scope.Region,http));
                _=await sdk.BatchGetImageAsync(new BatchGetImageRequest{
                    RegistryId=release.Scope.AccountId,RepositoryName=ComputeSdkRead.Repository(release.Scope,process),
                    ImageIds=[new ImageIdentifier{ImageDigest=release.Data.Tasks[process].Image.Split('@')[1]}],
                    AcceptedMediaTypes=[ComputeImageManifest.DockerMediaType,ComputeImageManifest.OciMediaType]},deadline.Token);
                manifests.Add(process,ComputeImageManifest.Parse(release,process,wire.ResponseJson));Fresh(release,session,deadline.Token);
            }
            ComputeRegistryAuthorization authorization;
            using(var wire=new ComputeAwsTransport(transport("ecr"),release.Scope,ComputeSdkRead.RegistryAuthorization(),deadline.Token))
            using(var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan})
            using(var sdk=new AmazonECRClient(session.Credentials,ComputeSdkConfiguration.Ecr(release.Scope.Region,http))) {
                Fresh(release,session,deadline.Token);
                _=await sdk.GetAuthorizationTokenAsync(new GetAuthorizationTokenRequest(),deadline.Token);
                authorization=ComputeRegistryAuthorization.Parse(release.Scope,wire.ResponseJson);Fresh(release,session,deadline.Token);
            }
            foreach(var process in ComputeRelease.Processes) {
                Fresh(release,session,deadline.Token);authorization.RequireFresh();
                await Configuration(release,process,manifests[process],authorization,transport("registry"),deadline.Token);
                authorization.RequireFresh();Fresh(release,session,deadline.Token);
            }
            return new(cell);
        }
        catch(Exception){throw new InvalidOperationException(ComputeImageAcquisition.Failure);}
    }
    private static void Fresh(ComputeRelease release,ComputeAwsSession session,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();session.RequireFresh();if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();}
    private static async Task Configuration(ComputeRelease release,string process,ComputeImageManifest manifest,ComputeRegistryAuthorization authorization,HttpMessageHandler transport,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var http=new HttpClient(transport){Timeout=Timeout.InfiniteTimeSpan};
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://{ComputeRegistryAuthorization.Host(release.Scope)}/v2/{ComputeSdkRead.Repository(release.Scope,process)}/blobs/{manifest.ConfigurationDigest}"){
            Version=HttpVersion.Version11,VersionPolicy=HttpVersionPolicy.RequestVersionExact};
        request.Headers.ConnectionClose=true;request.Headers.Authorization=new AuthenticationHeaderValue("Basic",authorization.HeaderToken);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentEncoding.Count!=0||response.Headers.Contains("Trailer"))throw new InvalidOperationException();
        if(response.Headers.TryGetValues("Docker-Content-Digest",out var hashes)&&hashes.Single()!=manifest.ConfigurationDigest)throw new InvalidOperationException();
        var bytes=await ComputeAwsTransport.Bounded(response.Content,manifest.ConfigurationSize,deadline.Token);
        try {
            if(response.TrailingHeaders.Any())throw new InvalidOperationException();
            ComputeImageContract.Validate(release,process,manifest,bytes);deadline.Token.ThrowIfCancellationRequested();
        } finally{CryptographicOperations.ZeroMemory(bytes);request.Headers.Authorization=null;}
    }
}
public static class ComputeImageAcquisition
{
    public const string Success="SaveIT acquired image configurations passed (no layer verification, promotion, launch or fencing).";
    public const string Failure="SaveIT compute image acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {try{await ComputeTaskAcquisition.AcquireAsync(input,ComputeAcquisitionKind.Images,ct);}catch(Exception){throw new InvalidOperationException(Failure);}}
}
