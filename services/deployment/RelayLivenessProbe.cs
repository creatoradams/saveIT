using System.Net;

namespace SaveIT.Deployment;

// Process liveness only; never upstream readiness, identity or tenant authority.
internal static class RelayLivenessProbe
{
    public static Task<int> RunAsync() => ProbeAsync(8080);

    internal static async Task<int> ProbeAsync(int port)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var handler = new SocketsHttpHandler {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(1),
                MaxResponseHeadersLength = 4, MaxConnectionsPerServer = 1
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/health/live") {
                Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.ConnectionClose = true;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Headers.Contains("Trailer") || response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentLength is > 0) return 1;
            using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
            return await body.ReadAsync(new byte[1], deadline.Token) == 0 && response.TrailingHeaders.Count() == 0 ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or IOException)
        { return 1; }
    }
}
