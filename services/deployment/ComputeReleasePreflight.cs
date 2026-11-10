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
            var trust=await ComputeReleaseTrust.ReadAsync(ct);
            return await ValidateAsync(input,trust.Scope,trust.PublicSpki,trust.Pin,ct);
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
