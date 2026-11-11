using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Task = System.Threading.Tasks.Task;

namespace SaveIT.Deployment;

internal static class ComputeConfigurationEnvelope
{
    // Validate the original raw envelope, not the SDK's lossy response model.
    internal static byte[] Read(ComputeRelease release,string process,JsonElement root)
    {
        if(process is not("api" or "worker" or "relay"))throw new InvalidOperationException();
        ComputeSdkRead.Configuration(release,process).ValidateResponse(root);
        var reference=release.Data.Tasks[process];
        if(root.GetProperty("ARN").GetString()!=reference.ConfigurationArn ||
            root.GetProperty("Name").GetString()!=$"{release.Scope.Path}/{process}/config" ||
            root.GetProperty("VersionId").GetString()!=reference.ConfigurationVersionId)throw new InvalidOperationException();
        if(root.TryGetProperty("CreatedDate",out var created)&&(!created.TryGetDouble(out var date)||!double.IsFinite(date)||date<=0||date>DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60))throw new InvalidOperationException();
        if(root.TryGetProperty("VersionStages",out var stages)) {
            if(stages.ValueKind!=JsonValueKind.Array||stages.GetArrayLength() is <1 or >20)throw new InvalidOperationException();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var stage in stages.EnumerateArray()) {
                var value=stage.GetString()??throw new InvalidOperationException();
                if(value.Length is <1 or >256||value.Any(c=>c<32||c==127)||!seen.Add(value))throw new InvalidOperationException();
            }
        }
        var payload=root.GetProperty("SecretString").GetString()??throw new InvalidOperationException();
        var utf8=new UTF8Encoding(false,true);var count=utf8.GetByteCount(payload);
        if(count is <1 or >ProtectedJsonInput.MaximumBytes)throw new InvalidOperationException();
        return utf8.GetBytes(payload);
    }
}

internal sealed class AcquiredComputeConfiguration
{
    internal AcquiredComputeImages Images {get;}
    private AcquiredComputeConfiguration(AcquiredComputeImages images)=>Images=images;
    internal static async Task<AcquiredComputeConfiguration> ReadAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,ComputeConfigurationTrust trust,CancellationToken ct)
    {
        var payloads=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        try {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(110));
            var images=await AcquiredComputeImages.ReadAsync(release,session,transport,deadline.Token);
            await ValidateAsync(release,session,transport,trust,payloads,deadline.Token);
            return new(images);
        }
        catch(Exception){throw new InvalidOperationException(ComputeConfigurationAcquisition.Failure);}
        finally{foreach(var bytes in payloads.Values)CryptographicOperations.ZeroMemory(bytes);payloads.Clear();}
    }
    // The acquisition result retains only checked public metadata. Private values
    // are neither returned, written to disk nor accepted as launch overrides.
    private static async Task ValidateAsync(ComputeRelease release,ComputeAwsSession session,Func<string,HttpMessageHandler> transport,ComputeConfigurationTrust trust,Dictionary<string,byte[]> payloads,CancellationToken ct)
    {
        foreach(var process in new[]{"api","worker","relay"}) {
            Fresh(release,session,ct);var reference=release.Data.Tasks[process];
            using var wire=new ComputeAwsTransport(transport("secretsmanager"),release.Scope,ComputeSdkRead.Configuration(release,process),ct);
            using var http=new HttpClient(wire,false){Timeout=Timeout.InfiniteTimeSpan};
            using var sdk=new AmazonSecretsManagerClient(session.Credentials,ComputeSdkConfiguration.Secrets(release.Scope.Region,http));
            _=await sdk.GetSecretValueAsync(new GetSecretValueRequest{SecretId=reference.ConfigurationArn,VersionId=reference.ConfigurationVersionId},ct);
            payloads.Add(process,ComputeConfigurationEnvelope.Read(release,process,wire.ResponseJson));Fresh(release,session,ct);
        }
        await ComputeConfigurationCohort.ValidateAsync(release,payloads,trust,ct);Fresh(release,session,ct);
    }
    private static void Fresh(ComputeRelease release,ComputeAwsSession session,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();session.RequireFresh();if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidOperationException();}
}

public static class ComputeConfigurationAcquisition
{
    public const string Success="SaveIT acquired configuration cohort passed (no live database, identity, launch or fencing verification).";
    public const string Failure="SaveIT compute configuration acquisition failed.";
    public static async Task ValidateAsync(Stream input,CancellationToken ct=default)
    {try{await ComputeTaskAcquisition.AcquireAsync(input,ComputeAcquisitionKind.Configuration,ct);}catch(Exception){throw new InvalidOperationException(Failure);}}
}
