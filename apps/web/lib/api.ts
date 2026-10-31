import type { components } from "./api.generated";
export type Asset = components["schemas"]["AssetDto"];
export type AssetType = components["schemas"]["AssetTypeDto"];
export type AssetTypeRevision = components["schemas"]["AssetTypeRevisionDto"];
export type AssetTypeRevisionSummary =
  components["schemas"]["AssetTypeRevisionSummaryDto"];
export type AssetFields = components["schemas"]["AssetFieldsDto"];
export type DirectoryEntry = components["schemas"]["DirectoryDto"];
export type DirectoryRevision = components["schemas"]["DirectoryRevisionDto"];
export type DirectoryComparison =
  components["schemas"]["DirectoryComparisonDto"];
export type DirectoryRevisionSummary =
  components["schemas"]["DirectoryRevisionSummaryDto"];
export type AssetRevision = components["schemas"]["AssetRevisionDto"];
export type AssetComparison = components["schemas"]["AssetComparisonDto"];
export type AssetComparisonSide =
  components["schemas"]["AssetComparisonSideDto"];
export type AssetRelationship = components["schemas"]["AssetRelationshipDto"];
export type Doc = components["schemas"]["DocumentDto"];
export type Folder = components["schemas"]["FolderDto"];
export type FolderDetail = components["schemas"]["FolderDetailDto"];
export type LibraryDocument = components["schemas"]["LibraryDocumentDto"];
export type DocumentPlacement = components["schemas"]["DocumentPlacementDto"];
export type Organization = components["schemas"]["OrganizationDto"];
export type OrganizationRevision =
  components["schemas"]["OrganizationRevisionDto"];
export type Session = components["schemas"]["SessionDto"];
export type Profile = components["schemas"]["ProfileDto"];
export type Revision = components["schemas"]["RevisionDto"];
export type Audit = components["schemas"]["AuditDto"];
export type SearchHit = components["schemas"]["SearchHit"];
export type SearchPage = components["schemas"]["SearchPage"];
export type DocumentComparison = components["schemas"]["DocumentComparisonDto"];
export type Page<T> = { items: T[]; nextCursor: string | null };
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export async function api<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body) headers.set("Content-Type", "application/json");
  if (options.method && options.method !== "GET") {
    // Fetch after identity changes so the antiforgery token matches the current principal.
    const response = await fetch("/api/v1/csrf", {
      credentials: "same-origin",
      cache: "no-store",
    });
    if (!response.ok)
      throw new ApiError(
        response.status,
        "Your session could not be verified. Try again.",
      );
    headers.set("X-CSRF-TOKEN", (await response.json()).token);
  }
  const response = await fetch(`/api/v1${path}`, {
    ...options,
    headers,
    credentials: "same-origin",
    cache: "no-store",
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status,
      problem.title ||
        (response.status === 401
          ? "Your session ended. Sign in again."
          : "This item is unavailable or you do not have access."),
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}
