using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SaveIT.Api.Data;

namespace SaveIT.Api.Security;

public sealed record OktaConnection(string Issuer, string ClientId, string ClientSecret, Guid TenantId, string PublicOrigin, string? RequiredAcr)
{
    public override string ToString() => "Okta connection (secret redacted)";
    public static OktaConnection Read(IConfiguration configuration)
    {
        var config = configuration.GetSection("Identity:Okta");
        var issuer = config["Issuer"] ?? ""; var origin = config["PublicOrigin"] ?? "";
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri) || issuerUri.Scheme != "https" || issuerUri.UserInfo.Length != 0 || issuerUri.Query.Length != 0 || issuerUri.Fragment.Length != 0
            || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme != "https" || originUri.UserInfo.Length != 0 || originUri.PathAndQuery != "/" || originUri.Fragment.Length != 0
            || !Guid.TryParse(config["TenantId"], out var tenant) || tenant == Guid.Empty || string.IsNullOrWhiteSpace(config["ClientId"]) || string.IsNullOrWhiteSpace(config["ClientSecret"]))
            throw new InvalidOperationException("Okta requires HTTPS issuer/public origin, a server-configured tenant, client ID and a secret from secure configuration.");
        return new(issuer.TrimEnd('/'), config["ClientId"]!, config["ClientSecret"]!, tenant, originUri.GetLeftPart(UriPartial.Authority), config["RequiredAcr"]);
    }
}

public static class OktaIdentity
{
    public const string Scheme = "SaveIT.Okta";
    public static void AddOktaIdentity(this WebApplicationBuilder builder, OktaConnection connection)
    {
        if (!builder.Environment.IsDevelopment())
        {
            var keys = builder.Configuration["Identity:KeyDirectory"];
            var certificate = builder.Configuration["Identity:KeyCertificatePath"];
            if (string.IsNullOrWhiteSpace(keys) || string.IsNullOrWhiteSpace(certificate))
                throw new InvalidOperationException("Enterprise identity requires an explicit persisted, certificate-protected Data Protection key ring.");
            var protector = X509CertificateLoader.LoadPkcs12FromFile(certificate, builder.Configuration["Identity:KeyCertificatePassword"]);
            if (!protector.HasPrivateKey || protector.NotBefore.ToUniversalTime() > DateTime.UtcNow || protector.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new InvalidOperationException("The Data Protection certificate requires an unexpired private key.");
            builder.Services.AddDataProtection().SetApplicationName("SaveIT").PersistKeysToFileSystem(new DirectoryInfo(keys)).ProtectKeysWithCertificate(protector);
        }
        builder.Services.AddAuthentication().AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = connection.Issuer;
            options.ClientId = connection.ClientId; options.ClientSecret = connection.ClientSecret;
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ResponseType = OpenIdConnectResponseType.Code; options.UsePkce = true;
            options.CallbackPath = "/api/v1/auth/callback";
            options.SaveTokens = false; options.GetClaimsFromUserInfoEndpoint = false; options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.Scope.Clear(); options.Scope.Add("openid"); options.Scope.Add("profile");
            options.TokenValidationParameters.ValidIssuer = connection.Issuer;
            options.TokenValidationParameters.ValidAudience = connection.ClientId;
            options.TokenValidationParameters.ValidateIssuer = true; options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true; options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.RequireSignedTokens = true; options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            options.Events.OnRedirectToIdentityProvider = context => {
                context.ProtocolMessage.RedirectUri = connection.PublicOrigin + options.CallbackPath;
                if (!string.IsNullOrWhiteSpace(connection.RequiredAcr)) context.ProtocolMessage.AcrValues = connection.RequiredAcr;
                return Task.CompletedTask;
            };
            options.Events.OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue("sub");
                if (string.IsNullOrEmpty(subject) || subject.Length > 256 ||
                    (!string.IsNullOrEmpty(connection.RequiredAcr) && context.Principal?.FindFirstValue("acr") != connection.RequiredAcr))
                { context.Fail("The verified identity does not satisfy this connection."); return; }
                var scope = context.HttpContext.RequestServices.GetRequiredService<TenantScope>(); scope.TenantId = connection.TenantId;
                var db = context.HttpContext.RequestServices.GetRequiredService<SaveItDb>();
                await using var transaction = await db.Database.BeginTransactionAsync(context.HttpContext.RequestAborted);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true)");
                var member = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(x => x.Active && x.ExternalIssuer == connection.Issuer && x.ExternalSubject == subject);
                if (member is null) { context.Fail("No active membership is provisioned for this verified identity."); return; }
                // No email matching, automatic provisioning, token-provided tenant or provider roles grant access.
                context.Principal = new ClaimsPrincipal(new ClaimsIdentity([
                    new(ClaimTypes.NameIdentifier, member.Id.ToString()), new(ClaimTypes.Name, member.DisplayName),
                    new("tenant", member.TenantId.ToString()), new("subject", member.Subject)
                ], CookieAuthenticationDefaults.AuthenticationScheme));
                context.Properties ??= new();
                context.Properties.RedirectUri = "/";
            };
            options.Events.OnRemoteFailure = async context => {
                context.HandleResponse(); context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { title = "Sign-in could not be verified. Retry or contact your administrator." });
            };
        });
    }
}
