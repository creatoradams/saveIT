using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Documentation;

public static class DocumentHistoryEndpoints
{
    private const int PageSize = 200;
    public static void MapDocumentHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/documents/{documentId:guid}")
            .RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("/compare", async (Guid organizationId, Guid documentId, int fromVersion, int toVersion,
            int? offset, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var doc = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.OrganizationId == organizationId);
            if (doc is null) return Results.NotFound();
            if (fromVersion < 1 || toVersion < 1 || offset is < 0) return Invalid("Choose valid revision numbers and comparison offset.");
            var snapshots = await db.DocumentRevisions.AsNoTracking().Where(x => x.DocumentId == documentId
                && x.OrganizationId == organizationId && (x.Version == fromVersion || x.Version == toVersion)).ToListAsync();
            var from = snapshots.SingleOrDefault(x => x.Version == fromVersion); var to = snapshots.SingleOrDefault(x => x.Version == toVersion);
            if (from is null || to is null) return Results.NotFound();
            var diff = LineComparison.Compare(from.Content, to.Content);
            var start = offset ?? 0;
            if (start > 0 && start >= diff.Lines.Count) return Invalid("The comparison offset is outside these revisions.");
            var next = start + PageSize < diff.Lines.Count ? start + PageSize : (int?)null;
            AddAudit(db, scope, context, organizationId, documentId, "Document.Compared", fromVersion, toVersion);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new DocumentComparisonDto(documentId, Summary(from), Summary(to), doc.Version,
                from.Title != to.Title, from.Content != to.Content, from.Content != to.Content && LineComparison.Normalize(from.Content) == LineComparison.Normalize(to.Content),
                diff.Simplified, diff.Added, diff.Removed, diff.Lines.Count, start, diff.Lines.Skip(start).Take(PageSize).ToList(), next));
        }).Produces<DocumentComparisonDto>().ProducesProblem(400);

        group.MapPost("/restore", async (Guid organizationId, Guid documentId, RestoreDocumentRequest input,
            SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound();
            if (role != "Editor") return Permissions.ReadOnly();
            if (input.SourceVersion < 1 || input.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(input.Reason)
                || input.Reason.Trim().Length is < 3 or > 500 || input.Reason.Length > 500 || input.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                return Invalid("Choose a source revision, current expected version, and a reason of 3–500 characters.");
            var doc = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId && x.OrganizationId == organizationId);
            if (doc is null) return Results.NotFound();
            if (doc.Version != input.ExpectedVersion) return Conflict();
            if (input.SourceVersion >= doc.Version) return Invalid("Choose an earlier revision to restore.");
            var source = await db.DocumentRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.DocumentId == documentId
                && x.OrganizationId == organizationId && x.Version == input.SourceVersion);
            if (source is null) return Results.NotFound();
            if (doc.Version == int.MaxValue) return Results.Problem(statusCode: 409, title: "This document has reached its revision limit.");
            doc.Title = source.Title; doc.Content = source.Content; doc.Version++; doc.UpdatedAt = DateTimeOffset.UtcNow;
            db.DocumentRevisions.Add(new() {
                TenantId = scope.TenantId, OrganizationId = organizationId, DocumentId = documentId, Version = doc.Version,
                Title = source.Title, Content = source.Content, Actor = scope.Subject, CreatedAt = doc.UpdatedAt,
                RestoredFromVersion = source.Version, RestoreReason = input.Reason.Trim()
            });
            AddAudit(db, scope, context, organizationId, documentId, "Document.Restored", source.Version, doc.Version);
            db.OutboxEvents.Add(new() { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = organizationId,
                TargetId = documentId, Type = "Document.Restored", SchemaVersion = 1, CreatedAt = doc.UpdatedAt });
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new DocumentDto(doc.Id, doc.OrganizationId, doc.AssetId, doc.Title, doc.Content, doc.Version, doc.UpdatedAt));
        }).Produces<DocumentDto>().ProducesProblem(400).ProducesProblem(409);
    }

    private static RevisionSummaryDto Summary(DocumentRevision revision) => new(revision.Version, revision.Title, revision.Actor, revision.CreatedAt, revision.RestoredFromVersion);
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "This record changed. Reload it before restoring a revision.");
    private static void AddAudit(SaveItDb db, TenantScope scope, HttpContext context, Guid organizationId,
        Guid documentId, string action, int from, int to) => db.AuditEvents.Add(new() {
            TenantId = scope.TenantId, OrganizationId = organizationId, TargetId = documentId, Actor = scope.Subject,
            Action = action, SourceVersion = from, TargetVersion = to, CreatedAt = DateTimeOffset.UtcNow, CorrelationId = context.TraceIdentifier
        });
}
