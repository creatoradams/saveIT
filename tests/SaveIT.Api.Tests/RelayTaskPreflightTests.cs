using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class RelayTaskPreflightTests
{
    private static JsonObject Payload()
    {
        // Independently reviewed Terraform fixture, not the validator's template.
        var container = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "relay-container.json")))!;
        return new JsonObject
        {
            ["expected"] = new JsonObject
            {
                ["accountId"] = "111122223333", ["region"] = "us-east-1", ["cell"] = "pilot", ["environment"] = "staging",
                ["taskDefinitionArn"] = "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-relay:10",
                ["image"] = container["image"]!.DeepClone(), ["configurationArn"] = container["command"]![3]!.DeepClone(), ["configurationVersionId"] = container["command"]![4]!.DeepClone()
            },
            ["observed"] = new JsonObject { ["taskDefinition"] = new JsonObject
            {
                ["taskDefinitionArn"] = "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-relay:10", ["family"] = "saveit-pilot-staging-relay", ["revision"] = 10, ["status"] = "ACTIVE",
                ["taskRoleArn"] = "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-task", ["executionRoleArn"] = "arn:aws:iam::111122223333:role/saveit-pilot-staging-relay-execution",
                ["networkMode"] = "awsvpc", ["requiresCompatibilities"] = new JsonArray("FARGATE"), ["cpu"] = "512", ["memory"] = "1024",
                ["runtimePlatform"] = new JsonObject { ["cpuArchitecture"] = "X86_64", ["operatingSystemFamily"] = "LINUX" },
                ["volumes"] = new JsonArray(new JsonObject { ["name"] = "runtime", ["configuredAtLaunch"] = false }),
                ["containerDefinitions"] = new JsonArray(container)
            } }
        };
    }

    private static JsonObject Task(JsonObject payload) => payload["observed"]!["taskDefinition"]!.AsObject();
    private static JsonObject Container(JsonObject payload) => Task(payload)["containerDefinitions"]![0]!.AsObject();
    private static Task Validate(string json) => RelayTaskPreflight.ValidateAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    private static async Task Rejected(string json)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Validate(json));
        Assert.Equal(RelayTaskPreflight.Failure, error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Independently_bound_workload_metadata_requires_exact_observed_public_environment()
    {
        var payload = Payload();
        payload["expected"]!["workloadAdmission"] = new JsonObject {
            ["poolId"] = "us-east-1:10000000-0000-0000-0000-000000000001", ["issuer"] = "https://cognito-identity.amazonaws.com",
            ["provider"] = "saveit.pilot.staging.web", ["jwksSha256"] = new string('b',64)
        };
        await Rejected(payload.ToJsonString()); // Expected binding alone cannot silently admit legacy intake.
        var environment = Container(payload)["environment"]!.AsArray();
        foreach (var (name,value) in new[] {
            ("SAVEIT_WORKLOAD_POOL_ID","us-east-1:10000000-0000-0000-0000-000000000001"),
            ("SAVEIT_WORKLOAD_ISSUER","https://cognito-identity.amazonaws.com"),
            ("SAVEIT_WORKLOAD_PROVIDER","saveit.pilot.staging.web"), ("SAVEIT_WORKLOAD_JWKS_SHA256",new string('b',64)) })
            environment.Add(new JsonObject { ["name"] = name, ["value"] = value });
        await Validate(payload.ToJsonString());
        foreach (var field in new[] { "poolId", "issuer", "provider", "jwksSha256" }) {
            var drifted = payload.DeepClone().AsObject(); drifted["expected"]!["workloadAdmission"]![field] = "foreign"; await Rejected(drifted.ToJsonString());
        }
        payload["expected"]!.AsObject().Remove("workloadAdmission");
        await Rejected(payload.ToJsonString()); // Supplied extra environment cannot opt itself in.
    }

    [Fact]
    public async Task Independent_container_fixture_and_explicit_return_defaults_pass()
    {
        var payload = Payload();
        await Validate(payload.ToJsonString());
        var task = Task(payload); var container = Container(payload);
        task["registeredBy"] = "synthetic-observer-not-authority"; task["registeredAt"] = "2026-11-09T00:00:00Z";
        task["requiresAttributes"] = new JsonArray(new JsonObject { ["name"] = "ecs.capability.task-iam-role" });
        task["compatibilities"] = new JsonArray("EC2", "FARGATE");
        task["enableFaultInjection"] = false; task["placementConstraints"] = new JsonArray(); task["inferenceAccelerators"] = new JsonArray();
        task["ephemeralStorage"] = new JsonObject { ["sizeInGiB"] = 20 };
        task["volumes"]![0]!["host"] = new JsonObject(); task["volumes"]![0]!.AsObject().Remove("configuredAtLaunch");
        container["cpu"] = 0;
        foreach (var field in new[] { "environmentFiles", "systemControls", "volumesFrom", "ulimits" }) container[field] = new JsonArray();
        container["linuxParameters"]!["capabilities"]!["add"] = new JsonArray();
        container["logConfiguration"]!["secretOptions"] = new JsonArray();
        container["environment"] = new JsonArray(container["environment"]!.AsArray().Reverse().Select(x => x!.DeepClone()).ToArray());
        payload["observed"]!["tags"] = new JsonArray(new JsonObject { ["key"] = "synthetic-not-authority", ["value"] = "unverified" });
        await Validate(payload.ToJsonString());
    }

    [Fact]
    public async Task Independently_scoped_production_and_maximum_canonical_revision_pass()
    {
        var json = Payload().ToJsonString().Replace("111122223333", "444455556666", StringComparison.Ordinal)
            .Replace("us-east-1", "us-west-2", StringComparison.Ordinal).Replace("pilot", "team42", StringComparison.Ordinal)
            .Replace("staging", "production", StringComparison.Ordinal).Replace("relay:10\"", "relay:2147483647\"", StringComparison.Ordinal)
            .Replace("\"revision\":10", "\"revision\":2147483647", StringComparison.Ordinal);
        await Validate(json);
    }

    [Theory]
    [InlineData("status", "\"INACTIVE\"")] [InlineData("revision", "11")] [InlineData("revision", "\"10\"")]
    [InlineData("family", "\"saveit-other-staging-relay\"")] [InlineData("taskDefinitionArn", "\"saveit-pilot-staging-relay:10\"")]
    [InlineData("taskRoleArn", "\"arn:aws:iam::111122223333:role/saveit-pilot-staging-api-task\"")]
    [InlineData("executionRoleArn", "\"arn:aws:iam::444455556666:role/saveit-pilot-staging-relay-execution\"")]
    [InlineData("networkMode", "\"host\"")] [InlineData("requiresCompatibilities", "[\"EC2\"]")]
    [InlineData("cpu", "\"1024\"")] [InlineData("memory", "\"2048\"")]
    [InlineData("runtimePlatform", "{\"cpuArchitecture\":\"ARM64\",\"operatingSystemFamily\":\"LINUX\"}")]
    [InlineData("enableFaultInjection", "true")] [InlineData("pidMode", "\"task\"")] [InlineData("ipcMode", "\"host\"")]
    [InlineData("proxyConfiguration", "{}")] [InlineData("ephemeralStorage", "{\"sizeInGiB\":21}")]
    [InlineData("placementConstraints", "[{}]")] [InlineData("newAwsProperty", "false")]
    public async Task Task_identity_resources_and_unknown_extensions_fail(string field, string json)
    {
        var payload = Payload(); Task(payload)[field] = JsonNode.Parse(json); await Rejected(payload.ToJsonString());
    }

    [Theory]
    [InlineData("image", "\"111122223333.dkr.ecr.us-east-1.amazonaws.com/saveit/pilot/staging/deployment:latest\"")]
    [InlineData("user", "\"0\"")] [InlineData("readonlyRootFilesystem", "false")] [InlineData("privileged", "true")]
    [InlineData("interactive", "true")] [InlineData("pseudoTerminal", "true")] [InlineData("stopTimeout", "120")]
    [InlineData("entryPoint", "[\"sh\",\"-c\"]")] [InlineData("command", "[\"serve-api-relay\"]")]
    [InlineData("environmentFiles", "[{\"type\":\"s3\",\"value\":\"synthetic\"}]")]
    [InlineData("secrets", "[{\"name\":\"ConnectionStrings__App\",\"valueFrom\":\"synthetic\"}]")]
    [InlineData("repositoryCredentials", "{\"credentialsParameter\":\"synthetic\"}")]
    [InlineData("linuxParameters", "{\"capabilities\":{\"drop\":[\"ALL\"],\"add\":[\"SYS_ADMIN\"]}}")]
    [InlineData("systemControls", "[{\"namespace\":\"synthetic\",\"value\":\"synthetic\"}]")]
    [InlineData("volumesFrom", "[{\"sourceContainer\":\"sidecar\"}]")]
    [InlineData("mountPoints", "[]")] [InlineData("portMappings", "[]")] [InlineData("healthCheck", "null")]
    [InlineData("logConfiguration", "{\"logDriver\":\"none\"}")] [InlineData("restartPolicy", "{\"enabled\":true}")]
    public async Task Additional_authority_or_changed_runtime_settings_fail(string field, string json)
    {
        var payload = Payload(); Container(payload)[field] = JsonNode.Parse(json); await Rejected(payload.ToJsonString());
    }

    [Theory]
    [InlineData("configuredAtLaunch", "true")] [InlineData("host", "{\"sourcePath\":\"/host\"}")]
    [InlineData("efsVolumeConfiguration", "{}")] [InlineData("dockerVolumeConfiguration", "{}")]
    [InlineData("s3filesVolumeConfiguration", "{}")] [InlineData("fsxWindowsFileServerVolumeConfiguration", "{}")]
    public async Task Scratch_cannot_become_persistent_host_or_managed_storage(string field, string json)
    {
        var payload = Payload(); Task(payload)["volumes"]![0]![field] = JsonNode.Parse(json); await Rejected(payload.ToJsonString());
    }

    [Theory]
    [InlineData("accountId", "111122223333\n")] [InlineData("cell", "Pilot")] [InlineData("region", "cn-north-1")]
    [InlineData("environment", "development")] [InlineData("configurationVersionId", "AWSCURRENT")]
    [InlineData("configurationArn", "arn:aws:secretsmanager:us-east-1:111122223333:secret:saveit/pilot/staging/relay/config-Abcd90:::")]
    [InlineData("taskDefinitionArn", "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-relay:010")]
    [InlineData("taskDefinitionArn", "arn:aws:ecs:us-east-1:111122223333:task-definition/saveit-pilot-staging-relay:2147483648")]
    [InlineData("image", "sha256:4444444444444444444444444444444444444444444444444444444444444444")]
    public async Task Expectations_require_canonical_scoped_metadata(string field, string value)
    {
        var payload = Payload(); payload["expected"]![field] = value; await Rejected(payload.ToJsonString());
    }

    [Fact]
    public async Task Sidecars_extra_volumes_and_duplicate_or_secret_environment_fail()
    {
        var payload = Payload(); Task(payload)["containerDefinitions"]!.AsArray().Add(Container(payload).DeepClone()); await Rejected(payload.ToJsonString());
        payload = Payload(); Task(payload)["volumes"]!.AsArray().Add(new JsonObject { ["name"] = "extra" }); await Rejected(payload.ToJsonString());
        payload = Payload(); Container(payload)["environment"]!.AsArray().Add(Container(payload)["environment"]![0]!.DeepClone()); await Rejected(payload.ToJsonString());
        payload = Payload(); Container(payload)["environment"]!.AsArray().Add(new JsonObject { ["name"] = "ConnectionStrings__App", ["value"] = "synthetic-private-marker" }); await Rejected(payload.ToJsonString());
        payload = Payload(); Container(payload)["command"]!.AsArray()[4] = "10000000-0000-0000-0000-000000000008"; await Rejected(payload.ToJsonString());
        payload = Payload(); Container(payload)["logConfiguration"]!["secretOptions"] = new JsonArray(new JsonObject { ["name"] = "synthetic", ["valueFrom"] = "synthetic" }); await Rejected(payload.ToJsonString());
    }

    [Fact]
    public async Task All_object_depths_reject_duplicate_and_escaped_aliases_even_in_ignored_metadata()
    {
        var json = Payload().ToJsonString();
        await Rejected(json.Replace("\"status\":\"ACTIVE\"", "\"status\":\"INACTIVE\",\"st\\u0061tus\":\"ACTIVE\"", StringComparison.Ordinal));
        await Rejected(json.Replace("\"observed\":{", "\"observed\":{\"tags\":[{\"key\":\"a\",\"key\":\"b\"}],", StringComparison.Ordinal));
        await Rejected(json.Replace("\"logDriver\":\"awslogs\"", "\"logDriver\":\"none\",\"logDriver\":\"awslogs\"", StringComparison.Ordinal));
        await Rejected(json.Replace("\"expected\":{", "\"expected\":{\"cell\":\"other\",", StringComparison.Ordinal));
        await Rejected(json[..^1] + ",\"unexpected\":false}");
    }

    [Fact]
    public async Task Original_byte_bound_utf8_eof_and_depth_are_enforced()
    {
        var json = Payload().ToJsonString();
        await Validate(json.PadRight(64 * 1024));
        await Rejected(json.PadRight(64 * 1024 + 1)); await Rejected(json + "{}"); await Rejected("");
        await Rejected(json.Replace("\"observed\":{", "\"observed\":{\"tags\":" + new string('[', 25) + "0" + new string(']', 25) + ",", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RelayTaskPreflight.ValidateAsync(new MemoryStream([0xff, 0xfe])));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RelayTaskPreflight.ValidateAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), cancelled.Token));
    }

    [Theory]
    [InlineData("valid")] [InlineData("private-drift")] [InlineData("mixed-arguments")] [InlineData("held-input")]
    public async Task Actual_cli_has_fixed_output_ignores_ambient_configuration_and_requires_eof(string scenario)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(RelayTaskPreflight).Assembly.Location); start.ArgumentList.Add("check-relay-task");
        if (scenario == "mixed-arguments") start.ArgumentList.Add("serve-api-relay-secrets");
        foreach (var name in new[] { "SAVEIT_DEPLOYMENT_CONNECTION", "AWS_PROFILE", "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "AWS_ENDPOINT_URL", "HTTP_PROXY" }) start.Environment[name] = "synthetic-private-marker";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        try
        {
            var payload = Payload();
            if (scenario == "private-drift") Container(payload)["environment"]!.AsArray().Add(new JsonObject { ["name"] = "ConnectionStrings__App", ["value"] = "synthetic-private-marker" });
            if (scenario != "mixed-arguments") { await process.StandardInput.WriteAsync(payload.ToJsonString()); await process.StandardInput.FlushAsync(); }
            if (scenario != "held-input") process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(17));
            if (scenario == "held-input") Assert.InRange(elapsed.Elapsed.TotalSeconds, 8, 17);
            Assert.Equal(scenario == "valid" ? 0 : 1, process.ExitCode);
            Assert.Equal(scenario == "valid" ? RelayTaskPreflight.Success : "", (await output).Trim());
            Assert.Equal(scenario == "valid" ? "" : RelayTaskPreflight.Failure, (await errors).Trim());
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }
}
