using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeReleaseFixture : IDisposable
{
    internal readonly RSA Key=RSA.Create(3072);
    internal byte[] PublicKey=>Key.ExportSubjectPublicKeyInfo();
    internal string Pin=>Convert.ToHexStringLower(SHA256.HashData(PublicKey));
    internal async Task<ComputeRelease> CreateAsync(long expected=1,Guid? operation=null,Guid? generation=null,long? expires=null,Action<JsonNode>? mutate=null)
    {
        var data=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-contract.json")))!;
        data["expectedVersion"]=expected;data["generation"]=(generation??Guid.NewGuid()).ToString("D");mutate?.Invoke(data);var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload=new JsonObject{["iss"]="saveit-release",["aud"]="saveit-compute:111122223333:us-east-1:pilot:staging",["iat"]=now-1,["exp"]=expires??now+299,["jti"]=(operation??Guid.NewGuid()).ToString("D"),["contract"]=data};
        var header=new JsonObject{["alg"]="PS256",["typ"]=ComputeRelease.Type,["kid"]="sha256-"+Pin};
        var body=Base64UrlEncoder.Encode(header.ToJsonString())+"."+Base64UrlEncoder.Encode(payload.ToJsonString());
        var token=body+"."+Base64UrlEncoder.Encode(Key.SignData(Encoding.ASCII.GetBytes(body),HashAlgorithmName.SHA256,RSASignaturePadding.Pss));
        return await ComputeRelease.VerifyAsync(token,new("111122223333","us-east-1","pilot","staging"),PublicKey,Pin);
    }
    public void Dispose()=>Key.Dispose();
}

public sealed class ComputeReleaseTests(ComputeReleaseFixture fixture) : IClassFixture<ComputeReleaseFixture>
{
    private static readonly ComputeScope Scope=new("111122223333","us-east-1","pilot","staging");
    private const string NamespaceArn = "arn:aws:servicediscovery:us-east-1:111122223333:namespace/ns-0000000000000001";
    private const string HostedZoneId = "Z0000000000000001";

    [Fact]
    public async Task Private_dns_binding_is_optional_for_legacy_inspection_and_retained_when_signed()
    {
        Assert.Null((await fixture.CreateAsync()).Data.Network.PrivateDns);
        var release = await fixture.CreateAsync(mutate: data => BindDns(data, NamespaceArn, HostedZoneId));
        Assert.Equal(new ComputePrivateDnsBinding(NamespaceArn, HostedZoneId), release.Data.Network.PrivateDns);
    }

    [Theory]
    [InlineData("arn:aws:servicediscovery:us-east-1:444455556666:namespace/ns-0000000000000001", HostedZoneId)]
    [InlineData("arn:aws:servicediscovery:us-west-2:111122223333:namespace/ns-0000000000000001", HostedZoneId)]
    [InlineData("arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001", HostedZoneId)]
    [InlineData(NamespaceArn, "/hostedzone/Z0000000000000001")]
    [InlineData(NamespaceArn, "z0000000000000001")]
    [InlineData(NamespaceArn, null)]
    public async Task Private_dns_binding_rejects_foreign_arns_and_noncanonical_zone_ids(string arn, string? zone)
    {
        await Rejected(() => fixture.CreateAsync(mutate: data => BindDns(data, arn, zone)));
    }

    private static void BindDns(JsonNode data, string arn, string? zone) =>
        data["network"]!["privateDns"] = new JsonObject { ["namespaceArn"] = arn, ["hostedZoneId"] = zone };

