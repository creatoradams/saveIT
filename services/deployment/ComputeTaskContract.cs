using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

internal static class ComputeTaskContract
{
    internal static void Validate(ComputeRelease release,string process,JsonElement observed)
    {
        RejectDuplicates(observed);
        var task=Normalize(observed); var container=task["containerDefinitions"]![0]!.AsObject();
        container["secrets"]=NamedValues(container["secrets"]!.AsArray(),"valueFrom");
        Sort(task,"volumes","name"); Sort(container,"mountPoints","containerPath");
        if(!JsonNode.DeepEquals(task,Expected(release,process))) throw new InvalidOperationException(ComputeRelease.Failure);
    }
    private static void Sort(JsonObject parent,string field,string key)
    {
        var sorted=parent[field]!.AsArray().Select(x=>x!.DeepClone()).OrderBy(x=>x[key]!.GetValue<string>(),StringComparer.Ordinal).ToArray();
        parent[field]=new JsonArray(sorted);
    }
    private static JsonObject Expected(ComputeRelease release,string process)
    {
        var s=release.Scope; var d=release.Data; var r=d.Runtime;
        if(!d.Tasks.TryGetValue(process,out var reference)) throw new InvalidOperationException();
        var api=$"api.{s.Cell}.{s.Environment}.saveit.internal"; var relay=$"relay.{s.Cell}.{s.Environment}.saveit.internal";
        var provider=$"saveit.{s.Cell}.{s.Environment}.web";
        var env=new Dictionary<string,string>(StringComparer.Ordinal);
        if(process=="web")
        {
            env=new() {
                ["NODE_ENV"]="production",["NEXT_TELEMETRY_DISABLED"]="1",["HOSTNAME"]="0.0.0.0",["PORT"]="3000",["TMPDIR"]="/var/saveit/runtime",
                ["SAVEIT_API_ORIGIN"]=$"https://{relay}:8443",["SAVEIT_PUBLIC_ORIGIN"]=r.PublicOrigin,["SAVEIT_OKTA_ISSUER"]=r.OktaIssuer,
                ["SAVEIT_API_CA_SHA256"]=r.RelayCaSha256,["NODE_EXTRA_CA_CERTS"]="/opt/saveit/trust/api-ca.crt",
                ["SAVEIT_WORKLOAD_ACCOUNT_ID"]=s.AccountId,["SAVEIT_WORKLOAD_POOL_ID"]=r.PoolId,["SAVEIT_WORKLOAD_PROVIDER"]=provider
            };
        }
        else
        {
            env["DOTNET_ENVIRONMENT"]="Production";env["DOTNET_EnableDiagnostics"]="0";env["TMPDIR"]="/var/saveit/runtime";
            if(process is "api" or "worker")
            {
                env["Deployment__ComputeGuard"]="true";env["Deployment__ComputeGeneration"]=d.Generation;
                env["Deployment__Trust__DatabaseCaSha256"]=r.DatabaseCaSha256;
            }
            if(process=="api")
            {
                env["ASPNETCORE_ENVIRONMENT"]="Production";env["Deployment__ApiTls__Enabled"]="true";
                env["Deployment__ApiTls__ClientAuthentication__Enabled"]="true";
                env["Deployment__ApiTls__ClientAuthentication__CaSha256"]=r.ClientCaSha256;
                for(var i=0;i<r.ClientLeafSha256.Length;i++) env[$"Deployment__ApiTls__ClientAuthentication__LeafSha256__{i}"]=r.ClientLeafSha256[i];
                env["AllowedHosts"]=$"{api};{r.PublicOrigin[8..]};127.0.0.1";env["DevelopmentIdentity__Enabled"]="false";
                env["Identity__Okta__Enabled"]="true";env["Identity__Okta__Issuer"]=r.OktaIssuer;env["Identity__Okta__PublicOrigin"]=r.PublicOrigin;
                env["Identity__Okta__TenantId"]=r.OktaTenantId;env["Identity__Okta__ClientId"]=r.OktaClientId;
                env["Identity__KeyDirectory"]="/var/saveit/keys";env["SAVEIT_HEALTH_HOST"]="127.0.0.1";env["SAVEIT_HEALTH_PORT"]="8080";
            }
            else if(process=="worker")
            {
                env["SearchWorker__HealthPath"]="/var/saveit/runtime/saveit-worker-health.json";
                for(var i=0;i<r.WorkerTenants.Length;i++) env[$"SearchWorker__Tenants__{i}"]=r.WorkerTenants[i];
            }
            else if(process=="relay")
            {
                env["ASPNETCORE_ENVIRONMENT"]="Production";env["SAVEIT_WORKLOAD_POOL_ID"]=r.PoolId;env["SAVEIT_WORKLOAD_PROVIDER"]=provider;
                env["SAVEIT_WORKLOAD_ISSUER"]=r.WorkloadIssuer;env["SAVEIT_WORKLOAD_JWKS_SHA256"]=r.WorkloadJwksSha256;
            }
        }
        var secrets=new Dictionary<string,string>(StringComparer.Ordinal);
        if(process=="api")
        {
            foreach(var field in new[]{"ConnectionStrings__App","Identity__Okta__ClientSecret","Identity__KeyCertificateBase64","Identity__KeyCertificatePassword","Deployment__ApiTls__CertificateBase64","Deployment__ApiTls__CertificatePassword"}) AddSecret(field);
            for(var i=0;i<r.RetiredKeyCount;i++) { AddSecret($"Identity__PreviousKeyCertificates__{i}__Base64");AddSecret($"Identity__PreviousKeyCertificates__{i}__Password"); }
        }
        else if(process=="worker") AddSecret("ConnectionStrings__SearchWorker");
        void AddSecret(string field) => secrets.Add(field,$"{reference.ConfigurationArn}:{field}::{reference.ConfigurationVersionId}");
        var port=process=="web"?3000:8443;
        string[] health=process switch {
            "api"=>["CMD","dotnet","SaveIT.Api.dll","--livecheck"],
            "worker"=>["CMD","dotnet","SaveIT.Workers.dll","--livecheck"],
            "relay"=>["CMD","dotnet","SaveIT.Deployment.dll","probe-api-relay"],
            "web"=>["CMD","/usr/local/bin/node","-e","fetch('http://127.0.0.1:3000/health/live',{signal:AbortSignal.timeout(3000)}).then(r=>process.exit(r.status===200?0:1)).catch(()=>process.exit(1))"],
            _=>throw new InvalidOperationException()
        };
        var task=JsonSerializer.SerializeToNode(new {
            taskDefinitionArn=reference.TaskDefinitionArn,family=$"{s.Name}-{process}",revision=int.Parse(reference.TaskDefinitionArn[(reference.TaskDefinitionArn.LastIndexOf(':')+1)..],CultureInfo.InvariantCulture),status="ACTIVE",
            taskRoleArn=$"arn:aws:iam::{s.AccountId}:role/{s.Name}-{process}-task",executionRoleArn=$"arn:aws:iam::{s.AccountId}:role/{s.Name}-{process}-execution",
            networkMode="awsvpc",requiresCompatibilities=new[]{"FARGATE"},cpu=process=="worker"?"256":"512",memory="1024",
            runtimePlatform=new { operatingSystemFamily="LINUX",cpuArchitecture="X86_64" },volumes=new[]{new{name="runtime"}},
            containerDefinitions=new[]{new {
                name=process,image=reference.Image,essential=true,user="1654:1654",readonlyRootFilesystem=true,privileged=false,interactive=false,pseudoTerminal=false,stopTimeout=30,
                linuxParameters=new {capabilities=new {drop=new[]{"ALL"}}},environment=env,secrets,
                mountPoints=new[]{new {sourceVolume="runtime",containerPath="/var/saveit/runtime",readOnly=false}},
                portMappings=process=="worker"?[]:new[]{new {containerPort=port,hostPort=port,protocol="tcp"}},
                healthCheck=new {command=health,interval=10,timeout=5,retries=3,startPeriod=30},
                logConfiguration=new {logDriver="awslogs",options=new Dictionary<string,string>{
                    ["awslogs-group"]=$"/{s.Path}/{process}",["awslogs-region"]=s.Region,["awslogs-stream-prefix"]=process,["awslogs-create-group"]="false",["mode"]="non-blocking",["max-buffer-size"]="4m"
                }}
            }}
        })!.AsObject();
        var container=task["containerDefinitions"]![0]!.AsObject();
        if(process=="api")
        {
            task["volumes"]!.AsArray().Add(JsonSerializer.SerializeToNode(new {name="session-keys",efsVolumeConfiguration=new {
                fileSystemId=r.FileSystemId,rootDirectory="/",transitEncryption="ENABLED",authorizationConfig=new {accessPointId=r.AccessPointId,iam="ENABLED"}
            }}));
            container["mountPoints"]!.AsArray().Add(JsonSerializer.SerializeToNode(new {sourceVolume="session-keys",containerPath="/var/saveit/keys",readOnly=false}));
        }
        if(process=="relay")
        {
            container["entryPoint"]=new JsonArray("dotnet","SaveIT.Deployment.dll");
            container["command"]=new JsonArray("serve-api-relay-secrets",$"https://{api}:8443",$"https://{relay}:8443",reference.ConfigurationArn,reference.ConfigurationVersionId);
        }
        Sort(task,"volumes","name");Sort(container,"mountPoints","containerPath");
        return task;
    }
}
