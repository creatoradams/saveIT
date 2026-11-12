using System.Net;
using System.Text;
using System.Text.Json;
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
        var release=await Release();var cidr=ComputeRouteContract.Vpc(release,ParseOriginal("vpc","DescribeVpcs"));Assert.Equal(Cidr,cidr);
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
    public async Task Real_SDK_query_sequence_stops_at_first_invalid_original_response(string? invalid,int reads)
    {
        var prior=await new ComputeSecurityGroupTests(signing,configuration).Acquisition();var count=0;var returned=false;var clock=new FinalReadClock();
        HttpMessageHandler Provider(string service)=>service!="ec2"||count++==0?prior.Provider(service):new Handler(async(request,ct)=>{
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
                default:throw new InvalidOperationException();
            }
            prior.Calls.Add(stage);var body=Original(stage);
            if(stage==invalid){var response=Observation(stage);response.Add(E("unreviewedAuthority","synthetic-private-marker"));body=response.ToString(SaveOptions.DisableFormatting);}
            if(stage=="routes"&&invalid=="expired"){clock.Now=DateTimeOffset.FromUnixTimeSeconds(prior.Bundle.Release.ExpiresAt);returned=true;}
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"text/xml")};
        });
        var operation=AcquiredComputeRoutes.ReadAsync(prior.Bundle.Release,await ComputeSecurityGroupTests.Session(),Provider,configuration.Trust,CancellationToken.None,clock);
        if(invalid is null)Assert.Same(prior.Bundle.Release,(await operation).SecurityGroups.Configuration.Images.Cell.Tasks.Release);
        else {var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeRouteAcquisition.Failure,error.Message);Assert.Null(error.InnerException);}
        Assert.Equal(new[]{"vpc","enableDnsSupport","enableDnsHostnames","subnets","routes"}.Take(reads),prior.Calls.Skip(20));
        Assert.Equal(20+reads,prior.Calls.Count);
        if(invalid=="expired")Assert.True(returned,"A valid response at simulated expiry must reach the validation boundary.");
    }
}
