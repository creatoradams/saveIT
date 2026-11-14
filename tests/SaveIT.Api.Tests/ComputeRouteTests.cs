using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeRouteTests(ComputeReleaseFixture signing,ComputeConfigurationTests.Fixture configuration) : IClassFixture<ComputeReleaseFixture>,IClassFixture<ComputeConfigurationTests.Fixture>
{
    private const string VpcId="vpc-00000000000000001", Cidr="10.84.0.0/16";
    private static readonly XNamespace Ns=ComputeQueryXml.Namespace;
    private static XElement E(string name,params object?[] values)=>new(Ns+name,values);
    // The same original synthetic XML is served by the native provider.
    private static readonly Dictionary<string,string[]> Observations=JsonSerializer.Deserialize<Dictionary<string,string[]>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-route-observations.json")))!;
    private static string Original(string stage)=>string.Join('\n',Observations[stage]);
    private static XElement Observation(string stage)=>XElement.Parse(Original(stage));
    private static XElement ParseOriginal(string stage,string action)=>ComputeQueryXml.Read(Encoding.UTF8.GetBytes(Original(stage)),action);
    private static XElement Vpc()=>Observation("vpc");
    private static XElement Subnets()=>Observation("subnets");
    private static XElement Routes()=>Observation("routes");
    private Task<ComputeRelease> Release()=>signing.CreateAsync(mutate:ComputeSecurityGroupTests.Bind);
    private static XElement Parse(XElement value)=>ComputeQueryXml.Read(Encoding.UTF8.GetBytes(value.ToString(SaveOptions.DisableFormatting)),value.Name.LocalName[..^8]);

    [Fact]
    public async Task Complete_parked_topology_accepts_exact_tiers_explicit_associations_and_local_only_main()
    {
        var release=await Release();var cidr=ComputeRouteContract.Vpc(release,ParseOriginal("vpc","DescribeVpcs")).Cidr;Assert.Equal(Cidr,cidr);
        ComputeRouteContract.DnsAttribute(release,false,ParseOriginal("enableDnsSupport","DescribeVpcAttribute"));
        ComputeRouteContract.DnsAttribute(release,true,ParseOriginal("enableDnsHostnames","DescribeVpcAttribute"));
        ComputeRouteContract.Subnets(release,cidr,ParseOriginal("subnets","DescribeSubnets"));ComputeRouteContract.Routes(release,cidr,ParseOriginal("routes","DescribeRouteTables"));
    }
    [Theory]
    [InlineData("owner")][InlineData("cidr")][InlineData("cidr-alias")][InlineData("secondary")][InlineData("ipv6")]
    [InlineData("default")][InlineData("pagination")][InlineData("foreign")][InlineData("unknown")]
    public async Task Vpc_refuses_unapproved_cidr_ownership_and_original_XML_fields(string mode)
    {
        var release=await Release();var response=Vpc();var item=response.Descendants(Ns+"vpcId").Single().Parent!;
        if(mode=="owner")item.Element(Ns+"ownerId")!.Value="444455556666";
        if(mode=="cidr")item.Element(Ns+"cidrBlock")!.Value="10.256.0.0/16";
        if(mode=="cidr-alias")item.Element(Ns+"cidrBlock")!.Value="10.084.0.0/16";
        if(mode=="secondary"){var set=item.Element(Ns+"cidrBlockAssociationSet")!;set.Add(new XElement(set.Elements().Single()));}
        if(mode=="ipv6")item.Element(Ns+"ipv6CidrBlockAssociationSet")!.Add(E("item",E("ipv6CidrBlock","2001:db8::/56")));
        if(mode=="default")item.Element(Ns+"isDefault")!.Value="true";
        if(mode=="pagination")response.Add(E("nextToken","synthetic-next"));
        if(mode=="foreign")item.Element(Ns+"vpcId")!.Value="vpc-00000000000000002";
        if(mode=="unknown")item.Add(E("additionalAuthority","synthetic"));
        Assert.ThrowsAny<Exception>(()=>ComputeRouteContract.Vpc(release,Parse(response)));
    }
    [Theory]
    [InlineData("public")][InlineData("ipv6")][InlineData("dns64")][InlineData("zone")][InlineData("zone-id")][InlineData("cidr")]
    [InlineData("missing")][InlineData("duplicate")][InlineData("foreign-arn")][InlineData("outpost")][InlineData("pagination")]
    public async Task Subnets_refuse_drift_from_four_exact_private_subnets_and_two_matching_AZs(string mode)
    {
        var release=await Release();var response=Subnets();var set=response.Element(Ns+"subnetSet")!;var subnets=set.Elements().ToArray();var item=subnets[0];
        if(mode=="public")item.Element(Ns+"mapPublicIpOnLaunch")!.Value="true";
        if(mode=="ipv6")item.Element(Ns+"assignIpv6AddressOnCreation")!.Value="true";
        if(mode=="dns64")item.Element(Ns+"enableDns64")!.Value="true";
        if(mode=="zone")subnets[2].Element(Ns+"availabilityZone")!.Value="us-east-1b";
        if(mode=="zone-id")subnets[2].Element(Ns+"availabilityZoneId")!.Value="use1-az9";
        if(mode=="cidr")item.Element(Ns+"cidrBlock")!.Value="10.84.4.0/24";
        if(mode=="missing")subnets[2].Remove();if(mode=="duplicate")subnets[3].ReplaceWith(new XElement(item));
        if(mode=="foreign-arn")item.Element(Ns+"subnetArn")!.Value="arn:aws:ec2:us-east-2:111122223333:subnet/subnet-00000000000000001";
        if(mode=="outpost")item.Add(E("outpostArn","synthetic-foreign"));
        if(mode=="pagination")response.Add(E("nextToken","synthetic-next"));
        Assert.ThrowsAny<Exception>(()=>ComputeRouteContract.Subnets(release,Cidr,Parse(response)));
    }
    [Theory]
    [InlineData("main-public")][InlineData("local-replacement")][InlineData("more-specific")][InlineData("nat")][InlineData("blackhole")]
    [InlineData("s3-other-table")][InlineData("s3-target")][InlineData("s3-prefix")][InlineData("missing-s3")][InlineData("propagated")]
    [InlineData("fallback")][InlineData("wrong-tier")][InlineData("association-failed")][InlineData("gateway-association")]
    [InlineData("extra-table")][InlineData("missing-main")][InlineData("duplicate-association")][InlineData("pagination")]
    public async Task Route_checks_refuse_any_public_replacement_propagation_or_implicit_subnet_fallback(string mode)
    {
        var release=await Release();var response=Routes();var set=response.Element(Ns+"routeTableSet")!;var tables=set.Elements().ToArray();
        var app=tables[0].Element(Ns+"routeSet")!;var database=tables[1].Element(Ns+"routeSet")!;var main=tables[2].Element(Ns+"routeSet")!;
        var local=app.Elements().First();var layer=app.Elements().Last();var associations=tables[0].Element(Ns+"associationSet")!;var first=associations.Elements().First();
        if(mode=="main-public")main.Add(E("item",E("gatewayId","igw-00000000000000001"),E("destinationCidrBlock","0.0.0.0/0"),E("origin","CreateRoute"),E("state","active")));
        if(mode=="local-replacement")local.Element(Ns+"gatewayId")!.ReplaceWith(E("networkInterfaceId","eni-00000000000000001"));
        if(mode=="more-specific")local.Element(Ns+"destinationCidrBlock")!.Value="10.84.1.0/24";
        if(mode=="nat")local.Add(E("natGatewayId","nat-00000000000000001"));
        if(mode=="blackhole")layer.Element(Ns+"state")!.Value="blackhole";
        if(mode=="s3-other-table")database.Add(new XElement(layer));
        if(mode=="s3-target")layer.Element(Ns+"gatewayId")!.Value="vpce-00000000000000007";
        if(mode=="s3-prefix")layer.Element(Ns+"destinationPrefixListId")!.Value="pl-00000002";
        if(mode=="missing-s3")layer.Remove();
        if(mode=="propagated")layer.Element(Ns+"origin")!.Value="EnableVgwRoutePropagation";
        if(mode=="fallback")first.Remove();if(mode=="wrong-tier")first.Element(Ns+"subnetId")!.Value="subnet-00000000000000003";
        if(mode=="association-failed")first.Descendants(Ns+"state").Single().Value="failed";
        if(mode=="gateway-association")first.Add(E("gatewayId","igw-00000000000000001"));
        if(mode=="extra-table")set.Add(new XElement(tables[1]));if(mode=="missing-main")tables[2].Remove();
        if(mode=="duplicate-association")associations.Elements().Last().Element(Ns+"routeTableAssociationId")!.Value=first.Element(Ns+"routeTableAssociationId")!.Value;
        if(mode=="pagination")response.Add(E("nextToken","synthetic-next"));
        Assert.ThrowsAny<Exception>(()=>ComputeRouteContract.Routes(release,Cidr,Parse(response)));
    }

    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>run(request,ct);}
    private sealed class FinalReadClock : TimeProvider
    {
        internal DateTimeOffset? Now {get;set;}
        public override DateTimeOffset GetUtcNow()=>Now??DateTimeOffset.UtcNow;
    }
    [Theory]
    [InlineData(null,5)][InlineData("vpc",1)][InlineData("enableDnsSupport",2)][InlineData("enableDnsHostnames",3)][InlineData("subnets",4)][InlineData("routes",5)]
    [InlineData("expired",5)]
    [InlineData(null,7,"endpoints")][InlineData("endpoints",6,"endpoints")][InlineData("prefix",7,"endpoints")]
    [InlineData(null,9,"network")][InlineData("dhcp",8,"network")][InlineData("interfaces",9,"network")]
    [InlineData(null,12,"resolver")]
    [InlineData("forward",10,"resolver")][InlineData("rule-vpc",10,"resolver")][InlineData("page",10,"resolver")]
    [InlineData("group",11,"resolver")][InlineData("group-page",11,"resolver")]
    [InlineData("owner",12,"resolver")][InlineData("open",12,"resolver")][InlineData("duplicate",12,"resolver")]
    [InlineData(null,6,"rds")][InlineData("rds-owner",6,"rds")][InlineData("rds-public",6,"rds")]
    [InlineData("rds-subnet",6,"rds")][InlineData("rds-group",6,"rds")][InlineData("rds-target",6,"rds")]
    [InlineData("rds-page",6,"rds")][InlineData("rds-pending",6,"rds")]
    public async Task Real_SDK_query_sequence_stops_at_first_invalid_original_response(string? invalid,int reads,string kind="routes")
    {
        var prior=await new ComputeSecurityGroupTests(signing,configuration).Acquisition(bind:kind=="routes"?null:ComputeEndpointTests.Bind);
        var count=0;var returned=false;var clock=new FinalReadClock();
        HttpMessageHandler Provider(string service)=>service=="rds"?new Handler(async(request,ct)=>{
            Assert.Equal("https://rds.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);
            Assert.Contains("/us-east-1/rds/aws4_request",request.Headers.GetValues("Authorization").Single());
            prior.Calls.Add("rds");
            var query=(await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(pair=>pair.Split('='))
                .ToDictionary(pair=>pair[0],pair=>Uri.UnescapeDataString(pair[1]));
            Assert.Equal(3,query.Count);
            Assert.Equal("DescribeDBInstances",query["Action"]);
            Assert.Equal("2014-10-31",query["Version"]);
            Assert.Equal("arn:aws:rds:us-east-1:111122223333:db:saveit-pilot-staging",query["DBInstanceIdentifier"]);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(RdsObservation(invalid),Encoding.UTF8,"text/xml")};
        }):service=="route53resolver"?new Handler(async(request,ct)=>{
            Assert.Equal("https://route53resolver.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);
            Assert.Contains("/us-east-1/route53resolver/aws4_request",request.Headers.GetValues("Authorization").Single());
            const string prefix = "Route53Resolver.";
            var target = request.Headers.GetValues("X-Amz-Target").Single();
            Assert.StartsWith(prefix,target);
            var action = target[prefix.Length..];
            prior.Calls.Add(action);
            var requestBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            var expected = action switch {
                "ListResolverRuleAssociations" => """{"Filters":[{"Name":"VPCId","Values":["vpc-00000000000000001"]}],"MaxResults":100}""",
                "ListFirewallRuleGroupAssociations" => """{"VpcId":"vpc-00000000000000001","MaxResults":100}""",
                "GetFirewallConfig" => """{"ResourceId":"vpc-00000000000000001"}""",
                _ => throw new InvalidOperationException()
            };
            Assert.True(JsonNode.DeepEquals(requestBody,JsonNode.Parse(expected)));
            var fixture=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-resolver-observations.json")))!;
            var reply=fixture[action]!;
            if(action=="ListResolverRuleAssociations")
            {
                var rules=reply["ResolverRuleAssociations"]!.AsArray();
                if(invalid=="forward")rules.Add(new JsonObject{["ResolverRuleId"]="rslvr-rr-synthetic"});
                if(invalid=="rule-vpc")rules[0]!["VPCId"]="vpc-00000000000000002";
                if(invalid=="page")reply["NextToken"]="synthetic-page";
            }
            if(action=="ListFirewallRuleGroupAssociations")
            {
                if(invalid=="group")reply["FirewallRuleGroupAssociations"]!.AsArray().Add(new JsonObject{["Priority"]=100});
                if(invalid=="group-page")reply["NextToken"]="synthetic-page";
            }
            if(action=="GetFirewallConfig")
            {
                if(invalid=="owner")reply["FirewallConfig"]!["OwnerId"]="444455556666";
                if(invalid=="open")reply["FirewallConfig"]!["FirewallFailOpen"]="ENABLED";
            }
            var body=reply.ToJsonString();
            if(action=="GetFirewallConfig"&&invalid=="duplicate")
                body=body.Replace("\"OwnerId\":","\"OwnerId\":\"444455556666\",\"OwnerId\":",StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/x-amz-json-1.1")};
        }):service!="ec2"||count++==0?prior.Provider(service):new Handler(async(request,ct)=>{
            Assert.Equal("https://ec2.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);
            Assert.Contains("/us-east-1/ec2/aws4_request",request.Headers.GetValues("Authorization").Single());
            var form=(await request.Content!.ReadAsStringAsync(ct)).Split('&').ToDictionary(p=>Uri.UnescapeDataString(p.Split('=')[0]),p=>Uri.UnescapeDataString(p.Split('=')[1]));
            Assert.Equal("2016-11-15",form["Version"]);
            string stage;
            switch(form["Action"]) {
                case "DescribeVpcs":Assert.Equal(VpcId,form["VpcId.1"]);Assert.Equal(3,form.Count);stage="vpc";break;
                case "DescribeVpcAttribute":Assert.Equal(VpcId,form["VpcId"]);Assert.Equal(4,form.Count);stage=form["Attribute"];break;
                case "DescribeSubnets":case "DescribeRouteTables":
                    Assert.Equal(5,form.Count);Assert.Equal(VpcId,form["Filter.1.Value.1"]);Assert.Equal("vpc-id",form["Filter.1.Name"]);
                    bool subnet=form["Action"]=="DescribeSubnets";Assert.Equal(subnet?"1000":"100",form["MaxResults"]);stage=subnet?"subnets":"routes";break;
                case "DescribeVpcEndpoints":
                    Assert.Equal(5,form.Count);Assert.Equal(VpcId,form["Filter.1.Value.1"]);Assert.Equal("vpc-id",form["Filter.1.Name"]);
                    Assert.Equal("100",form["MaxResults"]);stage="endpoints";break;
                case "DescribeManagedPrefixLists":
                    Assert.Equal(3,form.Count);Assert.Equal("pl-00000001",form["PrefixListId.1"]);stage="prefix";break;
                case "DescribeDhcpOptions":
                    Assert.Equal(3,form.Count);Assert.Equal("dopt-00000001",form["DhcpOptionsId.1"]);stage="dhcp";break;
                case "DescribeNetworkInterfaces":
                    Assert.Equal(7,form.Count);Assert.Equal("vpc-id",form["Filter.1.Name"]);Assert.Equal(VpcId,form["Filter.1.Value.1"]);
                    Assert.Equal("interface-type",form["Filter.2.Name"]);Assert.Equal("vpc_endpoint",form["Filter.2.Value.1"]);
                    Assert.Equal("100",form["MaxResults"]);stage="interfaces";break;
                default:throw new InvalidOperationException();
            }
            prior.Calls.Add(stage);var body=stage is "endpoints" or "prefix" or "dhcp" or "interfaces"?ComputeEndpointTests.Original(stage):Original(stage);
            if(stage==invalid){var response=XElement.Parse(body);response.Add(E("unreviewedAuthority","synthetic-private-marker"));body=response.ToString(SaveOptions.DisableFormatting);}
            if(stage=="routes"&&invalid=="expired"){clock.Now=DateTimeOffset.FromUnixTimeSeconds(prior.Bundle.Release.ExpiresAt);returned=true;}
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"text/xml")};
        });
        var session=await ComputeSecurityGroupTests.Session();
        async Task Acquire()
        {
            if(kind=="rds")await ComputeRdsAcquisition.ReadAsync(prior.Bundle.Release,session,Provider,configuration.Trust,CancellationToken.None);
            else if(kind=="resolver")await ComputeResolverAcquisition.ReadAsync(prior.Bundle.Release,session,Provider,configuration.Trust,CancellationToken.None);
            else if(kind=="network")await ComputeNetworkAcquisition.ReadAsync(prior.Bundle.Release,session,Provider,configuration.Trust,CancellationToken.None);
            else if(kind=="endpoints")await ComputeEndpointAcquisition.ReadAsync(prior.Bundle.Release,session,Provider,configuration.Trust,CancellationToken.None);
            else
            {
                var routes=await AcquiredComputeRoutes.ReadAsync(prior.Bundle.Release,session,Provider,configuration.Trust,CancellationToken.None,clock);
                Assert.Same(prior.Bundle.Release,routes.SecurityGroups.Configuration.Images.Cell.Tasks.Release);
            }
        }
        if(invalid is null)await Acquire();
        else
        {
            var error=await Assert.ThrowsAsync<InvalidOperationException>(Acquire);
            Assert.Equal(kind switch { "rds"=>ComputeRdsAcquisition.Failure, "resolver"=>ComputeResolverAcquisition.Failure, "network"=>ComputeNetworkAcquisition.Failure, "endpoints"=>ComputeEndpointAcquisition.Failure, _=>ComputeRouteAcquisition.Failure },error.Message);
            Assert.Null(error.InnerException);
        }
        var stages=new[]{"vpc","enableDnsSupport","enableDnsHostnames","subnets","routes","endpoints","prefix","dhcp","interfaces","ListResolverRuleAssociations","ListFirewallRuleGroupAssociations","GetFirewallConfig"};
        Assert.Equal(kind=="rds"?stages.Take(5).Append("rds"):stages.Take(reads),prior.Calls.Skip(20));
        Assert.Equal(20+reads,prior.Calls.Count);
        if(invalid=="expired")Assert.True(returned,"A valid response at simulated expiry must reach the validation boundary.");
    }

    private static string RdsObservation(string? invalid)
    {
        var xml=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-rds-observation.xml"));
        var (from,to)=invalid switch
        {
            "rds-owner"=>("111122223333:db:","444455556666:db:"),
            "rds-public"=>("<PubliclyAccessible>false","<PubliclyAccessible>true"),
            "rds-subnet"=>("subnet-00000000000000003","subnet-00000000000000001"),
            "rds-group"=>("sg-00000000000000005","sg-00000000000000004"),
            "rds-target"=>("db.synthetic.invalid","foreign.synthetic.invalid"),
            "rds-page"=>("</DBInstances>","</DBInstances><Marker>synthetic-page</Marker>"),
            "rds-pending"=>("<PendingModifiedValues/>","<PendingModifiedValues><DBInstanceClass>db.synthetic</DBInstanceClass></PendingModifiedValues>"),
            _=>("","")
        };
        return from.Length==0?xml:xml.Replace(from,to,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("ownerId", "444455556666")]
    [InlineData("dhcpOptionsId", "dopt-00000002")]
    [InlineData("value", "other.internal")]
    [InlineData("dns-server", "10.84.0.10")]
    [InlineData("extra-server", "10.84.0.10")]
    [InlineData("extra-option", "ntp-servers")]
    [InlineData("nextToken", "next")]
    public async Task Dhcp_accepts_only_the_observed_regional_amazon_resolver_options(string? field, string? value)
    {
        var release = await Release();
        var vpc = ComputeRouteContract.Vpc(release, Vpc());
        var response = XElement.Parse(ComputeEndpointTests.Original("dhcp"));
        var options = response.Descendants(Ns + "dhcpConfigurationSet").Single();
        if (field == "dns-server") options.Descendants(Ns + "value").Last().Value = value!;
        else if (field == "extra-server") options.Descendants(Ns + "valueSet").Last().Add(E("item", E("value", value)));
        else if (field == "extra-option") options.Add(E("item", E("key", value), E("valueSet", E("item", E("value", "10.84.0.10")))));
        else if (field == "nextToken") response.Add(E(field, value));
        else if (field is not null) response.Descendants(Ns + field).First().Value = value!;
        void Check() => ComputeNetworkObservation.Dhcp(release.Scope, vpc, Parse(response));
        if (field is null) Check();
        else Assert.Throws<InvalidOperationException>(Check);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("ownerId", "444455556666")]
    [InlineData("vpcId", "vpc-00000000000000002")]
    [InlineData("subnetId", "subnet-00000000000000003")]
    [InlineData("availabilityZoneId", "use1-az2")]
    [InlineData("interfaceType", "interface")]
    [InlineData("status", "detaching")]
    [InlineData("requesterManaged", "false")]
    [InlineData("description", "VPC Endpoint Interface vpce-00000000000000002")]
    [InlineData("groupId", "sg-00000000000000009")]
    [InlineData("networkInterfaceId", "eni-0000000000000000b")]
    [InlineData("ip", "10.84.0.1")]
    [InlineData("ip", "10.84.1.10")]
    [InlineData("same-subnet", null)]
    [InlineData("association", null)]
    [InlineData("secondary", null)]
    [InlineData("nextToken", "next")]
    public async Task Interface_inventory_requires_exact_private_endpoint_placements(string? field, string? value)
    {
        var release = await signing.CreateAsync(mutate: ComputeEndpointTests.Bind);
        var subnets = ComputeRouteContract.Subnets(release, Cidr, Subnets());
        var endpoints = ComputeEndpointContract.Endpoints(release, XElement.Parse(ComputeEndpointTests.Original("endpoints")));
        var response = XElement.Parse(ComputeEndpointTests.Original("interfaces"));
        var rows = response.Element(Ns + "networkInterfaceSet")!.Elements().ToArray();
        var first = rows[0];
        switch (field)
        {
            case "ip":
                foreach (var address in first.Descendants(Ns + "privateIpAddress")) address.Value = value!;
                break;
            case "same-subnet":
                foreach (var key in new[] { "subnetId", "availabilityZone", "availabilityZoneId" })
                    rows[1].Element(Ns + key)!.Value = first.Element(Ns + key)!.Value;
                foreach (var address in rows[1].Descendants(Ns + "privateIpAddress")) address.Value = "10.84.0.55";
                break;
            case "association": first.Add(E("association", E("publicIp", "192.0.2.1"))); break;
            case "secondary": first.Element(Ns + "privateIpAddressesSet")!.Add(E("item", E("privateIpAddress", "10.84.0.99"), E("primary", "false"))); break;
            case "nextToken": response.Add(E(field, value)); break;
            case not null: first.Descendants(Ns + field).First().Value = value!; break;
        }
        void Check() => ComputeNetworkObservation.Interfaces(release, subnets, endpoints, Parse(response));
        if (field is null) Check();
        else Assert.Throws<InvalidOperationException>(Check);
    }
}
