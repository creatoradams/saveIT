using System.Text.Json;

namespace SaveIT.Api;

public sealed record LoginRequest(string Profile);
public sealed record ProfileDto(string Id, string Name, string Tenant);
public sealed record SessionDto(string Name, Guid TenantId, string TenantName, bool CanManageOrganizations = false);
public sealed record OrganizationDto(Guid Id, string Name, string Role, int Version, DateTimeOffset UpdatedAt);
public sealed record CreateOrganizationRequest(string Name);
public sealed record ReviseOrganizationRequest(string Name, int ExpectedVersion);
public sealed record OrganizationRevisionDto(int Version, string Name, string Actor, DateTimeOffset CreatedAt, string Origin);
public sealed record CreateAssetRequest(string Name, string Kind, Guid? AssetTypeId = null, int? AssetTypeVersion = null, JsonElement? Fields = null);
public sealed record AssetDto(Guid Id, Guid OrganizationId, string Name, string Kind, DateTimeOffset CreatedAt, int Version = 1, Guid? LocationId = null, Guid? AssetTypeId = null, int? AssetTypeVersion = null);
public sealed record ReviseAssetRequest(string Name, string Kind, Guid? LocationId, int ExpectedVersion);
public sealed record AssetRevisionDto(int Version, string Name, string Kind, Guid? LocationId, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion, string? RestoreReason, Guid? AssetTypeId, int? AssetTypeVersion, JsonElement Fields, string? LocationName = null);
public sealed record AssetTypeRequest(string Name, JsonElement Definition, int ExpectedVersion = 0, bool IsArchived = false);
public sealed record AssetTypeDto(Guid Id, Guid OrganizationId, string Name, int Version, bool IsArchived, DateTimeOffset UpdatedAt);
public sealed record AssetTypeRevisionDto(Guid AssetTypeId, int Version, string Name, JsonElement Definition, string Actor, DateTimeOffset CreatedAt);
public sealed record AssetTypeRevisionSummaryDto(int Version, string Name, string Actor, DateTimeOffset CreatedAt);
public sealed record AssetFieldsDto(AssetDto Asset, AssetTypeRevisionDto? Schema, JsonElement Values);
public sealed record ReviseAssetFieldsRequest(int ExpectedVersion, Guid? AssetTypeId, int? AssetTypeVersion, JsonElement Fields);
public sealed record AssetComparisonTemplateDto(Guid Id, int Version, string Name);
public sealed record AssetComparisonRevisionDto(int Version, string Name, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion, AssetComparisonTemplateDto? Template);
public sealed record AssetComparisonSideDto(bool Present, JsonElement? Value, JsonElement? Definition, bool Unavailable = false);
public sealed record AssetComparisonRowDto(string Section, string Key, string Label, string Change, bool ValueChanged, bool DefinitionChanged, AssetComparisonSideDto From, AssetComparisonSideDto To);
public sealed record AssetComparisonDto(Guid AssetId, AssetComparisonRevisionDto From, AssetComparisonRevisionDto To,
    int CurrentVersion, bool ChangesOnly, int ChangedRows, int TotalRows, int Offset, IReadOnlyList<AssetComparisonRowDto> Rows, int? NextOffset);
public sealed record DirectoryRequest(string Name, string Details, string Email, string Phone, string Notes, bool IsArchived = false, int ExpectedVersion = 0);
public sealed record DirectoryDto(Guid Id, Guid OrganizationId, string Name, string Details, string Email, string Phone, string Notes, bool IsArchived, int Version, DateTimeOffset UpdatedAt);
public sealed record DirectoryRevisionSummaryDto(int Version, string Name, bool IsArchived, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion);
public sealed record DirectoryRevisionDto(int Version, string Name, string Details, string Email, string Phone, string Notes, bool IsArchived,
    string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion, string? RestoreReason);
public sealed record DirectoryComparisonRowDto(string Key, string Label, bool Changed, bool LineEndingsOnly, JsonElement From, JsonElement To);
public sealed record DirectoryComparisonDto(Guid RecordId, DirectoryRevisionSummaryDto From, DirectoryRevisionSummaryDto To,
    int CurrentVersion, bool ChangesOnly, int ChangedRows, int TotalRows, int Offset, IReadOnlyList<DirectoryComparisonRowDto> Rows, int? NextOffset);
public sealed record CreateRelationshipRequest(Guid TargetAssetId, string Kind);
public sealed record RemoveRelationshipRequest(int ExpectedVersion);
public sealed record AssetRelationshipDto(Guid Id, Guid OrganizationId, AssetDto Source, AssetDto Target,
    string Kind, int Version, string CreatedBy, DateTimeOffset CreatedAt);
public sealed record CreateDocumentRequest(Guid AssetId, string Title, string Content);
public sealed record FolderRequest(string Name, Guid? ParentId, int ExpectedVersion = 0);
public sealed record FolderDto(Guid Id, Guid OrganizationId, Guid? ParentId, string Name, int Version, DateTimeOffset UpdatedAt);
public sealed record FolderDetailDto(FolderDto Folder, IReadOnlyList<FolderDto> Path);
public sealed record FileDocumentRequest(Guid? FolderId, int ExpectedVersion);
public sealed record DocumentPlacementDto(Guid DocumentId, Guid? FolderId, int Version, DateTimeOffset? UpdatedAt);
public sealed record LibraryDocumentDto(Guid Id, Guid AssetId, string Title, int Version, DateTimeOffset UpdatedAt, Guid? FolderId, int PlacementVersion);
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
