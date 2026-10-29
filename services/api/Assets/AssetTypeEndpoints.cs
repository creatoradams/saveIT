using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Assets;

public static class AssetTypeEndpoints
{
    public static void MapAssetTypeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/asset-types").RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("", async (Guid organizationId, Guid? after, bool? includeArchived, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var rows = await db.AssetTypes.AsNoTracking().Where(x => x.OrganizationId == organizationId && (includeArchived == true || !x.IsArchived) && (after == null || x.Id.CompareTo(after.Value) > 0)).OrderBy(x => x.Id).Take(51).ToListAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, organizationId, "AssetTypes.Read", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new CursorPage<AssetTypeDto>(rows.Take(50).Select(AssetSchemas.Dto).ToList(), rows.Count > 50 ? rows[49].Id.ToString() : null));
        }).Produces<CursorPage<AssetTypeDto>>();
        group.MapGet("/{typeId:guid}", async (Guid organizationId, Guid typeId, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var type = await db.AssetTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == typeId && x.OrganizationId == organizationId, c.RequestAborted);
            if (type is null) return Results.NotFound();
            ChangeLog.Add(db, scope, c, organizationId, typeId, "AssetType.Read", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(AssetSchemas.Dto(type));
        }).Produces<AssetTypeDto>();
        group.MapPost("", async (Guid organizationId, AssetTypeRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (!ChangeLog.Text(input.Name, 160, true) || input.IsArchived || input.ExpectedVersion != 0 || !await AssetSchemas.ValidDefinition(db, input.Definition, c.RequestAborted)) return AssetSchemas.Invalid();
            var type = new AssetType { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = organizationId, Name = input.Name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
            db.AssetTypes.Add(type); db.AssetTypeRevisions.Add(AssetSchemas.Snapshot(type, input.Definition.GetRawText(), scope));
            ChangeLog.Add(db, scope, c, organizationId, type.Id, "AssetType.Created", version: 1); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Created($"/api/v1/organizations/{organizationId}/asset-types/{type.Id}", AssetSchemas.Dto(type));
        }).Produces<AssetTypeDto>(201).ProducesProblem(400);
        group.MapPut("/{typeId:guid}", async (Guid organizationId, Guid typeId, AssetTypeRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            var type = await db.AssetTypes.SingleOrDefaultAsync(x => x.Id == typeId && x.OrganizationId == organizationId, c.RequestAborted);
            if (type is null) return Results.NotFound();
            if (type.Version != input.ExpectedVersion || type.Version == int.MaxValue) return ChangeLog.Conflict();
            if (!ChangeLog.Text(input.Name, 160, true) || !await AssetSchemas.ValidDefinition(db, input.Definition, c.RequestAborted)) return AssetSchemas.Invalid();
            type.Name = input.Name.Trim(); type.Version++; type.IsArchived = input.IsArchived; type.UpdatedAt = DateTimeOffset.UtcNow;
            db.AssetTypeRevisions.Add(AssetSchemas.Snapshot(type, input.Definition.GetRawText(), scope));
            ChangeLog.Add(db, scope, c, organizationId, typeId, "AssetType.Revised", version: type.Version); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(AssetSchemas.Dto(type));
        }).Produces<AssetTypeDto>().ProducesProblem(400).ProducesProblem(409);
        group.MapGet("/{typeId:guid}/revisions", async (Guid organizationId, Guid typeId, int? before, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await db.AssetTypes.AnyAsync(x => x.Id == typeId && x.OrganizationId == organizationId, c.RequestAborted)) return Results.NotFound();
            var rows = await db.AssetTypeRevisions.AsNoTracking().Where(x => x.AssetTypeId == typeId && x.OrganizationId == organizationId && (before == null || x.Version < before)).OrderByDescending(x => x.Version).Take(51)
                .Select(x => new AssetTypeRevisionSummaryDto(x.Version, x.Name, x.Actor, x.CreatedAt)).ToListAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, typeId, "AssetType.RevisionsRead", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new CursorPage<AssetTypeRevisionSummaryDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Version.ToString() : null));
        }).Produces<CursorPage<AssetTypeRevisionSummaryDto>>();
        group.MapGet("/{typeId:guid}/revisions/{version:int}", async (Guid organizationId, Guid typeId, int version, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var revision = await db.AssetTypeRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetTypeId == typeId && x.OrganizationId == organizationId && x.Version == version, c.RequestAborted);
            if (revision is null) return Results.NotFound();
            ChangeLog.Add(db, scope, c, organizationId, typeId, "AssetType.RevisionRead", false, version: version); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(AssetSchemas.Dto(revision));
        }).Produces<AssetTypeRevisionDto>();

        var fields = app.MapGroup("/api/v1/organizations/{organizationId:guid}/assets/{assetId:guid}/fields").RequireAuthorization().AddEndpointFilter<TenantFilter>();
        fields.MapGet("", async (Guid organizationId, Guid assetId, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.OrganizationId == organizationId, c.RequestAborted);
            if (asset is null) return Results.NotFound();
            AssetTypeRevision? schema = null;
            if (asset.AssetTypeId != null) schema = await db.AssetTypeRevisions.AsNoTracking().SingleAsync(x => x.AssetTypeId == asset.AssetTypeId && x.OrganizationId == organizationId && x.Version == asset.AssetTypeVersion, c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, assetId, "Asset.FieldsRead", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new AssetFieldsDto(AssetHistoryEndpoints.Dto(asset), schema == null ? null : AssetSchemas.Dto(schema), AssetSchemas.Json(asset.FieldsJson)));
        }).Produces<AssetFieldsDto>();
        fields.MapPut("", async (Guid organizationId, Guid assetId, ReviseAssetFieldsRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == assetId && x.OrganizationId == organizationId, c.RequestAborted);
            if (asset is null) return Results.NotFound();
            if (asset.Version != input.ExpectedVersion || asset.Version == int.MaxValue) return ChangeLog.Conflict();
            if (!await AssetSchemas.ValidBinding(db, organizationId, input.AssetTypeId, input.AssetTypeVersion, input.Fields,
                asset.AssetTypeId != input.AssetTypeId || asset.AssetTypeVersion != input.AssetTypeVersion, c.RequestAborted)) return AssetSchemas.Invalid();
            asset.AssetTypeId = input.AssetTypeId; asset.AssetTypeVersion = input.AssetTypeVersion; asset.FieldsJson = input.Fields.GetRawText(); asset.Version++; asset.UpdatedAt = DateTimeOffset.UtcNow;
            AssetHistoryEndpoints.Snapshot(db, scope, asset); ChangeLog.Add(db, scope, c, organizationId, assetId, "Asset.Revised", version: asset.Version); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(AssetHistoryEndpoints.Dto(asset));
        }).Produces<AssetDto>().ProducesProblem(400).ProducesProblem(409);
    }
}
