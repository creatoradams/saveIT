using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Core;

public static class DirectoryHistoryEndpoints
{
    public static void Snapshot(SaveItDb db, TenantScope scope, Location row, int? source = null, string? reason = null) =>
        db.LocationRevisions.Add(new() { TenantId = row.TenantId, OrganizationId = row.OrganizationId, RecordId = row.Id, Version = row.Version,
            Name = row.Name, Details = row.Details, Notes = row.Notes, IsArchived = row.IsArchived,
            Actor = scope.Subject, CreatedAt = row.UpdatedAt, RestoredFromVersion = source, RestoreReason = reason });
    public static void Snapshot(SaveItDb db, TenantScope scope, Contact row, int? source = null, string? reason = null) =>
        db.ContactRevisions.Add(new() { TenantId = row.TenantId, OrganizationId = row.OrganizationId, RecordId = row.Id, Version = row.Version,
            Name = row.Name, Details = row.Details, Email = row.Email, Phone = row.Phone, Notes = row.Notes, IsArchived = row.IsArchived,
            Actor = scope.Subject, CreatedAt = row.UpdatedAt, RestoredFromVersion = source, RestoreReason = reason });

    private static IQueryable<DirectoryRevisionDto> Query(SaveItDb db, bool contact, Guid org, Guid id, int version) => contact
        ? db.ContactRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && x.Version == version)
            .Select(x => new DirectoryRevisionDto(x.Version, x.Name, x.Details, x.Email, x.Phone, x.Notes, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion, x.RestoreReason))
        : db.LocationRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && x.Version == version)
            .Select(x => new DirectoryRevisionDto(x.Version, x.Name, x.Details, "", "", x.Notes, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion, x.RestoreReason));
    private static IQueryable<DirectoryRevisionSummaryDto> Summaries(SaveItDb db, bool contact, Guid org, Guid id, int? before) => contact
        ? db.ContactRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && (before == null || x.Version < before))
            .OrderByDescending(x => x.Version).Select(x => new DirectoryRevisionSummaryDto(x.Version, x.Name, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion))
        : db.LocationRevisions.AsNoTracking().Where(x => x.OrganizationId == org && x.RecordId == id && (before == null || x.Version < before))
            .OrderByDescending(x => x.Version).Select(x => new DirectoryRevisionSummaryDto(x.Version, x.Name, x.IsArchived, x.Actor, x.CreatedAt, x.RestoredFromVersion));
    private static Task<bool> Exists(SaveItDb db, bool contact, Guid org, Guid id, CancellationToken token) => contact
        ? db.Contacts.AnyAsync(x => x.Id == id && x.OrganizationId == org, token)
        : db.Locations.AnyAsync(x => x.Id == id && x.OrganizationId == org, token);

    public static void Map(RouteGroupBuilder group, bool contact)
    {
        var prefix = contact ? "Contact." : "Location.";
        group.MapGet("/{recordId:guid}/revisions", async (Guid organizationId, Guid recordId, int? before, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await Exists(db, contact, organizationId, recordId, c.RequestAborted)) return Results.NotFound();
            if (before is < 1) return Results.Problem(statusCode: 400, title: "Choose a positive history cursor.");
            var rows = await Summaries(db, contact, organizationId, recordId, before).Take(51).ToListAsync(c.RequestAborted);
            ChangeLog.Add(db, scope, c, organizationId, recordId, prefix + "RevisionsRead", false);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new CursorPage<DirectoryRevisionSummaryDto>(rows.Take(50).ToList(), rows.Count > 50 ? rows[49].Version.ToString() : null));
        }).Produces<CursorPage<DirectoryRevisionSummaryDto>>().ProducesProblem(400);
        group.MapGet("/{recordId:guid}/revisions/{version:int}", async (Guid organizationId, Guid recordId, int version, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null || !await Exists(db, contact, organizationId, recordId, c.RequestAborted)) return Results.NotFound();
            if (version < 1) return Results.Problem(statusCode: 400, title: "Choose a positive revision.");
            var row = await Query(db, contact, organizationId, recordId, version).SingleOrDefaultAsync(c.RequestAborted);
            if (row is null) return Results.NotFound();
            ChangeLog.Add(db, scope, c, organizationId, recordId, prefix + "RevisionRead", false, source: version);
            await db.SaveChangesAsync(c.RequestAborted); return Results.Ok(row);
        }).Produces<DirectoryRevisionDto>().ProducesProblem(400);
        group.MapPost("/{recordId:guid}/restore", async (Guid organizationId, Guid recordId, RestoreDocumentRequest input, SaveItDb db, TenantScope scope, HttpContext c) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId);
            if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
            if (input.ExpectedVersion < 1 || input.SourceVersion < 1 || !ChangeLog.Text(input.Reason, 500, true) || input.Reason.Trim().Length < 3)
                return Results.Problem(statusCode: 400, title: "Choose an earlier revision, current version and a reason of 3–500 characters.");
            var person = contact ? await db.Contacts.SingleOrDefaultAsync(x => x.Id == recordId && x.OrganizationId == organizationId, c.RequestAborted) : null;
            var place = !contact ? await db.Locations.SingleOrDefaultAsync(x => x.Id == recordId && x.OrganizationId == organizationId, c.RequestAborted) : null;
            var version = person?.Version ?? place?.Version;
            if (version is null) return Results.NotFound();
            if (version != input.ExpectedVersion || version == int.MaxValue) return ChangeLog.Conflict();
            if (input.SourceVersion >= version) return Results.Problem(statusCode: 400, title: "Choose an earlier directory revision.");
            var source = await Query(db, contact, organizationId, recordId, input.SourceVersion).SingleOrDefaultAsync(c.RequestAborted);
            if (source is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;
            if (person is not null)
            {
                person.Name = source.Name; person.Details = source.Details; person.Email = source.Email; person.Phone = source.Phone;
                person.Notes = source.Notes; person.IsArchived = source.IsArchived; person.Version++; person.UpdatedAt = now;
                Snapshot(db, scope, person, source.Version, input.Reason.Trim());
            }
            else
            {
                place!.Name = source.Name; place.Details = source.Details; place.Notes = source.Notes;
                place.IsArchived = source.IsArchived; place.Version++; place.UpdatedAt = now;
                Snapshot(db, scope, place, source.Version, input.Reason.Trim());
            }
            ChangeLog.Add(db, scope, c, organizationId, recordId, prefix + "Restored", source: source.Version, version: version.Value + 1);
            await db.SaveChangesAsync(c.RequestAborted);
            return Results.Ok(new DirectoryDto(recordId, organizationId, source.Name, source.Details, source.Email, source.Phone, source.Notes, source.IsArchived, version.Value + 1, now));
        }).Produces<DirectoryDto>().ProducesProblem(400).ProducesProblem(409);
    }
}
