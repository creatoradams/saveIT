using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using SaveIT.Api.Assets;

namespace SaveIT.Api;

public static class Endpoints
{
    private const int PageSize = 50;
    public static void MapSaveItEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("/session", async (HttpContext c, SaveItDb db, TenantScope s) =>
            new SessionDto(c.User.Identity!.Name!, s.TenantId, await db.Tenants.Where(x => x.Id == s.TenantId).Select(x => x.Name).SingleAsync()));
        group.MapGet("/organizations", async (SaveItDb db, TenantScope s) =>
            await (from grant in db.OrganizationGrants join org in db.Organizations on grant.OrganizationId equals org.Id
                   where grant.MembershipId == s.MembershipId orderby org.Name, org.Id
                   select new OrganizationDto(org.Id, org.Name, grant.Role)).Take(100).ToListAsync());

        group.MapGet("/organizations/{organizationId:guid}/assets", async (Guid organizationId, Guid? after, SaveItDb db, TenantScope s) =>
        {
            if (await Permissions.RoleAsync(db, s, organizationId) is null) return Permissions.Hidden();
            var items = await db.Assets.Where(x => x.OrganizationId == organizationId && (after == null || x.Id.CompareTo(after.Value) > 0))
                .OrderBy(x => x.Id).Take(PageSize + 1).Select(x => new AssetDto(x.Id, x.OrganizationId, x.Name, x.Kind, x.CreatedAt, x.Version, x.LocationId)).ToListAsync();
            return Results.Ok(new CursorPage<AssetDto>(items.Take(PageSize).ToList(), items.Count > PageSize ? items[PageSize - 1].Id.ToString() : null));
        }).Produces<CursorPage<AssetDto>>();

