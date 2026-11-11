using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SaveIT.Api.Operations;
using SaveIT.Api.Security;
using SaveIT.Search;

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
            var api=await ProtectedConfiguration.ReadValidatedAsync(apiInput,"api",runtime.RetiredKeyCount,ct);
            var worker=await ProtectedConfiguration.ReadValidatedAsync(workerInput,"worker",0,ct);
            var apiConnection=new NpgsqlConnectionStringBuilder(api["ConnectionStrings__App"]);
            var workerConnection=new NpgsqlConnectionStringBuilder(worker["ConnectionStrings__SearchWorker"]);
            if(!string.Equals(apiConnection.Host,workerConnection.Host,StringComparison.OrdinalIgnoreCase)||apiConnection.Port!=workerConnection.Port||apiConnection.Database!=workerConnection.Database)throw new InvalidOperationException();
            PublicTrust.Validate(trust.Database,runtime.DatabaseCaSha256);
            var apiHost=$"api.{release.Scope.Cell}.{release.Scope.Environment}.saveit.internal";
            var relayHost=$"relay.{release.Scope.Cell}.{release.Scope.Environment}.saveit.internal";
            using var relayInput=new MemoryStream(payloads["relay"],false);
            using var relay=await ApiRelayConfiguration.ReadAsync($"https://{apiHost}:8443",$"https://{relayHost}:8443",relayInput,trust.Api,ct);
            using var server=ApiTls.Load(null,api["Deployment__ApiTls__CertificateBase64"],api["Deployment__ApiTls__CertificatePassword"]);
            if(!server.MatchesHostname(apiHost,allowWildcards:false,allowCommonName:false))throw new InvalidOperationException();
            ServerChain(server,relay.Roots);
            var relayRoots=PublicTrust.Load(trust.Relay,runtime.RelayCaSha256);
            try {ServerChain(relay.Server,relayRoots);}
            finally {foreach(var root in relayRoots)root.Dispose();}
            var values=new Dictionary<string,string?>{["Client:Enabled"]="true",["Client:CaSha256"]=runtime.ClientCaSha256};
            for(var i=0;i<runtime.ClientLeafSha256.Length;i++)values[$"Client:LeafSha256:{i}"]=runtime.ClientLeafSha256[i];
            using var client=ApiClientAuthentication.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Client"),trust.Client)??throw new InvalidOperationException();
            using var clientChain=new X509Chain{ChainPolicy=client.CreateChainPolicy()};
            if(!clientChain.Build(relay.Client)||!client.Validate(relay.Client,clientChain,SslPolicyErrors.None))throw new InvalidOperationException();

            // Separate all private transport roles and every session protector.
            // Reissued certificates cannot hide reuse of the same RSA key.
            var transports=new[]{PublicKey(server),PublicKey(relay.Server),PublicKey(relay.Client)};
            if(transports.Distinct(StringComparer.Ordinal).Count()!=3)throw new InvalidOperationException();
            var anchorKeys=new HashSet<string>(relay.Roots.Cast<X509Certificate2>().Select(PublicKey),StringComparer.Ordinal);
            foreach(var path in new[]{(trust.Database,runtime.DatabaseCaSha256),(trust.Relay,runtime.RelayCaSha256),(trust.Client,runtime.ClientCaSha256)}) {
                var anchors=PublicTrust.Load(path.Item1,path.Item2);
                try{foreach(var root in anchors)anchorKeys.Add(PublicKey(root));}
                finally{foreach(var root in anchors)root.Dispose();}
            }
            foreach(var leaf in transports)if(anchorKeys.Contains(leaf))throw new InvalidOperationException();
            for(var i=-1;i<runtime.RetiredKeyCount;i++) {
                ct.ThrowIfCancellationRequested();var prefix=i<0?"Identity__KeyCertificate":$"Identity__PreviousKeyCertificates__{i}__";
                using var protector=KeyProtectionCertificate.Load(null,api[prefix+"Base64"],api[prefix+"Password"],allowExpired:i>=0);
                var key=PublicKey(protector);if(transports.Contains(key,StringComparer.Ordinal)||anchorKeys.Contains(key))throw new InvalidOperationException();
            }
            ct.ThrowIfCancellationRequested();
        }
        catch(Exception){throw new InvalidOperationException(ComputeConfigurationAcquisition.Failure);}
    }
    private static string PublicKey(X509Certificate2 certificate)=>Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    private static void ServerChain(X509Certificate2 server,X509Certificate2Collection roots)
    {
        using var chain=new X509Chain();chain.ChainPolicy=new X509ChainPolicy{
            TrustMode=X509ChainTrustMode.CustomRootTrust,RevocationMode=X509RevocationMode.NoCheck,VerificationFlags=X509VerificationFlags.NoFlag,
            DisableCertificateDownloads=true,UrlRetrievalTimeout=TimeSpan.Zero,ApplicationPolicy={new Oid("1.3.6.1.5.5.7.3.1")}};
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);if(!chain.Build(server)||chain.ChainStatus.Any(x=>x.Status!=X509ChainStatusFlags.NoError))throw new InvalidOperationException();
    }
}
