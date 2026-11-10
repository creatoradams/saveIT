using System.Text.Json;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

public static class ComputeReleasePreflight
{
    public const string Success="SaveIT compute release admission passed (supplied observations only; no launch or fencing).";
    public const string Failure="SaveIT compute release admission failed.";

    public static async Task ValidateAsync(Stream input,CancellationToken ct=default) => _=await ReadAsync(input,ct);

    internal static async Task<ComputeRelease> ReadAsync(Stream input,CancellationToken ct=default)
    {
        try
        {
            string Required(string name)=>Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException();
            var scope=new ComputeScope(Required("SAVEIT_COMPUTE_ACCOUNT_ID"),Required("SAVEIT_COMPUTE_REGION"),Required("SAVEIT_COMPUTE_CELL"),Required("SAVEIT_COMPUTE_STAGE"));
            var pin=Required("SAVEIT_COMPUTE_RELEASE_KEY_SHA256");
            using var file=new FileStream(Required("SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE"),FileMode.Open,FileAccess.Read,FileShare.Read);
            if(file.Length is <1 or >2048) throw new InvalidOperationException();
            var bytes=new byte[2049];var count=0;
            while(count<bytes.Length) { var read=await file.ReadAsync(bytes.AsMemory(count),ct); if(read==0) break;count+=read; }
            if(count is <1 or >2048) throw new InvalidOperationException();
            return await ValidateAsync(input,scope,bytes.AsMemory(0,count),pin,ct);
        }
        catch(Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task<ComputeRelease> ValidateAsync(Stream input,ComputeScope scope,ReadOnlyMemory<byte> publicSpki,string pin,CancellationToken ct=default)
    {
        try
        {
            var envelope=await ProtectedJsonInput.ReadAsync(input,24,root=> {
                RejectDuplicates(root); Object(root,"signedRelease","observations"); return root.Clone();
            },ct);
            var release=await ComputeRelease.VerifyAsync(envelope.GetProperty("signedRelease").GetString()!,scope,publicSpki,pin,ct);
            var observations=Object(envelope.GetProperty("observations"),ComputeRelease.Processes);
            foreach(var process in ComputeRelease.Processes) { ct.ThrowIfCancellationRequested(); ComputeTaskContract.Validate(release,process,observations[process]); }
            // Approval must still be fresh after bounded original-input and observation work.
            if(release.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidOperationException();
            return release;
        }
        catch(Exception) { throw new InvalidOperationException(Failure); }
    }
}
