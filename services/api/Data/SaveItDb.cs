using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SaveIT.Api.Data;

public sealed class TenantScope
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public string Subject { get; set; } = "";
    public bool CanManageOrganizations { get; set; }
}

public sealed class SaveItDb(DbContextOptions<SaveItDb> options, TenantScope scope) : DbContext(options)
{
    public Guid CurrentTenantId => scope.TenantId;
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationRevision> OrganizationRevisions => Set<OrganizationRevision>();
    public DbSet<OrganizationGrant> OrganizationGrants => Set<OrganizationGrant>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<AssetTypeRevision> AssetTypeRevisions => Set<AssetTypeRevision>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<LocationRevision> LocationRevisions => Set<LocationRevision>();
    public DbSet<ContactRevision> ContactRevisions => Set<ContactRevision>();
    public DbSet<AssetRevision> AssetRevisions => Set<AssetRevision>();
    public DbSet<AssetRelationship> AssetRelationships => Set<AssetRelationship>();
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
        m.Entity<Membership>().Property(x => x.ExternalIssuer).HasMaxLength(500);
        m.Entity<Membership>().Property(x => x.ExternalSubject).HasMaxLength(256);
        m.Entity<Membership>().HasIndex(x => new { x.TenantId, x.ExternalIssuer, x.ExternalSubject }).IsUnique();
        m.Entity<Membership>().ToTable(t => t.HasCheckConstraint("CK_Membership_ExternalIdentity", "(\"ExternalIssuer\" IS NULL AND \"ExternalSubject\" IS NULL) OR (\"ExternalIssuer\" IS NOT NULL AND \"ExternalSubject\" IS NOT NULL AND length(\"ExternalIssuer\") > 0 AND length(\"ExternalSubject\") > 0)"));
        TenantEntity<Organization>(m, "Organizations");
        m.Entity<Organization>().HasKey(x => x.Id);
        m.Entity<Organization>().HasAlternateKey(x => new { x.TenantId, x.Id });
        m.Entity<Organization>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<Organization>().Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        m.Entity<Organization>().Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        m.Entity<Organization>().HasIndex(x => new { x.TenantId, x.Name, x.Id });
        m.Entity<Organization>().ToTable(t => t.HasCheckConstraint("CK_Organization_Version", "\"Version\" > 0"));
        TenantEntity<OrganizationRevision>(m, "OrganizationRevisions");
        m.Entity<OrganizationRevision>().HasKey(x => new { x.TenantId, x.OrganizationId, x.Version });
        m.Entity<OrganizationRevision>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrganizationRevision>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<OrganizationRevision>().Property(x => x.Origin).HasMaxLength(16);
        m.Entity<OrganizationRevision>().ToTable(t => {
            t.HasCheckConstraint("CK_OrganizationRevision_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_OrganizationRevision_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 160");
            t.HasCheckConstraint("CK_OrganizationRevision_Actor", "length(btrim(\"Actor\")) > 0");
            t.HasCheckConstraint("CK_OrganizationRevision_Origin", "\"Origin\" IN ('Created','Renamed','Migration','Seed')");
        });
        TenantEntity<OrganizationGrant>(m, "OrganizationGrants");
        m.Entity<OrganizationGrant>().HasKey(x => new { x.TenantId, x.MembershipId, x.OrganizationId });
        m.Entity<OrganizationGrant>().HasOne<Membership>().WithMany().HasForeignKey(x => new { x.TenantId, x.MembershipId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrganizationGrant>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrganizationGrant>().ToTable(t => t.HasCheckConstraint("CK_Grant_Role", "\"Role\" IN ('Editor', 'Viewer')"));
        TenantEntity<Location>(m, "Locations");
        m.Entity<Location>().HasKey(x => x.Id);
        m.Entity<Location>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Location>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Location>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<Location>().Property(x => x.Details).HasMaxLength(500);
        m.Entity<Location>().Property(x => x.Notes).HasMaxLength(4000);
        m.Entity<Location>().Property(x => x.Version).IsConcurrencyToken();
        m.Entity<Location>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.IsArchived, x.Id });
        m.Entity<Location>().ToTable(t => t.HasCheckConstraint("CK_Location_Version", "\"Version\" > 0"));
        TenantEntity<Contact>(m, "Contacts");
        m.Entity<Contact>().HasKey(x => x.Id);
        m.Entity<Contact>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Contact>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Contact>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<Contact>().Property(x => x.Details).HasMaxLength(500);
        m.Entity<Contact>().Property(x => x.Email).HasMaxLength(320);
        m.Entity<Contact>().Property(x => x.Phone).HasMaxLength(80);
        m.Entity<Contact>().Property(x => x.Notes).HasMaxLength(4000);
        m.Entity<Contact>().Property(x => x.Version).IsConcurrencyToken();
        m.Entity<Contact>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.IsArchived, x.Id });
        m.Entity<Contact>().ToTable(t => t.HasCheckConstraint("CK_Contact_Version", "\"Version\" > 0"));
        TenantEntity<LocationRevision>(m, "LocationRevisions");
        m.Entity<LocationRevision>().HasKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version });
        m.Entity<LocationRevision>().HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.RecordId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<LocationRevision>().HasOne<LocationRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.RestoredFromVersion }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<LocationRevision>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<LocationRevision>().Property(x => x.Details).HasMaxLength(500);
        m.Entity<LocationRevision>().Property(x => x.Notes).HasMaxLength(4000);
        m.Entity<LocationRevision>().Property(x => x.RestoreReason).HasMaxLength(500);
        m.Entity<LocationRevision>().ToTable(t => {
            t.HasCheckConstraint("CK_LocationRevision_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_LocationRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");
        });
        TenantEntity<ContactRevision>(m, "ContactRevisions");
        m.Entity<ContactRevision>().HasKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version });
        m.Entity<ContactRevision>().HasOne<Contact>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.RecordId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ContactRevision>().HasOne<ContactRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.RestoredFromVersion }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ContactRevision>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<ContactRevision>().Property(x => x.Details).HasMaxLength(500);
        m.Entity<ContactRevision>().Property(x => x.Email).HasMaxLength(320);
        m.Entity<ContactRevision>().Property(x => x.Phone).HasMaxLength(80);
        m.Entity<ContactRevision>().Property(x => x.Notes).HasMaxLength(4000);
        m.Entity<ContactRevision>().Property(x => x.RestoreReason).HasMaxLength(500);
        m.Entity<ContactRevision>().ToTable(t => {
            t.HasCheckConstraint("CK_ContactRevision_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_ContactRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");
        });
        TenantEntity<Asset>(m, "Assets");
        TenantEntity<AssetType>(m, "AssetTypes");
        m.Entity<AssetType>().HasKey(x => x.Id);
        m.Entity<AssetType>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<AssetType>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetType>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<AssetType>().Property(x => x.Version).IsConcurrencyToken();
        m.Entity<AssetType>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.IsArchived, x.Id });
        m.Entity<AssetType>().ToTable(t => t.HasCheckConstraint("CK_AssetType_Version", "\"Version\" > 0"));
        TenantEntity<AssetTypeRevision>(m, "AssetTypeRevisions");
        m.Entity<AssetTypeRevision>().HasKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.Version });
        m.Entity<AssetTypeRevision>().HasOne<AssetType>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetTypeRevision>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<AssetTypeRevision>().Property(x => x.DefinitionJson).HasColumnType("jsonb");
        m.Entity<AssetTypeRevision>().ToTable(t => {
            t.HasCheckConstraint("CK_AssetTypeRevision_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_AssetTypeRevision_Definition", "public.valid_asset_definition_v1(\"DefinitionJson\") IS TRUE");
        });
        m.Entity<Asset>().HasKey(x => x.Id);
        m.Entity<Asset>().HasAlternateKey(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Asset>().HasOne<Organization>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Asset>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<Asset>().Property(x => x.Kind).HasMaxLength(80);
        m.Entity<Asset>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.Id });
        m.Entity<Asset>().Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        m.Entity<Asset>().Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        m.Entity<Asset>().HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Asset>().ToTable(t => t.HasCheckConstraint("CK_Asset_Version", "\"Version\" > 0"));
        m.Entity<Asset>().Property(x => x.FieldsJson).HasColumnType("jsonb").HasDefaultValue("{}");
        m.Entity<Asset>().HasOne<AssetTypeRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.AssetTypeVersion }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Asset>().ToTable(t => t.HasCheckConstraint("CK_Asset_TypePair", "(\"AssetTypeId\" IS NULL AND \"AssetTypeVersion\" IS NULL AND \"FieldsJson\"='{}'::jsonb) OR (\"AssetTypeId\" IS NOT NULL AND \"AssetTypeVersion\" IS NOT NULL AND \"AssetTypeVersion\">0)"));
        TenantEntity<AssetRevision>(m, "AssetRevisions");
        m.Entity<AssetRevision>().HasKey(x => new { x.TenantId, x.AssetId, x.Version });
        m.Entity<AssetRevision>().HasOne<Asset>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRevision>().Property(x => x.Name).HasMaxLength(160);
        m.Entity<AssetRevision>().Property(x => x.Kind).HasMaxLength(80);
        m.Entity<AssetRevision>().Property(x => x.LocationName).HasMaxLength(160);
        m.Entity<AssetRevision>().ToTable(t => t.HasCheckConstraint("CK_AssetRevision_LocationName", "\"LocationName\" IS NULL OR (\"LocationId\" IS NOT NULL AND length(btrim(\"LocationName\")) BETWEEN 1 AND 160)"));
        m.Entity<AssetRevision>().Property(x => x.RestoreReason).HasMaxLength(500);
        m.Entity<AssetRevision>().Property(x => x.FieldsJson).HasColumnType("jsonb").HasDefaultValue("{}");
        m.Entity<AssetRevision>().HasOne<AssetTypeRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.AssetTypeVersion }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRevision>().ToTable(t => t.HasCheckConstraint("CK_AssetRevision_TypePair", "(\"AssetTypeId\" IS NULL AND \"AssetTypeVersion\" IS NULL AND \"FieldsJson\"='{}'::jsonb) OR (\"AssetTypeId\" IS NOT NULL AND \"AssetTypeVersion\" IS NOT NULL AND \"AssetTypeVersion\">0)"));
        m.Entity<AssetRevision>().HasOne<Location>().WithMany().HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.LocationId }).HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRevision>().HasOne<AssetRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.AssetId, x.RestoredFromVersion }).HasPrincipalKey(x => new { x.TenantId, x.AssetId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRevision>().ToTable(t => t.HasCheckConstraint("CK_AssetRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)"));
        TenantEntity<AssetRelationship>(m, "AssetRelationships");
        m.Entity<AssetRelationship>().HasKey(x => x.Id);
        m.Entity<AssetRelationship>().HasOne<Asset>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.SourceAssetId })
            .HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRelationship>().HasOne<Asset>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.OrganizationId, x.TargetAssetId })
            .HasPrincipalKey(x => new { x.TenantId, x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<AssetRelationship>().Property(x => x.Kind).HasMaxLength(24);
        m.Entity<AssetRelationship>().Property(x => x.CreatedBy).HasMaxLength(160);
        m.Entity<AssetRelationship>().Property(x => x.RemovedBy).HasMaxLength(160);
        m.Entity<AssetRelationship>().Property(x => x.Version).IsConcurrencyToken();
        m.Entity<AssetRelationship>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.SourceAssetId, x.TargetAssetId, x.Kind })
            .IsUnique().HasFilter("\"RemovedAt\" IS NULL");
        m.Entity<AssetRelationship>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.SourceAssetId, x.Id });
        m.Entity<AssetRelationship>().HasIndex(x => new { x.TenantId, x.OrganizationId, x.TargetAssetId, x.Id });
        m.Entity<AssetRelationship>().ToTable(t => {
            t.HasCheckConstraint("CK_Relationship_Endpoints", "\"SourceAssetId\" <> \"TargetAssetId\"");
            t.HasCheckConstraint("CK_Relationship_Kind", "\"Kind\" IN ('DependsOn','Contains')");
            t.HasCheckConstraint("CK_Relationship_Lifecycle", "COALESCE((\"Version\"=1 AND \"RemovedAt\" IS NULL AND \"RemovedBy\" IS NULL) OR (\"Version\"=2 AND \"RemovedAt\" IS NOT NULL AND length(btrim(\"RemovedBy\")) > 0),false)");
        });
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
        m.Entity<SearchDelivery>().HasIndex(x => new { x.TenantId, x.NextAttemptAt, x.EventId })
            .HasDatabaseName("IX_SearchDeliveries_Due").HasFilter("\"CompletedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
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
