using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Assets;

public static class AssetHistoryEndpoints
{
    public static AssetDto Dto(Asset x) => new(x.Id, x.OrganizationId, x.Name, x.Kind, x.CreatedAt, x.Version, x.LocationId);
    public static void Snapshot(SaveItDb db, TenantScope scope, Asset asset, int? source = null, string? reason = null) =>
        db.AssetRevisions.Add(new() { TenantId = scope.TenantId, OrganizationId = asset.OrganizationId, AssetId = asset.Id, Version = asset.Version,
            Name = asset.Name, Kind = asset.Kind, LocationId = asset.LocationId, Actor = scope.Subject, CreatedAt = asset.UpdatedAt, RestoredFromVersion = source, RestoreReason = reason });

    public static void MapAssetHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/assets/{assetId:guid}")
            .RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapPut("", async (Guid organizationId, Guid assetId, ReviseAssetRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (!ChangeLog.Text(input.Name, 160, true) || !ChangeLog.Text(input.Kind, 80, true) || input.ExpectedVersion < 1)
                return Results.Problem(statusCode: 400, title: "Use an asset name/type and the current expected version.");
            var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == assetId && x.OrganizationId == organizationId, context.RequestAborted);
            if (asset is null) return Results.NotFound();
            if (asset.Version != input.ExpectedVersion || asset.Version == int.MaxValue) return ChangeLog.Conflict();
            if (input.LocationId != null && input.LocationId != asset.LocationId && !await db.Locations.AnyAsync(x => x.Id == input.LocationId && x.OrganizationId == organizationId && !x.IsArchived))
                return Results.Problem(statusCode: 400, title: "Choose an active location in this organization.");
            asset.Name = input.Name.Trim(); asset.Kind = input.Kind.Trim(); asset.LocationId = input.LocationId; asset.Version++; asset.UpdatedAt = DateTimeOffset.UtcNow;
            Snapshot(db, scope, asset); ChangeLog.Add(db, scope, context, organizationId, assetId, "Asset.Revised", version: asset.Version);
            await db.SaveChangesAsync(context.RequestAborted); return Results.Ok(Dto(asset));
        }).Produces<AssetDto>().ProducesProblem(400).ProducesProblem(409);
        group.MapGet("/revisions", async (Guid organizationId, Guid assetId, int? before, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await db.Assets.AnyAsync(x => x.Id == assetId && x.OrganizationId == organizationId)) return Results.NotFound();
            var rows = await db.AssetRevisions.AsNoTracking().Where(x => x.AssetId == assetId && x.OrganizationId == organizationId && (before == null || x.Version < before))
                .OrderByDescending(x => x.Version).Take(51).Select(x => new AssetRevisionDto(x.Version, x.Name, x.Kind, x.LocationId, x.Actor, x.CreatedAt, x.RestoredFromVersion, x.RestoreReason)).ToListAsync(context.RequestAborted);
            ChangeLog.Add(db, scope, context, organizationId, assetId, "Asset.RevisionsRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new CursorPage<AssetRevisionDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Version.ToString() : null));
        }).Produces<CursorPage<AssetRevisionDto>>();
        group.MapPost("/restore", async (Guid organizationId, Guid assetId, RestoreDocumentRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (input.ExpectedVersion < 1 || input.SourceVersion < 1 || !ChangeLog.Text(input.Reason, 500, true) || input.Reason.Trim().Length < 3)
                return Results.Problem(statusCode: 400, title: "Choose an earlier revision, current version and a reason of 3–500 characters.");
            var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == assetId && x.OrganizationId == organizationId, context.RequestAborted);
            if (asset is null) return Results.NotFound();
            if (asset.Version != input.ExpectedVersion || asset.Version == int.MaxValue) return ChangeLog.Conflict();
            if (input.SourceVersion >= asset.Version) return Results.Problem(statusCode: 400, title: "Choose an earlier asset revision.");
            var source = await db.AssetRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == assetId && x.OrganizationId == organizationId && x.Version == input.SourceVersion);
            if (source is null) return Results.NotFound();
            if (source.LocationId != null && !await db.Locations.AnyAsync(x => x.Id == source.LocationId && x.OrganizationId == organizationId && !x.IsArchived))
                return Results.Problem(statusCode: 400, title: "The historical location is archived or unavailable. Restore that location or revise the asset placement first.");
            asset.Name = source.Name; asset.Kind = source.Kind; asset.LocationId = source.LocationId; asset.Version++; asset.UpdatedAt = DateTimeOffset.UtcNow;
            Snapshot(db, scope, asset, source.Version, input.Reason.Trim());
            ChangeLog.Add(db, scope, context, organizationId, assetId, "Asset.Restored", source: source.Version, version: asset.Version);
            await db.SaveChangesAsync(context.RequestAborted); return Results.Ok(Dto(asset));
        }).Produces<AssetDto>().ProducesProblem(400).ProducesProblem(409);
    }
}
