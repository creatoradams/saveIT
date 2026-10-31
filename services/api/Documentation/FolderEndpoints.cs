using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Documentation;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}").RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("/folders", async (Guid organizationId, Guid? parentId, Guid? after, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await ParentExists(db, organizationId, parentId, context)) return Results.NotFound();
            var rows = await db.DocumentFolders.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ParentId == parentId && (after == null || x.Id.CompareTo(after.Value) > 0))
                .OrderBy(x => x.Id).Take(51).Select(x => new FolderDto(x.Id, x.OrganizationId, x.ParentId, x.Name, x.Version, x.UpdatedAt)).ToListAsync(context.RequestAborted);
            ChangeLog.Add(db, scope, context, organizationId, parentId ?? organizationId, "DocumentFolder.ListRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new CursorPage<FolderDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Id.ToString() : null));
        }).Produces<CursorPage<FolderDto>>();

        group.MapGet("/folders/{folderId:guid}", async (Guid organizationId, Guid folderId, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var path = await db.Database.SqlQuery<FolderDto>($"""
                WITH RECURSIVE path AS (
                  SELECT f.*,1 AS depth FROM public."DocumentFolders" f
                  WHERE f."TenantId"={scope.TenantId} AND f."OrganizationId"={organizationId} AND f."Id"={folderId}
                  UNION ALL SELECT p.*,c.depth+1 FROM path c JOIN public."DocumentFolders" p
                  ON p."TenantId"=c."TenantId" AND p."OrganizationId"=c."OrganizationId" AND p."Id"=c."ParentId" WHERE c.depth<9
                ) SELECT "Id","OrganizationId","ParentId","Name","Version","UpdatedAt" FROM path ORDER BY depth DESC
                """).ToListAsync(context.RequestAborted);
            if (path.Count == 0) return Results.NotFound();
            if (path.Count > 8 || path[0].ParentId is not null) return Results.Problem(statusCode: 503, title: "The folder path is unavailable.");
            ChangeLog.Add(db, scope, context, organizationId, folderId, "DocumentFolder.Read", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new FolderDetailDto(path[^1], path));
        }).Produces<FolderDetailDto>();

        group.MapPost("/folders", async (Guid organizationId, FolderRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (!ChangeLog.Text(input.Name, 160, true) || input.ExpectedVersion != 0) return Invalid();
            await Lock(db, scope, organizationId, context);
            if (!await ParentExists(db, organizationId, input.ParentId, context)) return Results.NotFound();
            var row = new DocumentFolder { TenantId = scope.TenantId, OrganizationId = organizationId, Id = Guid.NewGuid(), ParentId = input.ParentId, Name = input.Name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
            db.DocumentFolders.Add(row);
            ChangeLog.Add(db, scope, context, organizationId, row.Id, "DocumentFolder.Created", version: 1);
            var failure = await Save(db, context); if (failure is not null) return failure;
            return Results.Created($"/api/v1/organizations/{organizationId}/folders/{row.Id}", Dto(row));
        }).Produces<FolderDto>(201).ProducesProblem(400).ProducesProblem(409);

        group.MapPut("/folders/{folderId:guid}", async (Guid organizationId, Guid folderId, FolderRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (!ChangeLog.Text(input.Name, 160, true) || input.ExpectedVersion < 1) return Invalid();
            await Lock(db, scope, organizationId, context);
            var row = await db.DocumentFolders.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == folderId, context.RequestAborted);
            if (row is null || !await ParentExists(db, organizationId, input.ParentId, context)) return Results.NotFound();
            if (row.Version != input.ExpectedVersion || row.Version == int.MaxValue) return ChangeLog.Conflict();
            row.Name = input.Name.Trim(); row.ParentId = input.ParentId; row.Version++; row.UpdatedAt = DateTimeOffset.UtcNow;
            ChangeLog.Add(db, scope, context, organizationId, row.Id, "DocumentFolder.Revised", source: input.ExpectedVersion, version: row.Version);
            var failure = await Save(db, context); if (failure is not null) return failure;
            return Results.Ok(Dto(row));
        }).Produces<FolderDto>().ProducesProblem(400).ProducesProblem(409);

        group.MapGet("/library/documents", async (Guid organizationId, Guid? folderId, Guid? after, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await ParentExists(db, organizationId, folderId, context)) return Results.NotFound();
            IQueryable<LibraryDocumentDto> query;
            if (folderId is Guid assigned)
                query = from placement in db.DocumentPlacements.AsNoTracking()
                        where placement.OrganizationId == organizationId && placement.FolderId == assigned && (after == null || placement.DocumentId.CompareTo(after.Value) > 0)
                        join doc in db.Documents.AsNoTracking() on new { placement.TenantId, placement.OrganizationId, Id = placement.DocumentId } equals new { doc.TenantId, doc.OrganizationId, doc.Id }
                        orderby placement.DocumentId
                        select new LibraryDocumentDto(doc.Id, doc.AssetId, doc.Title, doc.Version, doc.UpdatedAt, placement.FolderId, placement.Version);
            else query = from doc in db.Documents.AsNoTracking()
                        join placement in db.DocumentPlacements.AsNoTracking() on new { doc.TenantId, doc.OrganizationId, DocumentId = doc.Id } equals new { placement.TenantId, placement.OrganizationId, placement.DocumentId } into placements
                        from placement in placements.DefaultIfEmpty()
                        where doc.OrganizationId == organizationId && (placement == null || placement.FolderId == null)
                            && (after == null || doc.Id.CompareTo(after.Value) > 0)
                        orderby doc.Id
                        select new LibraryDocumentDto(doc.Id, doc.AssetId, doc.Title, doc.Version, doc.UpdatedAt, placement == null ? null : placement.FolderId, placement == null ? 0 : placement.Version);
            var rows = await query.Take(51).ToListAsync(context.RequestAborted);
            ChangeLog.Add(db, scope, context, organizationId, folderId ?? organizationId, "Document.LibraryRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new CursorPage<LibraryDocumentDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Id.ToString() : null));
        }).Produces<CursorPage<LibraryDocumentDto>>();

        group.MapGet("/documents/{documentId:guid}/placement", async (Guid organizationId, Guid documentId, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await db.Documents.AnyAsync(x => x.OrganizationId == organizationId && x.Id == documentId, context.RequestAborted)) return Results.NotFound();
            var row = await db.DocumentPlacements.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentId == documentId, context.RequestAborted);
            ChangeLog.Add(db, scope, context, organizationId, documentId, "Document.PlacementRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Placement(documentId, row));
        }).Produces<DocumentPlacementDto>();

        group.MapPut("/documents/{documentId:guid}/placement", async (Guid organizationId, Guid documentId, FileDocumentRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (input.ExpectedVersion < 0) return Invalid();
            if (!await db.Documents.AnyAsync(x => x.OrganizationId == organizationId && x.Id == documentId, context.RequestAborted)
                || !await ParentExists(db, organizationId, input.FolderId, context)) return Results.NotFound();
            var row = await db.DocumentPlacements.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.DocumentId == documentId, context.RequestAborted);
            if ((row?.Version ?? 0) != input.ExpectedVersion || row?.Version == int.MaxValue) return ChangeLog.Conflict();
            if (row?.FolderId == input.FolderId) return Results.Problem(statusCode: 400, title: "The document is already in that location.");
            if (row is null) { row = new() { TenantId = scope.TenantId, OrganizationId = organizationId, DocumentId = documentId }; db.DocumentPlacements.Add(row); }
            else row.Version++;
            row.FolderId = input.FolderId; row.UpdatedAt = DateTimeOffset.UtcNow;
            ChangeLog.Add(db, scope, context, organizationId, documentId, "Document.Filed", source: input.ExpectedVersion, version: row.Version);
            var failure = await Save(db, context); if (failure is not null) return failure;
            return Results.Ok(Placement(documentId, row));
        }).Produces<DocumentPlacementDto>().ProducesProblem(400).ProducesProblem(409);
    }

    // This separate statement obtains a fresh READ COMMITTED snapshot before hierarchy reads.
    private static Task Lock(SaveItDb db, TenantScope scope, Guid org, HttpContext context) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope.TenantId.ToString()} || ':' || {org.ToString()},190019))", context.RequestAborted);
    private static Task<bool> ParentExists(SaveItDb db, Guid org, Guid? id, HttpContext context) => id is null
        ? db.Organizations.AnyAsync(x => x.Id == org, context.RequestAborted)
        : db.DocumentFolders.AnyAsync(x => x.OrganizationId == org && x.Id == id, context.RequestAborted);
    private static FolderDto Dto(DocumentFolder x) => new(x.Id, x.OrganizationId, x.ParentId, x.Name, x.Version, x.UpdatedAt);
    private static DocumentPlacementDto Placement(Guid id, DocumentPlacement? x) => new(id, x?.FolderId, x?.Version ?? 0, x?.UpdatedAt);
    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Choose a name of 1–160 characters, a valid parent and the current version.");
    private static async Task<IResult?> Save(SaveItDb db, HttpContext context)
    {
        try { await db.SaveChangesAsync(context.RequestAborted); return null; }
        catch (DbUpdateConcurrencyException) { return ChangeLog.Conflict(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" }) { return ChangeLog.Conflict(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23514" or "54000" })
        { return Results.Problem(statusCode: 400, title: "Folders allow at most 8 levels and 1,000 folders per organization, without cycles. Choose another destination."); }
    }
}
