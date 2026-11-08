type ViewInfo = {
  label: string;
  breadcrumb: string;
  heading: string;
  description: string;
  icon?: string;
};

export const workspaceViews = {
  workspace: {
    label: "Assets & docs",
    breadcrumb: "Assets & documentation",
    heading: "Everything has a home.",
    description: "Keep your infrastructure and its know-how connected.",
    icon: "▦",
  },
  search: {
    label: "Search",
    breadcrumb: "Search",
    heading: "Find the detail that matters.",
    description:
      "Search your assets and documentation across the organizations you can access.",
    icon: "⌕",
  },
  audit: {
    label: "Audit history",
    breadcrumb: "Audit history",
    heading: "Every change leaves a trail.",
    description: "Follow the people and changes behind your documentation.",
    icon: "◷",
  },
  directory: {
    label: "Locations & contacts",
    breadcrumb: "Locations & contacts",
    heading: "People and places, connected.",
    description: "Keep location and contact records inside their organization.",
  },
  templates: {
    label: "Asset templates",
    breadcrumb: "Asset templates",
    heading: "A consistent starting point.",
    description:
      "Define the fields that matter, then keep their history intact.",
    icon: "▦",
  },
  "organization-history": {
    label: "Organization history",
    breadcrumb: "Organization history",
    heading: "A name for every chapter.",
    description: "Inspect the saved names of the organization you can access.",
  },
  library: {
    label: "Document library",
    breadcrumb: "Document library",
    heading: "A place for every document.",
    description:
      "Organize documentation while keeping its history and connections.",
  },
  "document-templates": {
    label: "Document templates",
    breadcrumb: "Document templates",
    heading: "A thoughtful start for every procedure.",
    description:
      "Publish reusable text and review each document before creating it.",
  },
  organizations: {
    label: "Manage organizations",
    breadcrumb: "Manage organizations",
    heading: "Make room for what comes next.",
    description: "Create and name the organizations your team documents.",
  },
} satisfies Record<string, ViewInfo>;

export type WorkspaceView = keyof typeof workspaceViews;

export default function WorkspaceNavigation({
  view,
  disabled,
  canManageOrganizations,
  onChoose,
}: {
  view: WorkspaceView;
  disabled: boolean;
  canManageOrganizations: boolean;
  onChoose: (view: WorkspaceView) => void;
}) {
  return (
    <nav aria-label="Workspace">
      {(Object.keys(workspaceViews) as WorkspaceView[]).map((id) => {
        if (id === "organizations" && !canManageOrganizations) return null;
        const details: ViewInfo = workspaceViews[id];
        return (
          <button
            key={id}
            className={view === id ? "nav-item active" : "nav-item"}
            disabled={disabled}
            onClick={() => onChoose(id)}
          >
            {details.icon && (
              <>
                <span aria-hidden="true">{details.icon}</span>{" "}
              </>
            )}
            {details.label}
          </button>
        );
      })}
    </nav>
  );
}
