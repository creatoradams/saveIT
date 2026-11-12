using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeEndpointTests(ComputeReleaseFixture signing) : IClassFixture<ComputeReleaseFixture>
{
    private static readonly XNamespace Ns = ComputeQueryXml.Namespace;
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "compute-endpoint-observations.json")))!;
    internal static string Original(string stage) => string.Join('\n', Fixture[stage]!.AsArray().Select(line => line!.GetValue<string>()));
    internal static void Bind(JsonNode data)
    {
        ComputeSecurityGroupTests.Bind(data);
        var digests = new JsonObject();
        foreach (var pair in Fixture["endpointPolicySha256"]!.AsArray())
            digests.Add(pair![0]!.GetValue<string>(), pair[1]!.DeepClone());
        data["network"]!["bindings"]!["endpointPolicySha256"] = digests;
    }
    private Task<ComputeRelease> Release() => signing.CreateAsync(mutate: Bind);
    private static XElement Parse(string body, string action) => ComputeQueryXml.Read(Encoding.UTF8.GetBytes(body), action);
    private static XElement Parse(XElement response, string action) => Parse(response.ToString(SaveOptions.DisableFormatting), action);

    [Fact]
    public async Task Exact_signed_inventory_and_regional_aws_prefix_pass()
    {
        var release = await Release();
        ComputeEndpointContract.Endpoints(release, Parse(Original("endpoints"), "DescribeVpcEndpoints"));
        ComputeEndpointContract.S3Prefix(release, Parse(Original("prefix"), "DescribeManagedPrefixLists"));
        var gatewayDns = XElement.Parse(Original("endpoints"));
        gatewayDns.Descendants(Ns + "privateDnsEnabled").Last().Remove();
        ComputeEndpointContract.Endpoints(release, Parse(gatewayDns, "DescribeVpcEndpoints"));
    }

    [Theory]
    [InlineData("ownerId", "444455556666")]
    [InlineData("vpcId", "vpc-00000000000000002")]
    [InlineData("vpcEndpointId", "vpce-00000000000000002")]
    [InlineData("vpcEndpointType", "GatewayLoadBalancer")]
    [InlineData("serviceName", "com.amazonaws.us-east-1.ecr.dkr")]
    [InlineData("serviceRegion", "us-east-2")]
    [InlineData("state", "pending")]
    [InlineData("ipAddressType", "dualstack")]
    [InlineData("privateDnsEnabled", "false")]
    [InlineData("requesterManaged", "true")]
    [InlineData("dnsRecordIpType", "ipv6")]
    [InlineData("groupId", "sg-00000000000000009")]
    [InlineData("policyDocument", "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":\"*\",\"Resource\":\"*\"}]}")]
    public async Task Endpoint_rejects_field_or_policy_drift(string field, string value)
    {
        var release = await Release();
        var response = XElement.Parse(Original("endpoints"));
        response.Descendants(Ns + field).First().Value = value;
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.Endpoints(release, Parse(response, "DescribeVpcEndpoints")));
    }

    [Theory]
    [InlineData("duplicate-policy")]
    [InlineData("duplicate-subnet")]
    [InlineData("shared-eni")]
    [InlineData("gateway-db-route")]
    [InlineData("gateway-interface")]
    [InlineData("next-page")]
    [InlineData("extra-endpoint")]
    [InlineData("missing-endpoint")]
    [InlineData("unknown-field")]
    public async Task Endpoint_rejects_inventory_and_attachment_drift(string change)
    {
        var release = await Release();
        var response = XElement.Parse(Original("endpoints"));
        var set = response.Element(Ns + "vpcEndpointSet")!;
        var items = set.Elements().ToArray();
        var first = items[0];
        var gateway = items[^1];
        switch (change)
        {
            case "duplicate-policy":
                first.Element(Ns + "policyDocument")!.Value = OriginalDuplicatePolicy;
                break;
            case "duplicate-subnet":
                var subnets = first.Element(Ns + "subnetIdSet")!.Elements().ToArray();
                subnets[1].Value = subnets[0].Value;
                break;
            case "shared-eni":
                items[1].Element(Ns + "networkInterfaceIdSet")!.Elements().First().Value =
                    first.Element(Ns + "networkInterfaceIdSet")!.Elements().First().Value;
                break;
            case "gateway-db-route":
                gateway.Element(Ns + "routeTableIdSet")!.Elements().Single().Value = "rtb-00000000000000002";
                break;
            case "gateway-interface":
                gateway.Add(new XElement(first.Element(Ns + "networkInterfaceIdSet")!));
                break;
            case "next-page": response.Add(new XElement(Ns + "nextToken", "synthetic-next")); break;
            case "extra-endpoint": set.Add(new XElement(first)); break;
            case "missing-endpoint": gateway.Remove(); break;
            case "unknown-field": first.Add(new XElement(Ns + "serviceNetworkArn", "foreign")); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.Endpoints(release, Parse(response, "DescribeVpcEndpoints")));
    }

    private const string OriginalDuplicatePolicy = """{"Version":"2012-10-17","Statement":[{"Effect":"Deny","Effect":"Allow"}]}""";

    [Theory]
    [InlineData("ownerId", "111122223333")]
    [InlineData("addressFamily", "IPv6")]
    [InlineData("state", "delete-complete")]
    [InlineData("prefixListName", "com.amazonaws.us-east-1.dynamodb")]
    [InlineData("prefixListArn", "arn:aws:ec2:us-east-2:aws:prefix-list/pl-00000001")]
    [InlineData("prefixListId", "pl-00000002")]
    public async Task Prefix_rejects_regional_or_owner_substitution(string field, string value)
    {
        var release = await Release();
        var response = XElement.Parse(Original("prefix"));
        response.Descendants(Ns + field).Single().Value = value;
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.S3Prefix(release, Parse(response, "DescribeManagedPrefixLists")));
    }

    [Fact]
    public void Policy_canonicalization_preserves_values_and_array_order()
    {
        const string original = """{"Version":"2012-10-17","Statement":[{"Effect":"Deny","Action":["a","b"]}]}""";
        const string reformatted = """
            { "Statement": [ { "Action" : [ "\u0061", "b" ], "Effect" : "Deny" } ],
              "Version": "2012-10-17" }
            """;
        var hash = ComputeEndpointContract.PolicyHash(original);
        Assert.Equal(hash, ComputeEndpointContract.PolicyHash(reformatted));
        Assert.NotEqual(hash, ComputeEndpointContract.PolicyHash(original.Replace("""["a","b"]""", """["b","a"]""", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.PolicyHash(OriginalDuplicatePolicy));
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.PolicyHash(new string(' ', 20481)));
        Assert.Throws<InvalidOperationException>(() => ComputeEndpointContract.PolicyHash(original.Replace("Deny", "ñ", StringComparison.Ordinal)));
    }
}
