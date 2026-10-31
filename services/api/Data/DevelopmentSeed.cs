using Microsoft.EntityFrameworkCore;

namespace SaveIT.Api.Data;

public static class DevelopmentSeed
{
    public static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid OrgA = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid OrgA2 = Guid.Parse("10000000-0000-0000-0000-000000000011");
    public static readonly Guid OrgB = Guid.Parse("20000000-0000-0000-0000-000000000010");
    public static readonly DevProfile[] Profiles = [
        new("alice", "Alice Chen", "Northstar IT", TenantA, Guid.Parse("10000000-0000-0000-0000-000000000100")),
        new("viewer", "Sam Rivera", "Northstar IT · read only", TenantA, Guid.Parse("10000000-0000-0000-0000-000000000101")),
        new("bob", "Bob Morgan", "Southbank IT", TenantB, Guid.Parse("20000000-0000-0000-0000-000000000100"))
    ];

    public static async Task SeedAsync(SaveItDb db, TenantScope scope)
    {
        if (await db.Tenants.AnyAsync())
        {
            // Explicit synthetic bootstrap only; ordinary API requests cannot provision this capability.
            foreach (var profile in Profiles.Where(x => x.Id is "alice" or "bob"))
            {
                scope.TenantId = profile.TenantId;
                var member = await db.Memberships.SingleOrDefaultAsync(x => x.Id == profile.MembershipId && x.Subject == profile.Id);
                if (member is not null) member.CanManageOrganizations = true;
                await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            }
            return;
        }
        db.Tenants.AddRange(new Tenant { Id = TenantA, Name = "Northstar IT" }, new Tenant { Id = TenantB, Name = "Southbank IT" });
        await db.SaveChangesAsync();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            scope.TenantId = tenant;
            var org = tenant == TenantA ? OrgA : OrgB;
            db.Organizations.Add(new Organization { Id = org, TenantId = tenant, Name = "Acme Manufacturing" });
            if (tenant == TenantA) db.Organizations.Add(new Organization { Id = OrgA2, TenantId = tenant, Name = "Harbor Clinic" });
            foreach (var profile in Profiles.Where(x => x.TenantId == tenant))
            {
                db.Memberships.Add(new Membership { Id = profile.MembershipId, TenantId = tenant, Subject = profile.Id, DisplayName = profile.Name, CanManageOrganizations = profile.Id is "alice" or "bob" });
                db.OrganizationGrants.Add(new OrganizationGrant { TenantId = tenant, MembershipId = profile.MembershipId, OrganizationId = org, Role = profile.Id == "viewer" ? "Viewer" : "Editor" });
                if (profile.Id == "alice") db.OrganizationGrants.Add(new OrganizationGrant { TenantId = tenant, MembershipId = profile.MembershipId, OrganizationId = OrgA2, Role = "Editor" });
            }
            db.Assets.Add(new Asset { Id = Guid.Parse(tenant == TenantA ? "10000000-0000-0000-0000-000000001000" : "20000000-0000-0000-0000-000000001000"), TenantId = tenant, OrganizationId = org, Name = "Core switch", Kind = "Network", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            foreach (var organization in await db.Organizations.ToListAsync())
                db.OrganizationRevisions.Add(new() { TenantId = tenant, OrganizationId = organization.Id, Version = organization.Version,
                    Name = organization.Name, Actor = "seed", Origin = "Seed", CreatedAt = organization.UpdatedAt });
            foreach (var asset in await db.Assets.Where(x => x.OrganizationId == org).ToListAsync())
                db.AssetRevisions.Add(new() { TenantId = tenant, OrganizationId = org, AssetId = asset.Id, Version = 1, Name = asset.Name, Kind = asset.Kind, Actor = "seed", CreatedAt = asset.CreatedAt });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }
}
public sealed record DevProfile(string Id, string Name, string Tenant, Guid TenantId, Guid MembershipId);
