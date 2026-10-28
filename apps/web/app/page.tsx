"use client";

import { useEffect, useState, type FormEvent } from "react";
import {
  api,
  ApiError,
  type Asset,
  type Doc,
  type Organization,
  type Session,
  type Profile,
  type Revision,
  type Audit,
  type Page,
} from "../lib/api";

const errorText = (error: unknown) =>
  error instanceof Error
    ? error.message
    : "Something went wrong. Please try again.";
const time = (value: string) =>
  new Date(value).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });

export default function Workspace() {
  const [session, setSession] = useState<Session | null>(null);
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [organizationId, setOrganizationId] = useState("");
  const [assets, setAssets] = useState<Asset[]>([]);
  const [assetCursor, setAssetCursor] = useState<string | null>(null);
  const [assetId, setAssetId] = useState("");
  const [documents, setDocuments] = useState<Doc[]>([]);
  const [documentCursor, setDocumentCursor] = useState<string | null>(null);
  const [document, setDocument] = useState<Doc | null>(null);
  const [revisions, setRevisions] = useState<Revision[]>([]);
  const [revisionCursor, setRevisionCursor] = useState<string | null>(null);
  const [events, setEvents] = useState<Audit[]>([]);
  const [auditCursor, setAuditCursor] = useState<string | null>(null);
  const [view, setView] = useState<"workspace" | "audit">("workspace");
  const [loading, setLoading] = useState(true);
  const [assetsLoading, setAssetsLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [conflict, setConflict] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [assetForm, setAssetForm] = useState(false);
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [editing, setEditing] = useState(false);
  const organization = organizations.find((x) => x.id === organizationId);
  const selectedAsset = assets.find((x) => x.id === assetId);
  const canEdit = organization?.role === "Editor";
  const base = `/organizations/${organizationId}`;

  async function loadSession() {
    const current = await api<Session>("/session");
    const orgs = await api<Organization[]>("/organizations");
    setSession(current);
    setOrganizations(orgs);
    setOrganizationId(orgs[0]?.id || "");
  }

  useEffect(() => {
    (async () => {
      try {
        await loadSession();
      } catch (err) {
        if (!(err instanceof ApiError && err.status === 401))
          setError(errorText(err));
        try {
          setProfiles(await api<Profile[]>("/dev/profiles"));
        } catch (err) {
          setError(errorText(err));
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  useEffect(() => {
    if (!organizationId) return;
    const abort = new AbortController();
    setAssetsLoading(true);
    setAssets([]);
    setAssetId("");
    setDocument(null);
    setDocuments([]);
    setEditing(false);
    setRevisions([]);
    api<Page<Asset>>(`${base}/assets`, { signal: abort.signal })
      .then((page) => {
        setAssets(page.items);
        setAssetCursor(page.nextCursor);
        setAssetId(page.items[0]?.id || "");
      })
      .catch((err) => {
        if (!abort.signal.aborted) setError(errorText(err));
      })
      .finally(() => {
        if (!abort.signal.aborted) setAssetsLoading(false);
      });
    return () => abort.abort();
  }, [organizationId]); // Organization changes cancel in-flight reads and clear all record state.

  useEffect(() => {
    setDocument(null);
    setDocuments([]);
    setEditing(false);
    setRevisions([]);
    setConflict(false);
    if (!assetId || !organizationId) return;
    const abort = new AbortController();
    api<Page<Doc>>(`${base}/assets/${assetId}/documents`, {
      signal: abort.signal,
    })
      .then((page) => {
        setDocuments(page.items);
        setDocumentCursor(page.nextCursor);
      })
      .catch((err) => {
        if (!abort.signal.aborted) setError(errorText(err));
      });
    return () => abort.abort();
  }, [assetId, organizationId, refresh]);

  useEffect(() => {
    setEvents([]);
    setAuditCursor(null);
    if (!organizationId || !canEdit) return;
    const abort = new AbortController();
    api<Page<Audit>>(`${base}/audit`, { signal: abort.signal })
      .then((page) => {
        setEvents(page.items);
        setAuditCursor(page.nextCursor);
      })
      .catch((err) => {
        if (!abort.signal.aborted) setError(errorText(err));
      });
    return () => abort.abort();
  }, [organizationId, canEdit, refresh, view]);

  async function perform(action: () => Promise<void>) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
    } catch (err) {
      setError(errorText(err));
      if (err instanceof ApiError && err.status === 409) setConflict(true);
    } finally {
      setBusy(false);
    }
  }

  async function login(profile: string) {
    await perform(async () => {
      await api("/dev/login", {
        method: "POST",
        body: JSON.stringify({ profile }),
      });
      await loadSession();
    });
  }
  async function logout() {
    await perform(async () => {
      await api("/logout", { method: "POST" });
      setSession(null);
      setOrganizations([]);
      setOrganizationId("");
      setAssets([]);
      setDocuments([]);
      setDocument(null);
      setEvents([]);
      setRevisions([]);
      setEditing(false);
      setProfiles(await api<Profile[]>("/dev/profiles"));
    });
  }
  async function createAsset(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    await perform(async () => {
      const asset = await api<Asset>(`${base}/assets`, {
        method: "POST",
        body: JSON.stringify({
          name: form.get("name"),
          kind: form.get("kind"),
        }),
      });
      setAssets((previous) => [...previous, asset]);
      setAssetId(asset.id);
      setAssetForm(false);
      setRefresh((x) => x + 1);
      setNotice("Asset created. Add documentation to give your team context.");
    });
  }
  async function openDocument(id: string) {
    await perform(async () => {
      const doc = await api<Doc>(`${base}/documents/${id}`);
      setDocument(doc);
      setTitle(doc.title);
      setContent(doc.content);
      setEditing(false);
      setRevisions([]);
      setConflict(false);
    });
  }
  async function saveDocument(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await perform(async () => {
      const url = document
        ? `${base}/documents/${document.id}`
        : `${base}/documents`;
      const body = document
        ? { title, content, expectedVersion: document.version }
        : { assetId, title, content };
      const saved = await api<Doc>(url, {
        method: document ? "PUT" : "POST",
        body: JSON.stringify(body),
      });
      setDocuments((previous) => [
        ...previous.filter((x) => x.id !== saved.id),
        { ...saved, content: "" },
      ]);
      setDocument(saved);
      setEditing(false);
      setConflict(false);
      setRevisions([]);
      setNotice(`Document saved as revision ${saved.version}.`);
      const audit = await api<Page<Audit>>(`${base}/audit`);
      setEvents(audit.items);
      setAuditCursor(audit.nextCursor);
    });
  }

  if (loading)
    return (
      <main className="login">
        <span className="brand">
          S<span>↗</span> SaveIT
        </span>
        <p role="status">Opening your workspace…</p>
      </main>
    );
  if (!session)
    return (
      <main className="login">
        <div className="login-mark">S↗</div>
        <span className="eyebrow">YOUR OPERATIONAL MEMORY</span>
        <h1>
          Good IT starts
          <br />
          with clear context.
        </h1>
        <p>
          Assets, documentation, and a history you can follow.
          <br />
          Welcome to SaveIT.
        </p>
        <section className="login-panel" aria-label="Development sign in">
          <div className="dev-label">
            <span className="dot" /> DEVELOPMENT WORKSPACE
          </div>
          <h2>Choose a synthetic profile</h2>
          <p>
            This local sign-in simulates identity for development. Use synthetic
            data only.
          </p>
          {profiles.map((profile) => (
            <button
              className="profile"
              key={profile.id}
              disabled={busy}
              onClick={() => login(profile.id)}
            >
              <span className="avatar">{profile.name.charAt(0)}</span>
              <span>
                <strong>{profile.name}</strong>
                <small>{profile.tenant}</small>
              </span>
              <span aria-hidden="true">↗</span>
            </button>
          ))}
          {!profiles.length && (
            <p>
              Development identity is unavailable. Check the API configuration.
            </p>
          )}
          {error && (
            <div className="message error" role="alert">
              {error}
            </div>
          )}
        </section>
        <footer>SaveIT / Foundation preview · Synthetic data only</footer>
      </main>
    );

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        Skip to workspace
      </a>
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-icon">S↗</span>SaveIT
          <span className="preview">preview</span>
        </div>
        <div className="tenant-card">
          <div className="avatar square">{session.tenantName.charAt(0)}</div>
          <div>
            <strong>{session.tenantName}</strong>
            <small>Team workspace</small>
          </div>
        </div>
        <span className="nav-label">WORKSPACE</span>
        <nav aria-label="Workspace">
          <button
            className={view === "workspace" ? "nav-item active" : "nav-item"}
            onClick={() => setView("workspace")}
          >
            <span aria-hidden="true">▦</span> Assets & docs
          </button>
          <button
            className={view === "audit" ? "nav-item active" : "nav-item"}
            onClick={() => setView("audit")}
          >
            <span aria-hidden="true">◷</span> Audit history
          </button>
        </nav>
        <span className="nav-label roadmap-label">ON THE ROADMAP</span>
        <div className="planned">
          <span>⌕</span> Authorized search <small>Next</small>
        </div>
        <div className="planned">
          <span>◇</span> Credential vault <small>Planned</small>
        </div>
        <div className="planned">
          <span>⇄</span> Integrations <small>Planned</small>
        </div>
        <div className="sidebar-bottom">
          <div className="foundation">
            <span className="dot" /> Foundation slice
            <p>
              A growing platform.
              <br />
              An accountable starting point.
            </p>
          </div>
          <button className="account" disabled={busy} onClick={logout}>
            <span className="avatar">{session.name.charAt(0)}</span>
            <span>
              <strong>{session.name}</strong>
              <small>Sign out ↗</small>
            </span>
          </button>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <div className="breadcrumb">
            Workspace <span>/</span>{" "}
            <strong>
              {view === "workspace"
                ? "Assets & documentation"
                : "Audit history"}
            </strong>
          </div>
          <span className="development">
            <span className="dot" /> Local development
          </span>
        </header>
        <main id="main">
          <div className="page-heading">
            <div>
              <span className="eyebrow">
                A LITTLE CONTEXT. A LOT OF CLARITY.
              </span>
              <h1>
                {view === "workspace"
                  ? "Everything has a home."
                  : "Every change leaves a trail."}
              </h1>
              <p>
                {view === "workspace"
                  ? "Keep your infrastructure and its know-how connected."
                  : "Follow the people and changes behind your documentation."}
              </p>
            </div>
          </div>
          <div className="context-bar">
            <div>
              <span className="context-icon" aria-hidden="true">
                ▤
              </span>
              <label htmlFor="organization">
                ORGANIZATION
                <select
                  id="organization"
                  value={organizationId}
                  disabled={busy}
                  onChange={(e) => {
                    setOrganizationId(e.target.value);
                    setError("");
                    setNotice("");
                    setAssetForm(false);
                  }}
                >
                  <option value="" disabled>
                    Select an organization
                  </option>
                  {organizations.map((org) => (
                    <option key={org.id} value={org.id}>
                      {org.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <span className="role-badge">
              {canEdit ? "Editor access" : "Read only"}
            </span>
          </div>
          {error && (
            <div className="message error" role="alert">
              {error}
              {conflict && document && (
                <button
                  disabled={busy}
                  onClick={() => openDocument(document.id)}
                >
                  Reload latest revision
                </button>
              )}
            </div>
          )}
          {notice && (
            <div className="message success" role="status">
              {notice}
            </div>
          )}
          {!organizations.length && (
            <div className="empty">
              <h2>No organizations assigned</h2>
              <p>
                Your membership has no organization grants. Ask your
                administrator for access.
              </p>
            </div>
          )}
          {organizationId && view === "workspace" && (
            <>
              <div className="section-heading">
                <div>
                  <h2>
                    Assets & documentation{" "}
                    <span className="count">
                      {assets.length}
                      {assetCursor ? "+" : ""}
                    </span>
                  </h2>
                  <p>
                    The details your next troubleshooting session will thank you
                    for.
                  </p>
                </div>
                {canEdit && (
                  <button
                    className="primary"
                    disabled={busy}
                    onClick={() => setAssetForm((x) => !x)}
                  >
                    ＋ New asset
                  </button>
                )}
              </div>
              {assetForm && (
                <form className="asset-form panel" onSubmit={createAsset}>
                  <h3>Add an asset</h3>
                  <label>
                    Asset name
                    <input
                      name="name"
                      maxLength={160}
                      required
                      autoFocus
                      placeholder="e.g. Edge firewall"
                    />
                  </label>
                  <label>
                    Asset type
                    <select name="kind">
                      <option>Network</option>
                      <option>Server</option>
                      <option>Workstation</option>
                      <option>Cloud</option>
                      <option>Other</option>
                    </select>
                  </label>
                  <div className="actions">
                    <button className="primary" disabled={busy} type="submit">
                      Create asset
                    </button>
                    <button
                      className="secondary"
                      type="button"
                      onClick={() => setAssetForm(false)}
                    >
                      Cancel
                    </button>
                  </div>
                </form>
              )}
              <div className="asset-grid">
                <section
                  className="panel inventory"
                  aria-label="Asset inventory"
                >
                  <div className="panel-top">
                    <h3>Inventory</h3>
                    <span className="subtle">{assets.length} loaded</span>
                  </div>
                  {assetsLoading ? (
                    <p className="empty" role="status">
                      Loading assets…
                    </p>
                  ) : assets.length === 0 ? (
                    <div className="empty">
                      <span className="empty-icon">▦</span>
                      <h3>A clean slate</h3>
                      <p>
                        {canEdit
                          ? "Create an asset to start connecting your documentation."
                          : "No assets in this organization yet."}
                      </p>
                    </div>
                  ) : (
                    <ul className="asset-list">
                      {assets.map((asset) => (
                        <li key={asset.id}>
                          <button
                            disabled={busy}
                            aria-pressed={assetId === asset.id}
                            className={
                              assetId === asset.id ? "asset selected" : "asset"
                            }
                            onClick={() => setAssetId(asset.id)}
                          >
                            <span className="asset-symbol">▧</span>
                            <span>
                              <strong>{asset.name}</strong>
                              <small>{asset.kind}</small>
                            </span>
                            <span className="asset-arrow" aria-hidden="true">
                              ›
                            </span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  )}
                  {assetCursor && (
                    <button
                      className="load-more"
                      disabled={busy}
                      onClick={() =>
                        perform(async () => {
                          const page = await api<Page<Asset>>(
                            `${base}/assets?after=${assetCursor}`,
                          );
                          setAssets((x) => [...x, ...page.items]);
                          setAssetCursor(page.nextCursor);
                        })
                      }
                    >
                      Load more assets
                    </button>
                  )}
                </section>
                <section
                  className="panel detail"
                  aria-label="Asset documentation"
                >
                  <div className="panel-top">
                    <div>
                      <span className="eyebrow">ASSET DETAILS</span>
                      <h2>{selectedAsset?.name || "Select an asset"}</h2>
                    </div>
                    {selectedAsset && (
                      <span className="tag">{selectedAsset.kind}</span>
                    )}
                  </div>
                  {selectedAsset ? (
                    <>
                      <div className="asset-meta">
                        <span>Created {time(selectedAsset.createdAt)}</span>
                        <span className="subtle">
                          Linked to this organization
                        </span>
                      </div>
                      <div className="documents-heading">
                        <h3>
                          Documentation{" "}
                          <span className="count">{documents.length}</span>
                        </h3>
                        {canEdit && (
                          <button
                            className="text-button"
                            disabled={busy}
                            onClick={() => {
                              setDocument(null);
                              setTitle("");
                              setContent("");
                              setEditing(true);
                              setRevisions([]);
                              setConflict(false);
                            }}
                          >
                            ＋ Add document
                          </button>
                        )}
                      </div>
                      {documents.length ? (
                        <div className="document-list">
                          {documents.map((doc) => (
                            <button
                              key={doc.id}
                              disabled={busy}
                              className={
                                document?.id === doc.id
                                  ? "document-row current"
                                  : "document-row"
                              }
                              onClick={() => openDocument(doc.id)}
                            >
                              <span aria-hidden="true">▤</span>
                              <strong>{doc.title}</strong>
                              <small>v{doc.version}</small>
                              <span aria-hidden="true">↗</span>
                            </button>
                          ))}
                        </div>
                      ) : (
                        !editing && (
                          <div className="empty doc-empty">
                            <span className="empty-icon">▤</span>
                            <h3>Pass the knowledge on.</h3>
                            <p>
                              Add a setup guide, a recovery procedure,
                              <br />
                              or the detail only your team knows.
                            </p>
                          </div>
                        )
                      )}
                      {documentCursor && (
                        <button
                          className="load-more"
                          disabled={busy}
                          onClick={() =>
                            perform(async () => {
                              const page = await api<Page<Doc>>(
                                `${base}/assets/${assetId}/documents?after=${documentCursor}`,
                              );
                              setDocuments((x) => [...x, ...page.items]);
                              setDocumentCursor(page.nextCursor);
                            })
                          }
                        >
                          Load more documents
                        </button>
                      )}
                      {editing ? (
                        <form
                          className="document-editor"
                          onSubmit={saveDocument}
                        >
                          <h3>
                            {document
                              ? `Revise document · v${document.version}`
                              : "New linked document"}
                          </h3>
                          <label>
                            Document title
                            <input
                              value={title}
                              onChange={(e) => setTitle(e.target.value)}
                              required
                              maxLength={160}
                              autoFocus
                            />
                          </label>
                          <label>
                            Content
                            <textarea
                              value={content}
                              onChange={(e) => setContent(e.target.value)}
                              required
                              maxLength={50000}
                              rows={10}
                              placeholder="Write a clear procedure. Use synthetic information only."
                            />
                          </label>
                          <span className="subtle">
                            Plain text · {content.length.toLocaleString()} /
                            50,000 characters
                          </span>
                          <div className="actions">
                            <button
                              className="primary"
                              type="submit"
                              disabled={busy || conflict}
                            >
                              {busy ? "Saving…" : "Save document"}
                            </button>
                            <button
                              className="secondary"
                              type="button"
                              disabled={busy}
                              onClick={() => setEditing(false)}
                            >
                              Cancel
                            </button>
                          </div>
                        </form>
                      ) : (
                        document && (
                          <article className="document-content">
                            <div className="documents-heading">
                              <h3>{document.title}</h3>
                              <span className="tag">
                                Revision {document.version}
                              </span>
                            </div>
                            <p className="document-text">{document.content}</p>
                            <div className="actions">
                              {canEdit && (
                                <button
                                  className="secondary"
                                  disabled={busy}
                                  onClick={() => {
                                    setTitle(document.title);
                                    setContent(document.content);
                                    setEditing(true);
                                  }}
                                >
                                  Edit document
                                </button>
                              )}
                              <button
                                className="text-button"
                                disabled={busy}
                                onClick={() =>
                                  perform(async () => {
                                    const page = await api<Page<Revision>>(
                                      `${base}/documents/${document.id}/revisions`,
                                    );
                                    setRevisions(page.items);
                                    setRevisionCursor(page.nextCursor);
                                  })
                                }
                              >
                                View revisions
                              </button>
                            </div>
                          </article>
                        )
                      )}
                      {!!revisions.length && (
                        <section
                          className="revisions"
                          aria-label="Document revisions"
                        >
                          <h3>Revision history</h3>
                          {revisions.map((rev) => (
                            <details key={rev.version}>
                              <summary>
                                <strong>Revision {rev.version}</strong> ·{" "}
                                {rev.actor} <small>{time(rev.createdAt)}</small>
                              </summary>
                              <h4>{rev.title}</h4>
                              <p className="document-text">{rev.content}</p>
                            </details>
                          ))}
                          {revisionCursor && document && (
                            <button
                              disabled={busy}
                              className="load-more"
                              onClick={() =>
                                perform(async () => {
                                  const page = await api<Page<Revision>>(
                                    `${base}/documents/${document.id}/revisions?before=${revisionCursor}`,
                                  );
                                  setRevisions((x) => [...x, ...page.items]);
                                  setRevisionCursor(page.nextCursor);
                                })
                              }
                            >
                              Older revisions
                            </button>
                          )}
                        </section>
                      )}
                    </>
                  ) : (
                    <div className="empty doc-empty">
                      <span className="empty-icon">↖</span>
                      <h3>Start with an asset.</h3>
                      <p>
                        Its documentation and revision history will live here.
                      </p>
                    </div>
                  )}
                </section>
              </div>
              <div className="workspace-note">
                <span aria-hidden="true">◈</span>
                <span>
                  Connected by design. Asset changes and document revisions are
                  recorded with an audit trail.
                </span>
              </div>
            </>
          )}
          {organizationId && view === "audit" && (
            <section className="panel audit-panel">
              <div className="panel-top">
                <h2>Organization activity</h2>
                <span className="tag">Latest first</span>
              </div>
              {!canEdit ? (
                <div className="empty">
                  <h3>Editor access required</h3>
                  <p>
                    Your role can read documentation. Audit history requires
                    editor access.
                  </p>
                </div>
              ) : events.length === 0 ? (
                <div className="empty">
                  <h3>No activity yet</h3>
                  <p>Create an asset or document to start the trail.</p>
                </div>
              ) : (
                <div className="table-scroll">
                  <table>
                    <caption className="sr-only">
                      Audit history for {organization?.name}
                    </caption>
                    <thead>
                      <tr>
                        <th>Event</th>
                        <th>Actor</th>
                        <th>Time</th>
                        <th>Outcome</th>
                      </tr>
                    </thead>
                    <tbody>
                      {events.map((event) => (
                        <tr key={event.id}>
                          <td>
                            <strong>{event.action}</strong>
                            <small className="target">
                              Target {event.targetId}
                            </small>
                          </td>
                          <td>{event.actor}</td>
                          <td>{time(event.createdAt)}</td>
                          <td>
                            <span className="success-badge">
                              {event.outcome}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {auditCursor && (
                <button
                  className="load-more"
                  disabled={busy}
                  onClick={() =>
                    perform(async () => {
                      const page = await api<Page<Audit>>(
                        `${base}/audit?before=${auditCursor}`,
                      );
                      setEvents((x) => [...x, ...page.items]);
                      setAuditCursor(page.nextCursor);
                    })
                  }
                >
                  Older events
                </button>
              )}
            </section>
          )}
          <footer className="page-footer">
            <span>
              SaveIT <span className="subtle">/</span> Foundation preview
            </span>
            <span>Synthetic data · Local identity</span>
          </footer>
        </main>
      </div>
    </div>
  );
}
