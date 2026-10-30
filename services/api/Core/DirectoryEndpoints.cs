using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Core;

public static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        Map(app, "locations", false); Map(app, "contacts", true);
    }
    private static void Map(WebApplication app, string resource, bool contact)
    {
        var group = app.MapGroup($"/api/v1/organizations/{{organizationId:guid}}/{resource}")
            .RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("", async (Guid organizationId, Guid? after, bool? includeArchived, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var items = await Query(db, contact, organizationId, after, includeArchived == true).Take(51).ToListAsync(context.RequestAborted);
            ChangeLog.Add(db, scope, context, organizationId, organizationId, contact ? "Contact.ListRead" : "Location.ListRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(new CursorPage<DirectoryDto>(items.Take(50).ToList(), items.Count > 50 ? items[49].Id.ToString() : null));
        }).Produces<CursorPage<DirectoryDto>>();
        group.MapGet("/{recordId:guid}", async (Guid organizationId, Guid recordId, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (await Permissions.RoleAsync(db, scope, organizationId) is null) return Results.NotFound();
            var row = await Query(db, contact, organizationId, null, true, recordId).SingleOrDefaultAsync(context.RequestAborted);
            if (row is null) return Results.NotFound();
            ChangeLog.Add(db, scope, context, organizationId, recordId, contact ? "Contact.Read" : "Location.Read", false);
            await db.SaveChangesAsync(context.RequestAborted); return Results.Ok(row);
        }).Produces<DirectoryDto>();
        group.MapPost("", async (Guid organizationId, DirectoryRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
            await Write(organizationId, null, input, contact, db, scope, context)).Produces<DirectoryDto>(201).ProducesProblem(400);
        group.MapPut("/{recordId:guid}", async (Guid organizationId, Guid recordId, DirectoryRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
            await Write(organizationId, recordId, input, contact, db, scope, context)).Produces<DirectoryDto>().ProducesProblem(400).ProducesProblem(409);
        DirectoryHistoryEndpoints.Map(group, contact);
    }
    private static IQueryable<DirectoryDto> Query(SaveItDb db, bool contact, Guid org, Guid? after, bool archived, Guid? id = null) => contact
        ? db.Contacts.AsNoTracking().Where(x => x.OrganizationId == org && (archived || !x.IsArchived) && (id == null || x.Id == id)
            && (after == null || x.Id.CompareTo(after.Value) > 0)).OrderBy(x => x.Id)
            .Select(x => new DirectoryDto(x.Id, x.OrganizationId, x.Name, x.Details, x.Email, x.Phone, x.Notes, x.IsArchived, x.Version, x.UpdatedAt))
        : db.Locations.AsNoTracking().Where(x => x.OrganizationId == org && (archived || !x.IsArchived) && (id == null || x.Id == id)
            && (after == null || x.Id.CompareTo(after.Value) > 0)).OrderBy(x => x.Id)
            .Select(x => new DirectoryDto(x.Id, x.OrganizationId, x.Name, x.Details, "", "", x.Notes, x.IsArchived, x.Version, x.UpdatedAt));
    private static async Task<IResult> Write(Guid org, Guid? id, DirectoryRequest input, bool contact, SaveItDb db, TenantScope scope, HttpContext context)
    {
        var role = await Permissions.RoleAsync(db, scope, org);
        if (role is null) return Results.NotFound(); if (role != "Editor") return Permissions.ReadOnly();
        if (!ChangeLog.Text(input.Name, 160, true) || !ChangeLog.Text(input.Details, 500) || !ChangeLog.Text(input.Notes, 4000)
            || !ChangeLog.Text(input.Email, 320) || !ChangeLog.Text(input.Phone, 80) || (id != null && input.ExpectedVersion < 1)
            || (!contact && (input.Email.Length != 0 || input.Phone.Length != 0))
            || (input.Email.Length != 0 && (!MailAddress.TryCreate(input.Email.Trim(), out var email) || email.Address != input.Email.Trim())))
            return Results.Problem(statusCode: 400, title: "Use a name, bounded plain-text fields and a valid email address/current version.");
        var now = DateTimeOffset.UtcNow; var target = id ?? Guid.NewGuid(); int version;
        if (contact)
        {
            var row = id is null ? new Contact { Id = target, TenantId = scope.TenantId, OrganizationId = org }
                : await db.Contacts.SingleOrDefaultAsync(x => x.Id == target && x.OrganizationId == org, context.RequestAborted);
            if (row is null) return Results.NotFound();
            if (id != null && (row.Version != input.ExpectedVersion || row.Version == int.MaxValue)) return ChangeLog.Conflict();
            if (id is null) db.Contacts.Add(row); else row.Version++;
            row.Name = input.Name.Trim(); row.Details = input.Details.Trim(); row.Email = input.Email.Trim(); row.Phone = input.Phone.Trim(); row.Notes = input.Notes;
            row.IsArchived = input.IsArchived; row.UpdatedAt = now; version = row.Version;
            DirectoryHistoryEndpoints.Snapshot(db, scope, row);
        }
        else
        {
            var row = id is null ? new Location { Id = target, TenantId = scope.TenantId, OrganizationId = org }
                : await db.Locations.SingleOrDefaultAsync(x => x.Id == target && x.OrganizationId == org, context.RequestAborted);
            if (row is null) return Results.NotFound();
            if (id != null && (row.Version != input.ExpectedVersion || row.Version == int.MaxValue)) return ChangeLog.Conflict();
            if (id is null) db.Locations.Add(row); else row.Version++;
            row.Name = input.Name.Trim(); row.Details = input.Details.Trim(); row.Notes = input.Notes;
            row.IsArchived = input.IsArchived; row.UpdatedAt = now; version = row.Version;
            DirectoryHistoryEndpoints.Snapshot(db, scope, row);
        }
        ChangeLog.Add(db, scope, context, org, target, (contact ? "Contact." : "Location.") + (id == null ? "Created" : "Revised"), version: version);
        await db.SaveChangesAsync(context.RequestAborted);
        return Results.Json(new DirectoryDto(target, org, input.Name.Trim(), input.Details.Trim(), contact ? input.Email.Trim() : "", contact ? input.Phone.Trim() : "",
            input.Notes, input.IsArchived, version, now), statusCode: id == null ? 201 : 200);
    }
}
