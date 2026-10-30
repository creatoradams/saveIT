using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Assets;

public static class AssetComparisonEndpoints
{
    public static void MapAssetComparisonEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/organizations/{organizationId:guid}/assets/{assetId:guid}/compare", async (
            Guid organizationId, Guid assetId, int fromVersion, int toVersion, int? offset, bool? changesOnly,
            SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var currentVersion = await db.Assets.AsNoTracking().Where(x => x.Id == assetId && x.OrganizationId == organizationId)
                .Select(x => (int?)x.Version).SingleOrDefaultAsync(c.RequestAborted);
            if (currentVersion is null) return Results.NotFound();
            if (fromVersion < 1 || toVersion < 1 || offset is < 0) return Invalid();
            var snapshots = await db.AssetRevisions.AsNoTracking().Where(x => x.AssetId == assetId && x.OrganizationId == organizationId
                && (x.Version == fromVersion || x.Version == toVersion))
                .Select(x => new AssetComparisonSnapshot(x.Version, x.Name, x.Kind, x.LocationId, x.AssetTypeId, x.AssetTypeVersion,
                    x.FieldsJson, x.Actor, x.CreatedAt, x.RestoredFromVersion)).Take(2).ToListAsync(c.RequestAborted);
            var from = snapshots.SingleOrDefault(x => x.Version == fromVersion); var to = snapshots.SingleOrDefault(x => x.Version == toVersion);
            if (from is null || to is null) return Results.NotFound();
            var schemas = await db.AssetTypeRevisions.AsNoTracking().Where(x => x.OrganizationId == organizationId
                && ((x.AssetTypeId == from.AssetTypeId && x.Version == from.AssetTypeVersion)
                    || (x.AssetTypeId == to.AssetTypeId && x.Version == to.AssetTypeVersion)))
                .Take(2).ToListAsync(c.RequestAborted);
            AssetTypeRevision? Schema(AssetComparisonSnapshot snapshot) => schemas.SingleOrDefault(x =>
                x.AssetTypeId == snapshot.AssetTypeId && x.Version == snapshot.AssetTypeVersion);
            var fromSchema = Schema(from); var toSchema = Schema(to);
            if ((from.AssetTypeId != null && fromSchema is null) || (to.AssetTypeId != null && toSchema is null))
                return Results.Problem(statusCode: 503, title: "Asset history is temporarily unavailable.");
            var oldDefinition = fromSchema is null ? null : AssetSchemas.Dto(fromSchema);
            var newDefinition = toSchema is null ? null : AssetSchemas.Dto(toSchema);
            var all = AssetComparison.Compare(from, to, oldDefinition, newDefinition);
            var changed = all.Count(x => x.Change != "unchanged"); var onlyChanges = changesOnly ?? true;
            var rows = onlyChanges ? all.Where(x => x.Change != "unchanged").ToList() : all;
            var start = offset ?? 0;
            if (start > 0 && start >= rows.Count) return Invalid();
            ChangeLog.Add(db, scope, c, organizationId, assetId, "Asset.Compared", false, fromVersion, toVersion);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new AssetComparisonDto(assetId, AssetComparison.Summary(from, oldDefinition), AssetComparison.Summary(to, newDefinition),
                currentVersion.Value, onlyChanges, changed, rows.Count, start, rows.Skip(start).Take(AssetComparison.PageSize).ToList(),
                start + AssetComparison.PageSize < rows.Count ? start + AssetComparison.PageSize : null));
        }).RequireAuthorization().AddEndpointFilter<TenantFilter>().Produces<AssetComparisonDto>().ProducesProblem(400).ProducesProblem(503);
    }
    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Choose valid asset revisions and a comparison offset within those revisions.");
}