    private static JsonObject Contract()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-contract.json")))!.AsObject();
    private static JsonObject Observations()=>JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","compute-release-observations.json")))!.AsObject();
    private static JsonObject Claims(JsonObject? contract=null)
    {
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new() { ["iss"]="saveit-release",["aud"]=Scope.Audience,["iat"]=now-1,["exp"]=now+299,["jti"]=Guid.NewGuid().ToString("D"),["contract"]=contract??Contract() };
    }
    private JsonObject Header()=>new() { ["alg"]="PS256",["typ"]=ComputeRelease.Type,["kid"]="sha256-"+fixture.Pin };
    private string Sign(string payload,string? header=null,RSA? key=null)
    {
        var body=Base64UrlEncoder.Encode(header??Header().ToJsonString())+"."+Base64UrlEncoder.Encode(payload);
        return body+"."+Base64UrlEncoder.Encode((key??fixture.Key).SignData(Encoding.ASCII.GetBytes(body),HashAlgorithmName.SHA256,RSASignaturePadding.Pss));
    }
    private Task<ComputeRelease> Verify(string token,ComputeScope? scope=null,byte[]? spki=null,string? pin=null)=>ComputeRelease.VerifyAsync(token,scope??Scope,spki??fixture.PublicKey,pin??fixture.Pin);
    private async Task Rejected(Func<Task> action)
    {
        var error=await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Equal(ComputeRelease.Failure,error.Message);Assert.Null(error.InnerException);
    }
    private Task<ComputeRelease> Check(string token,JsonObject? observations=null,CancellationToken ct=default)
    {
        var input=new JsonObject { ["signedRelease"]=token,["observations"]=observations??Observations() };
        return ComputeReleasePreflight.ValidateAsync(new MemoryStream(Encoding.UTF8.GetBytes(input.ToJsonString())),Scope,fixture.PublicKey,fixture.Pin,ct);
    }
    private static JsonObject Task(JsonObject observations,string process)=>observations[process]!["taskDefinition"]!.AsObject();
    private static JsonObject Container(JsonObject observations,string process)=>Task(observations,process)["containerDefinitions"]![0]!.AsObject();

