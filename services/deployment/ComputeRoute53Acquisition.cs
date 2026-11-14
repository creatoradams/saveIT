using System.Globalization;
using System.Xml.Linq;
using Amazon.Route53;
using Amazon.Route53.Model;
using static SaveIT.Deployment.ComputeQueryXml;

namespace SaveIT.Deployment;

public static class ComputeRoute53Acquisition
{
    internal static readonly XNamespace XmlNamespace = "https://route53.amazonaws.com/doc/2013-04-01/";
    public const string Success = "SaveIT parked private DNS passed (Profiles and live DNS answers unverified).";
    public const string Failure = "SaveIT parked private DNS acquisition failed.";

    public static async Task ValidateAsync(Stream input, CancellationToken ct = default)
    {
        try { await ComputeTaskAcquisition.AcquireAsync(input, ComputeAcquisitionKind.PrivateDns, ct); }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static async Task ReadAsync(ComputeRelease release, ComputeAwsSession session,
        Func<string, HttpMessageHandler> transport, CancellationToken ct)
    {
        try
        {
            var binding = release.Data.Network.PrivateDns ?? throw new InvalidOperationException();
            await ComputeDiscoveryAcquisition.ReadAsync(release, session, transport, ct);
            await Read(false, sdk => sdk.GetHostedZoneAsync(new GetHostedZoneRequest { Id = binding.HostedZoneId }, ct),
                xml => CheckZone(release, binding, xml));
            await Read(true, sdk => sdk.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest
            { HostedZoneId = binding.HostedZoneId, MaxItems = "10" }, ct), xml => CheckRecords(release.Scope, xml));

            async Task Read(bool records, Func<AmazonRoute53Client, Task> request, Action<XElement> check)
            {
                ComputeEc2Acquisition.Fresh(release, session, ct);
                var descriptor = ComputeSdkRead.HostedZone(binding.HostedZoneId, records);
                using var wire = new ComputeAwsTransport(transport(descriptor.Endpoint), release.Scope, descriptor, ct);
                using var http = new HttpClient(wire, false) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = new AmazonRoute53Client(session.Credentials, ComputeSdkConfiguration.Route53(http));
                await request(sdk);
                check(wire.ResponseXml ?? throw new InvalidOperationException());
                ComputeEc2Acquisition.Fresh(release, session, ct);
            }
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static void CheckZone(ComputeRelease release, ComputePrivateDnsBinding binding, XElement xml)
    {
        var root = Fields(xml, ["HostedZone", "VPCs"]);
        var zone = Fields(root["HostedZone"], ["Id", "Name", "CallerReference", "Config", "ResourceRecordSetCount"], "LinkedService");
        if (Text(zone["Id"]) != $"/hostedzone/{binding.HostedZoneId}" || Text(zone["Name"]) != Apex(release.Scope) ||
            Text(zone["ResourceRecordSetCount"]) != "2" ||
            Text(Fields(zone["Config"], ["PrivateZone"], "Comment")["PrivateZone"]) != "true")
            throw new InvalidOperationException();
        _ = Text(zone["CallerReference"], 1, 128);
        if (zone.TryGetValue("LinkedService", out var linked) &&
            Text(Fields(linked, ["ServicePrincipal"], "Description")["ServicePrincipal"]) != "servicediscovery.amazonaws.com")
            throw new InvalidOperationException();
        var vpc = Fields(Items(root["VPCs"], 1, "VPC").Single(), ["VPCId", "VPCRegion"]);
        if (Text(vpc["VPCId"]) != release.Data.Network.VpcId || Text(vpc["VPCRegion"]) != release.Scope.Region)
            throw new InvalidOperationException();
    }

    internal static void CheckRecords(ComputeScope scope, XElement xml)
    {
        var root = Fields(xml, ["ResourceRecordSets", "IsTruncated", "MaxItems"]);
        if (Text(root["IsTruncated"]) != "false" || Text(root["MaxItems"]) != "10") throw new InvalidOperationException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items(root["ResourceRecordSets"], 2, "ResourceRecordSet"))
        {
            var set = Fields(item, ["Name", "Type", "TTL", "ResourceRecords"]);
            var type = Text(set["Type"]);
            if (Text(set["Name"]) != Apex(scope) || type is not ("NS" or "SOA") || !seen.Add(type) ||
                !uint.TryParse(Text(set["TTL"]), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException();
            var records = Items(set["ResourceRecords"], type == "NS" ? 4 : 1, "ResourceRecord");
            if (records.Length != (type == "NS" ? 4 : 1)) throw new InvalidOperationException();
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                var value = Text(Fields(record, ["Value"])["Value"], 1, 512);
                if (!values.Add(value)) throw new InvalidOperationException();
            }
        }
        if (seen.Count != 2) throw new InvalidOperationException();
    }

    private static string Apex(ComputeScope scope) => $"{scope.Cell}.{scope.Environment}.saveit.internal.";
}
