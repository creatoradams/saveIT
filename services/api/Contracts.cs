namespace SaveIT.Api;

public sealed record LoginRequest(string Profile);
public sealed record ProfileDto(string Id, string Name, string Tenant);
public sealed record SessionDto(string Name, Guid TenantId, string TenantName, bool CanManageOrganizations = false);
public sealed record OrganizationDto(Guid Id, string Name, string Role, int Version, DateTimeOffset UpdatedAt);
public sealed record CreateOrganizationRequest(string Name);
public sealed record ReviseOrganizationRequest(string Name, int ExpectedVersion);
public sealed record CreateAssetRequest(string Name, string Kind);
public sealed record AssetDto(Guid Id, Guid OrganizationId, string Name, string Kind, DateTimeOffset CreatedAt, int Version = 1, Guid? LocationId = null);
public sealed record ReviseAssetRequest(string Name, string Kind, Guid? LocationId, int ExpectedVersion);
public sealed record AssetRevisionDto(int Version, string Name, string Kind, Guid? LocationId, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion, string? RestoreReason);
public sealed record DirectoryRequest(string Name, string Details, string Email, string Phone, string Notes, bool IsArchived = false, int ExpectedVersion = 0);
public sealed record DirectoryDto(Guid Id, Guid OrganizationId, string Name, string Details, string Email, string Phone, string Notes, bool IsArchived, int Version, DateTimeOffset UpdatedAt);
public sealed record CreateRelationshipRequest(Guid TargetAssetId, string Kind);
public sealed record RemoveRelationshipRequest(int ExpectedVersion);
public sealed record AssetRelationshipDto(Guid Id, Guid OrganizationId, AssetDto Source, AssetDto Target,
    string Kind, int Version, string CreatedBy, DateTimeOffset CreatedAt);
public sealed record CreateDocumentRequest(Guid AssetId, string Title, string Content);
public sealed record ReviseDocumentRequest(string Title, string Content, int ExpectedVersion);
public sealed record DocumentDto(Guid Id, Guid OrganizationId, Guid AssetId, string Title, string Content, int Version, DateTimeOffset UpdatedAt);
public sealed record RevisionDto(int Version, string Title, string Content, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion = null, string? RestoreReason = null);
public sealed record AuditDto(long Id, string Actor, Guid TargetId, string Action, string Outcome, string CorrelationId, DateTimeOffset CreatedAt, int? SourceVersion = null, int? TargetVersion = null);
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record RestoreDocumentRequest(int SourceVersion, int ExpectedVersion, string Reason);
public sealed record RevisionSummaryDto(int Version, string Title, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion);
public sealed record DiffLineDto(string Kind, int? OldLine, int? NewLine, string Text);
public sealed record DocumentComparisonDto(Guid DocumentId, RevisionSummaryDto From, RevisionSummaryDto To,
    int CurrentVersion, bool TitleChanged, bool ContentChanged, bool LineEndingsOnly, bool Simplified,
    int AddedLines, int RemovedLines, int TotalRows, int Offset, IReadOnlyList<DiffLineDto> Lines, int? NextOffset);