    [Fact]
    public async Task Standard_PSS_signature_admits_all_four_independent_frozen_producer_fixtures_and_binds_exact_bytes()
    {
        var claims=Claims();var token=Sign(claims.ToJsonString());var release=await Check(token);
        Assert.Equal(ComputeRelease.CanonicalGuid(claims["jti"]!.GetValue<string>()),release.OperationId);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token))),release.PlanSha256);
        Assert.Equal(fixture.Pin,release.KeySha256);Assert.Equal(1,release.Data.ExpectedVersion);
        Assert.Equal("70000000-0000-0000-0000-000000000001",release.Data.Generation);
        // Repeated independently imported anchors exercise provider-cache lifetime.
        for(var i=0;i<5;i++) Assert.Equal(release.PlanSha256,(await Check(token)).PlanSha256);
    }

    [Theory]
    [InlineData("alg","\"RS256\"")] [InlineData("alg","\"none\"")] [InlineData("typ","\"JWT\"")]
    [InlineData("kid","\"other\"")] [InlineData("jku","\"https://foreign.invalid/key\"")]
    [InlineData("jwk","{\"kty\":\"RSA\"}")] [InlineData("crit","[\"exp\"]")]
    public async Task Signed_header_algorithm_type_key_substitution_and_extra_authority_fail(string field,string json)
    { var h=Header();h[field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(Claims().ToJsonString(),h.ToJsonString()))); }

    [Theory]
    [InlineData("iss","\"foreign\"")] [InlineData("aud","\"saveit-compute:111122223333:us-east-1:other:staging\"")]
    [InlineData("aud","[\"saveit-compute:111122223333:us-east-1:pilot:staging\"]")]
    [InlineData("jti","\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("jti","\"AAAAAAAA-0000-0000-0000-000000000001\"")] [InlineData("approval","true")]
    [InlineData("nbf","1")]
    public async Task Signed_wrong_claim_types_issuer_audience_operation_and_unknown_claims_fail(string field,string json)
    {var p=Claims();p[field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(p.ToJsonString())));}

    [Theory]
    [InlineData("iss")] [InlineData("aud")] [InlineData("iat")] [InlineData("exp")] [InlineData("jti")] [InlineData("contract")]
    public async Task Missing_claim_fails(string field)
    {var p=Claims();p.Remove(field);await Rejected(()=>Verify(Sign(p.ToJsonString())));}

    [Theory]
    [InlineData(120,300)] [InlineData(-1000,100)] [InlineData(-1,-1)] [InlineData(-100,-200)] [InlineData(-1,1000)]
    public async Task Signed_expired_future_old_and_overlong_approvals_fail(int issued,int expires)
    {
        var p=Claims();var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();p["iat"]=now+issued;p["exp"]=now+expires;
        await Rejected(()=>Verify(Sign(p.ToJsonString())));
    }

    [Fact]
    public async Task Forgery_wrong_anchor_weak_key_noncanonical_encoding_and_trailing_DER_fail()
    {
        var token=Sign(Claims().ToJsonString());var parts=token.Split('.');
        await Rejected(()=>Verify(parts[0]+"."+parts[1]+"."+new string('A',parts[2].Length)));
        using var foreign=RSA.Create(3072);await Rejected(()=>Verify(Sign(Claims().ToJsonString(),key:foreign)));
        await Rejected(()=>Verify(token,pin:new string('0',64)));
        var trailing=fixture.PublicKey.Concat(new byte[]{0}).ToArray();await Rejected(()=>Verify(token,spki:trailing,pin:Convert.ToHexStringLower(SHA256.HashData(trailing))));
        using var weak=RSA.Create(2048);var publicWeak=weak.ExportSubjectPublicKeyInfo();await Rejected(()=>Verify(token,spki:publicWeak,pin:Convert.ToHexStringLower(SHA256.HashData(publicWeak))));
        await Rejected(()=>Verify(parts[0]+"=."+parts[1]+"."+parts[2]));
        await Rejected(()=>Verify(token+".extra"));await Rejected(()=>Verify(new string('x',49153)));
    }

    [Theory]
    [InlineData("expectedVersion","0")] [InlineData("generation","\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("generation","\"AAAAAAAA-0000-0000-0000-000000000001\"")] [InlineData("sourceRevision","\"main\"")]
    [InlineData("overrides","{}")] [InlineData("network","null")] [InlineData("runtime","null")]
    public async Task Even_signed_contracts_cannot_extend_the_fixed_schema_or_omit_bindings(string field,string json)
    {var d=Contract();d[field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));}

    [Theory]
    [InlineData("retiredKeyCount","5")] [InlineData("workerTenants","[]")] [InlineData("clientLeafSha256","[]")]
    [InlineData("publicOrigin","\"http://saveit.example.com\"")] [InlineData("oktaIssuer","\"https://user:pass@foreign.invalid\"")]
    [InlineData("poolId","\"us-west-2:10000000-0000-0000-0000-000000000001\"")]
    [InlineData("workloadIssuer","\"https://foreign.invalid\"")] [InlineData("fileSystemId","\"fs-other\"")]
    [InlineData("databaseCaSha256","\"private-marker\"")] [InlineData("arbitraryEnvironment","{}")]
    public async Task Signed_runtime_metadata_is_bounded_canonical_and_public_only(string field,string json)
    {var d=Contract();d["runtime"]![field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));}

    [Theory]
    [InlineData("111122223334","us-east-1","pilot","staging")]
    [InlineData("111122223333","us-west-2","pilot","staging")]
    [InlineData("111122223333","us-east-1","other","staging")]
    [InlineData("111122223333","us-east-1","pilot","production")]
    public async Task Independently_configured_target_cannot_be_selected_by_the_signed_input(string account,string region,string cell,string stage)
    {await Rejected(()=>Verify(Sign(Claims().ToJsonString()),new(account,region,cell,stage)));}

    [Fact]
    public async Task Signed_task_selectors_and_network_identity_reject_aliases_cross_scope_duplicates_and_extra_targets()
    {
        foreach(var (field,json) in new[]{("taskDefinitionArn","\"arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-api:010\""),("image","\"111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/api:latest\""),("configurationVersionId","\"AWSCURRENT\""),("configurationArn","\"arn:aws:secretsmanager:us-east-1:444455556666:secret:saveit/pilot/staging/api/config-Abcd00\"")})
        {var d=Contract();d["tasks"]!["api"]![field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));}
        var contract=Contract();contract["tasks"]!["web"]!["configurationArn"]=contract["tasks"]!["api"]!["configurationArn"]!.DeepClone();await Rejected(()=>Verify(Sign(Claims(contract).ToJsonString())));
        contract=Contract();contract["tasks"]!["extra"]=contract["tasks"]!["api"]!.DeepClone();await Rejected(()=>Verify(Sign(Claims(contract).ToJsonString())));
        foreach(var (field,json) in new[]{("vpcId","\"default\""),("subnetIds","[\"subnet-00000000000000001\",\"subnet-00000000000000001\"]"),("securityGroups","{\"api\":\"sg-00000000000000001\",\"web\":\"sg-00000000000000001\",\"worker\":\"sg-00000000000000003\",\"relay\":\"sg-00000000000000006\"}"),("relayDiscoveryServiceArn","\"arn:aws:servicediscovery:us-east-1:111122223333:service/srv-0000000000000001\"")})
        {var d=Contract();d["network"]![field]=JsonNode.Parse(json);await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));}
    }

    [Theory]
    [InlineData("api")] [InlineData("web")] [InlineData("worker")] [InlineData("relay")]
    public async Task All_four_tasks_refuse_extra_authority_and_fixed_metadata_substitution(string process)
    {
        var token=Sign(Claims().ToJsonString());
        foreach(var (field,json) in new[]{("privileged","true"),("user","\"0:0\""),("readonlyRootFilesystem","false"),("command","[\"arbitrary\"]"),("entryPoint","[\"sh\",\"-c\"]"),("restartPolicy","{\"enabled\":true}"),("credentialSpecs","[\"foreign\"]"),("environmentFiles","[{\"type\":\"s3\",\"value\":\"foreign\"}]"),("portMappings","[]")})
        {
            if(process=="worker"&&field=="portMappings") continue;
            var observations=Observations();Container(observations,process)[field]=JsonNode.Parse(json);await Rejected(()=>Check(token,observations));
        }
        foreach(var (field,json) in new[]{("pidMode","\"host\""),("taskRoleArn","\"foreign\""),("cpu","\"4096\""),("status","\"INACTIVE\""),("enableFaultInjection","true"),("ephemeralStorage","{\"sizeInGiB\":100}")})
        {var observations=Observations();Task(observations,process)[field]=JsonNode.Parse(json);await Rejected(()=>Check(token,observations));}
        var extra=Observations();Task(extra,process)["containerDefinitions"]!.AsArray().Add(Container(extra,process).DeepClone());await Rejected(()=>Check(token,extra));
        extra=Observations();Container(extra,process)["environment"]!.AsArray().Add(new JsonObject{["name"]="AWS_ACCESS_KEY_ID",["value"]="synthetic-private-marker"});await Rejected(()=>Check(token,extra));
        extra=Observations();Container(extra,process)["environment"]!.AsArray().Add(Container(extra,process)["environment"]![0]!.DeepClone());await Rejected(()=>Check(token,extra));
        extra=Observations();Container(extra,process)["secrets"]!.AsArray().Add(new JsonObject{["name"]="foreign",["valueFrom"]="foreign"});await Rejected(()=>Check(token,extra));
    }

    [Fact]
    public async Task Stale_generation_missing_workload_mTLS_secret_version_and_EFS_bindings_fail()
    {
        var token=Sign(Claims().ToJsonString());
        foreach(var (process,field) in new[]{("api","Deployment__ComputeGeneration"),("worker","Deployment__ComputeGeneration"),("api","Deployment__ApiTls__ClientAuthentication__Enabled"),("web","SAVEIT_WORKLOAD_POOL_ID"),("relay","SAVEIT_WORKLOAD_ISSUER")})
        {
            var observations=Observations();var e=Container(observations,process)["environment"]!.AsArray();e.Remove(e.Single(x=>x!["name"]!.GetValue<string>()==field));await Rejected(()=>Check(token,observations));
        }
        var o=Observations();Container(o,"api")["secrets"]![0]!["valueFrom"]="arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/api/config-Abcd00:ConnectionStrings__App:AWSCURRENT:";await Rejected(()=>Check(token,o));
        o=Observations();Task(o,"api")["volumes"]!.AsArray().Single(x=>x!["name"]!.GetValue<string>()=="session-keys")!["efsVolumeConfiguration"]!["authorizationConfig"]!["iam"]="DISABLED";await Rejected(()=>Check(token,o));
        o=Observations();Task(o,"api")["volumes"]!.AsArray().Single(x=>x!["name"]!.GetValue<string>()=="session-keys")!["efsVolumeConfiguration"]!["transitEncryption"]="DISABLED";await Rejected(()=>Check(token,o));
    }

    [Fact]
    public async Task Signed_duplicate_escaped_alias_unknown_nested_field_and_missing_required_constructor_field_fail()
    {
        var p=Claims().ToJsonString();
        await Rejected(()=>Verify(Sign(p.Replace("\"contract\":{","\"contract\":{\"expectedVersion\":9,",StringComparison.Ordinal))));
        await Rejected(()=>Verify(Sign(p.Replace("\"expectedVersion\":1","\"expectedVersion\":2,\"expectedV\\u0065rsion\":1",StringComparison.Ordinal))));
        var h=Header().ToJsonString();await Rejected(()=>Verify(Sign(p,h[..^1]+",\"k\\u0069d\":\"sha256-"+fixture.Pin+"\"}")));
        var d=Contract();d["tasks"]!["web"]!.AsObject().Remove("configurationArn");await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));
        d=Contract();d["tasks"]!["web"]!["environment"]=new JsonObject();await Rejected(()=>Verify(Sign(Claims(d).ToJsonString())));
    }

    [Fact]
    public async Task Only_understood_defaults_and_semantic_ordering_normalize()
    {
        var token=Sign(Claims().ToJsonString());var o=Observations();
        foreach(var process in ComputeRelease.Processes)
        {
            var t=Task(o,process);t["registeredBy"]="untrusted-no-authority";t["registeredAt"]="untrusted";t["requiresAttributes"]=new JsonArray();t["compatibilities"]=new JsonArray("EC2","FARGATE");
            t["enableFaultInjection"]=false;t["placementConstraints"]=new JsonArray();t["ephemeralStorage"]=new JsonObject{["sizeInGiB"]=20};
            var c=Container(o,process);c["cpu"]=0;c["environmentFiles"]=new JsonArray();c["logConfiguration"]!["secretOptions"]=new JsonArray();
            c["environment"]=new JsonArray(c["environment"]!.AsArray().Reverse().Select(x=>x!.DeepClone()).ToArray());
            t["volumes"]=new JsonArray(t["volumes"]!.AsArray().Reverse().Select(x=>x!.DeepClone()).ToArray());
        }
        await Check(token,o);
    }

    [Fact]
    public async Task Original_input_utf8_duplicate_EOF_depth_size_and_cancellation_are_enforced()
    {
        var token=Sign(Claims().ToJsonString());var json=new JsonObject{["signedRelease"]=token,["observations"]=Observations()}.ToJsonString();
        Task<ComputeRelease> Read(byte[] input,CancellationToken ct=default)=>ComputeReleasePreflight.ValidateAsync(new MemoryStream(input),Scope,fixture.PublicKey,fixture.Pin,ct);
        await Read(Encoding.UTF8.GetBytes(json.PadRight(65536)));await Rejected(()=>Read(Encoding.UTF8.GetBytes(json.PadRight(65537))));
        await Rejected(()=>Read([0xff,0xfe]));await Rejected(()=>Read(Encoding.UTF8.GetBytes(json+"{}")));
        await Rejected(()=>Read(Encoding.UTF8.GetBytes(json.Replace("\"observations\":{","\"observations\":{\"api\":null,",StringComparison.Ordinal))));
        await Rejected(()=>Read(Encoding.UTF8.GetBytes(json.Replace("\"observations\":{","\"observations\":{\"tags\":"+new string('[',25)+"0"+new string(']',25)+",",StringComparison.Ordinal))));
        using var timeout=new CancellationTokenSource();timeout.Cancel();await Rejected(()=>Read(Encoding.UTF8.GetBytes(json),timeout.Token));
    }

    [Theory]
    [InlineData("valid")] [InlineData("private-drift")] [InlineData("mixed-arguments")] [InlineData("held-input")]
    public async Task Actual_cli_uses_independent_anchor_scope_bounded_EOF_and_sanitized_output(string mode)
    {
        var path=Path.GetTempFileName();await File.WriteAllBytesAsync(path,fixture.PublicKey);
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(typeof(ComputeReleasePreflight).Assembly.Location);start.ArgumentList.Add("check-compute-release");if(mode=="mixed-arguments")start.ArgumentList.Add("migrate");
        foreach(var (name,value) in new Dictionary<string,string>{["SAVEIT_COMPUTE_ACCOUNT_ID"]=Scope.AccountId,["SAVEIT_COMPUTE_REGION"]=Scope.Region,["SAVEIT_COMPUTE_CELL"]=Scope.Cell,["SAVEIT_COMPUTE_STAGE"]=Scope.Environment,["SAVEIT_COMPUTE_RELEASE_PUBLIC_KEY_FILE"]=path,["SAVEIT_COMPUTE_RELEASE_KEY_SHA256"]=fixture.Pin,["SAVEIT_COMPUTE_CONNECTION"]="synthetic-private-marker",["AWS_PROFILE"]="synthetic-private-marker",["AWS_ENDPOINT_URL"]="synthetic-private-marker",["HTTP_PROXY"]="synthetic-private-marker"})start.Environment[name]=value;
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        try
        {
            var o=Observations();if(mode=="private-drift")Container(o,"web")["environment"]!.AsArray().Add(new JsonObject{["name"]="ConnectionStrings__App",["value"]="synthetic-private-marker"});
            var input=new JsonObject{["signedRelease"]=Sign(Claims().ToJsonString()),["observations"]=o};
            if(mode!="mixed-arguments") {await process.StandardInput.WriteAsync(input.ToJsonString());await process.StandardInput.FlushAsync();}
            if(mode!="held-input")process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(17));
            Assert.Equal(mode=="valid"?0:1,process.ExitCode);Assert.Equal(mode=="valid"?ComputeReleasePreflight.Success:"",(await output).Trim());Assert.Equal(mode=="valid"?"":ComputeReleasePreflight.Failure,(await errors).Trim());
        }
        finally {process.StandardInput.Close();if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}File.Delete(path);}
    }
}
