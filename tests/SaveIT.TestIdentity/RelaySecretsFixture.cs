using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Net.Sockets;
using System.Text;

// Test target only, never copied into an application image. The runner supplies
// a dedicated internal network, synthetic OS trust and no host-published ports.
internal static class RelaySecretsFixture
{
    private const string Arn = "arn:aws:secretsmanager:us-east-1:123456789012:secret:saveit/demo/staging/relay/config-Ab1234";
    private const string Version = "11111111-2222-4333-8444-555555555555";
    private const string Relative = "/v2/credentials/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private static readonly HashSet<string> Modes = ["valid", "role", "version", "binary", "oversize", "redirect", "metadata-stall", "body-stall"];

    internal static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder([]);
        if (!builder.Environment.IsEnvironment("ContainerTest")) throw new InvalidOperationException();
        builder.Logging.ClearProviders();
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile("/run/fixture/secrets.pfx", "synthetic-secrets-only", X509KeyStorageFlags.EphemeralKeySet);
        var file = new FileInfo("/run/fixture/relay-input.json");
        if (file.Length is < 1 or > 65536) throw new InvalidOperationException();
        var payload = await File.ReadAllTextAsync(file.FullName);
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 4096;
            options.ListenAnyIP(80);
            options.ListenAnyIP(443, listen => listen.UseHttps(certificate));
        });
        var mode = "valid"; var metadata = 0; var secrets = 0; var unexpected = 0; var udpPackets = 0; var controls = 0;
        await using var app = builder.Build();
        using var udp = new UdpClient(31000);
        async Task ReceiveAsync() {
            try {
                while (true) {
                    var packet = await udp.ReceiveAsync(app.Lifetime.ApplicationStopping);
                    if (Encoding.ASCII.GetString(packet.Buffer) == "synthetic-udp-control") {
                        Interlocked.Increment(ref controls);
                        await udp.SendAsync("synthetic-udp-ack"u8.ToArray(), packet.RemoteEndPoint, app.Lifetime.ApplicationStopping);
                    }
                    else Interlocked.Increment(ref udpPackets);
                }
            } catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
        }
        var receiving = ReceiveAsync();
        app.MapPost("/control/{next}", (HttpContext context, string next) => {
            if (context.Connection.LocalPort != 80 || !Modes.Contains(next)) return Results.BadRequest();
            mode = next; metadata = 0; secrets = 0; unexpected = 0; udpPackets = 0; controls = 0; return Results.NoContent();
        });
        app.MapGet("/status", (HttpContext context) => context.Connection.LocalPort == 80
            ? Results.Ok(new { metadata, secrets, unexpected, udpPackets, controls }) : Results.BadRequest());
        app.MapGet(Relative, async (HttpContext context) => {
            Interlocked.Increment(ref metadata);
            if (context.Connection.LocalPort != 80) { context.Response.StatusCode = 400; return; }
            if (mode == "metadata-stall") { await Task.Delay(Timeout.Infinite, context.RequestAborted); return; }
            // ECS uses PascalCase; ASP.NET's web JSON defaults would change it.
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new {
                RoleArn = mode == "role" ? "arn:aws:iam::123456789012:role/foreign" : "arn:aws:iam::123456789012:role/saveit-demo-staging-relay-task",
                AccessKeyId = "synthetic-access", SecretAccessKey = "synthetic-signing-material", Token = "synthetic-session-marker",
                Expiration = DateTimeOffset.UtcNow.AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            }));
        });
        app.MapPost("/", async (HttpContext context) => {
            Interlocked.Increment(ref secrets);
            if (context.Connection.LocalPort != 443 || context.Request.Headers["X-Amz-Target"] != "secretsmanager.GetSecretValue" ||
                context.Request.Headers["X-Amz-Security-Token"] != "synthetic-session-marker" ||
                !context.Request.Headers.Authorization.ToString().Contains("/us-east-1/secretsmanager/aws4_request", StringComparison.Ordinal))
            { context.Response.StatusCode = 400; return; }
            using var request = await JsonDocument.ParseAsync(context.Request.Body);
            var root = request.RootElement;
            if (root.EnumerateObject().Count() != 2 || root.GetProperty("SecretId").GetString() != Arn || root.GetProperty("VersionId").GetString() != Version)
            { context.Response.StatusCode = 400; return; }
            context.Response.ContentType = "application/x-amz-json-1.1";
            if (mode == "redirect") { context.Response.Redirect("https://secretsmanager.us-east-1.amazonaws.com/escaped"); return; }
            if (mode == "oversize") { await context.Response.WriteAsync(new string('x', 512 * 1024 + 1)); return; }
            if (mode == "body-stall") {
                await context.Response.WriteAsync("{\"ARN\":"); await context.Response.Body.FlushAsync();
                await Task.Delay(Timeout.Infinite, context.RequestAborted); return;
            }
            var response = new Dictionary<string, object?> { ["ARN"] = Arn, ["VersionId"] = mode == "version" ? "different-version" : Version, ["SecretString"] = payload };
            if (mode == "binary") response["SecretBinary"] = null;
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        });
        app.MapFallback(() => { Interlocked.Increment(ref unexpected); return Results.BadRequest(); });
        await app.StartAsync(); Console.WriteLine("Synthetic relay secrets fixture started.");
        await app.WaitForShutdownAsync();
        await receiving;
    }
}
