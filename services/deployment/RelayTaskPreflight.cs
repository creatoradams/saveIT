using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static SaveIT.Deployment.TaskMetadata;

namespace SaveIT.Deployment;

// Supplied observations and expectations are NOT authenticated launch authority.
// No cloud client, payload lookup, file output or task activation occurs here.
public static class RelayTaskPreflight
{
    public const string Failure = "SaveIT relay task preflight failed.";
    public const string Success = "SaveIT relay task preflight passed (supplied metadata only; no launch approval).";

    public static async Task ValidateAsync(Stream input, CancellationToken cancellationToken = default)
    {
        try
        {
            await ProtectedJsonInput.ReadAsync(input, 24, root =>
            {
                RejectDuplicates(root);
                var envelope = Object(root, "expected", "observed");
                var bound = envelope["expected"].TryGetProperty("workloadAdmission", out var workload);
                var fields = new[] { "accountId", "region", "cell", "environment", "taskDefinitionArn", "image", "configurationArn", "configurationVersionId" };
                var expected = Object(envelope["expected"], bound ? [.. fields, "workloadAdmission"] : fields);
                string Text(string name) => expected[name].GetString() ?? throw new InvalidOperationException();
                var account = Text("accountId"); var region = Text("region"); var cell = Text("cell"); var environment = Text("environment");
                Match(account, "[0-9]{12}"); Match(region, "[a-z]{2}-[a-z]+-[1-9]"); Match(cell, "[a-z][a-z0-9]{1,11}");
                if (region.StartsWith("cn-", StringComparison.Ordinal) || environment is not ("staging" or "production")) throw new InvalidOperationException();
                var name = $"saveit-{cell}-{environment}-relay";
                var scope = $"saveit/{cell}/{environment}";
                var taskArn = Text("taskDefinitionArn");
                Match(taskArn, Regex.Escape($"arn:aws:ecs:{region}:{account}:task-definition/{name}:") + "[1-9][0-9]{0,9}");
                var revision = int.Parse(taskArn[(taskArn.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
                var image = Text("image");
                Match(image, Regex.Escape($"{account}.dkr.ecr.{region}.amazonaws.com/{scope}/deployment@sha256:") + "[a-f0-9]{64}");
                var configuration = Text("configurationArn");
                Match(configuration, Regex.Escape($"arn:aws:secretsmanager:{region}:{account}:secret:{scope}/relay/config-") + "[A-Za-z0-9]{6}");
                var version = Text("configurationVersionId");
                Match(version, "[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
                var environmentValues = new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production", ["DOTNET_ENVIRONMENT"] = "Production", ["DOTNET_EnableDiagnostics"] = "0", ["TMPDIR"] = "/var/saveit/runtime" };
                if (bound)
                {
                    var admission = Object(workload, "poolId", "issuer", "provider", "jwksSha256");
                    string Value(string field) => admission[field].GetString() ?? throw new InvalidOperationException();
                    var pool = Value("poolId"); var issuer = Value("issuer"); var provider = Value("provider"); var hash = Value("jwksSha256");
                    Match(pool, Regex.Escape(region + ":") + "[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
                    Match(hash, "[a-f0-9]{64}");
                    if (provider != $"saveit.{cell}.{environment}.web" || issuer != "https://cognito-identity.amazonaws.com" && issuer != $"https://cognito-identity.{region}.amazonaws.com") throw new InvalidOperationException();
                    environmentValues["SAVEIT_WORKLOAD_POOL_ID"] = pool; environmentValues["SAVEIT_WORKLOAD_ISSUER"] = issuer;
                    environmentValues["SAVEIT_WORKLOAD_PROVIDER"] = provider; environmentValues["SAVEIT_WORKLOAD_JWKS_SHA256"] = hash;
                }
                // Tags and explicitly known service defaults never establish authority.
                var task = Normalize(envelope["observed"]);
                var contract = JsonSerializer.SerializeToNode(new
                {
                    taskDefinitionArn = taskArn, family = name, revision, status = "ACTIVE",
                    taskRoleArn = $"arn:aws:iam::{account}:role/{name}-task",
                    executionRoleArn = $"arn:aws:iam::{account}:role/{name}-execution",
                    networkMode = "awsvpc", requiresCompatibilities = new[] { "FARGATE" }, cpu = "512", memory = "1024",
                    runtimePlatform = new { operatingSystemFamily = "LINUX", cpuArchitecture = "X86_64" },
                    volumes = new[] { new { name = "runtime" } },
                    containerDefinitions = new[] { new
                    {
                        name = "relay", image, essential = true, user = "1654:1654", readonlyRootFilesystem = true,
                        privileged = false, interactive = false, pseudoTerminal = false, stopTimeout = 30,
                        linuxParameters = new { capabilities = new { drop = new[] { "ALL" } } },
                        entryPoint = new[] { "dotnet", "SaveIT.Deployment.dll" },
                        command = new[] { "serve-api-relay-secrets", $"https://api.{cell}.{environment}.saveit.internal:8443", $"https://relay.{cell}.{environment}.saveit.internal:8443", configuration, version },
                        environment = environmentValues,
                        secrets = Array.Empty<object>(),
                        mountPoints = new[] { new { sourceVolume = "runtime", containerPath = "/var/saveit/runtime", readOnly = false } },
                        portMappings = new[] { new { containerPort = 8443, hostPort = 8443, protocol = "tcp" } },
                        healthCheck = new { command = new[] { "CMD", "dotnet", "SaveIT.Deployment.dll", "probe-api-relay" }, interval = 10, timeout = 5, retries = 3, startPeriod = 30 },
                        logConfiguration = new { logDriver = "awslogs", options = new Dictionary<string, string>
                        {
                            ["awslogs-group"] = $"/{scope}/relay", ["awslogs-region"] = region, ["awslogs-stream-prefix"] = "relay",
                            ["awslogs-create-group"] = "false", ["mode"] = "non-blocking", ["max-buffer-size"] = "4m"
                        } }
                    } }
                });
                if (!JsonNode.DeepEquals(task, contract)) throw new InvalidOperationException();
                return true;
            }, cancellationToken);
        }
        catch (Exception) { throw new InvalidOperationException(Failure); }
    }

}
