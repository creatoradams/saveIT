using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal static class ComputeImageContract
{
    internal static void Validate(ComputeRelease release,string process,ComputeImageManifest manifest,ReadOnlyMemory<byte> original)
    {
        manifest.VerifyConfiguration(original.Span);
        _=new UTF8Encoding(false,true).GetCharCount(original.Span);
        using var document=JsonDocument.Parse(original,new JsonDocumentOptions{MaxDepth=16});var root=document.RootElement;RejectDuplicates(root);
        if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Any(x=>x.Name is not("architecture" or "os" or "config" or "rootfs" or "history" or "created" or "author")))throw new InvalidOperationException();
        if(root.GetProperty("architecture").GetString()!="amd64"||root.GetProperty("os").GetString()!="linux")throw new InvalidOperationException();
        foreach(var field in new[]{"created","author"})if(root.TryGetProperty(field,out var text)&&text.GetString() is not {Length:<=1024})throw new InvalidOperationException();
        if(root.TryGetProperty("history",out var history)) {
            if(history.ValueKind!=JsonValueKind.Array||history.GetArrayLength()>256)throw new InvalidOperationException();
            foreach(var item in history.EnumerateArray()) {
                if(item.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException();
                foreach(var p in item.EnumerateObject()) {
                    if(p.Name=="empty_layer"){if(p.Value.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException();}
                    else if(p.Name is not("created" or "created_by" or "author" or "comment")||p.Value.GetString() is not {Length:<=8192})throw new InvalidOperationException();
                }
            }
        }
        var fs=Object(root.GetProperty("rootfs"),"type","diff_ids");var ids=fs["diff_ids"];
        if(fs["type"].GetString()!="layers"||ids.ValueKind!=JsonValueKind.Array||ids.GetArrayLength()!=manifest.Layers)throw new InvalidOperationException();
        foreach(var id in ids.EnumerateArray())Match(id.GetString()!,"sha256:[a-f0-9]{64}");
        var actual=JsonNode.Parse(root.GetProperty("config").GetRawText())!.AsObject();
        foreach(var field in new[]{"OnBuild","Shell"}) {if(actual[field] is null)actual.Remove(field);else RemoveDefault(actual,field,new JsonArray());}
        foreach(var field in new[]{"AttachStdin","AttachStdout","AttachStderr","OpenStdin","StdinOnce","Tty","NetworkDisabled"})RemoveDefault(actual,field,JsonValue.Create(false)!);
        foreach(var field in new[]{"Hostname","Domainname","Image","MacAddress"})RemoveDefault(actual,field,JsonValue.Create("")!);
        if(actual.Remove("ArgsEscaped",out var escaped)&&escaped!.GetValueKind() is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException(); // Linux-only deprecated metadata.
        RemoveDefault(actual,"StopSignal",JsonValue.Create("SIGTERM")!);
        if(actual["Cmd"] is null)actual.Remove("Cmd");else if(process!="web")RemoveDefault(actual,"Cmd",new JsonArray());
        if(actual["Healthcheck"] is null)actual.Remove("Healthcheck");
        if(actual["ExposedPorts"] is null)actual.Remove("ExposedPorts");
        if(actual["ExposedPorts"] is JsonObject ports) {
            foreach(var port in ports) {if(port.Key is not("3000/tcp" or "8080/tcp" or "8443/tcp")||!JsonNode.DeepEquals(port.Value,new JsonObject()))throw new InvalidOperationException();}
            actual.Remove("ExposedPorts"); // EXPOSE is metadata; exact task/network contracts govern attachment.
        }
        var env=new JsonObject();foreach(var entry in actual["Env"]!.AsArray()) {
            var value=entry!.GetValue<string>();var split=value.IndexOf('=');if(split<1)throw new InvalidOperationException();env.Add(value[..split],value[(split+1)..]);
        }actual["Env"]=env;
        if(actual["Healthcheck"] is JsonObject health) {RemoveDefault(health,"Retries",JsonValue.Create(0)!);RemoveDefault(health,"StartInterval",JsonValue.Create(0)!);}
        if(!JsonNode.DeepEquals(actual,Expected(release,process)))throw new InvalidOperationException();
    }
    private static JsonObject Expected(ComputeRelease release,string process)
    {
        var env=new JsonObject{["PATH"]="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin",["APP_UID"]="1654",["ASPNETCORE_HTTP_PORTS"]="8080",["DOTNET_RUNNING_IN_CONTAINER"]="true",["TMPDIR"]="/var/saveit/runtime"};
        var result=new JsonObject{
            ["User"]="1654",["WorkingDir"]="/app",["Env"]=env,["Labels"]=new JsonObject{
                ["org.opencontainers.image.revision"]=release.Data.SourceRevision,["org.opencontainers.image.source"]="https://github.com/creatoradams/saveIT"},
            ["Volumes"]=new JsonObject{["/var/saveit/runtime"]=new JsonObject()}
        };
        JsonArray entry;JsonArray? probe=null;
        if(process=="web") {
            env["NODE_ENV"]="production";env["NEXT_TELEMETRY_DISABLED"]="1";env["HOSTNAME"]="0.0.0.0";env["PORT"]="3000";
            entry=new JsonArray("/usr/local/bin/node");result["Cmd"]=new JsonArray("runtime.cjs");
            probe=new JsonArray("CMD","/usr/local/bin/node","-e","fetch('http://127.0.0.1:3000/health/live',{signal:AbortSignal.timeout(3000)}).then(r=>process.exit(r.status===200?0:1)).catch(()=>process.exit(1))");
        } else {
            env["DOTNET_VERSION"]="10.0.14";env["ASPNET_VERSION"]="10.0.14";env["DOTNET_ENVIRONMENT"]="Production";env["ASPNETCORE_ENVIRONMENT"]="Production";env["DOTNET_EnableDiagnostics"]="0";
            var assembly=process switch{"api"=>"SaveIT.Api.dll","worker"=>"SaveIT.Workers.dll","relay"=>"SaveIT.Deployment.dll",_=>throw new InvalidOperationException()};
            entry=new JsonArray("dotnet",assembly);if(process!="relay")probe=new JsonArray("CMD","dotnet",assembly,"--livecheck");
        }
        result["Entrypoint"]=entry;
        if(probe is not null)result["Healthcheck"]=new JsonObject{["Test"]=probe,["Interval"]=10000000000L,["Timeout"]=5000000000L,["StartPeriod"]=20000000000L};
        return result;
    }
}
