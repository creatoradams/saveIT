using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal sealed class ComputeImageManifest
{
    internal const string DockerMediaType="application/vnd.docker.distribution.manifest.v2+json";
    internal const string OciMediaType="application/vnd.oci.image.manifest.v1+json";
    internal string ConfigurationDigest {get;}
    internal int ConfigurationSize {get;}
    internal int Layers {get;}
    private ComputeImageManifest(string digest,int size,int layers){ConfigurationDigest=digest;ConfigurationSize=size;Layers=layers;}
    internal static ComputeImageManifest Parse(ComputeRelease release,string process,JsonElement response)
    {
        RejectDuplicates(response);
        if(response.ValueKind!=JsonValueKind.Object||!response.TryGetProperty("images",out var images)||response.EnumerateObject().Any(x=>x.Name is not("images" or "failures")))throw new InvalidOperationException();
        if(response.TryGetProperty("failures",out var failures)&&(failures.ValueKind!=JsonValueKind.Array||failures.GetArrayLength()!=0))throw new InvalidOperationException();
        if(images.ValueKind!=JsonValueKind.Array||images.GetArrayLength()!=1)throw new InvalidOperationException();
        var image=Object(images[0],"registryId","repositoryName","imageId","imageManifest","imageManifestMediaType");
        if(image["registryId"].GetString()!=release.Scope.AccountId||image["repositoryName"].GetString()!=ComputeSdkRead.Repository(release.Scope,process))throw new InvalidOperationException();
        var id=image["imageId"];if(id.ValueKind!=JsonValueKind.Object||!id.TryGetProperty("imageDigest",out var digest)||id.EnumerateObject().Any(x=>x.Name is not("imageDigest" or "imageTag")))throw new InvalidOperationException();
        if(id.TryGetProperty("imageTag",out var tag)&&(tag.GetString() is not {Length:>0 and <=300}))throw new InvalidOperationException();
        var expected=release.Data.Tasks[process].Image.Split('@')[1];if(digest.GetString()!=expected)throw new InvalidOperationException();
        var original=image["imageManifest"].GetString()??throw new InvalidOperationException();var bytes=new UTF8Encoding(false,true).GetBytes(original);
        try {
            if(bytes.Length is <1 or >65536||"sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes))!=expected)throw new InvalidOperationException();
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=8});RejectDuplicates(document.RootElement);
            var manifest=Object(document.RootElement,"schemaVersion","mediaType","config","layers");var media=manifest["mediaType"].GetString();
            if(manifest["schemaVersion"].GetInt32()!=2||media is not(DockerMediaType or OciMediaType)||image["imageManifestMediaType"].GetString()!=media)throw new InvalidOperationException();
            var configuration=Descriptor(manifest["config"],media==DockerMediaType?"application/vnd.docker.container.image.v1+json":"application/vnd.oci.image.config.v1+json",65536);
            var layers=manifest["layers"];if(layers.ValueKind!=JsonValueKind.Array||layers.GetArrayLength() is <1 or >64)throw new InvalidOperationException();
            long total=0;foreach(var layer in layers.EnumerateArray())total+=Descriptor(layer,media==DockerMediaType?"application/vnd.docker.image.rootfs.diff.tar.gzip":"application/vnd.oci.image.layer.v1.tar+gzip",5368709120L).Size;
            if(total>10737418240L)throw new InvalidOperationException();
            return new(configuration.Digest,checked((int)configuration.Size),layers.GetArrayLength());
        } finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    private static (string Digest,long Size) Descriptor(JsonElement value,string type,long maximum)
    {
        var fields=Object(value,"mediaType","digest","size");var digest=fields["digest"].GetString()??throw new InvalidOperationException();Match(digest,"sha256:[a-f0-9]{64}");
        var size=fields["size"].GetInt64();if(fields["mediaType"].GetString()!=type||size<1||size>maximum)throw new InvalidOperationException();return(digest,size);
    }
    internal void VerifyConfiguration(ReadOnlySpan<byte> bytes)
    {if(bytes.Length!=ConfigurationSize||"sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes))!=ConfigurationDigest)throw new InvalidOperationException();}
}
