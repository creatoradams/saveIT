using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal sealed record ComputeScope(string AccountId, string Region, string Cell, string Environment)
{
    internal string Name => $"saveit-{Cell}-{Environment}";
    internal string Path => $"saveit/{Cell}/{Environment}";
    internal string Audience => $"saveit-compute:{AccountId}:{Region}:{Cell}:{Environment}";
    internal void Validate()
    {
        Match(AccountId,"[0-9]{12}"); Match(Region,"[a-z]{2}-[a-z]+-[1-9]"); Match(Cell,"[a-z][a-z0-9]{1,11}");
        if (Region.StartsWith("cn-",StringComparison.Ordinal) || Environment is not ("staging" or "production")) throw new InvalidOperationException();
    }
}
internal sealed record ComputeTaskReference(string TaskDefinitionArn, string Image, string? ConfigurationArn, string? ConfigurationVersionId);
internal sealed record ComputeRuntime(string PublicOrigin, string OktaIssuer, string OktaClientId, string OktaTenantId,
    ImmutableArray<string> WorkerTenants, int RetiredKeyCount, string DatabaseCaSha256, string RelayCaSha256,
    string ClientCaSha256, ImmutableArray<string> ClientLeafSha256, string FileSystemId, string AccessPointId,
    string PoolId, string WorkloadIssuer, string WorkloadJwksSha256);
internal sealed record ComputeNetwork(string VpcId, ImmutableArray<string> SubnetIds, ImmutableDictionary<string,string> SecurityGroups,
    string ApiDiscoveryServiceArn, string RelayDiscoveryServiceArn);
internal sealed record ComputeReleaseData(long ExpectedVersion, string Generation, string SourceRevision,
    ImmutableDictionary<string,ComputeTaskReference> Tasks, ComputeRuntime Runtime, ComputeNetwork Network);

// Construction is private: provider code must receive a successfully verified plan.
internal sealed class ComputeRelease
{
    internal const string Type = "saveit-compute-release+jwt";
    internal const string Failure = "SaveIT compute release admission failed.";
    internal static readonly string[] Processes = ["api","web","worker","relay"];
    internal ComputeScope Scope { get; }
    internal ComputeReleaseData Data { get; }
    internal Guid OperationId { get; }
    internal string PlanSha256 { get; }
    internal string KeySha256 { get; }
    internal long ExpiresAt { get; }
    private ComputeRelease(ComputeScope scope, ComputeReleaseData data, Guid operation, string hash, string keyHash, long expires)
    { Scope=scope; Data=data; OperationId=operation; PlanSha256=hash; KeySha256=keyHash; ExpiresAt=expires; }

