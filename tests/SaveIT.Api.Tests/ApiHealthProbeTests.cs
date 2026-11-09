using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SaveIT.Api.Operations;
using Xunit;

namespace SaveIT.Api.Tests;

[CollectionDefinition("health-probe", DisableParallelization = true)]
public sealed class HealthProbeCollection;

[Collection("health-probe")]
public sealed class ApiHealthProbeTests
{
    [Fact]
    public async Task Liveness_probe_does_not_depend_on_readiness_or_contact_the_database()
    {
        await using var app=await StartAsync(context=>{context.Response.StatusCode=context.Request.Path=="/health/live"?200:503;return Task.CompletedTask;});
        Assert.Equal(0,await ApiHealthProbe.RunAsync(Port(app),live:true));
        Assert.Equal(1,await ApiHealthProbe.RunAsync(Port(app)));
    }
    [Theory]
    [InlineData(200, 0)]
    [InlineData(204, 1)]
    [InlineData(301, 1)]
    [InlineData(401, 1)]
    [InlineData(503, 1)]
    public async Task Probe_checks_only_readiness_without_credentials_or_redirects(int status, int expected)
    {
        var requests = 0;
        await using var app = await StartAsync(context =>
        {
            Interlocked.Increment(ref requests);
            Assert.Equal("/health/ready", context.Request.Path.Value);
            Assert.Equal("api.saveit.internal", context.Request.Host.Value);
            Assert.False(context.Request.Headers.ContainsKey("Authorization"));
            Assert.False(context.Request.Headers.ContainsKey("Cookie"));
            context.Response.StatusCode = status;
            context.Response.Headers.Location = "/must-not-follow";
            return Task.CompletedTask;
        });
        Assert.Equal(expected, await ApiHealthProbe.RunAsync(Port(app), "api.saveit.internal"));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("08080", "localhost")]
    [InlineData("+8080", "localhost")]
    [InlineData("1023", "localhost")]
    [InlineData("65536", "localhost")]
    [InlineData("8080", "http://localhost")]
    [InlineData("8080", "localhost:8080")]
    [InlineData("8080", "localhost\r\nInjected: yes")]
    [InlineData("8080", "localhost\n")]
    [InlineData("8080", "")]
    public async Task Malformed_metadata_fails_closed(string port, string host) =>
        Assert.Equal(1, await ApiHealthProbe.RunAsync(port, host));

    [Fact]
    public async Task Header_wait_is_bounded()
    {
        await using var app = await StartAsync(async context =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        Assert.Equal(1, await ApiHealthProbe.RunAsync(Port(app)).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Oversized_headers_fail_without_consuming_a_body()
    {
        await using var app = await StartAsync(context =>
        {
            context.Response.Headers["X-Synthetic-Oversized"] = new string('a', 8192);
            return Task.CompletedTask;
        });
        Assert.Equal(1, await ApiHealthProbe.RunAsync(Port(app)));
    }

    [Fact]
    public async Task Probe_finishes_after_headers_without_waiting_for_body()
    {
        await using var app = await StartAsync(async context =>
        {
            context.Response.ContentLength = 1;
            await context.Response.StartAsync();
            await context.Response.Body.FlushAsync(context.RequestAborted);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        Assert.Equal(0, await ApiHealthProbe.RunAsync(Port(app)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Standalone_cli_never_starts_the_production_host(bool mixedArguments, int expected)
    {
        var requests = 0;
        await using var app = await StartAsync(context =>
        {
            Interlocked.Increment(ref requests);
            return Task.CompletedTask;
        });
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(ApiHealthProbe).Assembly.Location);
        start.ArgumentList.Add("--healthcheck");
        if (mixedArguments) start.ArgumentList.Add("--migrate");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__App"] = "";
        start.Environment["Identity__Okta__Enabled"] = "false";
        start.Environment["SAVEIT_HEALTH_PORT"] = Port(app);
        start.Environment["SAVEIT_HEALTH_HOST"] = "127.0.0.1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.Equal(expected, process.ExitCode);
        Assert.Equal(mixedArguments ? 0 : 1, requests);
        Assert.Empty(await stdout);
        Assert.Empty(await stderr);
    }

    [Fact]
    public async Task Probe_bypasses_an_active_default_proxy()
    {
        var proxyRequests = 0;
        var directRequests = 0;
        await using var proxy = await StartAsync(context =>
        {
            Interlocked.Increment(ref proxyRequests);
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        await using var target = await StartAsync(context =>
        {
            Interlocked.Increment(ref directRequests);
            context.Response.StatusCode = 200;
            return Task.CompletedTask;
        });
        var previous = HttpClient.DefaultProxy;
        try
        {
            HttpClient.DefaultProxy = new WebProxy(proxy.Urls.Single(), false);
            using var ordinaryClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var control = await ordinaryClient.GetAsync("http://probe.invalid/positive-control");
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            Assert.Equal(1, proxyRequests);
            Assert.Equal(0, await ApiHealthProbe.RunAsync(Port(target)));
            Assert.Equal(1, directRequests);
            Assert.Equal(1, proxyRequests);
        }
        finally { HttpClient.DefaultProxy = previous; }
    }

    private static string Port(WebApplication app) => new Uri(app.Urls.Single()).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<WebApplication> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["AllowedHosts"] = "*"; // Isolated loopback test server, not application ingress.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(handler);
        await app.StartAsync();
        return app;
    }
}
