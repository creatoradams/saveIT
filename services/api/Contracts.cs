namespace SaveIT.Api;

public sealed record LoginRequest(string Profile);
public sealed record ProfileDto(string Id, string Name, string Tenant);
public sealed record SessionDto(string Name, Guid TenantId, string TenantName);
public sealed record OrganizationDto(Guid Id, string Name, string Role);
public sealed record CreateAssetRequest(string Name, string Kind);
public sealed record AssetDto(Guid Id, Guid OrganizationId, string Name, string Kind, DateTimeOffset CreatedAt);
public sealed record CreateDocumentRequest(Guid AssetId, string Title, string Content);
public sealed record ReviseDocumentRequest(string Title, string Content, int ExpectedVersion);
public sealed record DocumentDto(Guid Id, Guid OrganizationId, Guid AssetId, string Title, string Content, int Version, DateTimeOffset UpdatedAt);
public sealed record RevisionDto(int Version, string Title, string Content, string Actor, DateTimeOffset CreatedAt);
public sealed record AuditDto(long Id, string Actor, Guid TargetId, string Action, string Outcome, string CorrelationId, DateTimeOffset CreatedAt);
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
