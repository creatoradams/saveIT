using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Documentation;

public static class DocumentTemplateComparisonEndpoints
{
    public const int PageSize = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{templateId:guid}/compare", async (Guid organizationId, Guid templateId,
            int fromVersion, int toVersion, int? offset, bool? changesOnly,
            SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            // The head establishes current existence, never historical content or write authority.
            var current = await db.DocumentTemplates.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.Id == templateId)
                .Select(x => (int?)x.Version).SingleOrDefaultAsync(c.RequestAborted);
            if (current is null) return Results.NotFound();
            if (fromVersion < 1 || toVersion < 1 || offset is < 0) return Invalid();
            var snapshots = await db.DocumentTemplateRevisions.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.TemplateId == templateId
                    && (x.Version == fromVersion || x.Version == toVersion))
                .Take(2).ToListAsync(c.RequestAborted);
            var from = snapshots.SingleOrDefault(x => x.Version == fromVersion);
            var to = snapshots.SingleOrDefault(x => x.Version == toVersion);
            if (from is null || to is null) return Results.NotFound();
            var diff = LineComparison.Compare(from.Content, to.Content);
            var onlyChanges = changesOnly ?? true;
            var lines = onlyChanges ? diff.Lines.Where(x => x.Kind != "unchanged").ToList() : diff.Lines;
            var start = offset ?? 0;
            if (start > 0 && start >= lines.Count) return Invalid();
            ChangeLog.Add(db, scope, c, organizationId, templateId, "DocumentTemplate.Compared", false, fromVersion, toVersion);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new DocumentTemplateComparisonDto(templateId, Summary(from), Summary(to), current.Value,
                from.Name != to.Name, from.Title != to.Title, from.IsArchived != to.IsArchived,
                from.Content != to.Content, from.Content != to.Content && LineComparison.Normalize(from.Content) == LineComparison.Normalize(to.Content),
                onlyChanges, diff.Simplified, diff.Added, diff.Removed, lines.Count, start,
                lines.Skip(start).Take(PageSize).ToList(), start + PageSize < lines.Count ? start + PageSize : null));
        }).Produces<DocumentTemplateComparisonDto>().ProducesProblem(400);
    }

    private static DocumentTemplateComparisonRevisionDto Summary(DocumentTemplateRevision x) =>
        new(x.Version, x.Name, x.Title, x.IsArchived, x.Actor, x.CreatedAt);
    private static IResult Invalid() => Results.Problem(statusCode: 400,
        title: "Choose valid template revisions and a comparison offset within those revisions.");
}
