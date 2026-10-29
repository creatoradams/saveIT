using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SaveIT.Api.Data;

public sealed class TenantScope
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public string Subject { get; set; } = "";
}

public sealed class SaveItDb(DbContextOptions<SaveItDb> options, TenantScope scope) : DbContext(options)
{
    public Guid CurrentTenantId => scope.TenantId;
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationGrant> OrganizationGrants => Set<OrganizationGrant>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<SearchEntry> SearchEntries => Set<SearchEntry>();
    public DbSet<SearchDelivery> SearchDeliveries => Set<SearchDelivery>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Tenant>().ToTable("Tenants").HasKey(x => x.Id);
        m.Entity<Tenant>().Property(x => x.Name).HasMaxLength(160);
        TenantEntity<Membership>(m, "Memberships");
        m.Entity<Membership>().HasKey(x => x.Id);
        m.Entity<Membership>().HasAlternateKey(x => new { x.TenantId, x.Id });
        m.Entity<Membership>().HasIndex(x => new { x.TenantId, x.Subject }).IsUnique();
        TenantEntity<Organization>(m, "Organizations");
        m.Entity<Organization>().HasKey(x => x.Id);
        m.Entity<Organization>().HasAlternateKey(x => new { x.TenantId, x.Id });
        m.Entity<Organization>().Property(x => x.Name).HasMaxLength(160);
        TenantEntity<OrganizationGrant>(m, "OrganizationGrants");
        m.Entity<OrganizationGrant>().HasKey(x => new { x.TenantId, x.MembershipId, x.OrganizationId });
        m.Entity<OrganizationGrant>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrganizationGrant>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrganizationGrant>().ToTable(t => t.HasCheckConstraint("CK_Grant_Role", "\"Role\" IN ('Editor', 'Viewer')"));
        TenantEntity<Asset>(m, "Assets");
        m.Entity<Asset>().HasKey(x => x.Id);
        m.Entity<Asset>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Asset>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Asset>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<Asset>().Property(x => x.Kind).HasMaxLength(80);
        m.Entity<Asset>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.Id });
        TenantEntity<Document>(m, "Documents");
        m.Entity<Document>().HasKey(x => x.Id);
        m.Entity<Document>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Document>().HasOne<Asset>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Document>().Property(x => x.Title).HasMaxLength(160);
        m.Entity<Document>().Property(x => x.Content).HasMaxLength(50000);
        m.Entity<Document>().Property(x => x.Version).IsConcurrencyToken();
        m.Entity<Document>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.AssetId, x.Id });
        TenantEntity<DocumentRevision>(m, "DocumentRevisions");
        m.Entity<DocumentRevision>().HasKey(x => new { x.TenantId, x.DocumentId, x.Version });
        m.Entity<DocumentRevision>().HasOne<Document>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.DocumentId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<DocumentRevision>().Property(x => x.Title).HasMaxLength(160);
        m.Entity<DocumentRevision>().Property(x => x.Content).HasMaxLength(50000);
        m.Entity<DocumentRevision>().Property(x => x.RestoreReason).HasMaxLength(500);
        m.Entity<DocumentRevision>().HasOne<DocumentRevision>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DocumentId, x.RestoredFromVersion })
            .HasPrincipalKey(x => new { x.TenantId, x.DocumentId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<DocumentRevision>().ToTable(t => t.HasCheckConstraint("CK_Revision_RestoreProvenance",
            "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)"));
        TenantEntity<AuditEvent>(m, "AuditEvents");
        m.Entity<AuditEvent>().HasKey(x => x.Id);
        m.Entity<AuditEvent>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AuditEvent>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.Id });
        TenantEntity<OutboxEvent>(m, "OutboxEvents");
        m.Entity<OutboxEvent>().HasKey(x => x.Id);
        m.Entity<OutboxEvent>().HasAlternateKey(x => new { x.TenantId, x.Id });
        m.Entity<OutboxEvent>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OutboxEvent>().HasIndex(x => new { x.TenantId, x.DeliveredAt });

        TenantEntity<SearchEntry>(m, "SearchEntries");
        m.Entity<SearchEntry>().HasKey(x => new { x.TenantId, x.RecordType, x.Id });
        m.Entity<SearchEntry>().Property(x => x.RecordType).HasMaxLength(16);
        m.Entity<SearchEntry>().Property(x => x.Title).HasMaxLength(160);
        m.Entity<SearchEntry>().Property(x => x.Body).HasMaxLength(50000);
        m.Entity<SearchEntry>().HasOne<Asset>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SearchEntry>().HasGeneratedTsVectorColumn(x => x.SearchVector, "simple", x => new { x.Title, x.Body })
            .HasIndex(x => x.SearchVector).HasMethod("GIN");
        m.Entity<SearchEntry>().HasIndex(x => new { x.TenantId, x.OrganizationId });
        m.Entity<SearchEntry>().ToTable(t => t.HasCheckConstraint("CK_SearchEntry_Type", "\"RecordType\" IN ('asset', 'document')"));
        TenantEntity<SearchDelivery>(m, "SearchDeliveries");
        m.Entity<SearchDelivery>().HasKey(x => new { x.TenantId, x.EventId });
        m.Entity<SearchDelivery>().HasOne<OutboxEvent>().WithMany().HasForeignKey(x => new { x.TenantId, x.EventId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SearchDelivery>().Property(x => x.ErrorCode).HasMaxLength(48);
    }

    private void TenantEntity<T>(ModelBuilder m, string table) where T : class, ITenantOwned
    {
        m.Entity<T>().ToTable(table);
        m.Entity<T>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        m.Entity<T>().HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.TenantId != scope.TenantId || scope.TenantId == Guid.Empty)
                throw new InvalidOperationException("A write requires the current tenant boundary.");
            if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
                throw new InvalidOperationException("Tenant ownership is immutable.");
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

public sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<SaveItDb>
{
    public SaveItDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SaveItDb>()
        .UseNpgsql(Environment.GetEnvironmentVariable("SAVEIT_MIGRATION_CONNECTION") ?? "Host=127.0.0.1;Port=55432;Database=saveit;Username=postgres;Password=local-development-only").Options, new TenantScope());
}
