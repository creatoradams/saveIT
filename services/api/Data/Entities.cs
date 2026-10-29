namespace SaveIT.Api.Data;

public interface ITenantOwned { Guid TenantId { get; set; } }
public sealed class Tenant { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed class Membership : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
}
public sealed class Organization : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
}
public sealed class OrganizationGrant : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = "Viewer";
}
public sealed class Asset : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class Document : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AssetId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class DocumentRevision : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int? RestoredFromVersion { get; set; }
    public string? RestoreReason { get; set; }
}
public sealed class AuditEvent : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Actor { get; set; } = "";
    public Guid TargetId { get; set; }
    public string Action { get; set; } = "";
    public string Outcome { get; set; } = "success";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int? SourceVersion { get; set; }
    public int? TargetVersion { get; set; }
}
public sealed class OutboxEvent : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TargetId { get; set; }
    public string Type { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class SearchEntry : ITenantOwned
{
    public Guid TenantId { get; set; }
    public string RecordType { get; set; } = "";
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AssetId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
    public NpgsqlTypes.NpgsqlTsVector SearchVector { get; set; } = null!;
}

public sealed class SearchDelivery : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? ErrorCode { get; set; }
}
