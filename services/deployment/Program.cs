using SaveIT.Deployment;
using Npgsql;
using Microsoft.Extensions.Hosting;

namespace SaveIT.Deployment;

internal static class DeploymentProgram
{
public static async Task<int> Main(string[] args)
{
if (args.FirstOrDefault() == "check-compute-release")
{
    try
    {
        if(args is not ["check-compute-release"] || !Console.IsInputRedirected) throw new InvalidOperationException();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var input=Console.OpenStandardInput();
        await ComputeReleasePreflight.ValidateAsync(input,timeout.Token);
        Console.WriteLine(ComputeReleasePreflight.Success);return 0;
    }
    catch(Exception) { Console.Error.WriteLine(ComputeReleasePreflight.Failure);return 1; }
}
if (args.FirstOrDefault() == "check-relay-task")
{
    try
    {
        if (args is not ["check-relay-task"] || !Console.IsInputRedirected) throw new InvalidOperationException();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var input = Console.OpenStandardInput();
        await RelayTaskPreflight.ValidateAsync(input, timeout.Token);
        Console.WriteLine(RelayTaskPreflight.Success);
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine(RelayTaskPreflight.Failure); return 1; }
}
if (args.FirstOrDefault() == "probe-api-relay")
    return args is ["probe-api-relay"] ? await RelayLivenessProbe.RunAsync() : 1;
if (args.FirstOrDefault() == "serve-api-relay-secrets")
{
    try
    {
        if (!OperatingSystem.IsLinux() || args is not ["serve-api-relay-secrets", var api, var relay, var arn, var version]) throw new InvalidOperationException();
        using var configuration = await RelaySecretLoader.ReadAsync(api, relay, arn, version);
        await using var app = ApiRelay.Build(configuration, RelayWorkloadAdmission.FromEnvironment());
        await app.StartAsync(); Console.WriteLine(ApiRelayConfiguration.Started);
        await app.WaitForShutdownAsync(); return 0;
    }
    catch (Exception) { Console.Error.WriteLine(ApiRelayConfiguration.Failure); return 1; }
}
if (args.FirstOrDefault() == "serve-api-relay")
{
    try
    {
        if (!OperatingSystem.IsLinux() || args is not ["serve-api-relay", var api, var relay] || !Console.IsInputRedirected) throw new InvalidOperationException();
        using var input = Console.OpenStandardInput();
        using var configuration = await ApiRelayConfiguration.ReadAsync(api, relay, input);
        await using var app = ApiRelay.Build(configuration, RelayWorkloadAdmission.FromEnvironment());
        await app.StartAsync(); Console.WriteLine(ApiRelayConfiguration.Started);
        await app.WaitForShutdownAsync(); return 0;
    }
    catch (Exception) { Console.Error.WriteLine(ApiRelayConfiguration.Failure); return 1; }
}
if (args.FirstOrDefault() == "probe-api-peer")
{
    try
    {
        if (!OperatingSystem.IsLinux() || args is not ["probe-api-peer", var origin] || !Console.IsInputRedirected) throw new InvalidOperationException();
        using var input = Console.OpenStandardInput();
        await ApiPeerProbe.RunAsync(origin, input);
        Console.WriteLine(ApiPeerProbe.Success);
        return 0;
    }
    catch (Exception) { Console.Error.WriteLine(ApiPeerProbe.Failure); return 1; }
}
if (args.FirstOrDefault() == "check-configuration")
{
    try
    {
        if (args is not ["check-configuration", var workload, var retired] || retired.Length != 1 || retired[0] is < '0' or > '4' || !Console.IsInputRedirected)
            throw new InvalidOperationException();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var input = Console.OpenStandardInput();
        await ProtectedConfiguration.ValidateAsync(input, workload, retired[0] - '0', timeout.Token);
        Console.WriteLine(ProtectedConfiguration.Success);
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine(ProtectedConfiguration.Failure);
        return 1;
    }
}
try
{
    if (args.FirstOrDefault()=="compute")
    {
        var computeConnection = SaveIT.Search.PublicTrust.DatabaseConnection(Environment.GetEnvironmentVariable("SAVEIT_COMPUTE_CONNECTION") ?? throw new InvalidOperationException(),Environment.GetEnvironmentVariable("Deployment__Trust__DatabaseCaSha256"));
        var localCompute = Environment.GetEnvironmentVariable("SAVEIT_COMPUTE_ALLOW_LOOPBACK")=="true";
        if (args is ["compute","inspect"]) Console.WriteLine(await ComputeRunner.InspectAsync(computeConnection,localCompute));
        else if(args is ["compute","inspect-release",var releaseOperation]) Console.WriteLine(await ComputeRunner.InspectReleaseAsync(computeConnection,ComputeRelease.CanonicalGuid(releaseOperation),localCompute));
        else if(args is ["compute","admit-release"] && Console.IsInputRedirected) {
            using var admissionDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var releaseInput=Console.OpenStandardInput();
            var release=await ComputeReleasePreflight.ReadAsync(releaseInput,admissionDeadline.Token);
            var attribution=Environment.GetEnvironmentVariable("SAVEIT_COMPUTE_OPERATOR")??throw new InvalidOperationException();
            var admitted=await ComputeRunner.AdmitReleaseAsync(computeConnection,release,attribution,localCompute,admissionDeadline.Token);
            Console.WriteLine($"SaveIT compute release intent committed at version {admitted} (supplied observations; no provider actions).");
        }
        else if (args is ["compute","change",var computeFile]) {
            var computeManifest = await DeploymentRunner.ReadManifestAsync(computeFile);
            Console.WriteLine($"SaveIT compute decision committed at version {await ComputeRunner.ChangeAsync(computeConnection,computeManifest,localCompute)} (no provider fencing evidence).");
        }
        else throw new InvalidOperationException();
        return 0;
    }
    if (args is ["access", var operation, var accessFile] && operation is "apply" or "inspect")
    {
        var accessConnection = SaveIT.Search.PublicTrust.DatabaseConnection(Environment.GetEnvironmentVariable("SAVEIT_ACCESS_CONNECTION")
            ?? throw new InvalidOperationException("Separate access operator connection is required."), Environment.GetEnvironmentVariable("Deployment__Trust__DatabaseCaSha256"));
        var manifest = await DeploymentRunner.ReadManifestAsync(accessFile);
        var local = Environment.GetEnvironmentVariable("SAVEIT_ACCESS_ALLOW_LOOPBACK") == "true";
        if (operation == "inspect") Console.WriteLine(await AccessRunner.InspectAsync(accessConnection, manifest, local));
        else Console.WriteLine($"SaveIT access operation committed at version {await AccessRunner.ChangeAsync(accessConnection, manifest, local)}.");
        return 0;
    }
    var connection = SaveIT.Search.PublicTrust.DatabaseConnection(Environment.GetEnvironmentVariable("SAVEIT_DEPLOYMENT_CONNECTION")
        ?? throw new InvalidOperationException("Deployment connection is required through protected configuration."), Environment.GetEnvironmentVariable("Deployment__Trust__DatabaseCaSha256"));
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