        group.MapPost("/organizations/{organizationId:guid}/assets", async (Guid organizationId, CreateAssetRequest input, SaveItDb db, TenantScope s, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, s, organizationId);
            if (role is null) return Permissions.Hidden();
            if (role != "Editor") return Permissions.ReadOnly();
            if (!Valid(input.Name, 160) || !Valid(input.Kind, 80)) return Invalid("Name and kind are required (maximum 160 and 80 characters).");
            var asset = new Asset { Id = Guid.NewGuid(), TenantId = s.TenantId, OrganizationId = organizationId, Name = input.Name.Trim(), Kind = input.Kind.Trim(), CreatedAt = DateTimeOffset.UtcNow };
            db.Assets.Add(asset);
            AssetHistoryEndpoints.Snapshot(db, s, asset);
            Record(db, s, c, organizationId, asset.Id, "Asset.Created");
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/organizations/{organizationId}/assets/{asset.Id}", AssetHistoryEndpoints.Dto(asset));
        }).Produces<AssetDto>(201);

        group.MapGet("/organizations/{organizationId:guid}/assets/{assetId:guid}", async (Guid organizationId, Guid assetId, SaveItDb db, TenantScope s) =>
        {
            if (await Permissions.RoleAsync(db, s, organizationId) is null) return Permissions.Hidden();
            var asset = await db.Assets.Where(x => x.OrganizationId == organizationId && x.Id == assetId).Select(x => new AssetDto(x.Id, x.OrganizationId, x.Name, x.Kind, x.CreatedAt, x.Version, x.LocationId)).SingleOrDefaultAsync();
            return asset is null ? Results.NotFound() : Results.Ok(asset);
        }).Produces<AssetDto>();

        group.MapGet("/organizations/{organizationId:guid}/assets/{assetId:guid}/documents", async (Guid organizationId, Guid assetId, Guid? after, SaveItDb db, TenantScope s) =>
        {
            if (await Permissions.RoleAsync(db, s, organizationId) is null || !await db.Assets.AnyAsync(x => x.Id == assetId && x.OrganizationId == organizationId)) return Permissions.Hidden();
            var items = await db.Documents.Where(x => x.OrganizationId == organizationId && x.AssetId == assetId && (after == null || x.Id.CompareTo(after.Value) > 0))
                .OrderBy(x => x.Id).Take(PageSize + 1).Select(x => new DocumentDto(x.Id, x.OrganizationId, x.AssetId, x.Title, "", x.Version, x.UpdatedAt)).ToListAsync();
            return Results.Ok(new CursorPage<DocumentDto>(items.Take(PageSize).ToList(), items.Count > PageSize ? items[PageSize - 1].Id.ToString() : null));
        }).Produces<CursorPage<DocumentDto>>();

        group.MapPost("/organizations/{organizationId:guid}/documents", async (Guid organizationId, CreateDocumentRequest input, SaveItDb db, TenantScope s, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, s, organizationId);
            if (role is null) return Permissions.Hidden();
            if (role != "Editor") return Permissions.ReadOnly();
            if (!Valid(input.Title, 160) || !Valid(input.Content, 50000)) return Invalid("Title and content are required (maximum 160 and 50,000 characters).");
            if (!await db.Assets.AnyAsync(x => x.Id == input.AssetId && x.OrganizationId == organizationId)) return Permissions.Hidden();
            var doc = new Document { Id = Guid.NewGuid(), TenantId = s.TenantId, OrganizationId = organizationId, AssetId = input.AssetId, Title = input.Title.Trim(), Content = input.Content, UpdatedAt = DateTimeOffset.UtcNow };
            db.Documents.Add(doc);
            AddRevision(db, s, doc);
            Record(db, s, c, organizationId, doc.Id, "Document.Created");
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/organizations/{organizationId}/documents/{doc.Id}", Dto(doc));
        }).Produces<DocumentDto>(201);

        group.MapGet("/organizations/{organizationId:guid}/documents/{documentId:guid}", async (Guid organizationId, Guid documentId, SaveItDb db, TenantScope s, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, s, organizationId) is null) return Permissions.Hidden();
            var doc = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.OrganizationId == organizationId);
            if (doc is null) return Permissions.Hidden();
            Record(db, s, c, organizationId, doc.Id, "Document.Read", false);
            await db.SaveChangesAsync();
            return Results.Ok(Dto(doc));
        }).Produces<DocumentDto>();

        group.MapPut("/organizations/{organizationId:guid}/documents/{documentId:guid}", async (Guid organizationId, Guid documentId, ReviseDocumentRequest input, SaveItDb db, TenantScope s, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, s, organizationId);
            if (role is null) return Permissions.Hidden();
            if (role != "Editor") return Permissions.ReadOnly();
            if (!Valid(input.Title, 160) || !Valid(input.Content, 50000) || input.ExpectedVersion < 1) return Invalid("A title, content, and expected version are required.");
            var doc = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.OrganizationId == organizationId);
            if (doc is null) return Permissions.Hidden();
            if (doc.Version != input.ExpectedVersion) return Results.Problem(statusCode: 409, title: "This record changed. Reload it before saving again.");
            doc.Title = input.Title.Trim(); doc.Content = input.Content; doc.Version++; doc.UpdatedAt = DateTimeOffset.UtcNow;
            AddRevision(db, s, doc);
            Record(db, s, c, organizationId, doc.Id, "Document.Revised");
            await db.SaveChangesAsync();
            return Results.Ok(Dto(doc));
        }).Produces<DocumentDto>().ProducesProblem(409);

        group.MapGet("/organizations/{organizationId:guid}/documents/{documentId:guid}/revisions", async (Guid organizationId, Guid documentId, int? before, SaveItDb db, TenantScope s, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, s, organizationId) is null || !await db.Documents.AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId)) return Permissions.Hidden();
            var revisions = await db.DocumentRevisions.Where(x => x.DocumentId == documentId && x.OrganizationId == organizationId && (before == null || x.Version < before))
                .OrderByDescending(x => x.Version).Take(PageSize + 1).Select(x => new RevisionDto(x.Version, x.Title, x.Content, x.Actor, x.CreatedAt, x.RestoredFromVersion, x.RestoreReason)).ToListAsync();
            Record(db, s, c, organizationId, documentId, "Document.RevisionsRead", false);
            await db.SaveChangesAsync();
            return Results.Ok(new CursorPage<RevisionDto>(revisions.Take(PageSize).ToList(), revisions.Count > PageSize ? revisions[PageSize - 1].Version.ToString() : null));
        }).Produces<CursorPage<RevisionDto>>();

        group.MapGet("/organizations/{organizationId:guid}/audit", async (Guid organizationId, long? before, SaveItDb db, TenantScope s) =>
        {
            var role = await Permissions.RoleAsync(db, s, organizationId);
            if (role is null) return Permissions.Hidden();
            if (role != "Editor") return Permissions.ReadOnly();
            var items = await db.AuditEvents.Where(x => x.OrganizationId == organizationId && (before == null || x.Id < before))
                .OrderByDescending(x => x.Id).Take(PageSize + 1).Select(x => new AuditDto(x.Id, x.Actor, x.TargetId, x.Action, x.Outcome, x.CorrelationId, x.CreatedAt, x.SourceVersion, x.TargetVersion)).ToListAsync();
            return Results.Ok(new CursorPage<AuditDto>(items.Take(PageSize).ToList(), items.Count > PageSize ? items[PageSize - 1].Id.ToString() : null));
        }).Produces<CursorPage<AuditDto>>();
    }

    private static bool Valid(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max;
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static DocumentDto Dto(Document d) => new(d.Id, d.OrganizationId, d.AssetId, d.Title, d.Content, d.Version, d.UpdatedAt);
    private static void AddRevision(SaveItDb db, TenantScope s, Document d) => db.DocumentRevisions.Add(new() {
        TenantId = s.TenantId, OrganizationId = d.OrganizationId, DocumentId = d.Id, Version = d.Version,
        Title = d.Title, Content = d.Content, Actor = s.Subject, CreatedAt = d.UpdatedAt
    });
    private static void Record(SaveItDb db, TenantScope s, HttpContext c, Guid org, Guid target, string action, bool publish = true)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new() { TenantId = s.TenantId, OrganizationId = org, Actor = s.Subject, TargetId = target, Action = action, CreatedAt = now, CorrelationId = c.TraceIdentifier });
        if (publish) db.OutboxEvents.Add(new() { Id = Guid.NewGuid(), TenantId = s.TenantId, OrganizationId = org, TargetId = target, Type = action, CreatedAt = now });
    }
}
