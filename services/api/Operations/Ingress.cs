using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace SaveIT.Api.Operations;

public static class Ingress
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
            var proxies = builder.Configuration.GetSection("Deployment:TrustedProxies").Get<string[]>() ?? [];
            var networks = builder.Configuration.GetSection("Deployment:TrustedNetworks").Get<string[]>() ?? [];
            if (proxies.Length + networks.Length > 16) throw new InvalidOperationException("Bound the trusted ingress configuration.");
            foreach (var proxy in proxies)
            {
                var address = IPAddress.Parse(proxy);
                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) throw new InvalidOperationException("Wildcard proxy trust is prohibited.");
                options.KnownProxies.Add(address);
            }
            foreach (var value in networks)
            {
                var network = System.Net.IPNetwork.Parse(value);
                if (network.PrefixLength == 0) throw new InvalidOperationException("Global proxy network trust is prohibited.");
                options.KnownIPNetworks.Add(network);
            }
            // ASP.NET treats both lists empty as trust-all. An unconfigured deployment must ignore forwarded headers.
            if (options.KnownProxies.Count + options.KnownIPNetworks.Count == 0) options.ForwardedHeaders = ForwardedHeaders.None;
        });
    }
}
