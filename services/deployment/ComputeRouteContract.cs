using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static SaveIT.Deployment.ComputeQueryXml;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal sealed record ComputeVpcObservation(string Cidr, string DhcpOptionsId);
internal sealed record ComputeSubnetObservation(string Cidr, string Zone, string ZoneId);

// Initial parked-cell routing only. Endpoint/Resolver policy, attachments and
// an atomic view of the control plane require separate checks.
internal static class ComputeRouteContract
{
    internal static ComputeVpcObservation Vpc(ComputeRelease release, XElement response)
    {
        var fields = Fields(Inventory(response, "vpcSet", 1).Single(),
            ["vpcId", "ownerId", "state", "cidrBlock", "cidrBlockAssociationSet", "dhcpOptionsId", "instanceTenancy", "isDefault", "tagSet"],
            "ipv6CidrBlockAssociationSet", "blockPublicAccessStates");
        var scope = release.Scope;
        if (Text(fields["vpcId"]) != release.Data.Network.VpcId || Text(fields["ownerId"]) != scope.AccountId ||
            Text(fields["state"]) != "available" || Text(fields["instanceTenancy"]) != "default" || Text(fields["isDefault"]) != "false") throw new InvalidOperationException();
        var dhcpOptionsId = Text(fields["dhcpOptionsId"]);
        Match(dhcpOptionsId, "dopt-([a-f0-9]{8}|[a-f0-9]{17})");
        var cidr = Text(fields["cidrBlock"]);
        Match(cidr, @"10[.](0|[1-9][0-9]{0,2})[.]0[.]0/16");
        if (!IPAddress.TryParse(cidr[..^3], out var ip) || ip.ToString() + "/16" != cidr) throw new InvalidOperationException();
        var association = Fields(Items(fields["cidrBlockAssociationSet"], 1).Single(), ["cidrBlock", "associationId", "cidrBlockState"]);
        Match(Text(association["associationId"]), "vpc-cidr-assoc-[a-f0-9]{17}");
        if (Text(association["cidrBlock"]) != cidr || Text(Fields(association["cidrBlockState"], ["state"])["state"]) != "associated") throw new InvalidOperationException();
        RequireEmpty(fields, "ipv6CidrBlockAssociationSet");
        CheckPublicAccess(fields);
        var tags = ReadTags(fields["tagSet"]); RequireScopeTags(tags, scope);
        if (tags.GetValueOrDefault("Name") != scope.Name) throw new InvalidOperationException();
        return new(cidr, dhcpOptionsId);
    }

    internal static void DnsAttribute(ComputeRelease release, bool hostnames, XElement response)
    {
        var attribute = hostnames ? "enableDnsHostnames" : "enableDnsSupport";
        var fields = Fields(response, ["requestId", "vpcId", attribute]);
        _ = Text(fields["requestId"], 1, 128);
        if (Text(fields["vpcId"]) != release.Data.Network.VpcId || Text(Fields(fields[attribute], ["value"])["value"]) != "true") throw new InvalidOperationException();
    }

