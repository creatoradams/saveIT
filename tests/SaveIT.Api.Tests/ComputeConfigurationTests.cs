using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ComputeConfigurationTests(ComputeReleaseFixture signing,ComputeConfigurationTests.Fixture fixture):IClassFixture<ComputeReleaseFixture>,IClassFixture<ComputeConfigurationTests.Fixture>
{
    private const string Password="synthetic-cohort-only",Marker="synthetic-private-cohort-marker";
    public sealed class Fixture:IDisposable
    {
        internal ApiClientAuthenticationTests.Fixture Pki {get;}=new();
        internal X509Certificate2 Api {get;}
        internal X509Certificate2 Relay {get;}
        internal X509Certificate2 Protector {get;}
        internal X509Certificate2 Retired {get;}
        internal X509Certificate2 ForeignApi {get;}
        internal X509Certificate2 ForeignRelay {get;}
        internal ComputeConfigurationTrust Trust=>new(Pki.Path,Pki.Path,Pki.Path,Pki.Path);
        public Fixture()
        {
            Api=Pki.ServerForName("api.pilot.staging.saveit.internal");Relay=Pki.ServerForName("relay.pilot.staging.saveit.internal");
            using var key=RSA.Create(2048);var request=new CertificateRequest("CN=SaveIT synthetic session",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            Protector=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(2));
            Retired=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(-1));
            var caRequest=new CertificateRequest("CN=SaveIT synthetic foreign root",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
            using var foreign=caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(2));
            ForeignApi=Server(foreign,"api.pilot.staging.saveit.internal");ForeignRelay=Server(foreign,"relay.pilot.staging.saveit.internal");
        }
        private static X509Certificate2 Server(X509Certificate2 issuer,string host)
        {
            using var key=RSA.Create(2048);var request=new CertificateRequest("CN=SaveIT synthetic foreign server",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));var names=new SubjectAlternativeNameBuilder();names.AddDnsName(host);request.CertificateExtensions.Add(names.Build());
            using var issued=request.Create(issuer,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),RandomNumberGenerator.GetBytes(16));return issued.CopyWithPrivateKey(key);
        }
        public void Dispose(){Api.Dispose();Relay.Dispose();Protector.Dispose();Retired.Dispose();ForeignApi.Dispose();ForeignRelay.Dispose();Pki.Dispose();}
    }
    private static string Pfx(X509Certificate2 cert)
    {var bytes=cert.Export(X509ContentType.Pfx,Password);try{return Convert.ToBase64String(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}}
    private Dictionary<string,JsonObject> Values(int retired=0)
    {
        var result=new Dictionary<string,JsonObject>{
            ["api"]=new(){["ConnectionStrings__App"]=$"Host=db.synthetic.invalid;Port=5432;Database=saveit;Username=saveit_app;Password={Marker};SSL Mode=VerifyFull",
                ["Identity__Okta__ClientSecret"]=Marker,["Identity__KeyCertificateBase64"]=Pfx(fixture.Protector),["Identity__KeyCertificatePassword"]=Password,
                ["Deployment__ApiTls__CertificateBase64"]=Pfx(fixture.Api),["Deployment__ApiTls__CertificatePassword"]=Password},
            ["worker"]=new(){["ConnectionStrings__SearchWorker"]=$"Host=db.synthetic.invalid;Port=5432;Database=saveit;Username=saveit_search_worker;Password={Marker};SSL Mode=VerifyFull"},
            ["relay"]=new(){["CertificateBase64"]=Pfx(fixture.Pki.Clients["valid"]),["CertificatePassword"]=Password,
                ["ApiCaSha256"]=fixture.Pki.Hash,["ClientLeafSha256"]=Pin(fixture.Pki.Clients["valid"]),
                ["ServerCertificateBase64"]=Pfx(fixture.Relay),["ServerCertificatePassword"]=Password,["ServerLeafSha256"]=Pin(fixture.Relay)}
        };
        for(var i=0;i<retired;i++){result["api"][$"Identity__PreviousKeyCertificates__{i}__Base64"]=Pfx(fixture.Retired);result["api"][$"Identity__PreviousKeyCertificates__{i}__Password"]=Password;}
        return result;
    }
    private static string Pin(X509Certificate2 certificate)=>certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
    private void Approve(JsonNode data,int retired=0)
    {
        var r=data["runtime"]!;foreach(var field in new[]{"databaseCaSha256","relayCaSha256","clientCaSha256"})r[field]=fixture.Pki.Hash;
        r["clientLeafSha256"]=new JsonArray(Pin(fixture.Pki.Clients["valid"]));r["retiredKeyCount"]=retired;
    }
    private Task<ComputeRelease> Release(int retired=0)=>signing.CreateAsync(mutate:d=>Approve(d,retired));
    private static Dictionary<string,byte[]> Bytes(Dictionary<string,JsonObject> values)=>values.ToDictionary(x=>x.Key,x=>JsonSerializer.SerializeToUtf8Bytes(x.Value));
    private static JsonObject Envelope(ComputeRelease release,string process,JsonObject payload)=>new(){
        ["ARN"]=release.Data.Tasks[process].ConfigurationArn,["Name"]=$"{release.Scope.Path}/{process}/config",
        ["VersionId"]=release.Data.Tasks[process].ConfigurationVersionId,["SecretString"]=payload.ToJsonString(),
        ["CreatedDate"]=DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),["VersionStages"]=new JsonArray("AWSPREVIOUS")};
    private static async Task Failure(Task operation)
    {var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>operation);Assert.Equal(ComputeConfigurationAcquisition.Failure,error.Message);Assert.Null(error.InnerException);Assert.DoesNotContain(Marker,error.ToString());}

    [Theory] [InlineData(0)] [InlineData(4)]
    public async Task Whole_cohort_accepts_three_separate_transport_keys_and_current_or_expired_retired_protectors(int retired)
        =>await ComputeConfigurationCohort.ValidateAsync(await Release(retired),Bytes(Values(retired)),fixture.Trust,CancellationToken.None);

    [Theory]
    [InlineData("database-host")] [InlineData("database-port")] [InlineData("database-name")] [InlineData("worker-role")] [InlineData("database-tls")]
    [InlineData("api-name")] [InlineData("api-chain")] [InlineData("relay-chain")] [InlineData("client-chain")] [InlineData("client-pin")]
    [InlineData("api-ca")] [InlineData("client-ca")] [InlineData("relay-ca")] [InlineData("database-ca")]
    [InlineData("extra-field")] [InlineData("protector-transport")] [InlineData("protector-anchor")] [InlineData("retired-transport")]
    [InlineData("relay-client-key")] [InlineData("api-relay-key")] [InlineData("client-server-key")]
    public async Task Individually_parseable_or_approved_payloads_cannot_bypass_cohort_consistency_and_trust(string mode)
    {
        var retired=mode=="retired-transport"?1:0;var values=Values(retired);var release=await Release(retired);var trust=fixture.Trust;
        var worker=values["worker"]["ConnectionStrings__SearchWorker"]!.GetValue<string>();
        if(mode=="database-host")worker=worker.Replace("db.synthetic.invalid","other.synthetic.invalid",StringComparison.Ordinal);
        if(mode=="database-port")worker=worker.Replace("5432","5433",StringComparison.Ordinal);
        if(mode=="database-name")worker=worker.Replace("Database=saveit","Database=other",StringComparison.Ordinal);
        if(mode=="worker-role")worker=worker.Replace("saveit_search_worker","saveit_app",StringComparison.Ordinal);
        if(mode=="database-tls")worker=worker.Replace("VerifyFull","Require",StringComparison.Ordinal);
        values["worker"]["ConnectionStrings__SearchWorker"]=worker;
        if(mode=="api-name")values["api"]["Deployment__ApiTls__CertificateBase64"]=Pfx(fixture.Relay);
        if(mode=="api-chain")values["relay"]["ApiCaSha256"]=new string('0',64);
        if(mode=="client-pin")values["relay"]["ClientLeafSha256"]=Pin(fixture.Pki.Clients["spare"]);
        if(mode=="client-chain") {values["relay"]["CertificateBase64"]=Pfx(fixture.Pki.Clients["foreign"]);values["relay"]["ClientLeafSha256"]=Pin(fixture.Pki.Clients["foreign"]);release=await signing.CreateAsync(mutate:d=>{Approve(d);d["runtime"]!["clientLeafSha256"]=new JsonArray(Pin(fixture.Pki.Clients["foreign"]));});}
        if(mode=="relay-chain")trust=trust with {Relay="missing-synthetic-public-trust"};
        if(mode=="api-ca")trust=trust with {Api="missing-synthetic-public-trust"};
        if(mode=="client-ca")trust=trust with {Client="missing-synthetic-public-trust"};
        if(mode=="relay-ca")release=await signing.CreateAsync(mutate:d=>{Approve(d);d["runtime"]!["relayCaSha256"]=new string('0',64);});
        if(mode=="database-ca")release=await signing.CreateAsync(mutate:d=>{Approve(d);d["runtime"]!["databaseCaSha256"]=new string('0',64);});
        if(mode=="extra-field")values["api"]["Identity__KeyCertificatePath"]=Marker;
        if(mode=="protector-transport")values["api"]["Identity__KeyCertificateBase64"]=Pfx(fixture.Relay);
        if(mode=="protector-anchor")values["api"]["Identity__KeyCertificateBase64"]=Pfx(fixture.Pki.Root);
        if(mode=="retired-transport")values["api"]["Identity__PreviousKeyCertificates__0__Base64"]=Pfx(fixture.Pki.Clients["valid"]);
        if(mode=="relay-client-key")values["api"]["Identity__KeyCertificateBase64"]=Pfx(fixture.Pki.Clients["valid"]);
        // Fresh purpose/name-correct leaves around the same key bypass certificate-DER inequality.
        using var reused=mode is "api-relay-key" or "client-server-key"?Reissue(mode=="api-relay-key"?fixture.Api:fixture.Pki.Clients["valid"],"relay.pilot.staging.saveit.internal"):null;
        if(reused is not null){values["relay"]["ServerCertificateBase64"]=Pfx(reused);values["relay"]["ServerLeafSha256"]=Pin(reused);}
        await Failure(ComputeConfigurationCohort.ValidateAsync(release,Bytes(values),trust,CancellationToken.None));
    }
    private X509Certificate2 Reissue(X509Certificate2 certificate,string name)
    {
        using var key=certificate.GetRSAPrivateKey()!;var request=new CertificateRequest("CN=Synthetic reused key",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        var names=new SubjectAlternativeNameBuilder();names.AddDnsName(name);request.CertificateExtensions.Add(names.Build());
        using var issued=request.Create(fixture.Pki.Root,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddHours(1),RandomNumberGenerator.GetBytes(16));return issued.CopyWithPrivateKey(key);
    }
    [Theory] [InlineData("api")] [InlineData("relay")]
    public async Task Valid_name_purpose_private_key_and_exact_hash_do_not_admit_a_foreign_server_issuer(string process)
    {
        var release=await Release();var values=Values();var leaf=process=="api"?fixture.ForeignApi:fixture.ForeignRelay;
        if(process=="api")values["api"]["Deployment__ApiTls__CertificateBase64"]=Pfx(leaf);
        else{values["relay"]["ServerCertificateBase64"]=Pfx(leaf);values["relay"]["ServerLeafSha256"]=Pin(leaf);}
        // Reach both existing private-leaf checks with the real unchanged pinned
        // root file first. The refusal must come from cohort chain verification.
        var bytes=Bytes(values);await ProtectedConfiguration.ValidateAsync(new MemoryStream(bytes["api"]),"api",0);
        using var relay=await ApiRelayConfiguration.ReadAsync("https://api.pilot.staging.saveit.internal:8443","https://relay.pilot.staging.saveit.internal:8443",new MemoryStream(bytes["relay"]),fixture.Pki.Path);
        using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;chain.ChainPolicy.CustomTrustStore.Add(fixture.Pki.Root);
        chain.ChainPolicy.DisableCertificateDownloads=true;chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;Assert.False(chain.Build(leaf));
        await Failure(ComputeConfigurationCohort.ValidateAsync(release,bytes,fixture.Trust,CancellationToken.None));
    }
    [Theory]
    [InlineData("ARN")] [InlineData("Name")] [InlineData("VersionId")] [InlineData("SecretBinary")] [InlineData("unknown")]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("payload-type")] [InlineData("payload-size")] [InlineData("utf8-size")]
    [InlineData("date")] [InlineData("stages-empty")] [InlineData("stages-duplicate")] [InlineData("stages-type")]
    public async Task Original_exact_version_envelope_and_decoded_bounds_are_required(string mode)
    {
        var release=await Release();var json=Envelope(release,"api",Values()["api"]);
        if(mode is "ARN" or "Name" or "VersionId")json[mode]=Marker;
        if(mode=="SecretBinary")json[mode]=null;if(mode=="unknown")json[mode]=Marker;if(mode=="missing")json.Remove("ARN");
        if(mode=="payload-type")json["SecretString"]=new JsonObject();if(mode=="payload-size")json["SecretString"]=new string('a',65537);if(mode=="utf8-size")json["SecretString"]=new string('é',32769);
        if(mode=="date")json["CreatedDate"]=DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        if(mode=="stages-empty")json["VersionStages"]=new JsonArray();if(mode=="stages-duplicate")json["VersionStages"]=new JsonArray("AWSCURRENT","AWSCURRENT");if(mode=="stages-type")json["VersionStages"]=null;
        var body=json.ToJsonString();if(mode=="duplicate")body=body.Replace("\"ARN\":","\"ARN\":\"foreign\",\"A\\u0052N\":",StringComparison.Ordinal);
        using var parsed=JsonDocument.Parse(body);Assert.ThrowsAny<Exception>(()=>ComputeConfigurationEnvelope.Read(release,"api",parsed.RootElement));
    }
    [Fact]
    public async Task Exact_previous_or_unlabelled_version_is_accepted_without_mutable_stage_authority()
    {
        var release=await Release();var json=Envelope(release,"api",Values()["api"]);foreach(var labels in new[]{true,false}) {
            if(!labels){json.Remove("VersionStages");json.Remove("CreatedDate");}
            using var doc=JsonDocument.Parse(json.ToJsonString());var bytes=ComputeConfigurationEnvelope.Read(release,"api",doc.RootElement);
            try{Assert.Equal(json["SecretString"]!.GetValue<string>(),Encoding.UTF8.GetString(bytes));}finally{CryptographicOperations.ZeroMemory(bytes);}
        }
        using var envelope=JsonDocument.Parse(json.ToJsonString());Assert.ThrowsAny<Exception>(()=>ComputeConfigurationEnvelope.Read(release,"web",envelope.RootElement));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handler):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>handler(request,ct);}
    private static Task<ComputeAwsSession> Session(DateTimeOffset? expires=null)=>ComputeAwsSession.ReadAsync(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new{
        accessKeyId="ASIA0000000000000000",secretAccessKey=new string('a',40),sessionToken="synthetic-temporary-session-token",expiration=(expires??DateTimeOffset.UtcNow.AddMinutes(30)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")})),CancellationToken.None);
    private static HttpResponseMessage Response(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/x-amz-json-1.1")};
    private Func<string,HttpMessageHandler> Provider(ComputeImageTests.Bundle bundle,List<string> calls,Func<string,JsonObject,HttpResponseMessage?>? change=null,Func<string,CancellationToken,Task>? wait=null)
    {
        var images=ComputeImageTests.Provider(bundle,calls,observations:tasks=>{
            foreach(var p in new[]{"api","worker","web"})foreach(var env in tasks[p]!["taskDefinition"]!["containerDefinitions"]![0]!["environment"]!.AsArray()) {
                var name=env!["name"]!.GetValue<string>();if(name=="Deployment__Trust__DatabaseCaSha256"||name=="Deployment__ApiTls__ClientAuthentication__CaSha256"||name=="SAVEIT_API_CA_SHA256")env["value"]=fixture.Pki.Hash;
                if(name=="Deployment__ApiTls__ClientAuthentication__LeafSha256__0")env["value"]=Pin(fixture.Pki.Clients["valid"]);
            }
        });
        var values=Values();return service=>service!="secretsmanager"?images(service):new Handler(async(request,ct)=>{
            Assert.Equal("https://secretsmanager.us-east-1.amazonaws.com/",request.RequestUri!.AbsoluteUri);Assert.Equal(HttpMethod.Post,request.Method);
            Assert.Equal("secretsmanager.GetSecretValue",request.Headers.GetValues("X-Amz-Target").Single());
            Assert.Contains("/us-east-1/secretsmanager/aws4_request",request.Headers.GetValues("Authorization").Single());
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Assert.Equal(2,doc.RootElement.EnumerateObject().Count());
            var p=bundle.Release.Data.Tasks.Single(x=>x.Value.ConfigurationArn==doc.RootElement.GetProperty("SecretId").GetString()).Key;
            Assert.NotEqual("web",p);Assert.Equal(bundle.Release.Data.Tasks[p].ConfigurationVersionId,doc.RootElement.GetProperty("VersionId").GetString());
            calls.Add("secret:"+p);if(wait is not null)await wait(p,ct);var envelope=Envelope(bundle.Release,p,values[p]);return change?.Invoke(p,envelope)??Response(envelope.ToJsonString());
        });
    }
    [Fact]
    public async Task Full_acquisition_reads_three_exact_versions_after_all_image_checks_and_returns_no_private_values()
    {
        var bundle=await new ComputeImageTests(signing).Images(approve:d=>Approve(d));var calls=new List<string>();
        var result=await AcquiredComputeConfiguration.ReadAsync(bundle.Release,await Session(),Provider(bundle,calls),fixture.Trust,CancellationToken.None);
        Assert.Same(bundle.Release,result.Images.Cell.Tasks.Release);Assert.Equal(19,calls.Count);Assert.Equal(new[]{"secret:api","secret:worker","secret:relay"},calls.Skip(16));
    }
    [Theory]
    [InlineData("version")] [InlineData("duplicate")] [InlineData("unknown")] [InlineData("binary")] [InlineData("redirect")]
    [InlineData("encoding")] [InlineData("trailer")] [InlineData("overflow")] [InlineData("length")] [InlineData("payload-duplicate")]
    public async Task Raw_secret_failure_is_sanitized_and_never_becomes_a_later_read_or_effect(string mode)
    {
        var bundle=await new ComputeImageTests(signing).Images(approve:d=>Approve(d));var calls=new List<string>();
        await Failure(AcquiredComputeConfiguration.ReadAsync(bundle.Release,await Session(),Provider(bundle,calls,(p,envelope)=>{
            if(mode=="version")envelope["VersionId"]=Guid.NewGuid().ToString("D");if(mode=="unknown")envelope["Private"]=Marker;if(mode=="binary")envelope["SecretBinary"]=null;
            if(mode=="payload-duplicate")envelope["SecretString"]=envelope["SecretString"]!.GetValue<string>().Replace("\"ConnectionStrings__App\":","\"ConnectionStrings__App\":\"private\",\"ConnectionStrings__A\\u0070p\":",StringComparison.Ordinal);
            var body=envelope.ToJsonString();if(mode=="duplicate")body=body.Replace("\"VersionId\":","\"VersionId\":\"private\",\"VersionI\\u0064\":",StringComparison.Ordinal);if(mode=="overflow")body=body.PadRight(524289);
            var response=Response(body);if(mode=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new Uri("https://foreign.invalid/private");}
            if(mode=="encoding")response.Content.Headers.ContentEncoding.Add("gzip");if(mode=="trailer")response.Headers.TryAddWithoutValidation("Trailer","private");if(mode=="length")response.Content.Headers.ContentLength=body.Length+1;return response;
        }),fixture.Trust,CancellationToken.None));Assert.Equal(mode=="payload-duplicate"?19:17,calls.Count);
    }
    [Theory] [InlineData("api")] [InlineData("worker")] [InlineData("relay")]
    public async Task Cancellation_after_observed_secret_entry_stops_without_later_reads(string target)
    {
        var bundle=await new ComputeImageTests(signing).Images(approve:d=>Approve(d));var calls=new List<string>();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
        var operation=AcquiredComputeConfiguration.ReadAsync(bundle.Release,await Session(),Provider(bundle,calls,wait:async(p,ct)=>{if(p==target){entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}}),fixture.Trust,cancel.Token);
        try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));}finally{cancel.Cancel();}
        await Failure(operation.WaitAsync(TimeSpan.FromSeconds(5)));Assert.Equal("secret:"+target,calls[^1]);
    }
    [Theory] [InlineData("api")] [InlineData("worker")] [InlineData("relay")]
    public async Task An_actual_secret_response_after_approval_expiry_cannot_start_another_read_or_return_a_cohort(string target)
    {
        var bundle=await new ComputeImageTests(signing).Images(expires:DateTimeOffset.UtcNow.ToUnixTimeSeconds()+4,approve:d=>Approve(d));var calls=new List<string>();var returned=false;
        var operation=AcquiredComputeConfiguration.ReadAsync(bundle.Release,await Session(),Provider(bundle,calls,wait:async(p,ct)=>{
            if(p!=target)return;while(DateTimeOffset.UtcNow.ToUnixTimeSeconds()<bundle.Release.ExpiresAt)await Task.Delay(20,ct);returned=true;
        }),fixture.Trust,CancellationToken.None);
        await Failure(operation.WaitAsync(TimeSpan.FromSeconds(12)));Assert.True(returned,"A response after expiry must reach validation, rather than passing because of a transport failure.");
        Assert.Equal("secret:"+target,calls[^1]);Assert.Equal(17+Array.IndexOf(new[]{"api","worker","relay"},target),calls.Count);
    }
    [Theory] [InlineData("api")] [InlineData("worker")] [InlineData("relay")]
    public async Task Session_safety_window_is_rechecked_after_each_secret_response(string target)
    {
        var bundle=await new ComputeImageTests(signing).Images(approve:d=>Approve(d));var calls=new List<string>();var returned=false;
        var session=await Session(DateTimeOffset.UtcNow.AddSeconds(63));
        var operation=AcquiredComputeConfiguration.ReadAsync(bundle.Release,session,Provider(bundle,calls,wait:async(p,ct)=>{
            if(p!=target)return;while(DateTimeOffset.UtcNow.AddMinutes(1)<session.ExpiresAt)await Task.Delay(20,ct);returned=true;
        }),fixture.Trust,CancellationToken.None);
        await Failure(operation.WaitAsync(TimeSpan.FromSeconds(12)));Assert.True(returned);Assert.Equal("secret:"+target,calls[^1]);
    }
}
