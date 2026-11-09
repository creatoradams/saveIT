using System.Buffers;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SaveIT.Deployment;

// Narrow REST/OIDC carrier; bound deployments require a separate workload token.
public sealed class ApiRelay(ApiRelayConfiguration configuration, CancellationToken stopping = default, RelayWorkloadAdmission? admission = null) : IDisposable
{
    public const int RequestLimit = 1_048_576;
    public const int ResponseLimit = 32 * 1_048_576;
    private readonly SemaphoreSlim slots = new(16, 16);
    private static readonly string[] RequestHeaders = ["Cookie", "X-CSRF-TOKEN", "Accept", "Origin", "Referer"];
    private static readonly string[] ResponseHeaders = ["Content-Type", "Set-Cookie", "Location", "Retry-After"];

    public static WebApplication Build(ApiRelayConfiguration configuration, RelayWorkloadAdmission? admission = null)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, Args = [] });
        builder.Logging.ClearProviders();
        builder.Host.UseConsoleLifetime(o => o.SuppressStatusMessages = true);
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.WebHost.UseKestrel(options => {
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = RequestLimit;
            options.Limits.MaxRequestLineSize = 8192; options.Limits.MaxRequestHeadersTotalSize = 32768;
            options.Limits.MaxConcurrentConnections = 32; options.Limits.MaxConcurrentUpgradedConnections = 0;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
            options.Listen(IPAddress.Any, 8443, endpoint => { endpoint.Protocols = HttpProtocols.Http1; endpoint.UseHttps(https => {
                https.ServerCertificate = configuration.Server; https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                https.HandshakeTimeout = TimeSpan.FromSeconds(5);
                https.OnAuthenticate = (_, ssl) => { ssl.AllowTlsResume = false; ssl.AllowRenegotiation = false; };
            }); });
            options.Listen(IPAddress.Loopback, 8080, endpoint => endpoint.Protocols = HttpProtocols.Http1);
        });
        builder.Services.AddSingleton(sp => new ApiRelay(configuration, sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping, admission));
        var app = builder.Build();
        app.Run(app.Services.GetRequiredService<ApiRelay>().HandleAsync);
        return app;
    }

    public static Uri Target(Uri origin, string raw, string method)
    {
        if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD") || raw.Length is < 1 or > 8192 ||
            raw.Any(c => c <= 32 || c >= 127 || c == '#')) throw new InvalidOperationException();
        var path = raw.Split('?', 2)[0];
        if (path.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_')) || path.Contains("//", StringComparison.Ordinal) ||
            !(path.StartsWith("/api/v1/", StringComparison.Ordinal) || (path == "/health/ready" && method == "GET" && raw == path))) throw new InvalidOperationException();
        for (var i = 0; i < raw.Length; i++)
            if (raw[i] == '%' && (i + 2 >= raw.Length || !Uri.IsHexDigit(raw[++i]) || !Uri.IsHexDigit(raw[++i]))) throw new InvalidOperationException();
        var result = new Uri(origin.GetLeftPart(UriPartial.Authority) + raw, UriKind.Absolute);
        if (result.PathAndQuery != raw || result.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)) throw new InvalidOperationException();
        return result;
    }

    public async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.XContentTypeOptions = "nosniff";
        var request = context.Request;
        if (context.Features.Get<ITlsConnectionFeature>() is null)
        {
            if (IPAddress.IsLoopback(context.Connection.LocalIpAddress ?? IPAddress.Any) && context.Connection.LocalPort == 8080 && request.Method == "GET" && request.Path == "/health/live" && !request.QueryString.HasValue)
            { context.Response.StatusCode = 200; return; }
            context.Response.StatusCode = 404; return;
        }
        if (!string.Equals(request.Host.Value, configuration.Relay.Authority, StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = 400; return; }
        if (stopping.IsCancellationRequested) { context.Response.StatusCode = 503; return; }
        if (admission is not null && (request.Headers[RelayWorkloadAdmission.Header].Count != 1 || !await admission.AcceptAsync(request.Headers[RelayWorkloadAdmission.Header][0]!)))
        { context.Response.StatusCode = 401; return; }
        Uri target;
        try
        {
            target = Target(configuration.Api, context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "", request.Method);
            if (request.Headers.ContainsKey("Upgrade") || request.Headers.ContainsKey("Content-Encoding") || request.Headers.ContainsKey("Trailer")) throw new InvalidOperationException();
        }
        catch { context.Response.StatusCode = 400; return; }
        if (request.ContentLength > RequestLimit) { context.Response.StatusCode = 413; return; }
        if (!await slots.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        byte[]? input = null, output = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            // Buffer only the bounded request, before any upstream mutation. The
            // current history DTO can exceed15MiB; responses stream with a32MiB cap.
            var count = 0;
            if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody != false)
            {
                input = ArrayPool<byte>.Shared.Rent(RequestLimit + 1);
                while (count <= RequestLimit)
                {
                    var read = await request.Body.ReadAsync(input.AsMemory(count, RequestLimit + 1 - count), deadline.Token);
                    if (read == 0) break; count += read;
                }
            }
            if (count > RequestLimit) { context.Response.StatusCode = 413; return; }
            if (context.Features.Get<IHttpRequestTrailersFeature>() is { Available: true, Trailers.Count: > 0 }) { context.Response.StatusCode = 400; return; }
            var health = request.Path == "/health/ready";
            if ((health || request.Method is "GET" or "HEAD") && count != 0) { context.Response.StatusCode = 400; return; }
            using var handler = ApiPeerTransport.CreateHandler(configuration.Client, configuration.Roots);
            handler.MaxResponseHeadersLength = 32;
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var outgoing = new HttpRequestMessage(new HttpMethod(request.Method), target) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            outgoing.Headers.ConnectionClose = true;
            var nominated = request.Headers.Connection.ToString().Split(',').Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!health)
            {
                foreach (var name in RequestHeaders)
                    if (!nominated.Contains(name) && request.Headers.TryGetValue(name, out var values))
                        outgoing.Headers.TryAddWithoutValidation(name, name == "Cookie" ? [string.Join("; ", values.ToArray())] : values.ToArray());
                if (count != 0 || request.Method is "POST" or "PUT" or "PATCH")
                {
                    outgoing.Content = new ByteArrayContent(input ?? [], 0, count);
                    if (!nominated.Contains("Content-Type") && request.Headers.TryGetValue("Content-Type", out var type)) outgoing.Content.Headers.TryAddWithoutValidation("Content-Type", type.ToArray());
                }
            }
            using var response = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentEncoding.Count != 0 || response.Content.Headers.ContentLength > ResponseLimit || response.Headers.Contains("Trailer") || (int)response.StatusCode is < 200 or > 599) throw new InvalidOperationException();
            context.Response.StatusCode = (int)response.StatusCode;
            var excluded = response.Headers.Connection.SelectMany(x => x.Split(',')).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ResponseHeaders)
            {
                if (excluded.Contains(name)) continue;
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)) context.Response.Headers[name] = values.ToArray();
            }
            // Do not forward upstream framing. Kestrel frames exactly the bytes
            // copied; failure after headers aborts rather than appending an error.
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            output = ArrayPool<byte>.Shared.Rent(8192); long total = 0;
            while (true)
            {
                var read = await body.ReadAsync(output.AsMemory(0, 8192), deadline.Token); if (read == 0) break;
                total += read; if (total > ResponseLimit) throw new InvalidOperationException();
                await context.Response.Body.WriteAsync(output.AsMemory(0, read), deadline.Token);
            }
            if (response.TrailingHeaders.Any()) throw new InvalidOperationException();
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException error) { Fail(context, error.StatusCode == 413 ? 413 : 400); }
        catch (Exception) { Fail(context, 502); }
        finally
        {
            if (input is not null) { CryptographicOperations.ZeroMemory(input); ArrayPool<byte>.Shared.Return(input); }
            if (output is not null) { CryptographicOperations.ZeroMemory(output); ArrayPool<byte>.Shared.Return(output); }
            slots.Release();
        }
    }

    private static void Fail(HttpContext context, int status)
    {
        if (context.Response.HasStarted) context.Abort();
        else { context.Response.Headers.Clear(); context.Response.Headers.CacheControl = "no-store"; context.Response.StatusCode = status; }
    }
    public void Dispose() => slots.Dispose();
}
