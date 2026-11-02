using Microsoft.EntityFrameworkCore;

namespace SaveIT.Api.Data;

// Protocol state contains identifiers and completion metadata only. It is not a business audit.
public sealed class AccessResponse : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid RequestId { get; set; }
    public Guid InstanceId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Outcome { get; set; }
}

public sealed class AccessAudit : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Action { get; set; } = "";
    public string DatabaseLogin { get; set; } = "";
    public string Operator { get; set; } = "";
    public int AccessVersion { get; set; }
    public int SessionEpoch { get; set; }
    public bool Active { get; set; }
    public bool CanManageOrganizations { get; set; }
    public string? Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AccessControlEvent : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid Id { get; set; }
    public Guid AuditId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed partial class SaveItDb
{
    private void ConfigureAccess(ModelBuilder m)
    {
        TenantEntity<AccessResponse>(m, "AccessResponses");
        m.Entity<AccessResponse>().HasKey(x => new { x.TenantId, x.MembershipId, x.RequestId });
        m.Entity<AccessResponse>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AccessResponse>().Property(x => x.Outcome).HasMaxLength(16);
        m.Entity<AccessResponse>().HasIndex(x => new { x.TenantId, x.MembershipId }).HasDatabaseName("IX_AccessResponses_Pending")
            .HasFilter("\"CompletedAt\" IS NULL");
        m.Entity<AccessResponse>().HasIndex(x => new { x.TenantId, x.MembershipId, x.CompletedAt });
        m.Entity<AccessResponse>().ToTable(t => t.HasCheckConstraint("CK_AccessResponse_Completion",
            "(\"CompletedAt\" IS NULL AND \"Outcome\" IS NULL) OR (\"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NOT NULL AND \"CompletedAt\">=\"StartedAt\" AND \"Outcome\" IN ('Completed','Aborted'))"));

        TenantEntity<AccessAudit>(m, "AccessAudits");
        m.Entity<AccessAudit>().HasKey(x => new { x.TenantId, x.MembershipId, x.Id });
        m.Entity<AccessAudit>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AccessAudit>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AccessAudit>().Property(x => x.Action).HasMaxLength(32);
        m.Entity<AccessAudit>().Property(x => x.DatabaseLogin).HasMaxLength(63);
        m.Entity<AccessAudit>().Property(x => x.Operator).HasMaxLength(160);
        m.Entity<AccessAudit>().Property(x => x.Role).HasMaxLength(16);
        m.Entity<AccessAudit>().HasIndex(x => new { x.TenantId, x.MembershipId, x.CreatedAt, x.Id });
        m.Entity<AccessAudit>().ToTable(t => {
            t.HasCheckConstraint("CK_AccessAudit_Version", "\"AccessVersion\">0 AND \"SessionEpoch\">0");
            t.HasCheckConstraint("CK_AccessAudit_Action", "\"Action\" IN ('Created','ActiveChanged','GrantChanged','CapabilityChanged','Inspected')");
            t.HasCheckConstraint("CK_AccessAudit_Role", "\"Role\" IS NULL OR (\"OrganizationId\" IS NOT NULL AND \"Role\" IN ('Viewer','Editor'))");
        });

        TenantEntity<AccessControlEvent>(m, "AccessControlEvents");
        m.Entity<AccessControlEvent>().HasKey(x => new { x.TenantId, x.Id });
        m.Entity<AccessControlEvent>().HasOne<AccessAudit>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId, x.AuditId })
            .HasPrincipalKey(x => new { x.TenantId, x.MembershipId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AccessControlEvent>().ToTable(t => t.HasCheckConstraint("CK_AccessControlEvent_Schema", "\"SchemaVersion\"=1"));
    }
}
