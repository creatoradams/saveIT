using System.Security.Cryptography;
using System.Text.Json;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Provider bearer credential. Never persist/serialize it or derive a destination from it.
internal sealed class ComputeRegistryAuthorization
{
    internal string HeaderToken {get;}
    private readonly double expires;
    private ComputeRegistryAuthorization(string token,double at){HeaderToken=token;expires=at;}
    internal static string Host(ComputeScope s)=>$"{s.AccountId}.dkr.ecr.{s.Region}.amazonaws.com";
    internal void RequireFresh(){if(expires<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60)throw new InvalidOperationException();}
    internal static ComputeRegistryAuthorization Parse(ComputeScope scope,JsonElement value)
    {
        RejectDuplicates(value);var envelope=Object(value,"authorizationData");var items=envelope["authorizationData"];
        if(items.ValueKind!=JsonValueKind.Array||items.GetArrayLength()!=1)throw new InvalidOperationException();
        var data=Object(items[0],"authorizationToken","expiresAt","proxyEndpoint");
        if(data["proxyEndpoint"].GetString()!="https://"+Host(scope))throw new InvalidOperationException();
        var token=data["authorizationToken"].GetString()??throw new InvalidOperationException();if(token.Length is <8 or >16384)throw new InvalidOperationException();
        var bytes=Convert.FromBase64String(token);
        try {
            if(Convert.ToBase64String(bytes)!=token||bytes.Length is <5 or >12288||!bytes.AsSpan(0,4).SequenceEqual("AWS:"u8)||bytes.AsSpan(4).ContainsAnyExceptInRange((byte)33,(byte)126))throw new InvalidOperationException();
        } finally{CryptographicOperations.ZeroMemory(bytes);}
        var expires=data["expiresAt"].GetDouble();var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if(!double.IsFinite(expires)||expires<=now+60||expires>now+43260)throw new InvalidOperationException();return new(token,expires);
    }
}
