using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Core;

// Deliberately excludes RestoreReason; comparison neither selects nor returns recovery reasons.
public sealed record DirectoryComparisonSnapshot(int Version, string Name, string Details, string Email, string Phone,
    string Notes, bool IsArchived, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion);

public static class DirectoryComparisonEndpoints
{
    public const int PageSize = 4;

    private static IQueryable<DirectoryComparisonSnapshot> Snapshots(SaveItDb db, bool contact, Guid org, Guid id, int from, int to) => contact
        ? db.ContactRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && (x.Version == from || x.Version == to))
            .Select(x => new DirectoryComparisonSnapshot(x.Version, x.Name, x.Details, x.Email, x.Phone, x.Notes, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion))
        : db.LocationRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && (x.Version == from || x.Version == to))
            .Select(x => new DirectoryComparisonSnapshot(x.Version, x.Name, x.Details, "", "", x.Notes, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion));

    public static void Map(RouteGroupBuilder group, bool contact)
    {
        group.MapGet("/{recordId:guid}/compare", async (Guid organizationId, Guid recordId, int fromVersion, int toVersion,
            int? offset, bool? changesOnly, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var currentVersion = contact
                ? await db.Contacts.AsNoTracking().Where(x => x.Id == recordId && x.OrganizationId == organizationId).Select(x => (int?)x.Version).SingleOrDefaultAsync(c.RequestAborted)
                : await db.Locations.AsNoTracking().Where(x => x.Id == recordId && x.OrganizationId == organizationId).Select(x => (int?)x.Version).SingleOrDefaultAsync(c.RequestAborted);
            if (currentVersion is null) return Results.NotFound();
            if (fromVersion < 1 || toVersion < 1 || offset is < 0) return Invalid();
            var snapshots = await Snapshots(db, contact, organizationId, recordId, fromVersion, toVersion).Take(2).ToListAsync(c.RequestAborted);
            var from = snapshots.SingleOrDefault(x => x.Version == fromVersion); var to = snapshots.SingleOrDefault(x => x.Version == toVersion);
            if (from is null || to is null) return Results.NotFound();
            var all = Rows(from, to, contact); var changes = all.Count(x => x.Changed); var onlyChanges = changesOnly ?? true;
            var selected = onlyChanges ? all.Where(x => x.Changed).ToList() : all;
            var start = offset ?? 0;
            if (start > 0 && start >= selected.Count) return Invalid();
            ChangeLog.Add(db, scope, c, organizationId, recordId, contact ? "Contact.Compared" : "Location.Compared", false, fromVersion, toVersion);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new DirectoryComparisonDto(recordId, Summary(from), Summary(to), currentVersion.Value,
                onlyChanges, changes, selected.Count, start, selected.Skip(start).Take(PageSize).ToList(),
                start + PageSize < selected.Count ? start + PageSize : null));
        }).Produces<DirectoryComparisonDto>().ProducesProblem(400);
    }

    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Choose valid directory revisions and a comparison offset within those revisions.");
    private static DirectoryRevisionSummaryDto Summary(DirectoryComparisonSnapshot x) => new(x.Version, x.Name, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion);
    private static List<DirectoryComparisonRowDto> Rows(DirectoryComparisonSnapshot from, DirectoryComparisonSnapshot to, bool contact)
    {
        var rows = new List<DirectoryComparisonRowDto> { Text("name", "Name", from.Name, to.Name),
            Text("details", contact ? "Role or department" : "Address", from.Details, to.Details) };
        if (contact) { rows.Add(Text("email", "Email", from.Email, to.Email)); rows.Add(Text("phone", "Phone", from.Phone, to.Phone)); }
        rows.Add(Text("notes", "Notes", from.Notes, to.Notes));
        rows.Add(new("isArchived", "Lifecycle", from.IsArchived != to.IsArchived, false,
            JsonSerializer.SerializeToElement(from.IsArchived), JsonSerializer.SerializeToElement(to.IsArchived)));
        return rows;
    }
    private static string NormalizeLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static DirectoryComparisonRowDto Text(string key, string label, string from, string to)
    {
        var changed = !string.Equals(from, to, StringComparison.Ordinal);
        return new(key, label, changed, changed && string.Equals(NormalizeLines(from), NormalizeLines(to), StringComparison.Ordinal),
            JsonSerializer.SerializeToElement(from), JsonSerializer.SerializeToElement(to));
    }
}
