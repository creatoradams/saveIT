using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Assets;

public static class AssetRelationshipEndpoints
{
    private const int PageSize = 50;
    public static void MapAssetRelationshipEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/assets/{assetId:guid}/relationships")
            .RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("", async (Guid organizationId, Guid assetId, Guid? after, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null ||
                !await db.Assets.AnyAsync(x => x.Id == assetId && x.OrganizationId == organizationId)) return Results.NotFound();
            // Join both current endpoints in the granted organization before returning any labels or cursor.
            var rows = await (from edge in db.AssetRelationships.AsNoTracking()
                join source in db.Assets on new { edge.TenantId, edge.OrganizationId, Id = edge.SourceAssetId }
                    equals new { source.TenantId, source.OrganizationId, source.Id }
                join target in db.Assets on new { edge.TenantId, edge.OrganizationId, Id = edge.TargetAssetId }
                    equals new { target.TenantId, target.OrganizationId, target.Id }
                where edge.OrganizationId == organizationId && edge.RemovedAt == null
                    && (edge.SourceAssetId == assetId || edge.TargetAssetId == assetId)
                    && (after == null || edge.Id.CompareTo(after.Value) > 0)
                orderby edge.Id
                select new AssetRelationshipDto(edge.Id, edge.OrganizationId,
                    new AssetDto(source.Id, source.OrganizationId, source.Name, source.Kind, source.CreatedAt, source.Version, source.LocationId, source.AssetTypeId, source.AssetTypeVersion),
                    new AssetDto(target.Id, target.OrganizationId, target.Name, target.Kind, target.CreatedAt, target.Version, target.LocationId, target.AssetTypeId, target.AssetTypeVersion),
                    edge.Kind, edge.Version, edge.CreatedBy, edge.CreatedAt)).Take(PageSize + 1).ToListAsync(context.RequestAborted);
            Record(db, scope, context, organizationId, assetId, "Asset.RelationshipsRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new CursorPage<AssetRelationshipDto>(rows.Take(PageSize).ToList(),
                rows.Count > PageSize ? rows[PageSize - 1].Id.ToString() : null));
        }).Produces<CursorPage<AssetRelationshipDto>>();

        group.MapPost("", async (Guid organizationId, Guid assetId, CreateRelationshipRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound();
            if (role != "Editor") return Permissions.ReadOnly();
            if (input.Kind is not ("DependsOn" or "Contains") || input.TargetAssetId == Guid.Empty || input.TargetAssetId == assetId)
                return Invalid("Choose a different asset and a supported relationship type.");
            var endpoints = await db.Assets.AsNoTracking().Where(x => x.OrganizationId == organizationId
                && (x.Id == assetId || x.Id == input.TargetAssetId))
                .Select(x => new AssetDto(x.Id, x.OrganizationId, x.Name, x.Kind, x.CreatedAt, x.Version, x.LocationId, x.AssetTypeId, x.AssetTypeVersion)).ToListAsync(context.RequestAborted);
            if (endpoints.Count != 2) return Results.NotFound();
            if (await db.AssetRelationships.AnyAsync(x => x.OrganizationId == organizationId && x.SourceAssetId == assetId
                && x.TargetAssetId == input.TargetAssetId && x.Kind == input.Kind && x.RemovedAt == null)) return Conflict();
            var edge = new AssetRelationship { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = organizationId,
                SourceAssetId = assetId, TargetAssetId = input.TargetAssetId, Kind = input.Kind, CreatedBy = scope.Subject, CreatedAt = DateTimeOffset.UtcNow };
            db.AssetRelationships.Add(edge);
            Record(db, scope, context, organizationId, edge.Id, "AssetRelationship.Created");
            await db.SaveChangesAsync(context.RequestAborted);
            var source = endpoints.Single(x => x.Id == assetId); var target = endpoints.Single(x => x.Id == input.TargetAssetId);
            return Results.Json(new AssetRelationshipDto(edge.Id, organizationId, source, target, edge.Kind, edge.Version, edge.CreatedBy, edge.CreatedAt), statusCode: 201);
        }).Produces<AssetRelationshipDto>(201).ProducesProblem(400).ProducesProblem(409);

        group.MapPost("/{relationshipId:guid}/remove", async (Guid organizationId, Guid assetId, Guid relationshipId,
            RemoveRelationshipRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound();
            if (role != "Editor") return Permissions.ReadOnly();
            if (input.ExpectedVersion < 1) return Invalid("A current expected relationship version is required.");
            var edge = await db.AssetRelationships.SingleOrDefaultAsync(x => x.Id == relationshipId && x.OrganizationId == organizationId
                && (x.SourceAssetId == assetId || x.TargetAssetId == assetId), context.RequestAborted);
            if (edge is null) return Results.NotFound();
            if (edge.RemovedAt != null || edge.Version != input.ExpectedVersion) return Conflict();
            // Both endpoints are in this organization by FK. Recheck current rows at the write boundary.
            if (await db.Assets.CountAsync(x => x.OrganizationId == organizationId &&
                (x.Id == edge.SourceAssetId || x.Id == edge.TargetAssetId), context.RequestAborted) != 2) return Results.NotFound();
            edge.Version++; edge.RemovedAt = DateTimeOffset.UtcNow; edge.RemovedBy = scope.Subject;
            Record(db, scope, context, organizationId, edge.Id, "AssetRelationship.Removed");
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(409);
    }

    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "This relationship already exists or changed. Reload relationships before trying again.");
    private static void Record(SaveItDb db, TenantScope scope, HttpContext context, Guid org, Guid target, string action, bool publish = true)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new() { TenantId = scope.TenantId, OrganizationId = org, TargetId = target,
            Actor = scope.Subject, Action = action, CreatedAt = now, CorrelationId = context.TraceIdentifier });
        if (publish) db.OutboxEvents.Add(new() { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = org,
            TargetId = target, Type = action, SchemaVersion = 1, CreatedAt = now });
    }
}
