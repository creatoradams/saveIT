using Npgsql;
using SaveIT.Search;

namespace SaveIT.Api.Operations;

public static class HealthEndpoints
{
    public static void MapOperationalHealth(this WebApplication app, string appConnection)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
        app.MapGet("/health/ready", async (IWebHostEnvironment environment, HttpContext c) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await using var connection = new NpgsqlConnection(appConnection);
                await connection.OpenAsync(timeout.Token);
                await DatabaseRoleGuard.ValidateAsync(connection, timeout.Token);
                await DatabaseRoleGuard.ValidateSchemaAsync(connection, timeout.Token, !environment.IsDevelopment());
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception)
            {
                // Public readiness never returns schema names, roles, exceptions or connection details.
                return Results.Json(new { status = "unavailable" }, statusCode: 503);
            }
        }).ExcludeFromDescription();
    }
}
