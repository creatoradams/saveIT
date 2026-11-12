using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using static SaveIT.Deployment.ComputeQueryXml;

namespace SaveIT.Deployment;

internal static class ComputeEndpointContract
{
    internal static void Endpoints(ComputeRelease release, XElement response)
    {
        var network = release.Data.Network;
        var binding = ComputeNetworkBindings.Require(release);
        var byId = binding.Endpoints.ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var interfaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Inventory(response, "vpcEndpointSet", byId.Count))
        {
            var fields = Fields(item, ["vpcEndpointId", "vpcEndpointType", "vpcId", "ownerId", "serviceName", "state",
                "ipAddressType", "policyDocument"], "privateDnsEnabled", "serviceRegion", "requesterManaged",
                "creationTimestamp", "routeTableIdSet", "subnetIdSet", "groupSet", "networkInterfaceIdSet",
                "dnsEntrySet", "dnsOptions", "tagSet");
            var id = Text(fields["vpcEndpointId"]);
            if (!byId.TryGetValue(id, out var role) || !observed.Add(id)) throw new InvalidOperationException();
            var gateway = role == "layers";
            var service = role switch
            {
                "ecr-api" => "ecr.api",
                "ecr-dkr" => "ecr.dkr",
                "configuration" => "secretsmanager",
                "web-token" => "cognito-identity",
                "layers" => "s3",
                "logs" => "logs",
                _ => throw new InvalidOperationException()
            };
            Require(fields, "vpcEndpointType", gateway ? "Gateway" : "Interface");
            Require(fields, "vpcId", network.VpcId);
            Require(fields, "ownerId", release.Scope.AccountId);
            Require(fields, "serviceName", $"com.amazonaws.{release.Scope.Region}.{service}");
            Require(fields, "state", "available");
            Require(fields, "ipAddressType", "ipv4");
            if (gateway) RequireOptional(fields, "privateDnsEnabled", "false");
            else if (!fields.TryGetValue("privateDnsEnabled", out var privateDns) || Text(privateDns) != "true")
                throw new InvalidOperationException();
            RequireOptional(fields, "serviceRegion", release.Scope.Region);
            RequireOptional(fields, "requesterManaged", "false");
            if (fields.TryGetValue("creationTimestamp", out var created)) _ = Text(created, 1, 64);
            _ = ReadTags(fields.GetValueOrDefault("tagSet"));

            ExactSet(fields, "routeTableIdSet", gateway ? [binding.RouteTables["application"]] : []);
            ExactSet(fields, "subnetIdSet", gateway ? [] : network.SubnetIds);
            var groups = OptionalItems(fields, "groupSet", gateway ? 0 : 1);
            var enis = OptionalItems(fields, "networkInterfaceIdSet", gateway ? 0 : 2);
            if (!gateway)
            {
                if (groups.Length != 1 || enis.Length != 2) throw new InvalidOperationException();
                var group = Fields(groups[0], ["groupId"], "groupName");
                Require(group, "groupId", binding.SecurityGroups[role]);
                if (group.TryGetValue("groupName", out var name)) _ = Text(name, 1, 255);
                foreach (var eni in enis)
                {
                    var value = Text(eni);
                    TaskMetadata.Match(value, "eni-[a-f0-9]{17}");
                    if (!interfaces.Add(value)) throw new InvalidOperationException();
                }
            }
            Dns(fields, gateway);
            var policy = fields["policyDocument"];
            if (policy.HasElements || PolicyHash(policy.Value) != binding.EndpointPolicySha256[role])
                throw new InvalidOperationException();
        }
    }

    internal static void S3Prefix(ComputeRelease release, XElement response)
    {
        var binding = ComputeNetworkBindings.Require(release);
        var fields = Fields(Inventory(response, "prefixListSet", 1)[0],
            ["prefixListId", "prefixListArn", "prefixListName", "ownerId", "addressFamily", "state"], "tagSet");
        Require(fields, "prefixListId", binding.S3PrefixListId);
        Require(fields, "prefixListArn", $"arn:aws:ec2:{release.Scope.Region}:aws:prefix-list/{binding.S3PrefixListId}");
        Require(fields, "prefixListName", $"com.amazonaws.{release.Scope.Region}.s3");
        Require(fields, "ownerId", "AWS");
        Require(fields, "addressFamily", "IPv4");
        Require(fields, "state", "create-complete");
        _ = ReadTags(fields.GetValueOrDefault("tagSet"));
    }

    private static void Dns(Dictionary<string, XElement> fields, bool gateway)
    {
        if (fields.TryGetValue("dnsOptions", out var options))
        {
            var dns = Fields(options, ["dnsRecordIpType"], "privateDnsOnlyForInboundResolverEndpoint");
            Require(dns, "dnsRecordIpType", "ipv4");
            RequireOptional(dns, "privateDnsOnlyForInboundResolverEndpoint", "false");
        }
        else if (!gateway) throw new InvalidOperationException();

        // These names are provider metadata only and are never resolved or used for transport.
        foreach (var entry in OptionalItems(fields, "dnsEntrySet", gateway ? 0 : 16))
        {
            var dns = Fields(entry, ["hostedZoneId", "dnsName"]);
            _ = Text(dns["hostedZoneId"], 1, 64);
            _ = Text(dns["dnsName"], 1, 253);
        }
    }

    private static XElement[] OptionalItems(Dictionary<string, XElement> fields, string key, int maximum) =>
        fields.TryGetValue(key, out var set) ? Items(set, maximum) : [];

    private static void ExactSet(Dictionary<string, XElement> fields, string key, IReadOnlyCollection<string> expected)
    {
        var actual = OptionalItems(fields, key, expected.Count).Select(item => Text(item)).ToArray();
        if (actual.Length != expected.Count || !expected.ToHashSet(StringComparer.Ordinal).SetEquals(actual))
            throw new InvalidOperationException();
    }

    private static void Require(Dictionary<string, XElement> fields, string key, string expected)
    {
        if (Text(fields[key]) != expected) throw new InvalidOperationException();
    }

    private static void RequireOptional(Dictionary<string, XElement> fields, string key, string expected)
    {
        if (fields.TryGetValue(key, out var field) && Text(field) != expected) throw new InvalidOperationException();
    }

    // Producer jsonencode and this writer agree on the restricted ASCII policy vocabulary.
    // Object order and formatting can vary on readback. Array order and decoded values remain exact.
    internal static string PolicyHash(string policy)
    {
        if (policy.Length == 0 || Encoding.UTF8.GetByteCount(policy) > 20480) throw new InvalidOperationException();
        using var document = JsonDocument.Parse(policy, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        TaskMetadata.RejectDuplicates(root);
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2012-10-17" ||
            !root.TryGetProperty("Statement", out var statements) || statements.ValueKind != JsonValueKind.Array ||
            statements.GetArrayLength() is < 1 or > 64 || statements.EnumerateArray().Any(statement => statement.ValueKind != JsonValueKind.Object))
            throw new InvalidOperationException();
        using var canonical = new MemoryStream();
        using (var writer = new Utf8JsonWriter(canonical, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteCanonical(writer, root);
        return Convert.ToHexStringLower(SHA256.HashData(canonical.GetBuffer().AsSpan(0, (int)canonical.Length)));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(PolicyText(property.Name));
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(PolicyText(value.GetString()!));
                break;
            default:
                throw new InvalidOperationException();
        }
    }

    private static string PolicyText(string text)
    {
        if (text.Any(character => character is < ' ' or > '~' or '<' or '>' or '&'))
            throw new InvalidOperationException();
        return text;
    }
}
