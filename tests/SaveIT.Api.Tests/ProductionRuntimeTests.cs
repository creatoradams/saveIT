using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveIT.Api.Operations;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class ProductionRuntimeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Readiness_uses_the_startup_bound_connection_instead_of_raw_configuration()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration["ConnectionStrings:App"] = "Host=must-not-use.invalid;SSL Mode=VerifyFull;Root Certificate=/must-not-read";
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapOperationalHealth(ConnectionPolicy.RuntimeConnection(fixture.AppConnection));
        await app.StartAsync();
        using var client = app.GetTestClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ready\"}", await response.Content.ReadAsStringAsync());
    }
    private sealed class Peer(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.Use(async (context, run) => { context.Connection.RemoteIpAddress = IPAddress.Parse(address); await run(); });
            next(app);
        };
    }
    [Theory]
    [InlineData("10.77.0.1", "proxy", 200)]
    [InlineData("10.77.0.2", "proxy", 500)]
    [InlineData("10.77.0.2", "network", 200)]
    [InlineData("10.77.0.1", "none", 500)]
    public async Task Secure_csrf_respects_only_explicit_proxy_or_network_trust(string peer, string trust, int status)
    {
        using var root = new ApiFactory(fixture.AppConnection, developmentIdentity: false);
        using var factory = root.WithWebHostBuilder(builder => {
            foreach (var (key, value) in new Dictionary<string,string> {
                ["Identity:Okta:Enabled"]="true", ["Identity:Okta:Issuer"]="https://synthetic.okta.example",
                ["Identity:Okta:ClientId"]="synthetic", ["Identity:Okta:ClientSecret"]="synthetic-only",
                ["Identity:Okta:TenantId"]="10000000-0000-0000-0000-000000000001", ["Identity:Okta:PublicOrigin"]="https://localhost"
            }) builder.UseSetting(key,value);
            if (trust == "proxy") builder.UseSetting("Deployment:TrustedProxies:0", "10.77.0.1");
            if (trust == "network") builder.UseSetting("Deployment:TrustedNetworks:0", "10.77.0.0/24");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new Peer(peer)));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/csrf");
        request.Headers.Add("X-Forwarded-Proto", "https");
        var response = await client.SendAsync(request); Assert.Equal(status, (int)response.StatusCode);
        if (status == 200) Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.Contains("secure", StringComparison.OrdinalIgnoreCase));
        else Assert.DoesNotContain("ConnectionStrings", await response.Content.ReadAsStringAsync());
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public void Committed_progress_stays_fresh_across_slow_tenant_cycles_and_failures_remain_until_that_tenant_recovers()
    {
        var path = Path.GetTempFileName(); var clock = new Clock(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        try
        {
            var progress = new WorkerProgress(path, clock);
            for (var i = 0; i < 10; i++)
            { clock.Now += TimeSpan.FromSeconds(20); progress.Success(a); Assert.True(WorkerHealth.IsReady(path, clock)); }
            progress.Failure(b); Assert.False(WorkerHealth.IsReady(path, clock));
            clock.Now += TimeSpan.FromSeconds(20); progress.Success(a); Assert.False(WorkerHealth.IsReady(path, clock));
            progress.Success(b); Assert.True(WorkerHealth.IsReady(path, clock));
            clock.Now += TimeSpan.FromSeconds(61); Assert.False(WorkerHealth.IsReady(path, clock));
        }
        finally { File.Delete(path); }
    }
}
