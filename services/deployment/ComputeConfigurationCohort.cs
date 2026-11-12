using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using SaveIT.Api.Operations;
using SaveIT.Search;
using static SaveIT.Deployment.ProtectedConfiguration;

namespace SaveIT.Deployment;

// Only internal tests inject paths. The CLI always uses these packaged public files.
internal sealed record ComputeConfigurationTrust(string Database,string Api,string Relay,string Client)
{
    internal static ComputeConfigurationTrust Packaged {get;}=new(PublicTrust.DatabasePath,ApiPeerProbe.TrustPath,"/opt/saveit/trust/relay-ca.crt",ApiClientAuthentication.TrustPath);
}

internal static class ComputeConfigurationCohort
{
    internal static async Task ValidateAsync(ComputeRelease release,IReadOnlyDictionary<string,byte[]> payloads,ComputeConfigurationTrust trust,CancellationToken ct)
    {
        try {
            if(payloads.Count!=3||new[]{"api","worker","relay"}.Any(p=>!payloads.ContainsKey(p)))throw new InvalidOperationException();
            var runtime=release.Data.Runtime;
            using var apiInput=new MemoryStream(payloads["api"],false);using var workerInput=new MemoryStream(payloads["worker"],false);
            using var api=await ReadValidatedAsync(apiInput,"api",runtime.RetiredKeyCount,ct);
            using var worker=await ReadValidatedAsync(workerInput,"worker",0,ct);
            if(!string.Equals(api.Target.Host,worker.Target.Host,StringComparison.OrdinalIgnoreCase)||api.Target.Port!=worker.Target.Port||api.Target.Database!=worker.Target.Database)throw new InvalidOperationException();
            var anchorKeys=new HashSet<string>(StringComparer.Ordinal);
            AddOwnedAnchors(anchorKeys,trust.Database,runtime.DatabaseCaSha256);
            var apiHost=$"api.{release.Scope.Cell}.{release.Scope.Environment}.saveit.internal";
            var relayHost=$"relay.{release.Scope.Cell}.{release.Scope.Environment}.saveit.internal";
            using var relayInput=new MemoryStream(payloads["relay"],false);
            using var relay=await ApiRelayConfiguration.ReadAsync($"https://{apiHost}:8443",$"https://{relayHost}:8443",relayInput,trust.Api,ct);
            var server=api.Server??throw new InvalidOperationException();
            if(!server.MatchesHostname(apiHost,allowWildcards:false,allowCommonName:false))throw new InvalidOperationException();
            ServerChain(server,relay.Roots);
            AddAnchors(anchorKeys,relay.Roots);
            AddOwnedAnchors(anchorKeys,trust.Relay,runtime.RelayCaSha256,relay.Server);
            var values=new Dictionary<string,string?>{["Client:Enabled"]="true",["Client:CaSha256"]=runtime.ClientCaSha256};
            for(var i=0;i<runtime.ClientLeafSha256.Length;i++)values[$"Client:LeafSha256:{i}"]=runtime.ClientLeafSha256[i];
            using var client=ApiClientAuthentication.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Client"),trust.Client)??throw new InvalidOperationException();
            using var clientChain=new X509Chain{ChainPolicy=client.CreateChainPolicy()};
            if(!clientChain.Build(relay.Client)||!client.Validate(relay.Client,clientChain,SslPolicyErrors.None))throw new InvalidOperationException();
            AddAnchors(anchorKeys,clientChain.ChainPolicy.CustomTrustStore);

            // Separate all private transport roles and every session protector.
            // Reissued certificates cannot hide reuse of the same RSA key.
            var transports=new HashSet<string>([PublicKeyHash(server),PublicKeyHash(relay.Server),PublicKeyHash(relay.Client)],StringComparer.Ordinal);
            if(transports.Count!=3||transports.Overlaps(anchorKeys)||api.ProtectorKeys.Any(key=>transports.Contains(key)||anchorKeys.Contains(key)))throw new InvalidOperationException();
            ct.ThrowIfCancellationRequested();
        }
        catch(Exception){throw new InvalidOperationException(ComputeConfigurationAcquisition.Failure);}
    }
    private static void AddAnchors(HashSet<string> anchorHashes,X509Certificate2Collection roots)
    {foreach(var root in roots)anchorHashes.Add(PublicKeyHash(root));}
    private static void AddOwnedAnchors(HashSet<string> anchorHashes,string path,string hash,X509Certificate2? server=null)
    {
        var roots=PublicTrust.Load(path,hash);
        try{if(server is not null)ServerChain(server,roots);AddAnchors(anchorHashes,roots);}
        finally{foreach(var root in roots)root.Dispose();}
    }
    private static void ServerChain(X509Certificate2 server,X509Certificate2Collection roots)
    {
        using var chain=new X509Chain();chain.ChainPolicy=new X509ChainPolicy{
            TrustMode=X509ChainTrustMode.CustomRootTrust,RevocationMode=X509RevocationMode.NoCheck,VerificationFlags=X509VerificationFlags.NoFlag,
            DisableCertificateDownloads=true,UrlRetrievalTimeout=TimeSpan.Zero,ApplicationPolicy={new Oid("1.3.6.1.5.5.7.3.1")}};
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);if(!chain.Build(server)||chain.ChainStatus.Any(x=>x.Status!=X509ChainStatusFlags.NoError))throw new InvalidOperationException();
    }
}