    internal static IReadOnlyDictionary<string, ComputeSubnetObservation> Subnets(ComputeRelease release, string cidr, XElement response)
    {
        var scope = release.Scope; var network = release.Data.Network; var binding = ComputeNetworkBindings.Require(release);
        var prefix = cidr[..cidr.IndexOf('.', 3)]; // canonical 10.x from the checked VPC
        var subnets = new Dictionary<string, ComputeSubnetObservation>(StringComparer.Ordinal);
        var slots = new Dictionary<string, (string Zone, string ZoneId)>(StringComparer.Ordinal);
        foreach (var item in Inventory(response, "subnetSet", 4))
        {
            var fields = Fields(item, ["subnetId", "subnetArn", "ownerId", "vpcId", "state", "cidrBlock", "availableIpAddressCount", "availabilityZone", "availabilityZoneId",
                "defaultForAz", "mapPublicIpOnLaunch", "assignIpv6AddressOnCreation", "tagSet"],
                "ipv6CidrBlockAssociationSet", "ipv6Native", "enableDns64", "mapCustomerOwnedIpOnLaunch", "privateDnsNameOptionsOnLaunch", "blockPublicAccessStates");
            string Value(string name) => Text(fields[name]);
            var id = Value("subnetId");
            if (Value("subnetArn") != $"arn:aws:ec2:{scope.Region}:{scope.AccountId}:subnet/{id}" ||
                Value("ownerId") != scope.AccountId || Value("vpcId") != network.VpcId || Value("state") != "available" ||
                Value("defaultForAz") != "false" || Value("mapPublicIpOnLaunch") != "false" || Value("assignIpv6AddressOnCreation") != "false") throw new InvalidOperationException();
            var tier = network.SubnetIds.Contains(id, StringComparer.Ordinal) ? "application" :
                binding.DatabaseSubnetIds.Contains(id, StringComparer.Ordinal) ? "database" : throw new InvalidOperationException();
            RequireEmpty(fields, "ipv6CidrBlockAssociationSet");
            RequireFalse(fields, "ipv6Native", "enableDns64", "mapCustomerOwnedIpOnLaunch");
            CheckPublicAccess(fields);
            if (fields.TryGetValue("privateDnsNameOptionsOnLaunch", out var dns))
            {
                var options = Fields(dns, ["hostnameType"], "enableResourceNameDnsARecord", "enableResourceNameDnsAAAARecord");
                if (Text(options["hostnameType"]) != "ip-name") throw new InvalidOperationException();
                RequireFalse(options, "enableResourceNameDnsARecord", "enableResourceNameDnsAAAARecord");
            }
            if (!int.TryParse(Value("availableIpAddressCount"), NumberStyles.None, CultureInfo.InvariantCulture, out var available) || available is < 0 or > 251) throw new InvalidOperationException();
            var start = tier == "application" ? 0 : 16;
            var slot = Value("cidrBlock") == $"{prefix}.{start}.0/24" ? 0 : Value("cidrBlock") == $"{prefix}.{start + 1}.0/24" ? 1 : throw new InvalidOperationException();
            var name = $"{tier}-{slot}";
            var tags = ReadTags(fields["tagSet"]); RequireScopeTags(tags, scope);
            if (tags.GetValueOrDefault("Name") != $"{scope.Name}-{name}" || tags.GetValueOrDefault("Tier") != tier) throw new InvalidOperationException();
            var zone = Value("availabilityZone"); var zoneId = Value("availabilityZoneId");
            Match(zone, Regex.Escape(scope.Region) + "[a-z]"); Match(zoneId, "[a-z0-9]{2,12}-az[1-9][0-9]?");
            if (!slots.TryAdd(name, (zone, zoneId)) || !subnets.TryAdd(id, new(Value("cidrBlock"), zone, zoneId))) throw new InvalidOperationException();
        }
        var first = slots["application-0"]; var second = slots["application-1"];
        if (StringComparer.Ordinal.Compare(first.Zone, second.Zone) >= 0 || first.ZoneId == second.ZoneId || first != slots["database-0"] || second != slots["database-1"]) throw new InvalidOperationException();
        return subnets;
    }

