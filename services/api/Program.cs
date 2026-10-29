using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Api;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

var builder = WebApplication.CreateBuilder(args);
var devIdentity = builder.Configuration.GetValue<bool>("DevelopmentIdentity:Enabled");
if (devIdentity && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Development identity requires the Development environment.");
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Production identity is unconfigured. Configure and review an enterprise identity adapter before deployment.");

builder.Services.AddScoped<TenantScope>();
builder.Services.AddDbContext<SaveItDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException("ConnectionStrings:App is required.")));
builder.Services.AddScoped<TenantFilter>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "SaveIT.Development";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "SaveIT.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
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
    await using var command = new NpgsqlCommand("SELECT rolsuper OR rolbypassrls OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='Assets' AND pg_has_role(current_user,c.relowner,'USAGE')) FROM pg_roles WHERE rolname=current_user", connection);
    if (await command.ExecuteScalarAsync() is not false)
        throw new InvalidOperationException("The API database role must be a non-owner without superuser or BYPASSRLS privileges.");
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
await app.RunAsync();

public partial class Program;
