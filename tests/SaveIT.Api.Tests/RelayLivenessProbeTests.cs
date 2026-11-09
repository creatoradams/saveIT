using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SaveIT.Deployment;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("health-probe")]
public sealed class RelayLivenessProbeTests
{
    [Theory]
    [InlineData(200, 0)]
    [InlineData(204, 1)]
    [InlineData(301, 1)]
    [InlineData(401, 1)]
    [InlineData(503, 1)]
    public async Task Only_empty_200_liveness_passes_without_credentials_or_redirects(int status, int expected)
    {
        var requests = 0;
        await using var app = await StartAsync(context => {
            requests++;
            Assert.Equal("GET", context.Request.Method);
            Assert.Equal("/health/live", context.Request.Path.Value);
            Assert.Empty(context.Request.Query);
            Assert.Equal(0, context.Request.Headers.Authorization.Count);
            Assert.Equal(0, context.Request.Headers.Cookie.Count);
            Assert.Equal("close", context.Request.Headers.Connection);
            context.Response.StatusCode = status;
            context.Response.Headers.Location = "/must-not-follow";
            return Task.CompletedTask;
        });
        Assert.Equal(expected, await RelayLivenessProbe.ProbeAsync(Port(app)));
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Any_body_or_encoding_fails(bool body)
    {
        await using var app = await StartAsync(async context => {
            if (body) await context.Response.WriteAsync("synthetic-body");
            else context.Response.Headers.ContentEncoding = "gzip";
        });
        Assert.Equal(1, await RelayLivenessProbe.ProbeAsync(Port(app)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_deadline_bounds_headers_and_body(bool headers)
    {
        await using var app = await StartAsync(async context => {
            if (headers) {
                context.Response.Headers.TransferEncoding = "chunked";
                await context.Response.StartAsync();
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        Assert.Equal(1, await RelayLivenessProbe.ProbeAsync(Port(app)).WaitAsync(TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public async Task Oversized_headers_fail()
    {
        await using (var app = await StartAsync(context => {
            context.Response.Headers["X-Synthetic"] = new string('x', 8192);
            return Task.CompletedTask;
        })) Assert.Equal(1, await RelayLivenessProbe.ProbeAsync(Port(app)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Announced_or_actual_trailers_fail(bool actual)
    {
        await using var app = await StartAsync(async context => {
            context.Response.DeclareTrailer("X-Synthetic");
            await context.Response.StartAsync();
            if (actual) context.Response.AppendTrailer("X-Synthetic", "present");
        });
        Assert.Equal(1, await RelayLivenessProbe.ProbeAsync(Port(app)));
    }

    [Fact]
    public async Task Mixed_cli_arguments_fail_before_any_probe_or_configuration()
    {
        Assert.Equal(1, await DeploymentProgram.Main(["probe-api-relay", "http://foreign.invalid"]));
        Assert.Equal(1, await DeploymentProgram.Main(["probe-api-relay", "serve-api-relay-secrets"]));
    }

    [Fact]
    public async Task Ambient_proxy_is_ignored_with_an_active_proxy_control()
    {
        var proxyRequests = 0;
        await using var proxy = await StartAsync(context => { proxyRequests++; return Task.CompletedTask; });
        var proxyUri = $"http://127.0.0.1:{Port(proxy)}";
        var directRequests = 0;
        await using var live = await StartAsync(_ => { directRequests++; return Task.CompletedTask; });
        var previous = HttpClient.DefaultProxy;
        try {
            HttpClient.DefaultProxy = new WebProxy(proxyUri) { BypassProxyOnLocal = false };
            using var ordinaryClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var control = await ordinaryClient.GetAsync("http://synthetic.invalid/control");
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            Assert.Equal(1, proxyRequests);
            Assert.Equal(0, await RelayLivenessProbe.ProbeAsync(Port(live)));
            Assert.Equal(1, directRequests);
            Assert.Equal(1, proxyRequests);
        }
        finally { HttpClient.DefaultProxy = previous; }
    }

    private static int Port(WebApplication app) => new Uri(app.Urls.Single()).Port;
    private static async Task<WebApplication> StartAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["AllowedHosts"] = "*"; // Isolated loopback fixture, including absolute-form proxy controls.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build(); app.Run(handler); await app.StartAsync(); return app;
    }
}
