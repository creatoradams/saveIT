using System.Text.Json;
using Amazon.ServiceDiscovery;
using Amazon.ServiceDiscovery.Model;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

public static class ComputeDiscoveryAcquisition
{
    public const string Success = "SaveIT parked Cloud Map inventory passed (Route 53 records, associations and live DNS unverified).";
    public const string Failure = "SaveIT parked Cloud Map acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.Discovery, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, CancellationToken ct)
    {
        try
        {
            var binding = release.Data.Network.PrivateDns ?? throw new InvalidOperationException();
            var namespaceId = binding.NamespaceArn[(binding.NamespaceArn.LastIndexOf('/') + 1)..];
            await ComputeParkedTaskAcquisition.ReadAsync(release, session, transport, ct);
            await Read(ComputeSdkRead.DiscoveryNamespace(binding.NamespaceArn),
                sdk => sdk.GetNamespaceAsync(new GetNamespaceRequest { Id = binding.NamespaceArn }, ct),
                root => CheckNamespace(root.GetProperty("Namespace"), release.Scope, binding, namespaceId));
            foreach (var (name, arn) in new[] { ("api", release.Data.Network.ApiDiscoveryServiceArn),
                ("relay", release.Data.Network.RelayDiscoveryServiceArn) })
            {
                await Read(ComputeSdkRead.DiscoveryService(arn), sdk => sdk.GetServiceAsync(new GetServiceRequest { Id = arn }, ct),
                    root => CheckService(root.GetProperty("Service"), release.Scope, name, arn, namespaceId));
                await Read(ComputeSdkRead.DiscoveryInstances(arn),
                    sdk => sdk.ListInstancesAsync(new ListInstancesRequest { ServiceId = arn, MaxResults = 100 }, ct), CheckInstances);
            }

            async Task Read(ComputeSdkRead descriptor, Func<AmazonServiceDiscoveryClient, Task> request, Action<JsonElement> check)
            {
                ComputeEc2Acquisition.Fresh(release, session, ct);
                using var wire = new ComputeAwsTransport(transport(descriptor.Endpoint), release.Scope, descriptor, ct);
                using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = new AmazonServiceDiscoveryClient(session.Credentials, ComputeSdkConfiguration.Discovery(release.Scope.Region, http));
                await request(sdk);
                check(wire.ResponseJson);
                ComputeEc2Acquisition.Fresh(release, session, ct);
            }
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static void CheckNamespace(JsonElement value, ComputeScope scope, ComputePrivateDnsBinding binding, string id)
    {
        Allow(value, "Arn", "Id", "Name", "Type", "Properties", "ResourceOwner", "ServiceCount", "CreateDate", "CreatorRequestId", "Description");
        Require(value, "Arn", binding.NamespaceArn);
        Require(value, "Id", id);
        Require(value, "Name", $"{scope.Cell}.{scope.Environment}.saveit.internal");
        Require(value, "Type", "DNS_PRIVATE");
        OptionalOwner(value, "ResourceOwner", scope.AccountId);
        var properties = value.GetProperty("Properties");
        Allow(properties, "DnsProperties", "HttpProperties");
        var dns = properties.GetProperty("DnsProperties");
        Allow(dns, "HostedZoneId", "SOA");
        Require(dns, "HostedZoneId", binding.HostedZoneId);
    }

    internal static void CheckService(JsonElement value, ComputeScope scope, string name, string arn, string namespaceId)
    {
        Allow(value, "Arn", "Id", "Name", "NamespaceId", "DnsConfig", "HealthCheckCustomConfig", "ResourceOwner",
            "CreatedByAccount", "Type", "InstanceCount", "CreateDate", "CreatorRequestId", "Description");
        Require(value, "Arn", arn);
        Require(value, "Id", arn[(arn.LastIndexOf('/') + 1)..]);
        Require(value, "Name", name);
        Require(value, "NamespaceId", namespaceId);
        OptionalOwner(value, "ResourceOwner", scope.AccountId);
        OptionalOwner(value, "CreatedByAccount", scope.AccountId);
        if (value.TryGetProperty("Type", out var type) && type.GetString() is not ("DNS" or "DNS_HTTP"))
            throw new InvalidOperationException();
        if (value.TryGetProperty("InstanceCount", out var count) && count.GetInt32() != 0)
            throw new InvalidOperationException();
        var dns = value.GetProperty("DnsConfig");
        Allow(dns, "NamespaceId", "RoutingPolicy", "DnsRecords");
        OptionalOwner(dns, "NamespaceId", namespaceId);
        var records = dns.GetProperty("DnsRecords");
        if (dns.GetProperty("RoutingPolicy").GetString() != "MULTIVALUE" || records.GetArrayLength() != 1)
            throw new InvalidOperationException();
        var record = Object(records[0], "Type", "TTL");
        if (record["Type"].GetString() != "A" || record["TTL"].GetInt64() != 10 ||
            Object(value.GetProperty("HealthCheckCustomConfig"), "FailureThreshold")["FailureThreshold"].GetInt32() != 1)
            throw new InvalidOperationException();
    }

    internal static void CheckInstances(JsonElement root)
    {
        if (root.GetProperty("Instances").GetArrayLength() != 0 ||
            (root.TryGetProperty("NextToken", out var token) && token.GetString() is not (null or "")))
            throw new InvalidOperationException();
    }

    private static void Allow(JsonElement value, params string[] fields)
    {
        if (value.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)))
            throw new InvalidOperationException();
    }

    private static void Require(JsonElement value, string key, string expected)
    {
        if (value.GetProperty(key).GetString() != expected) throw new InvalidOperationException();
    }

    private static void OptionalOwner(JsonElement value, string key, string expected)
    {
        if (value.TryGetProperty(key, out var owner) && owner.GetString() != expected) throw new InvalidOperationException();
    }
}
