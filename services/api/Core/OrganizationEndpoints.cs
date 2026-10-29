using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Api.Security;

namespace SaveIT.Api.Core;

public static class OrganizationEndpoints
{
    private sealed record Cursor(Guid TenantId, Guid MembershipId, string Name, Guid Id);
    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("SaveIT.OrganizationCursor.v1");
    public static void MapOrganizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations").RequireAuthorization().AddEndpointFilter<TenantFilter>();
        group.MapGet("", async (string? after, SaveItDb db, TenantScope scope, IDataProtectionProvider provider, HttpContext context) =>
        {
            Cursor? cursor = null;
            if (after is not null)
            {
                try
                {
                    if (after.Length > 2048) return InvalidCursor();
                    cursor = JsonSerializer.Deserialize<Cursor>(Protector(provider).Unprotect(after));
                    if (cursor is null || cursor.TenantId != scope.TenantId || cursor.MembershipId != scope.MembershipId
                        || !ChangeLog.Text(cursor.Name, 160, true) || cursor.Id == Guid.Empty) return InvalidCursor();
                }
                catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return InvalidCursor(); }
            }
            var query = from grant in db.OrganizationGrants.AsNoTracking()
                        join org in db.Organizations.AsNoTracking() on new { grant.TenantId, Id = grant.OrganizationId } equals new { org.TenantId, org.Id }
                        where grant.MembershipId == scope.MembershipId
                        select new { Organization = org, grant.Role };
            if (cursor is not null)
                query = query.Where(x => string.Compare(x.Organization.Name, cursor.Name) > 0
                    || (x.Organization.Name == cursor.Name && x.Organization.Id.CompareTo(cursor.Id) > 0));
            var rows = await query.OrderBy(x => x.Organization.Name).ThenBy(x => x.Organization.Id).Take(51)
                .Select(x => new OrganizationDto(x.Organization.Id, x.Organization.Name, x.Role, x.Organization.Version, x.Organization.UpdatedAt))
                .ToListAsync(context.RequestAborted);
            var items = rows.Take(50).ToList();
            foreach (var row in items) ChangeLog.Add(db, scope, context, row.Id, row.Id, "Organization.ListRead", false);
            await db.SaveChangesAsync(context.RequestAborted);
            var next = rows.Count > 50 ? Protector(provider).Protect(JsonSerializer.Serialize(new Cursor(scope.TenantId, scope.MembershipId, items[^1].Name, items[^1].Id))) : null;
            return Results.Ok(new CursorPage<OrganizationDto>(items, next));
        }).Produces<CursorPage<OrganizationDto>>().ProducesProblem(400);
        group.MapGet("/{organizationId:guid}", async (Guid organizationId, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId); if (role is null) return Results.NotFound();
            var row = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organizationId, context.RequestAborted);
            if (row is null) return Results.NotFound();
            ChangeLog.Add(db, scope, context, organizationId, organizationId, "Organization.Read", false);
            await db.SaveChangesAsync(context.RequestAborted); return Results.Ok(Dto(row, role));
        }).Produces<OrganizationDto>();
        group.MapPost("", async (CreateOrganizationRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            if (!scope.CanManageOrganizations) return AdministrativeAccess();
            if (!ChangeLog.Text(input.Name, 160, true)) return InvalidName();
            var id = Guid.NewGuid();
            try
            {
                // Fixed DB operation provisions exactly this new organization + its creator grant.
                // The runtime role has no direct organization/grant INSERT permission.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.create_organization({id},{input.Name.Trim()},{scope.MembershipId})", context.RequestAborted);
            }
            catch (PostgresException error) when (error.SqlState == "42501") { return AdministrativeAccess(); }
            var row = await db.Organizations.AsNoTracking().SingleAsync(x => x.Id == id, context.RequestAborted);
            ChangeLog.Add(db, scope, context, id, id, "Organization.Created", version: row.Version);
            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Created($"/api/v1/organizations/{id}", Dto(row, "Editor"));
        }).Produces<OrganizationDto>(201).ProducesProblem(400).ProducesProblem(403);
        group.MapPut("/{organizationId:guid}", async (Guid organizationId, ReviseOrganizationRequest input, SaveItDb db, TenantScope scope, HttpContext context) =>
        {
            var role = await Permissions.RoleAsync(db, scope, organizationId); if (role is null) return Results.NotFound();
            if (!scope.CanManageOrganizations || role != "Editor") return AdministrativeAccess();
            if (!ChangeLog.Text(input.Name, 160, true) || input.ExpectedVersion < 1) return InvalidName();
            var row = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organizationId, context.RequestAborted);
            if (row is null) return Results.NotFound();
            if (row.Version != input.ExpectedVersion || row.Version == int.MaxValue) return ChangeLog.Conflict();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.rename_organization({organizationId},{input.Name.Trim()},{scope.MembershipId},{input.ExpectedVersion})", context.RequestAborted);
            }
            catch (PostgresException error) when (error.SqlState == "42501") { return AdministrativeAccess(); }
            catch (PostgresException error) when (error.SqlState == "40001") { return ChangeLog.Conflict(); }
            row = await db.Organizations.AsNoTracking().SingleAsync(x => x.Id == organizationId, context.RequestAborted);
            ChangeLog.Add(db, scope, context, row.Id, row.Id, "Organization.Revised", version: row.Version);
            await db.SaveChangesAsync(context.RequestAborted); return Results.Ok(Dto(row, role));
        }).Produces<OrganizationDto>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(409);
    }
    private static OrganizationDto Dto(Organization row, string role) => new(row.Id, row.Name, role, row.Version, row.UpdatedAt);
    private static IResult InvalidCursor() => Results.Problem(statusCode: 400, title: "Reload organizations and use the returned cursor for your current session.");
    private static IResult InvalidName() => Results.Problem(statusCode: 400, title: "Use a plain-text organization name of 1–160 characters and the current version when editing.");
    private static IResult AdministrativeAccess() => Results.Problem(statusCode: 403, title: "Organization administration requires an administrator capability and Editor access when renaming.");
}
