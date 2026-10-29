using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using SaveIT.Search;

namespace SaveIT.Api;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/search", async (string? q, Guid? organizationId, string? type, string? after,
            SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (organizationId.HasValue && await Permissions.RoleAsync(db, scope, organizationId.Value) is null)
                return Results.NotFound();
            var page = await SearchReader.FindAsync((NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction(), scope.TenantId, scope.MembershipId,
                scope.Subject, q, organizationId, type, after, context.RequestAborted);
            foreach (var hit in page.Items)
            {
                db.AuditEvents.Add(new AuditEvent {
                    TenantId = scope.TenantId, OrganizationId = hit.OrganizationId, TargetId = hit.Id,
                    Actor = scope.Subject, Action = hit.Type == "document" ? "Search.DocumentRead" : "Search.AssetRead",
                    CreatedAt = DateTimeOffset.UtcNow, CorrelationId = context.TraceIdentifier
                });
            }
            // Auditing fails closed before serializing any returned previews or cursor.
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(page);
        }).RequireAuthorization().AddEndpointFilter<TenantFilter>()
          .Produces<SearchPage>().ProducesProblem(400).ProducesProblem(503);
    }
}