    internal static void Routes(ComputeRelease release, string cidr, XElement response)
    {
        var network = release.Data.Network; var binding = ComputeNetworkBindings.Require(release); var scope = release.Scope;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        var associationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in Inventory(response, "routeTableSet", 3))
        {
            var fields = Fields(table, ["routeTableId", "ownerId", "vpcId", "routeSet", "associationSet"], "propagatingVgwSet", "tagSet");
            var id = Text(fields["routeTableId"]); Match(id, "rtb-[a-f0-9]{17}");
            if (!seen.Add(id) || Text(fields["ownerId"]) != scope.AccountId || Text(fields["vpcId"]) != network.VpcId) throw new InvalidOperationException();
            var tier = id == binding.RouteTables["application"] ? "application" : id == binding.RouteTables["database"] ? "database" : "main";
            if (!roles.Add(tier)) throw new InvalidOperationException();
            RequireEmpty(fields, "propagatingVgwSet");
            var tags = ReadTags(fields.GetValueOrDefault("tagSet"));
            if (tier != "main")
            {
                RequireScopeTags(tags, scope);
                var name = tier == "application" ? "application" : "isolated";
                if (tags.GetValueOrDefault("Tier") != tier || tags.GetValueOrDefault("Name") != $"{scope.Name}-{name}") throw new InvalidOperationException();
            }
            var expectedSubnets = tier == "application" ? network.SubnetIds : binding.DatabaseSubnetIds;
            var associated = new HashSet<string>(StringComparer.Ordinal);
            var associations = Items(fields["associationSet"], 2);
            if (associations.Length != (tier == "main" ? 1 : 2)) throw new InvalidOperationException();
            foreach (var item in associations)
            {
                var a = Fields(item, ["routeTableAssociationId", "routeTableId", "main"], "subnetId", "associationState");
                var associationId = Text(a["routeTableAssociationId"]); Match(associationId, "rtbassoc-[a-f0-9]{17}");
                if (!associationIds.Add(associationId) || Text(a["routeTableId"]) != id || Text(a["main"]) != (tier == "main" ? "true" : "false")) throw new InvalidOperationException();
                if (a.TryGetValue("associationState", out var state))
                {
                    var stateFields = Fields(state, ["state"], "statusMessage");
                    if (Text(stateFields["state"]) != "associated") throw new InvalidOperationException();
                    if (stateFields.TryGetValue("statusMessage", out var message)) _ = Text(message, 0, 256);
                }
                else if (tier != "main") throw new InvalidOperationException();
                if (tier == "main" ? a.ContainsKey("subnetId") : !associated.Add(Text(a["subnetId"]))) throw new InvalidOperationException();
            }
            if (tier != "main" && !associated.SetEquals(expectedSubnets)) throw new InvalidOperationException();
            var local = false; var layers = false;
            foreach (var item in Items(fields["routeSet"], 2))
            {
                var route = Fields(item, ["gatewayId", "state", "origin"], "destinationCidrBlock", "destinationPrefixListId");
                if (Text(route["state"]) != "active" || route.Count != 4) throw new InvalidOperationException();
                if (route.TryGetValue("destinationCidrBlock", out var destination) && !local && Text(destination) == cidr && Text(route["gatewayId"]) == "local" && Text(route["origin"]) == "CreateRouteTable") local = true;
                else if (tier == "application" && route.TryGetValue("destinationPrefixListId", out var prefix) && !layers && Text(prefix) == binding.S3PrefixListId && Text(route["gatewayId"]) == binding.Endpoints["layers"] && Text(route["origin"]) == "CreateRoute") layers = true;
                else throw new InvalidOperationException();
            }
            if (!local || layers != (tier == "application")) throw new InvalidOperationException();
        }
    }

    private static void RequireEmpty(Dictionary<string, XElement> fields, string key)
    { if (fields.TryGetValue(key, out var set)) _ = Items(set, 0); }
    private static void RequireFalse(Dictionary<string, XElement> fields, params string[] keys)
    { foreach (var key in keys) if (fields.TryGetValue(key, out var value) && Text(value) != "false") throw new InvalidOperationException(); }
    private static void CheckPublicAccess(Dictionary<string, XElement> fields)
    {
        if (fields.TryGetValue("blockPublicAccessStates", out var state) && Text(Fields(state, ["internetGatewayBlockMode"])["internetGatewayBlockMode"]) is not ("off" or "block-ingress" or "block-bidirectional")) throw new InvalidOperationException();
    }
}
