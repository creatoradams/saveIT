using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Api;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using SaveIT.Api.Documentation;
using SaveIT.Api.Assets;
using SaveIT.Api.Core;
using SaveIT.Search;

var builder = WebApplication.CreateBuilder(args);
var devIdentity = builder.Configuration.GetValue<bool>("DevelopmentIdentity:Enabled");
var oktaEnabled = builder.Configuration.GetValue<bool>("Identity:Okta:Enabled");
if (devIdentity && oktaEnabled) throw new InvalidOperationException("Choose development identity or Okta, never both.");
var okta = oktaEnabled ? OktaConnection.Read(builder.Configuration) : null;
if (devIdentity && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Development identity requires the Development environment.");
if (!builder.Environment.IsDevelopment() && !oktaEnabled)
    throw new InvalidOperationException("Production identity is unconfigured. Configure and review an enterprise identity adapter before deployment.");

builder.Services.AddScoped<TenantScope>();
builder.Services.AddDbContext<SaveItDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException("ConnectionStrings:App is required.")));
builder.Services.AddScoped<TenantFilter>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = oktaEnabled ? "SaveIT.Session" : "SaveIT.Development";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = oktaEnabled ? SameSiteMode.Lax : SameSiteMode.Strict;
    o.Cookie.SecurePolicy = oktaEnabled ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
if (okta is not null) builder.AddOktaIdentity(okta);
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "SaveIT.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = oktaEnabled ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.Cookie.HttpOnly = true;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("development-login", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (args.Contains("--migrate"))
{
    if (!devIdentity) throw new InvalidOperationException("Synthetic migration/seed command requires explicit development identity opt-in.");
    var connection = builder.Configuration.GetConnectionString("Migration") ?? throw new InvalidOperationException("Migration connection required.");
    var scope = new TenantScope();
    await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(connection).Options, scope);
    await db.Database.MigrateAsync();
    await using var seedTransaction = await db.Database.BeginTransactionAsync();
    await DevelopmentSeed.SeedAsync(db, scope);
    await seedTransaction.CommitAsync();
    return;
}
await using (var connection = new NpgsqlConnection(builder.Configuration.GetConnectionString("App")))
{
    await connection.OpenAsync();
    await DatabaseRoleGuard.ValidateAsync(connection);
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(statusCode: 400, title: "Refresh your session and retry. A valid CSRF token is required.").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "SaveIT" }));
app.MapOpenApi();
app.MapGet("/api/v1/csrf", (HttpContext c, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(c).RequestToken! }));
app.MapGet("/api/v1/auth/options", () => Results.Ok(new { development = devIdentity, okta = oktaEnabled }));
if (oktaEnabled)
    app.MapPost("/api/v1/auth/login", () => Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OktaIdentity.Scheme]));
if (devIdentity)
{
    app.MapGet("/api/v1/dev/profiles", () => DevelopmentSeed.Profiles.Select(p => new ProfileDto(p.Id, p.Name, p.Tenant)));
    app.MapPost("/api/v1/dev/login", async (LoginRequest request, HttpContext context, SaveItDb db, TenantScope scope) =>
    {
        var profile = DevelopmentSeed.Profiles.SingleOrDefault(x => x.Id == request.Profile);
        if (profile is null) return Results.Unauthorized();
        scope.TenantId = profile.TenantId;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {scope.TenantId.ToString()}, true)");
        if (!await db.Memberships.AnyAsync(x => x.Id == profile.MembershipId && x.Subject == profile.Id && x.Active)) return Results.Unauthorized();
        var identity = new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, profile.MembershipId.ToString()), new("tenant", profile.TenantId.ToString()),
            new("subject", profile.Id), new(ClaimTypes.Name, profile.Name)
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.NoContent();
    }).RequireRateLimiting("development-login");
}
app.MapPost("/api/v1/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
app.MapSaveItEndpoints();
app.MapSearchEndpoints();
app.MapDocumentHistoryEndpoints();
app.MapAssetRelationshipEndpoints();
app.MapDirectoryEndpoints();
app.MapOrganizationEndpoints();
app.MapAssetHistoryEndpoints();
await app.RunAsync();

public partial class Program;
