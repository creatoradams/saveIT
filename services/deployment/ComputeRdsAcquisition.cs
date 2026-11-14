using System.Xml.Linq;
using Amazon.RDS;
using Amazon.RDS.Model;
using static SaveIT.Deployment.ComputeQueryXml;

namespace SaveIT.Deployment;

public static class ComputeRdsAcquisition
{
    internal static readonly XNamespace XmlNamespace = "http://rds.amazonaws.com/doc/2014-10-31/";
    public const string Success = "SaveIT private RDS metadata passed (database TLS, roles and recovery unverified).";
    public const string Failure = "SaveIT private RDS acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Database, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, ComputeConfigurationTrust trust, CancellationToken ct)
    {
        try
        {
            _ = ComputeNetworkBindings.Require(release);
            var routes = await AcquiredComputeRoutes.ReadAsync(release, session, transport, trust, ct);
            ComputeEc2Acquisition.Fresh(release, session, ct);
            using var wire = new ComputeAwsTransport(transport("rds"), release.Scope, ComputeSdkRead.Database(release.Scope), ct);
            using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
            using var sdk = new AmazonRDSClient(session.Credentials, ComputeSdkConfiguration.Rds(release.Scope.Region, http));
            _ = await sdk.DescribeDBInstancesAsync(new DescribeDBInstancesRequest
                { DBInstanceIdentifier = ComputeSdkRead.DatabaseArn(release.Scope) }, ct);
            Check(release, routes.SecurityGroups.Configuration.Database, routes.Subnets,
                wire.ResponseXml ?? throw new InvalidOperationException());
            ComputeEc2Acquisition.Fresh(release, session, ct);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static void Check(ComputeRelease release, ProtectedConfiguration.DatabaseTarget target,
        IReadOnlyDictionary<string, ComputeSubnetObservation> subnets, XElement xml)
    {
        var binding = ComputeNetworkBindings.Require(release);
        var root = Fields(xml, ["DescribeDBInstancesResult", "ResponseMetadata"]);
        _ = Text(Fields(root["ResponseMetadata"], ["RequestId"])["RequestId"], 1, 128);
        var result = Fields(root["DescribeDBInstancesResult"], ["DBInstances"]);
        var entry = Items(result["DBInstances"], 1, "DBInstance").Single();
        // RDS returns many unrelated operational fields. Read only the fields used by this check
        // and reject duplicate fields and competing legacy/cluster network associations.
        if (entry.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            throw new InvalidOperationException();
        var db = entry.Elements().ToDictionary(element => element.Name.LocalName, StringComparer.Ordinal);
        string Value(string name) => Text(db[name]);
        if (Value("DBInstanceArn") != ComputeSdkRead.DatabaseArn(release.Scope) ||
            Value("DBInstanceIdentifier") != release.Scope.Name || Value("DBInstanceStatus") != "available" ||
            Value("Engine") != "postgres" || Value("NetworkType") != "IPV4" ||
            Value("PubliclyAccessible") != "false" || Value("DBName") != "saveit" || target.Database != "saveit" ||
            target.Port != 5432 || db.ContainsKey("DBClusterIdentifier")) throw new InvalidOperationException();
        if (db.TryGetValue("DBSecurityGroups", out var legacyGroups)) _ = Items(legacyGroups, 0);
        if (db.TryGetValue("PendingModifiedValues", out var pending)) _ = Fields(pending, []);

        var endpoint = Fields(db["Endpoint"], ["Address", "Port"], "HostedZoneId");
        if (!string.Equals(Text(endpoint["Address"], 1, 253), target.Host, StringComparison.OrdinalIgnoreCase) ||
            Text(endpoint["Port"]) != "5432") throw new InvalidOperationException();
        var group = Fields(Items(db["VpcSecurityGroups"], 1, "VpcSecurityGroupMembership").Single(), ["VpcSecurityGroupId", "Status"]);
        if (Text(group["VpcSecurityGroupId"]) != binding.SecurityGroups["database"] || Text(group["Status"]) != "active")
            throw new InvalidOperationException();

        var subnetGroup = Fields(db["DBSubnetGroup"], ["DBSubnetGroupName", "VpcId", "SubnetGroupStatus", "Subnets"],
            "DBSubnetGroupDescription", "DBSubnetGroupArn", "SupportedNetworkTypes");
        if (Text(subnetGroup["DBSubnetGroupName"]) != release.Scope.Name || Text(subnetGroup["VpcId"]) != release.Data.Network.VpcId ||
            Text(subnetGroup["SubnetGroupStatus"]) != "Complete") throw new InvalidOperationException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subnet in Items(subnetGroup["Subnets"], 2, "Subnet"))
        {
            var fields = Fields(subnet, ["SubnetIdentifier", "SubnetAvailabilityZone", "SubnetStatus"], "SubnetOutpost");
            var id = Text(fields["SubnetIdentifier"]);
            var zone = Fields(fields["SubnetAvailabilityZone"], ["Name"]);
            if (!binding.DatabaseSubnetIds.Contains(id, StringComparer.Ordinal) || !seen.Add(id) ||
                Text(zone["Name"]) != subnets[id].Zone || Text(fields["SubnetStatus"]) != "Active" ||
                fields.ContainsKey("SubnetOutpost")) throw new InvalidOperationException();
        }
        if (seen.Count != 2) throw new InvalidOperationException();
    }
}