    // This verifies approval identity and freshness, not replay prevention or AWS provenance.
    internal static async Task<ComputeRelease> VerifyAsync(string token, ComputeScope scope, ReadOnlyMemory<byte> publicSpki, string keySha256, CancellationToken ct=default)
    {
        try
        {
            ct.ThrowIfCancellationRequested(); scope.Validate(); Match(keySha256,"[a-f0-9]{64}");
            if (publicSpki.Length is < 1 or > 2048 || Convert.ToHexStringLower(SHA256.HashData(publicSpki.Span))!=keySha256 || token.Length is < 1 or > 49152) throw new InvalidOperationException();
            using var rsa=RSA.Create(); rsa.ImportSubjectPublicKeyInfo(publicSpki.Span,out var consumed);
            if (consumed!=publicSpki.Length || rsa.KeySize is < 3072 or > 4096) throw new InvalidOperationException();
            var parts=token.Split('.'); if (parts.Length!=3) throw new InvalidOperationException();
            using var header=Parse(Decode(parts[0],1024),4); using var payload=Parse(Decode(parts[1],32768),12);
            _=Decode(parts[2],1024);
            var h=Object(header.RootElement,"alg","typ","kid"); var kid="sha256-"+keySha256;
            if (h["alg"].GetString()!="PS256" || h["typ"].GetString()!=Type || h["kid"].GetString()!=kid) throw new InvalidOperationException();
            var p=Object(payload.RootElement,"iss","aud","iat","exp","jti","contract");
            var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var issued=p["iat"].GetInt64(); var expires=p["exp"].GetInt64();
            if (issued>now || issued<now-900 || expires<=now || expires<=issued || expires>issued+900 || p["iss"].GetString()!="saveit-release" || p["aud"].GetString()!=scope.Audience) throw new InvalidOperationException();
            var operation=CanonicalGuid(p["jti"].GetString()!);
            var result=await new JsonWebTokenHandler { MapInboundClaims=false, MaximumTokenSizeInBytes=49152 }.ValidateTokenAsync(token,new TokenValidationParameters {
                RequireSignedTokens=true,RequireExpirationTime=true,RequireAudience=true,ValidateIssuerSigningKey=true,
                // IdentityModel may cache providers beyond this call; do not hand it
                // an RSA instance whose lifetime ends when this verification returns.
                IssuerSigningKey=new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId=kid },ValidAlgorithms=[SecurityAlgorithms.RsaSsaPssSha256],
                ValidateIssuer=true,ValidIssuer="saveit-release",ValidateAudience=true,ValidAudience=scope.Audience,
                ValidateLifetime=true,ClockSkew=TimeSpan.Zero,ValidTypes=[Type],TryAllIssuerSigningKeys=false,
                IncludeTokenOnFailedValidation=false,LogValidationExceptions=false
            });
            if (!result.IsValid) throw new InvalidOperationException();
            var data=p["contract"].Deserialize<ComputeReleaseData>(new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=false,
                RespectRequiredConstructorParameters=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12
            }) ?? throw new InvalidOperationException();
            ValidateData(scope,data); ct.ThrowIfCancellationRequested();
            return new(scope,data,operation,Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token))),keySha256,expires);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

    internal static Guid CanonicalGuid(string value)
    {
        if (!Guid.TryParseExact(value,"D",out var id) || id==Guid.Empty || id.ToString("D")!=value) throw new InvalidOperationException(); return id;
    }
    private static void ValidateData(ComputeScope s,ComputeReleaseData d)
    {
        if (d.ExpectedVersion<1) throw new InvalidOperationException(); _=CanonicalGuid(d.Generation); Match(d.SourceRevision,"[a-f0-9]{40}");
        if (d.Tasks.Count!=4 || Processes.Any(p=>!d.Tasks.ContainsKey(p))) throw new InvalidOperationException();
        foreach(var (process,t) in d.Tasks)
        {
            Match(t.TaskDefinitionArn,Regex.Escape($"arn:aws:ecs:{s.Region}:{s.AccountId}:task-definition/{s.Name}-{process}:")+"[1-9][0-9]{0,9}");
            _=int.Parse(t.TaskDefinitionArn[(t.TaskDefinitionArn.LastIndexOf(':')+1)..],CultureInfo.InvariantCulture);
            Match(t.Image,Regex.Escape($"{s.AccountId}.dkr.ecr.{s.Region}.amazonaws.com/{s.Path}/{(process=="relay"?"deployment":process)}@sha256:")+"[a-f0-9]{64}");
            if(process=="web") { if(t.ConfigurationArn is not null || t.ConfigurationVersionId is not null) throw new InvalidOperationException(); }
            else { Match(t.ConfigurationArn!,Regex.Escape($"arn:aws:secretsmanager:{s.Region}:{s.AccountId}:secret:{s.Path}/{process}/config-")+"[A-Za-z0-9]{6}"); _=CanonicalGuid(t.ConfigurationVersionId!); }
        }
        var r=d.Runtime;
        if(r.PublicOrigin.Length>253 || r.OktaIssuer.Length>512) throw new InvalidOperationException();
        Match(r.PublicOrigin,"https://[a-z0-9]+([.-][a-z0-9]+)*[.][a-z]{2,63}");
        Match(r.OktaIssuer,"https://[a-z0-9]+([.-][a-z0-9]+)*[.][a-z]{2,63}(/oauth2/[A-Za-z0-9_-]+)?");
        Match(r.OktaClientId,"[A-Za-z0-9_-]{1,128}"); _=CanonicalGuid(r.OktaTenantId);
        if(r.RetiredKeyCount is <0 or >4 || r.WorkerTenants.IsDefaultOrEmpty || r.WorkerTenants.Length>100 || r.WorkerTenants.Distinct(StringComparer.Ordinal).Count()!=r.WorkerTenants.Length) throw new InvalidOperationException();
        foreach(var id in r.WorkerTenants) _=CanonicalGuid(id);
        if(r.ClientLeafSha256.IsDefaultOrEmpty || r.ClientLeafSha256.Length>4 || r.ClientLeafSha256.Distinct(StringComparer.Ordinal).Count()!=r.ClientLeafSha256.Length) throw new InvalidOperationException();
        foreach(var hash in r.ClientLeafSha256.Concat([r.DatabaseCaSha256,r.RelayCaSha256,r.ClientCaSha256,r.WorkloadJwksSha256])) Match(hash,"[a-f0-9]{64}");
        Match(r.FileSystemId,"fs-[a-f0-9]{17}"); Match(r.AccessPointId,"fsap-[a-f0-9]{17}");
        Match(r.PoolId,Regex.Escape(s.Region+":")+"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
        if(r.WorkloadIssuer!="https://cognito-identity.amazonaws.com" && r.WorkloadIssuer!=$"https://cognito-identity.{s.Region}.amazonaws.com") throw new InvalidOperationException();
        var n=d.Network; Match(n.VpcId,"vpc-[a-f0-9]{17}");
        if(n.SubnetIds.IsDefaultOrEmpty || n.SubnetIds.Length!=2 || n.SubnetIds.Distinct(StringComparer.Ordinal).Count()!=2 || n.SecurityGroups.Count!=4 || Processes.Any(p=>!n.SecurityGroups.ContainsKey(p)) || n.SecurityGroups.Values.Distinct(StringComparer.Ordinal).Count()!=4) throw new InvalidOperationException();
        foreach(var subnet in n.SubnetIds) Match(subnet,"subnet-[a-f0-9]{17}");
        foreach(var sg in n.SecurityGroups.Values) Match(sg,"sg-[a-f0-9]{17}");
        foreach(var arn in new[]{n.ApiDiscoveryServiceArn,n.RelayDiscoveryServiceArn}) Match(arn,Regex.Escape($"arn:aws:servicediscovery:{s.Region}:{s.AccountId}:service/srv-")+"[a-z0-9]{16}");
        if(n.ApiDiscoveryServiceArn==n.RelayDiscoveryServiceArn) throw new InvalidOperationException();
    }
    private static byte[] Decode(string value,int limit)
    {
        if(value.Length is <1 || value.Length>limit*2 || value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '-' and not '_')) throw new InvalidOperationException();
        var bytes=Base64UrlEncoder.DecodeBytes(value); if(bytes.Length>limit || Base64UrlEncoder.Encode(bytes)!=value) throw new InvalidOperationException(); return bytes;
    }
    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes,int depth)
    {
        _=new UTF8Encoding(false,true).GetCharCount(bytes.Span);
        var document=JsonDocument.Parse(bytes,new JsonDocumentOptions { MaxDepth=depth });
        try { RejectDuplicates(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }
}
