using Npgsql;
using SaveIT.Search;

namespace SaveIT.Api.Operations;

public static class HealthEndpoints
{
    public static void MapOperationalHealth(this WebApplication app, string appConnection, Guid? computeGeneration = null)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();
        async Task<IResult> Check(IWebHostEnvironment environment, HttpContext c, bool startup)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await using var connection = new NpgsqlConnection(appConnection);
                await connection.OpenAsync(timeout.Token);
                await DatabaseRoleGuard.ValidateAsync(connection, timeout.Token);
                await DatabaseRoleGuard.ValidateSchemaAsync(connection, timeout.Token, !environment.IsDevelopment());
                if (!await ComputeAdmission.CheckAsync(connection,null,computeGeneration,timeout.Token,startup))
                    return Results.Json(new { status = "unavailable" }, statusCode:503);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception)
            {
                // Public readiness never returns schema names, roles, exceptions or connection details.
                return Results.Json(new { status = "unavailable" }, statusCode: 503);
            }
        }
        app.MapGet("/health/ready", (IWebHostEnvironment environment,HttpContext c) => Check(environment,c,false)).ExcludeFromDescription();
        app.MapGet("/health/startup", (IWebHostEnvironment environment,HttpContext c) => Check(environment,c,true)).ExcludeFromDescription();
    }
}
