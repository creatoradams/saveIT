using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Amazon.ElasticFileSystem;
using Amazon.ElasticFileSystem.Model;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

public static class ComputeEfsAcquisition
{
    public const string Success = "SaveIT EFS attachment metadata passed (mounts, policy enforcement and recovery unverified).";
    public const string Failure = "SaveIT EFS attachment acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Efs, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct)
    {
        try
        {
            var binding = ComputeNetworkBindings.Require(release);
            var routes = await AcquiredComputeRoutes.ReadAsync(release, session, transport, trust, ct);
            var runtime = release.Data.Runtime;
            _ = await Read(ComputeSdkRead.EfsAccessPoint(runtime.AccessPointId),
                sdk => sdk.DescribeAccessPointsAsync(new DescribeAccessPointsRequest { AccessPointId = runtime.AccessPointId }, ct),
                root => { CheckAccessPoint(release, root); return true; });
            var targets = await Read(ComputeSdkRead.EfsMountTargets(runtime.FileSystemId),
                sdk => sdk.DescribeMountTargetsAsync(new DescribeMountTargetsRequest { FileSystemId = runtime.FileSystemId }, ct),
                root => CheckMountTargets(release, routes.Subnets, root));
            foreach (var id in targets)
                _ = await Read(ComputeSdkRead.EfsMountGroups(id),
                    sdk => sdk.DescribeMountTargetSecurityGroupsAsync(new DescribeMountTargetSecurityGroupsRequest { MountTargetId = id }, ct),
                    root =>
                    {
                        var groups = root.GetProperty("SecurityGroups");
                        if (groups.GetArrayLength() != 1 || groups[0].GetString() != binding.SecurityGroups["session-keys"])
                            throw new InvalidOperationException();
                        return true;
                    });

            async Task<T> Read<T>(ComputeSdkRead descriptor, Func<AmazonElasticFileSystemClient, Task> call, Func<JsonElement, T> check)
            {
                ComputeEc2Acquisition.Fresh(release, session, ct);
                using var wire = new ComputeAwsTransport(transport(descriptor.Endpoint), release.Scope, descriptor, ct);
                using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = new AmazonElasticFileSystemClient(session.Credentials, ComputeSdkConfiguration.Efs(release.Scope.Region, http));
                await call(sdk);
                var result = check(wire.ResponseJson);
                ComputeEc2Acquisition.Fresh(release, session, ct);
                return result;
            }
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static void CheckAccessPoint(ComputeRelease release, JsonElement root)
    {
        Complete(root, "NextToken");
        var points = root.GetProperty("AccessPoints");
        if (points.GetArrayLength() != 1) throw new InvalidOperationException();
        var point = points[0];
        var runtime = release.Data.Runtime;
        var scope = release.Scope;
        if (point.EnumerateObject().Any(p => p.Name is not ("AccessPointArn" or "AccessPointId" or "FileSystemId" or
            "OwnerId" or "LifeCycleState" or "PosixUser" or "RootDirectory" or "ClientToken" or "Name" or "Tags")) ||
            point.GetProperty("AccessPointId").GetString() != runtime.AccessPointId ||
            point.GetProperty("AccessPointArn").GetString() != $"arn:aws:elasticfilesystem:{scope.Region}:{scope.AccountId}:access-point/{runtime.AccessPointId}" ||
            point.GetProperty("FileSystemId").GetString() != runtime.FileSystemId ||
            point.GetProperty("OwnerId").GetString() != scope.AccountId ||
            point.GetProperty("LifeCycleState").GetString() != "available") throw new InvalidOperationException();
        var user = point.GetProperty("PosixUser");
        if (user.EnumerateObject().Any(p => p.Name is not ("Uid" or "Gid" or "SecondaryGids")) ||
            user.GetProperty("Uid").GetInt64() != 1654 || user.GetProperty("Gid").GetInt64() != 1654 ||
            (user.TryGetProperty("SecondaryGids", out var extra) && extra.GetArrayLength() != 0))
            throw new InvalidOperationException();
        var directory = Object(point.GetProperty("RootDirectory"), "Path", "CreationInfo");
        var creation = Object(directory["CreationInfo"], "OwnerUid", "OwnerGid", "Permissions");
        if (directory["Path"].GetString() != "/saveit-session-keys" || creation["OwnerUid"].GetInt64() != 1654 ||
            creation["OwnerGid"].GetInt64() != 1654 || creation["Permissions"].GetString() != "0700")
            throw new InvalidOperationException();
    }

    internal static string[] CheckMountTargets(ComputeRelease release,
        IReadOnlyDictionary<string, ComputeSubnetObservation> subnets, JsonElement root)
    {
        Complete(root, "NextMarker");
        var targets = root.GetProperty("MountTargets");
        if (targets.GetArrayLength() != 2) throw new InvalidOperationException();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var interfaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets.EnumerateArray())
        {
            var fields = Object(target, "MountTargetId", "FileSystemId", "OwnerId", "VpcId", "SubnetId",
                "LifeCycleState", "NetworkInterfaceId", "AvailabilityZoneName", "AvailabilityZoneId", "IpAddress");
            var id = fields["MountTargetId"].GetString() ?? "";
            var eni = fields["NetworkInterfaceId"].GetString() ?? "";
            Match(id, "fsmt-[a-f0-9]{8,40}");
            Match(eni, "eni-[a-f0-9]{17}");
            var subnetId = fields["SubnetId"].GetString() ?? "";
            if (!release.Data.Network.SubnetIds.Contains(subnetId, StringComparer.Ordinal) ||
                !subnets.TryGetValue(subnetId, out var subnet) || !ids.TryAdd(subnetId, id) || !interfaces.Add(eni) ||
                fields["FileSystemId"].GetString() != release.Data.Runtime.FileSystemId ||
                fields["OwnerId"].GetString() != release.Scope.AccountId ||
                fields["VpcId"].GetString() != release.Data.Network.VpcId ||
                fields["LifeCycleState"].GetString() != "available" ||
                fields["AvailabilityZoneName"].GetString() != subnet.Zone ||
                fields["AvailabilityZoneId"].GetString() != subnet.ZoneId)
                throw new InvalidOperationException();
            var address = fields["IpAddress"].GetString();
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                ip.ToString() != address || !IPNetwork.Parse(subnet.Cidr).Contains(ip) ||
                ip.GetAddressBytes()[3] is < 4 or 255) throw new InvalidOperationException();
        }
        if (ids.Values.Distinct(StringComparer.Ordinal).Count() != 2) throw new InvalidOperationException();
        return release.Data.Network.SubnetIds.Select(id => ids[id]).ToArray();
    }

    private static void Complete(JsonElement root, string marker)
    {
        if (root.TryGetProperty(marker, out var next) && next.GetString() is not (null or "")) throw new InvalidOperationException();
    }
}
