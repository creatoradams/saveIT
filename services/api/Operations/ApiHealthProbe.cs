using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SaveIT.Api.Operations;

public static partial class ApiHealthProbe
{
    public static Task<int> FromEnvironmentAsync(bool live = false) => RunAsync(
        Environment.GetEnvironmentVariable("SAVEIT_HEALTH_PORT"), Environment.GetEnvironmentVariable("SAVEIT_HEALTH_HOST"),live);

    public static async Task<int> RunAsync(string? portText = null, string? host = null, bool live = false)
    {
        portText ??= "8080";
        host ??= "127.0.0.1";
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1024 or > 65535 || port.ToString(CultureInfo.InvariantCulture) != portText ||
            host.Length is < 1 or > 253 || !HostPattern().IsMatch(host)) return 1;
        try
        {
            using var handler = new HttpClientHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 4,
                MaxConnectionsPerServer = 1
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/health/{(live ? "live" : "ready")}"));
            request.Headers.Host = host;
            request.Headers.ConnectionClose = true;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.StatusCode == HttpStatusCode.OK ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or FormatException)
        {
            return 1;
        }
    }

    [GeneratedRegex(@"\A[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?\z")]
    private static partial Regex HostPattern();
}
