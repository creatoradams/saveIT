using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Documentation;

public static class DocumentTemplateEndpoints
{
    public static void MapDocumentTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/document-templates")
            .RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("", async (Guid organizationId, Guid? after, bool? includeArchived, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var rows = await db.DocumentTemplates.AsNoTracking().Where(x => x.OrganizationId == organizationId
                && (includeArchived == true || !x.IsArchived) && (after == null || x.Id.CompareTo(after.Value) > 0))
                .OrderBy(x => x.Id).Take(51).Select(x => new DocumentTemplateDto(x.Id, x.OrganizationId, x.Name, x.Version, x.IsArchived, x.UpdatedAt)).ToListAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, organizationId, "DocumentTemplates.Read", false);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new CursorPage<DocumentTemplateDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Id.ToString() : null));
        }).Produces<CursorPage<DocumentTemplateDto>>();
        group.MapGet("/{templateId:guid}", async (Guid organizationId, Guid templateId, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var row = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == templateId, c.RequestAborted);
            if (row is null) return Results.NotFound();
            ChangeLog.Add(db, scope, c, organizationId, templateId, "DocumentTemplate.Read", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(Dto(row));
        }).Produces<DocumentTemplateDto>();
        group.MapPost("", async (Guid organizationId, DocumentTemplateRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
            await Publish(organizationId, null, input, db, scope, c)).Produces<DocumentTemplateDto>(201).ProducesProblem(400);
        group.MapPut("/{templateId:guid}", async (Guid organizationId, Guid templateId, DocumentTemplateRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
            await Publish(organizationId, templateId, input, db, scope, c)).Produces<DocumentTemplateDto>().ProducesProblem(400).ProducesProblem(409);
        group.MapGet("/{templateId:guid}/revisions", async (Guid organizationId, Guid templateId, int? before, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await db.DocumentTemplates.AnyAsync(x => x.OrganizationId == organizationId && x.Id == templateId, c.RequestAborted)) return Results.NotFound();
            if (before is < 1) return Invalid();
            var rows = await db.DocumentTemplateRevisions.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.TemplateId == templateId && (before == null || x.Version < before))
                .OrderByDescending(x => x.Version).Take(51).Select(x => new DocumentTemplateRevisionSummaryDto(x.Version, x.Name, x.IsArchived, x.Actor, x.CreatedAt)).ToListAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, templateId, "DocumentTemplate.RevisionsRead", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new CursorPage<DocumentTemplateRevisionSummaryDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Version.ToString() : null));
        }).Produces<CursorPage<DocumentTemplateRevisionSummaryDto>>();
        group.MapGet("/{templateId:guid}/revisions/{version:int}", async (Guid organizationId, Guid templateId, int version, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var row = await db.DocumentTemplateRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.TemplateId == templateId && x.Version == version, c.RequestAborted);
            if (row is null) return Results.NotFound();
            ChangeLog.Add(db, scope, c, organizationId, templateId, "DocumentTemplate.RevisionRead", false, version: version); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new DocumentTemplateRevisionDto(row.TemplateId, row.Version, row.Name, row.Title, row.Content, row.IsArchived, row.Actor, row.CreatedAt));
        }).Produces<DocumentTemplateRevisionDto>();
        group.MapPost("/{templateId:guid}/documents", async (Guid organizationId, Guid templateId, CreateTemplateDocumentRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (input.TemplateVersion < 1 || !Text(input.Title, 160) || !Text(input.Content, 50000)) return Invalid();
            // Lock before reading current lifecycle/version; publication/archive waits through the outer commit.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"DocumentTemplates\" WHERE \"TenantId\"={scope.TenantId} AND \"OrganizationId\"={organizationId} AND \"Id\"={templateId} FOR SHARE", c.RequestAborted);
            var head = await db.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == templateId, c.RequestAborted);
            if (head is null) return Results.NotFound();
            if (head.Version != input.TemplateVersion || head.IsArchived) return ChangeLog.Conflict();
            if (!await db.DocumentTemplateRevisions.AnyAsync(x => x.OrganizationId == organizationId && x.TemplateId == templateId && x.Version == input.TemplateVersion && !x.IsArchived, c.RequestAborted)) return Results.Problem(statusCode: 503, title: "The template revision is unavailable.");
            if (!await db.Assets.AnyAsync(x => x.OrganizationId == organizationId && x.Id == input.AssetId, c.RequestAborted)) return Results.NotFound();
            var doc = new Document { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = organizationId, AssetId = input.AssetId, Title = input.Title.Trim(), Content = input.Content, UpdatedAt = DateTimeOffset.UtcNow };
            db.Documents.Add(doc);
            db.DocumentRevisions.Add(new() { TenantId = scope.TenantId, OrganizationId = organizationId, DocumentId = doc.Id, Version = 1, Title = doc.Title, Content = doc.Content, Actor = scope.Subject, CreatedAt = doc.UpdatedAt });
            db.DocumentTemplateOrigins.Add(new() { TenantId = scope.TenantId, OrganizationId = organizationId, DocumentId = doc.Id, TemplateId = templateId, TemplateVersion = input.TemplateVersion });
            ChangeLog.Add(db, scope, c, organizationId, templateId, "DocumentTemplate.Used", false, version: input.TemplateVersion);
            ChangeLog.Add(db, scope, c, organizationId, doc.Id, "Document.Created", version: 1);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Created($"/api/v1/organizations/{organizationId}/documents/{doc.Id}", new DocumentDto(doc.Id, organizationId, doc.AssetId, doc.Title, doc.Content, 1, doc.UpdatedAt));
        }).Produces<DocumentDto>(201).ProducesProblem(400).ProducesProblem(409);
        app.MapGet("/api/v1/organizations/{organizationId:guid}/documents/{documentId:guid}/template-origin", async (Guid organizationId, Guid documentId, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await db.Documents.AnyAsync(x => x.OrganizationId == organizationId && x.Id == documentId, c.RequestAborted)) return Results.NotFound();
            var origin = await db.DocumentTemplateOrigins.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentId == documentId, c.RequestAborted);
            string? name = null;
            if (origin is not null) name = await db.DocumentTemplateRevisions.Where(x => x.OrganizationId == organizationId && x.TemplateId == origin.TemplateId && x.Version == origin.TemplateVersion).Select(x => x.Name).SingleAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, documentId, "Document.TemplateOriginRead", false); await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new DocumentTemplateOriginDto(documentId, origin?.TemplateId, origin?.TemplateVersion, name));
        }).RequireAuthorization().AddEndpointFilter<TenantFilter>().Produces<DocumentTemplateOriginDto>();
    }

    private static async Task<IResult> Publish(Guid org, Guid? id, DocumentTemplateRequest input, SaveItDb db, TenantScope scope, HttpContext c)
    {
        var role = await Permissions.RoleAsync(db, scope, org);
        if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
        if (!Text(input.Name, 160) || !Text(input.Title, 160) || !Text(input.Content, 50000) || (id is null && (input.ExpectedVersion != 0 || input.IsArchived)) || (id is not null && input.ExpectedVersion < 1)) return Invalid();
        var row = id is null ? new DocumentTemplate { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = org }
            : await db.DocumentTemplates.SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id, c.RequestAborted);
        if (row is null) return Results.NotFound();
        if (id is not null && (row.Version != input.ExpectedVersion || row.Version == int.MaxValue)) return ChangeLog.Conflict();
        if (id is null) db.DocumentTemplates.Add(row); else row.Version++;
        row.Name = input.Name.Trim(); row.IsArchived = input.IsArchived; row.UpdatedAt = DateTimeOffset.UtcNow;
        // Existing-head UPDATE must precede snapshot INSERT. Both saves share TenantFilter's transaction;
        // the deferred tip constraint and audit/outbox commit only after the complete publication.
        await db.SaveChangesAsync(c.RequestAborted);
        db.DocumentTemplateRevisions.Add(new() { TenantId = scope.TenantId, OrganizationId = org, TemplateId = row.Id, Version = row.Version,
            Name = row.Name, Title = input.Title.Trim(), Content = input.Content, IsArchived = row.IsArchived, Actor = scope.Subject, CreatedAt = row.UpdatedAt });
        ChangeLog.Add(db, scope, c, org, row.Id, id is null ? "DocumentTemplate.Created" : "DocumentTemplate.Revised", version: row.Version);
        await db.SaveChangesAsync(c.RequestAborted);
        return Results.Json(Dto(row), statusCode: id is null ? 201 : 200);
    }
    private static bool Text(string? value, int max) => ChangeLog.Text(value, max, true);
    private static DocumentTemplateDto Dto(DocumentTemplate x) => new(x.Id, x.OrganizationId, x.Name, x.Version, x.IsArchived, x.UpdatedAt);
    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Use bounded plain-text name/title/content and the current template version.");
}
