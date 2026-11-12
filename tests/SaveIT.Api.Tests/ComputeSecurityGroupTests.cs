using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeSecurityGroupTests(ComputeReleaseFixture signing,ComputeConfigurationTests.Fixture configurationFixture):IClassFixture<ComputeReleaseFixture>,IClassFixture<ComputeConfigurationTests.Fixture>
{
    private static readonly XNamespace Ns=ComputeQueryXml.Namespace;
    private static JsonObject Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-network-graph.json")))!.AsObject();
    private static XElement E(string name,params object?[] values)=>new(Ns+name,values);
    internal static void Bind(JsonNode data)=>data["network"]!["bindings"]=Fixture()["bindings"]!.DeepClone();
    private Task<ComputeRelease> Release()=>signing.CreateAsync(mutate:Bind);
    internal static XElement Observed(ComputeRelease release)
    {
        var fixture=Fixture();var ids=release.Data.Network.SecurityGroups.Concat(ComputeNetworkBindings.Require(release).SecurityGroups).ToDictionary(x=>x.Key,x=>x.Value);
        ids.Add("default",fixture["defaultGroupId"]!.GetValue<string>());var edges=fixture["edges"]!.AsArray();
        XElement Permissions(string role,bool inbound)=>E(inbound?"ipPermissions":"ipPermissionsEgress",edges.Where(e=>e![inbound?"to":"from"]!.GetValue<string>()==role).GroupBy(e=>e!["port"]!.GetValue<int>()).Select(g=>
            E("item",E("ipProtocol","tcp"),E("fromPort",g.Key),E("toPort",g.Key),
                E("groups",g.Where(e=>e!["to"]!.GetValue<string>()!="layers").Select(e=>E("item",E("userId",release.Scope.AccountId),E("groupId",ids[e![inbound?"from":"to"]!.GetValue<string>()])))),
                E("ipRanges"),E("ipv6Ranges"),E("prefixListIds",g.Where(e=>!inbound&&e!["to"]!.GetValue<string>()=="layers").Select(_=>E("item",E("prefixListId",ComputeNetworkBindings.Require(release).S3PrefixListId)))))));
        var groups=ids.Select(pair=>{
            var (role,id)=pair;var suffix=role is "ecr-api" or "ecr-dkr" or "logs" or "configuration" or "web-token"?role+"-endpoint":role;
            var tags=new Dictionary<string,string>{{"Project","SaveIT"},{"Cell",release.Scope.Cell},{"Environment",release.Scope.Environment}};
            if(role is "api" or "web" or "worker" or "deployment" or "relay" or "web-token")tags.Add("Process",role);
            return E("item",E("ownerId",release.Scope.AccountId),E("groupId",id),E("groupName",role=="default"?"default":release.Scope.Name+"-"+suffix+"-synthetic"),E("groupDescription","synthetic private graph"),E("vpcId",release.Data.Network.VpcId),
                Permissions(role,true),Permissions(role,false),E("tagSet",tags.Select(t=>E("item",E("key",t.Key),E("value",t.Value))))) ;
        });
        return E("DescribeSecurityGroupsResponse",E("requestId","synthetic-request"),E("securityGroupInfo",groups));
    }
    private static XElement Parse(XElement response)=>ComputeQueryXml.Read(Encoding.UTF8.GetBytes(response.ToString(SaveOptions.DisableFormatting)),"DescribeSecurityGroups");
    private static XElement Group(XElement response,ComputeRelease release,string role)=>response.Element(Ns+"securityGroupInfo")!.Elements().Single(e=>e.Element(Ns+"groupId")!.Value==(role=="default"?Fixture()["defaultGroupId"]!.GetValue<string>():release.Data.Network.SecurityGroups.GetValueOrDefault(role)??ComputeNetworkBindings.Require(release).SecurityGroups[role]));
    [Fact]
    public async Task Complete_authenticated_graph_accepts_grouped_rules_and_empty_default_group()
    {
        var release=await Release();ComputeSecurityGroups.Validate(release,Parse(Observed(release)));
        Assert.Equal(13,Observed(release).Element(Ns+"securityGroupInfo")!.Elements().Count());
    }
    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("duplicate")] [InlineData("default-self")]
    [InlineData("owner")] [InlineData("vpc")] [InlineData("role-id")] [InlineData("tag")] [InlineData("name")] [InlineData("namespace")]
    [InlineData("pagination")] [InlineData("duplicate-envelope")] [InlineData("unknown-root")] [InlineData("unknown-group")]
    [InlineData("cidr")] [InlineData("ipv6")] [InlineData("all-protocols")] [InlineData("range")] [InlineData("port")]
    [InlineData("peering")] [InlineData("foreign-owner")] [InlineData("foreign-vpc")] [InlineData("duplicate-edge")]
    [InlineData("extra-prefix")] [InlineData("different-prefix")] [InlineData("missing-reciprocal")] [InlineData("unknown-rule")]
    public async Task Unapproved_group_inventory_and_rule_authority_fail_closed(string mode)
    {
        var release=await Release();var xml=Observed(release);var api=Group(xml,release,"api");var rule=api.Element(Ns+"ipPermissionsEgress")!.Elements().First();
        if(mode=="missing")Group(xml,release,"web-token").Remove();if(mode=="extra")xml.Element(Ns+"securityGroupInfo")!.Add(new XElement(api));if(mode=="duplicate")Group(xml,release,"worker").ReplaceWith(new XElement(api));
        if(mode=="default-self")Group(xml,release,"default").Element(Ns+"ipPermissions")!.Add(new XElement(rule));
        if(mode=="owner")api.Element(Ns+"ownerId")!.Value="444455556666";if(mode=="vpc")api.Element(Ns+"vpcId")!.Value="vpc-00000000000000002";
        if(mode=="role-id")api.Element(Ns+"groupId")!.Value="sg-fffffffffffffffff";if(mode=="tag")api.Element(Ns+"tagSet")!.Elements().First().Element(Ns+"value")!.Value="foreign";
        if(mode=="name")api.Element(Ns+"groupName")!.Value="foreign";if(mode=="namespace")api.Element(Ns+"ownerId")!.Name=XName.Get("ownerId","https://foreign.invalid");
        if(mode=="pagination")xml.Add(E("nextToken","private-next-page"));if(mode=="duplicate-envelope")xml.Add(E("requestId","duplicate"));if(mode=="unknown-root")xml.Add(E("extraAuthority","private"));if(mode=="unknown-group")api.Add(E("extraAuthority","private"));
        if(mode=="cidr")rule.Element(Ns+"ipRanges")!.Add(E("item",E("cidrIp","0.0.0.0/0")));if(mode=="ipv6")rule.Element(Ns+"ipv6Ranges")!.Add(E("item",E("cidrIpv6","::/0")));
        if(mode=="all-protocols")rule.Element(Ns+"ipProtocol")!.Value="-1";if(mode=="range")rule.Element(Ns+"toPort")!.Value="65535";if(mode=="port"){rule.Element(Ns+"fromPort")!.Value="443";rule.Element(Ns+"toPort")!.Value="443";}
        if(mode=="peering")rule.Element(Ns+"groups")!.Elements().First().Add(E("vpcPeeringConnectionId","pcx-00000000000000001"));
        if(mode=="foreign-owner")rule.Element(Ns+"groups")!.Elements().First().Element(Ns+"userId")!.Value="444455556666";
        if(mode=="foreign-vpc")rule.Element(Ns+"groups")!.Elements().First().Add(E("vpcId","vpc-00000000000000002"));
        if(mode=="duplicate-edge")api.Element(Ns+"ipPermissionsEgress")!.Add(new XElement(rule));
        var prefix=api.Element(Ns+"ipPermissionsEgress")!.Descendants(Ns+"prefixListId").Single();if(mode=="different-prefix")prefix.Value="pl-ffffffff";if(mode=="extra-prefix")prefix.Parent!.Parent!.Add(new XElement(prefix.Parent!));
        if(mode=="missing-reciprocal")Group(xml,release,"database").Element(Ns+"ipPermissions")!.RemoveNodes();if(mode=="unknown-rule")rule.Add(E("extraAuthority","private"));
        Assert.ThrowsAny<Exception>(()=>ComputeSecurityGroups.Validate(release,Parse(xml)));
    }
    [Theory] [InlineData("dtd")] [InlineData("comment")] [InlineData("attribute")] [InlineData("cdata")] [InlineData("depth")]
    public async Task XML_parser_refuses_external_entities_attributes_and_nonsemantic_aliases(string mode)
    {
        var release=await Release();var xml=Observed(release);if(mode=="attribute")xml.SetAttributeValue("authority","private");
        if(mode=="comment")xml.Add(new XComment("private"));if(mode=="cdata")xml.Element(Ns+"requestId")!.ReplaceNodes(new XCData("private"));
        if(mode=="depth"){var x=E("nested");xml.Add(x);for(var i=0;i<25;i++){var child=E("nested");x.Add(child);x=child;}}
        var body=xml.ToString();if(mode=="dtd")body="<!DOCTYPE x [<!ENTITY private SYSTEM 'https://foreign.invalid/private'>]>"+body;
        Assert.ThrowsAny<Exception>(()=>ComputeQueryXml.Read(Encoding.UTF8.GetBytes(body),"DescribeSecurityGroups"));
    }
    [Theory]
    [InlineData("database-subnet")] [InlineData("routes")] [InlineData("groups")] [InlineData("endpoint")] [InlineData("hash")] [InlineData("prefix")] [InlineData("unknown")]
    public async Task Valid_signatures_do_not_allow_malformed_or_aliased_network_bindings(string mode)
    {
        var operation=signing.CreateAsync(mutate:d=>{
            Bind(d);var b=d["network"]!["bindings"]!;
            if(mode=="database-subnet")b["databaseSubnetIds"]![0]="subnet-00000000000000001";
            if(mode=="routes")b["routeTables"]!["application"]=b["routeTables"]!["database"]!.DeepClone();
            if(mode=="groups")b["securityGroups"]!["database"]="sg-00000000000000001";
            if(mode=="endpoint")b["endpoints"]!["web-token"]=b["endpoints"]!["layers"]!.DeepClone();
            if(mode=="hash")b["endpointPolicySha256"]!["layers"]="not-a-policy-hash";if(mode=="prefix")b["s3PrefixListId"]="pl-FFFFFFFF";if(mode=="unknown")b["callerApproved"]=true;
        });var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeRelease.Failure,error.Message);
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>run(request,ct);}
    private static Task<ComputeAwsSession> Session()=>ComputeAwsSession.ReadAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new{accessKeyId="ASIA0000000000000000",secretAccessKey=new string('a',40),sessionToken="synthetic-temporary-session-token",expiration=DateTimeOffset.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")})),CancellationToken.None);
    private async Task<(ComputeImageTests.Bundle Bundle,Func<string,HttpMessageHandler> Provider,List<string> Calls)> Acquisition(Func<XElement,string>? change=null,Func<CancellationToken,Task>? wait=null,long? expires=null,Action<HttpResponseMessage>? response=null)
    {
        var configuration=new ComputeConfigurationTests(signing,configurationFixture);var bundle=await new ComputeImageTests(signing).Images(expires:expires,approve:d=>{configuration.Approve(d);Bind(d);});var calls=new List<string>();
        var prior=configuration.Provider(bundle,calls);
        return (bundle,service=>service!="ec2"?prior(service):new Handler(async(request,ct)=>{
            calls.Add("groups");Assert.Equal("https://ec2.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);Assert.Contains("/us-east-1/ec2/aws4_request",request.Headers.GetValues("Authorization").Single());
            Assert.False(request.Headers.Contains("X-Amz-Target"));var parts=(await request.Content!.ReadAsStringAsync(ct)).Split('&').ToDictionary(x=>Uri.UnescapeDataString(x.Split('=')[0]),x=>Uri.UnescapeDataString(x.Split('=')[1]));
            Assert.Equal(5,parts.Count);Assert.Equal("DescribeSecurityGroups",parts["Action"]);Assert.Equal("2016-11-15",parts["Version"]);Assert.Equal("1000",parts["MaxResults"]);Assert.Equal("vpc-id",parts["Filter.1.Name"]);Assert.Equal(bundle.Release.Data.Network.VpcId,parts["Filter.1.Value.1"]);
            if(wait is not null)await wait(ct);var observed=Observed(bundle.Release);var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(change?.Invoke(observed)??observed.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"text/xml")};response?.Invoke(result);return result;
        }),calls);
    }
    [Fact]
    public async Task Official_query_SDK_acquires_one_complete_VPC_graph_after_private_configuration_checks()
    {
        var f=await Acquisition();var result=await AcquiredComputeSecurityGroups.ReadAsync(f.Bundle.Release,await Session(),f.Provider,configurationFixture.Trust,CancellationToken.None);
        Assert.Same(f.Bundle.Release,result.Configuration.Images.Cell.Tasks.Release);Assert.Equal(20,f.Calls.Count);Assert.Equal("groups",f.Calls[^1]);
    }
    [Fact]
    public async Task Legacy_release_is_still_inspectable_but_cannot_acquire_network_or_contact_a_provider()
    {
        var release=await signing.CreateAsync();Assert.Null(release.Data.Network.Bindings);var calls=0;
        await Failure(AcquiredComputeSecurityGroups.ReadAsync(release,await Session(),_=>{calls++;throw new InvalidOperationException();},configurationFixture.Trust,CancellationToken.None));Assert.Equal(0,calls);
    }
    private static async Task Failure(Task operation)
    {var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeSecurityGroupAcquisition.Failure,error.Message);Assert.Null(error.InnerException);}
    [Fact]
    public async Task Raw_unknown_XML_is_rejected_even_when_the_SDK_would_discard_it()
    {
        var f=await Acquisition(x=>{x.Add(E("unknownAuthority","private-marker"));return x.ToString();});await Failure(AcquiredComputeSecurityGroups.ReadAsync(f.Bundle.Release,await Session(),f.Provider,configurationFixture.Trust,CancellationToken.None));Assert.Equal(20,f.Calls.Count);
    }
    [Theory] [InlineData("redirect")] [InlineData("encoding")] [InlineData("trailer")] [InlineData("length")] [InlineData("overflow")]
    public async Task Query_response_framing_fails_without_retry_or_private_provider_output(string mode)
    {
        var f=await Acquisition(change:mode=="overflow"?x=>x.ToString().PadRight(524289):null,response:r=>{
            if(mode=="redirect"){r.StatusCode=HttpStatusCode.Redirect;r.Headers.Location=new Uri("https://foreign.invalid/private-marker");}
            if(mode=="encoding")r.Content.Headers.ContentEncoding.Add("gzip");if(mode=="trailer")r.Headers.TryAddWithoutValidation("Trailer","private-marker");
            if(mode=="length")r.Content.Headers.ContentLength=(r.Content.Headers.ContentLength??0)+1;
        });
        await Failure(AcquiredComputeSecurityGroups.ReadAsync(f.Bundle.Release,await Session(),f.Provider,configurationFixture.Trust,CancellationToken.None));Assert.Equal(20,f.Calls.Count);
    }
    [Theory] [InlineData("duplicate")] [InlineData("extra-filter")] [InlineData("action")] [InlineData("vpc")] [InlineData("percent")] [InlineData("token")]
    public async Task Query_request_factory_refuses_extra_or_decoded_duplicate_authority(string mode)
    {
        var read=ComputeSdkRead.SecurityGroups(await Release());var body="Action=DescribeSecurityGroups&Version=2016-11-15&Filter.1.Name=vpc-id&Filter.1.Value.1=vpc-00000000000000001&MaxResults=1000";
        read.ValidateRequest(body);
        if(mode=="duplicate")body+="&%41ction=DescribeSecurityGroups";if(mode=="extra-filter")body+="&Filter.2.Name=tag:Project&Filter.2.Value.1=SaveIT";
        if(mode=="action")body=body.Replace("DescribeSecurityGroups","RunInstances",StringComparison.Ordinal);if(mode=="vpc")body=body.Replace("vpc-00000000000000001","vpc-00000000000000002",StringComparison.Ordinal);
        if(mode=="percent")body+="&%GG=private";if(mode=="token")body+="&NextToken=private-marker";
        Assert.ThrowsAny<Exception>(()=>read.ValidateRequest(body));
    }
    [Fact]
    public async Task Observed_cancellation_stops_the_query_read()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
        var f=await Acquisition(wait:async ct=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);});
        var operation=AcquiredComputeSecurityGroups.ReadAsync(f.Bundle.Release,await Session(),f.Provider,configurationFixture.Trust,cancel.Token);
        try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));}finally{cancel.Cancel();}await Failure(operation.WaitAsync(TimeSpan.FromSeconds(5)));Assert.Equal(20,f.Calls.Count);
    }
    [Fact]
    public async Task Actual_query_response_after_signed_expiry_never_returns_an_acquired_graph()
    {
        long expires=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+4;var returned=false;
        var f=await Acquisition(expires:expires,wait:async ct=>{while(DateTimeOffset.UtcNow.ToUnixTimeSeconds()<expires)await Task.Delay(20,ct);returned=true;});
        await Failure(AcquiredComputeSecurityGroups.ReadAsync(f.Bundle.Release,await Session(),f.Provider,configurationFixture.Trust,CancellationToken.None));Assert.True(returned);Assert.Equal(20,f.Calls.Count);
    }
}
