using SaveIT.Deployment;

namespace SaveIT.Deployment;

internal static class DeploymentProgram
{
public static async Task<int> Main(string[] args)
{
try
{
    var connection = Environment.GetEnvironmentVariable("SAVEIT_DEPLOYMENT_CONNECTION")
        ?? throw new InvalidOperationException("Deployment connection is required through protected configuration.");
    var allowLoopback = Environment.GetEnvironmentVariable("SAVEIT_DEPLOYMENT_ALLOW_LOOPBACK") == "true";
    if (args is ["migrate"]) await DeploymentRunner.MigrateAsync(connection, allowLoopback);
    else if (args is ["provision", var file])
    {
        var manifest = await DeploymentRunner.ReadManifestAsync(file);
        await DeploymentRunner.ProvisionAsync(connection, manifest, allowLoopback);
    }
    else throw new InvalidOperationException("Use migrate, or provision <protected manifest path>. No credentials belong in arguments.");
    Console.WriteLine("SaveIT deployment operation completed.");
    return 0;
}
catch (Exception)
{
    // Provider exception messages can contain connection details, SQL or identity data.
    Console.Error.WriteLine("SaveIT deployment operation failed. Check the protected configuration, role, schema and operation prerequisites.");
    return 1;
}
}
}
