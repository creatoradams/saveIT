namespace SaveIT.Deployment;

internal sealed record ComputeReleaseTrust(ComputeScope Scope,ReadOnlyMemory<byte> PublicSpki,string Pin)
{
    internal static async Task<ComputeReleaseTrust> ReadAsync(CancellationToken ct)
    {
        string Required(string name)=>Environment.GetEnvironmentVariable(name)??throw new InvalidOperationException();
        var scope=new ComputeScope(Required("SAVEIT_COMPUTE_ACCOUNT_ID"),Required("SAVEIT_COMPUTE_REGION"),Required("SAVEIT_COMPUTE_CELL"),Required("SAVEIT_COMPUTE_STAGE"));scope.Validate();
        var pin=Required("SAVEIT_COMPUTE_RELEASE_KEY_SHA256");
        using var file=new FileStream(Required("SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE"),FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length is <1 or >2048)throw new InvalidOperationException();
        var bytes=new byte[2049];var count=0;
        while(count<bytes.Length){var read=await file.ReadAsync(bytes.AsMemory(count),ct);if(read==0)break;count+=read;}
        if(count is <1 or >2048)throw new InvalidOperationException();
        return new(scope,bytes.AsMemory(0,count),pin);
    }
}
