using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using static SaveIT.Deployment.ComputeQueryXml;

namespace SaveIT.Deployment;

internal static class ComputeNetworkObservation
{
    internal static void Dhcp(ComputeScope scope, ComputeVpcObservation vpc, XElement response)
    {
        var fields = Fields(Inventory(response, "dhcpOptionsSet", 1)[0],
            ["dhcpOptionsId", "ownerId", "dhcpConfigurationSet"], "tagSet");
        if (Text(fields["dhcpOptionsId"]) != vpc.DhcpOptionsId || Text(fields["ownerId"]) != scope.AccountId)
            throw new InvalidOperationException();
        _ = ReadTags(fields.GetValueOrDefault("tagSet"));

        var expectedDomain = scope.Region == "us-east-1" ? "ec2.internal" : $"{scope.Region}.compute.internal";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items(fields["dhcpConfigurationSet"], 3))
        {
            var option = Fields(item, ["key", "valueSet"]);
            var key = Text(option["key"]);
            var expected = key switch
            {
                "domain-name" => expectedDomain,
                "domain-name-servers" => "AmazonProvidedDNS",
                "ipv6-address-preferred-lease-time" => "140",
                _ => throw new InvalidOperationException()
            };
            if (!seen.Add(key) || Text(Fields(Items(option["valueSet"], 1).Single(), ["value"])["value"]) != expected)
                throw new InvalidOperationException();
        }
        if (!seen.Contains("domain-name") || !seen.Contains("domain-name-servers")) throw new InvalidOperationException();
    }

    internal static void Interfaces(ComputeRelease release, IReadOnlyDictionary<string, ComputeSubnetObservation> subnets,
        IReadOnlyDictionary<string, ComputeEndpointInterface> interfaces, XElement response)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var placements = new HashSet<(string Endpoint, string Subnet)>();
        var addresses = new HashSet<IPAddress>();
        foreach (var item in Inventory(response, "networkInterfaceSet", interfaces.Count))
        {
            var fields = Fields(item, ["networkInterfaceId", "vpcId", "subnetId", "ownerId", "availabilityZone", "description",
                "interfaceType", "status", "requesterManaged", "privateIpAddress", "privateIpAddressesSet", "groupSet"],
                "availabilityZoneId", "requesterId", "sourceDestCheck", "macAddress", "privateDnsName", "attachment", "tagSet",
                "ipv6AddressesSet", "ipv4PrefixSet", "ipv6PrefixSet", "ipv6Native", "denyAllIgwTraffic");
            string Value(string key) => Text(fields[key]);
            var id = Value("networkInterfaceId");
            var subnetId = Value("subnetId");
            if (!interfaces.TryGetValue(id, out var expected) || !seen.Add(id) ||
                !release.Data.Network.SubnetIds.Contains(subnetId, StringComparer.Ordinal) ||
                !subnets.TryGetValue(subnetId, out var subnet) || !placements.Add((expected.EndpointId, subnetId)) ||
                Value("vpcId") != release.Data.Network.VpcId || Value("ownerId") != release.Scope.AccountId ||
                Value("availabilityZone") != subnet.Zone || Value("interfaceType") != "vpc_endpoint" ||
                Value("description") != $"VPC Endpoint Interface {expected.EndpointId}" ||
                Value("status") != "in-use" || Value("requesterManaged") != "true") throw new InvalidOperationException();

            CheckOptional(fields, "availabilityZoneId", subnet.ZoneId);
            CheckOptional(fields, "sourceDestCheck", "true");
            CheckOptional(fields, "ipv6Native", "false");
            foreach (var key in new[] { "ipv6AddressesSet", "ipv4PrefixSet", "ipv6PrefixSet" })
                if (fields.TryGetValue(key, out var set)) _ = Items(set, 0);
            foreach (var key in new[] { "requesterId", "macAddress", "privateDnsName" })
                if (fields.TryGetValue(key, out var metadata)) _ = Text(metadata, 0, 253);
            if (fields.TryGetValue("denyAllIgwTraffic", out var igw) && Text(igw) is not ("true" or "false"))
                throw new InvalidOperationException();
            _ = ReadTags(fields.GetValueOrDefault("tagSet"));
            if (fields.TryGetValue("attachment", out var attachment)) Attachment(attachment);

            var group = Fields(Items(fields["groupSet"], 1).Single(), ["groupId"], "groupName");
            if (Text(group["groupId"]) != expected.SecurityGroupId) throw new InvalidOperationException();
            if (group.TryGetValue("groupName", out var groupName)) _ = Text(groupName, 0, 255);

            var address = Value("privateIpAddress");
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                ip.ToString() != address || !IPNetwork.Parse(subnet.Cidr).Contains(ip) ||
                ip.GetAddressBytes()[3] is < 4 or 255 || !addresses.Add(ip)) throw new InvalidOperationException();
            var primary = Fields(Items(fields["privateIpAddressesSet"], 1).Single(), ["privateIpAddress", "primary"], "privateDnsName");
            if (Text(primary["privateIpAddress"]) != address || Text(primary["primary"]) != "true") throw new InvalidOperationException();
            if (primary.TryGetValue("privateDnsName", out var dnsName)) _ = Text(dnsName, 0, 253);
        }
    }

    private static void Attachment(XElement value)
    {
        var fields = Fields(value, ["attachmentId", "status"], "instanceOwnerId", "deviceIndex", "networkCardIndex", "attachTime", "deleteOnTermination");
        TaskMetadata.Match(Text(fields["attachmentId"]), "eni-attach-([a-f0-9]{8}|[a-f0-9]{17})");
        if (Text(fields["status"]) != "attached") throw new InvalidOperationException();
        foreach (var key in new[] { "instanceOwnerId", "deviceIndex", "networkCardIndex", "attachTime", "deleteOnTermination" })
            if (fields.TryGetValue(key, out var metadata)) _ = Text(metadata, 1, 64);
    }

    private static void CheckOptional(Dictionary<string, XElement> fields, string key, string expected)
    {
        if (fields.TryGetValue(key, out var value) && Text(value) != expected) throw new InvalidOperationException();
    }
}
