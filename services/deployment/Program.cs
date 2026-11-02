using SaveIT.Deployment;
using Npgsql;

namespace SaveIT.Deployment;

internal static class DeploymentProgram
{
public static async Task<int> Main(string[] args)
{
try
{
    if (args is ["access", var operation, var accessFile] && operation is "apply" or "inspect")
    {
        var accessConnection = Environment.GetEnvironmentVariable("SAVEIT_ACCESS_CONNECTION")
            ?? throw new InvalidOperationException("Separate access operator connection is required.");
        var manifest = await DeploymentRunner.ReadManifestAsync(accessFile);
        var local = Environment.GetEnvironmentVariable("SAVEIT_ACCESS_ALLOW_LOOPBACK") == "true";
        if (operation == "inspect") Console.WriteLine(await AccessRunner.InspectAsync(accessConnection, manifest, local));
        else Console.WriteLine($"SaveIT access operation committed at version {await AccessRunner.ChangeAsync(accessConnection, manifest, local)}.");
        return 0;
    }
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
catch (PostgresException e) when (args.FirstOrDefault() == "access" && e.SqlState is "55P03" or "40001")
{
    Console.Error.WriteLine(e.SqlState == "55P03"
        ? "SaveIT access operation timed out and rolled back. Inspect pending responses; uncertain instances require privileged fencing/recovery."
        : "SaveIT access version changed. Inspect current access before preparing another operation.");
    return 1;
}
catch (Exception)
{
    // Provider exception messages can contain connection details, SQL or identity data.
    Console.Error.WriteLine("SaveIT deployment operation failed. Check the protected configuration, role, schema and operation prerequisites.");
    return 1;
}
}
}
