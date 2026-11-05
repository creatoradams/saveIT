using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;
using SaveIT.Api.Security;

namespace SaveIT.Api.Operations;

// Optional packaged transport mode. Cloud certificate delivery and client workload
// authentication remain separate deployment gates; this does not confer tenant authority.
public static class ApiTls
{
    private const string Invalid = "API TLS requires a separate bounded valid server certificate and fixed listeners without forwarded trust. This transport mode requires Linux.";

    public static X509Certificate2? Configure(WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Deployment:ApiTls");
        if (!section.GetValue<bool>("Enabled"))
        {
            if (section.GetChildren().Any(x => x.Key != "Enabled")) throw new InvalidOperationException(Invalid);
            return null;
        }
        if (!OperatingSystem.IsLinux() || builder.Configuration.GetSection("Kestrel").Exists() ||
            builder.Configuration.GetSection("Deployment:TrustedProxies").Exists() ||
            builder.Configuration.GetSection("Deployment:TrustedNetworks").Exists())
            throw new InvalidOperationException(Invalid);

        var certificate = Load(section["CertificatePath"], section["CertificateBase64"], section["CertificatePassword"]);
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Replace the default reloadable loader: configuration must not append
            // a plaintext listener later. Explicit Listen endpoints override URLs.
            options.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            options.Listen(IPAddress.Any, 8443, endpoint => endpoint.UseHttps(https =>
            {
                https.ServerCertificate = certificate;
                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            }));
            options.Listen(IPAddress.Loopback, 8080);
        });
        return certificate;
    }

    public static X509Certificate2 Load(string? path, string? base64, string? password)
    {
        X509Certificate2? certificate = null;
        try
        {
            certificate = KeyProtectionCertificate.Load(path, base64, password);
            var usage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
            var names = certificate.Extensions.Where(x => x.Oid?.Value == "2.5.29.17").ToArray();
            if (names.Length != 1) throw new InvalidOperationException(Invalid);
            var dnsNames = new X509SubjectAlternativeNameExtension(names[0].RawData, names[0].Critical).EnumerateDnsNames().ToArray();
            if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(x => x.CertificateAuthority) ||
                usage is null || !usage.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(x => x.Value == "1.3.6.1.5.5.7.3.1") ||
                dnsNames.Length == 0 || dnsNames.Any(x => x.Length > 253 ||
                    Uri.CheckHostName(x.StartsWith("*.", StringComparison.Ordinal) ? x[2..] : x) != UriHostNameType.Dns))
                throw new InvalidOperationException(Invalid);
            var result = certificate;
            certificate = null;
            return result;
        }
        catch (Exception error) when (error is InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(Invalid);
        }
        finally { certificate?.Dispose(); }
    }

    public static void RestrictPlaintext(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Features.Get<ITlsConnectionFeature>() is null &&
                !(IPAddress.IsLoopback(context.Connection.LocalIpAddress ?? IPAddress.Any) &&
                  context.Connection.LocalPort == 8080 && HttpMethods.IsGet(context.Request.Method) &&
                  (context.Request.Path == "/health/live" || context.Request.Path == "/health/ready")))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }
            await next(context);
        });
    }
}
