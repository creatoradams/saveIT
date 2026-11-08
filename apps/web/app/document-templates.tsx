"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { useOwnedRequest } from "../lib/use-owned-request";
import DocumentTemplateComparisonPanel from "./document-template-comparison";
import {
  api,
  ApiError,
  type Page,
  type Asset,
  type Doc,
  type DocumentTemplate,
  type DocumentTemplateRevision,
  type DocumentTemplateSummary,
  type DocumentTemplateOrigin,
} from "../lib/api";

const message = (e: unknown) =>
  e instanceof Error ? e.message : "Document templates are unavailable.";
const denied = (e: unknown) =>
  e instanceof ApiError && [401, 403, 404].includes(e.status);
const merge = <T extends { id: string }>(a: T[], b: T[]) => [
  ...new Map([...a, ...b].map((x) => [x.id, x])).values(),
];

export default function DocumentTemplates({
  organizationId,
  canEdit,
  disabled,
  onBusyChange,
  onDenied,
  onCreated,
}: {
  organizationId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onDenied: (value: string) => void;
  onCreated: (doc: Doc) => void;
}) {
  const base = `/organizations/${organizationId}`,
    path = `${base}/document-templates`;
  const [items, setItems] = useState<DocumentTemplate[]>([]),
    [cursor, setCursor] = useState<string | null>(null);
  const [includeArchived, setIncludeArchived] = useState(false),
    [loading, setLoading] = useState(true),
    [refresh, setRefresh] = useState(0);
  const [selected, setSelected] = useState<DocumentTemplate | null>(null),
    [preview, setPreview] = useState<DocumentTemplateRevision | null>(null);
  const [history, setHistory] = useState<DocumentTemplateSummary[]>([]),
    [historyCursor, setHistoryCursor] = useState<string | null>(null);
  const [mode, setMode] = useState<"publish" | "document" | null>(null),
    [name, setName] = useState(""),
    [title, setTitle] = useState(""),
    [content, setContent] = useState("");
  const [archived, setArchived] = useState(false),
    [conflict, setConflict] = useState(false),
    [reviewed, setReviewed] = useState(false);
  const [current, setCurrent] = useState<DocumentTemplateRevision | null>(null),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [assets, setAssets] = useState<Asset[]>([]),
    [assetCursor, setAssetCursor] = useState<string | null>(null),
    [assetId, setAssetId] = useState("");
  const [assetFailed, setAssetFailed] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null),
    opener = useRef<HTMLButtonElement | null>(null);
  const historyFocus = useRef<HTMLButtonElement | null>(null);
  const comparisonOpener = useRef<HTMLButtonElement | null>(null);
  const [comparing, setComparing] = useState(false);
  const callbacks = useRef({ onDenied, onCreated });
  callbacks.current = { onDenied, onCreated };
  const { busy, run } = useOwnedRequest(onBusyChange),
    locked = disabled || busy || loading;
  useEffect(() => {
    if (!locked && historyFocus.current) {
      const button = historyFocus.current;
      historyFocus.current = null;
      if (button.isConnected) button.focus();
    }
  }, [locked]);
  function fail(e: unknown) {
    setError(message(e));
    if (denied(e)) callbacks.current.onDenied(message(e));
  }
  function clearDetail() {
    setComparing(false);
    historyFocus.current = null;
    setSelected(null);
    setPreview(null);
    setHistory([]);
    setHistoryCursor(null);
  }
  function failRead(e: unknown) {
    clearDetail();
    fail(e);
  }
  function failPage(e: unknown) {
    setItems([]);
    setCursor(null);
    failRead(e);
  }
  useEffect(() => {
    const c = new AbortController();
    setLoading(true);
    setItems([]);
    setCursor(null);
    clearDetail();
    setError("");
    api<Page<DocumentTemplate>>(`${path}?includeArchived=${includeArchived}`, {
      signal: c.signal,
    })
      .then((p) => {
        if (!c.signal.aborted) {
          setItems(p.items);
          setCursor(p.nextCursor);
        }
      })
      .catch((e) => {
        if (!c.signal.aborted) failPage(e);
      })
      .finally(() => {
        if (!c.signal.aborted) setLoading(false);
      });
    return () => c.abort();
  }, [path, includeArchived, refresh]);
  async function currentSource(id: string, signal: AbortSignal) {
    const head = await api<DocumentTemplate>(`${path}/${id}`, { signal });
    const revision = await api<DocumentTemplateRevision>(
      `${path}/${id}/revisions/${head.version}`,
      { signal },
    );
    if (signal.aborted) throw new DOMException("Cancelled", "AbortError");
    return { head, revision };
  }
  function select(row: DocumentTemplate) {
    setError("");
    setNotice("");
    clearDetail();
    void run(async (signal) => {
      const [source, page] = await Promise.all([
        currentSource(row.id, signal),
        api<Page<DocumentTemplateSummary>>(`${path}/${row.id}/revisions`, {
          signal,
        }),
      ]);
      if (signal.aborted) return;
      setSelected(source.head);
      setPreview(source.revision);
      setHistory(page.items);
      setHistoryCursor(page.nextCursor);
    }, failRead);
  }
  function start(
    kind: "publish" | "document",
    button: HTMLButtonElement,
    fresh = false,
  ) {
    opener.current = button;
    setComparing(false);
    setError("");
    setNotice("");
    setCurrent(null);
    setConflict(false);
    setReviewed(false);
    if (fresh) {
      clearDetail();
      setName("");
      setTitle("");
      setContent("");
      setArchived(false);
      setMode(kind);
      return;
    }
    if (!selected) return;
    void run(async (signal) => {
      const source = await currentSource(selected.id, signal);
      const page =
        kind === "document"
          ? await api<Page<Asset>>(`${base}/assets`, { signal })
          : null;
      if (signal.aborted) return;
      setSelected(source.head);
      setPreview(source.revision);
      setName(source.revision.name);
      setTitle(source.revision.title);
      setContent(source.revision.content);
      setArchived(source.head.isArchived);
      if (page) {
        setAssets(page.items);
        setAssetCursor(page.nextCursor);
        setAssetId("");
        setAssetFailed(false);
      }
      setMode(kind);
    }, failRead);
  }
  function cancel() {
    setMode(null);
    setCurrent(null);
    setConflict(false);
    setError("");
    setReviewed(false);
    requestAnimationFrame(() => opener.current?.focus());
  }
  function reload() {
    if (!selected) return;
    setError("");
    setCurrent(null);
    void run(async (signal) => {
      const source = await currentSource(selected.id, signal);
      if (signal.aborted) return;
      setSelected(source.head);
      setPreview(source.revision);
      setCurrent(source.revision);
      setConflict(false);
      setReviewed(false);
    }, fail); // Failed reload deliberately preserves the conflict barrier and drafts.
  }
  function save(event: FormEvent) {
    event.preventDefault();
    if (
      locked ||
      conflict ||
      (mode === "document" &&
        (!selected || selected.isArchived || !reviewed || !assetId))
    )
      return;
    setError("");
    void run(
      async (signal) => {
        if (mode === "document" && selected) {
          const doc = await api<Doc>(`${path}/${selected.id}/documents`, {
            method: "POST",
            signal,
            body: JSON.stringify({
              assetId,
              templateVersion: Number(selected.version),
              title,
              content,
            }),
          });
          if (!signal.aborted) {
            setMode(null);
            callbacks.current.onCreated(doc);
          }
        } else {
          await api<DocumentTemplate>(
            selected ? `${path}/${selected.id}` : path,
            {
              method: selected ? "PUT" : "POST",
              signal,
              body: JSON.stringify({
                name,
                title,
                content,
                expectedVersion: Number(selected?.version ?? 0),
                isArchived: archived,
              }),
            },
          );
          if (!signal.aborted) {
            setMode(null);
            clearDetail();
            setItems([]);
            setLoading(true);
            setRefresh((x) => x + 1);
            setNotice(
              "Template version published. Existing documents keep their content and source version.",
            );
            heading.current?.focus();
          }
        }
      },
      (e) => {
        if (e instanceof ApiError && e.status === 409) {
          setConflict(true);
          setCurrent(null);
        }
        fail(e);
      },
    );
  }
  return (
    <section
      className="panel document-library document-templates"
      aria-label="Document templates"
      aria-busy={loading || busy}
    >
      <div className="panel-top">
        <h2 ref={heading} tabIndex={-1}>
          Document templates
        </h2>
        <button
          className="secondary"
          disabled={locked || !!mode}
          onClick={() => {
            setError("");
            setRefresh((x) => x + 1);
          }}
        >
          Refresh document templates
        </button>
      </div>
      <p>
        Publish plain-text starters for this organization. Review and edit the
        text before creating a linked document.
      </p>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {loading && <p role="status">Loading templates…</p>}
      <div hidden={!!mode}>
        <label>
          <input
            type="checkbox"
            checked={includeArchived}
            disabled={locked}
            onChange={(e) => setIncludeArchived(e.target.checked)}
          />{" "}
          Include archived document templates
        </label>
        {canEdit && (
          <button
            className="primary"
            disabled={locked}
            onClick={(e) => start("publish", e.currentTarget, true)}
          >
            New document template
          </button>
        )}
        <ul className="folder-list" aria-label="Published document templates">
          {items.map((row) => (
            <li key={row.id}>
              <button
                className="secondary"
                disabled={locked}
                onClick={() => select(row)}
                aria-label={`Inspect template ${row.name}`}
              >
                {row.name} · v{row.version}
                {row.isArchived ? " · Archived" : ""}
              </button>
            </li>
          ))}
        </ul>
        {!loading && !items.length && <p>No templates on this page.</p>}
        {cursor && (
          <button
            className="secondary"
            disabled={locked}
            onClick={() =>
              void run(async (signal) => {
                const page = await api<Page<DocumentTemplate>>(
                  `${path}?includeArchived=${includeArchived}&after=${cursor}`,
                  { signal },
                );
                if (!signal.aborted) {
                  setItems((old) => merge(old, page.items));
                  setCursor(page.nextCursor);
                }
              }, failPage)
            }
          >
            More document templates
          </button>
        )}
        {selected && (
          <div className="folder-destination" aria-label="Template inspection">
            <h3>{selected.name}</h3>
            <p>
              Current publication v{selected.version}
              {selected.isArchived ? " · Archived" : ""}
            </p>
            <button
              className="secondary"
              ref={comparisonOpener}
              disabled={locked}
              onClick={() => setComparing(true)}
            >
              Compare template revisions
            </button>
            {comparing && (
              <DocumentTemplateComparisonPanel
                key={selected.id}
                path={`${path}/${selected.id}`}
                currentVersion={Number(selected.version)}
                disabled={locked}
                onBusyChange={onBusyChange}
                onDenied={onDenied}
                onClose={() => {
                  setComparing(false);
                  requestAnimationFrame(() =>
                    comparisonOpener.current?.focus(),
                  );
                }}
              />
            )}
            {canEdit && (
              <div className="actions">
                <button
                  className="secondary"
                  disabled={locked}
                  onClick={(e) => start("publish", e.currentTarget)}
                >
                  Edit document template
                </button>
                <button
                  className="primary"
                  disabled={locked || selected.isArchived}
                  onClick={(e) => start("document", e.currentTarget)}
                >
                  Create document from template
                </button>
              </div>
            )}
            {preview ? (
              <>
                <h4>Saved template version {preview.version}</h4>
                <strong>{preview.title}</strong>
                <p className="document-text">{preview.content}</p>
                <p className="subtle">
                  Published by {preview.actor} ·{" "}
                  {new Date(preview.createdAt).toLocaleString()}
                </p>
              </>
            ) : (
              <p role="status">Loading saved template version…</p>
            )}
            <ul aria-label="Document template history">
              {history.map((row) => (
                <li key={row.version}>
                  <button
                    className="text-button"
                    disabled={locked}
                    onClick={(e) => {
                      historyFocus.current = e.currentTarget;
                      setPreview(null);
                      void run(async (signal) => {
                        const p = await api<DocumentTemplateRevision>(
                          `${path}/${selected.id}/revisions/${row.version}`,
                          { signal },
                        );
                        if (!signal.aborted) setPreview(p);
                      }, failRead);
                    }}
                  >
                    Inspect version {row.version} · {row.name}
                    {row.isArchived ? " · Archived" : ""}
                  </button>
                </li>
              ))}
            </ul>
            {historyCursor && (
              <button
                className="secondary"
                disabled={locked}
                onClick={() =>
                  void run(async (signal) => {
                    const p = await api<Page<DocumentTemplateSummary>>(
                      `${path}/${selected.id}/revisions?before=${historyCursor}`,
                      { signal },
                    );
                    if (!signal.aborted) {
                      setHistory((old) => [
                        ...new Map(
                          [...old, ...p.items].map((x) => [x.version, x]),
                        ).values(),
                      ]);
                      setHistoryCursor(p.nextCursor);
                    }
                  }, failRead)
                }
              >
                More template history
              </button>
            )}
          </div>
        )}
      </div>
      {mode && (
        <form
          className="folder-edit"
          onSubmit={save}
          onKeyDown={(e) => {
            if (e.key === "Escape" && !locked) {
              e.preventDefault();
              cancel();
            }
          }}
        >
          <h3>
            {mode === "publish"
              ? selected
                ? "Publish another template version"
                : "Publish a document template"
              : "Review the new document"}
          </h3>
          {mode === "publish" ? (
            <>
              <label>
                Template name
                <input
                  autoFocus
                  required
                  maxLength={160}
                  value={name}
                  disabled={locked}
                  onChange={(e) => setName(e.target.value)}
                />
              </label>
              {!selected && (
                <div className="actions">
                  <button
                    type="button"
                    className="secondary"
                    disabled={locked}
                    onClick={() => {
                      setName("Procedure starter");
                      setTitle("Procedure title");
                      setContent(
                        "Purpose\nDescribe the goal.\n\nPrerequisites\nList approved access and dependencies.\n\nSteps\n1. Write the reviewed action.\n\nVerification\nDescribe how to confirm the outcome.\n\nRecovery\nDocument the approved recovery path.\n\nReview\nRecord the owner and next review.",
                      );
                    }}
                  >
                    Use SOP outline
                  </button>
                  <button
                    type="button"
                    className="secondary"
                    disabled={locked}
                    onClick={() => {
                      setName("Incident starter");
                      setTitle("Incident review");
                      setContent(
                        "Summary\nDescribe the synthetic incident.\n\nImpact\nRecord the affected scope.\n\nTimeline\nRecord observations and approved actions.\n\nRecovery and verification\nRecord the outcome.\n\nFollow-up\nList reviewed improvements.",
                      );
                    }}
                  >
                    Use incident outline
                  </button>
                </div>
              )}
              {selected && (
                <label>
                  <input
                    type="checkbox"
                    checked={archived}
                    disabled={locked}
                    onChange={(e) => setArchived(e.target.checked)}
                  />{" "}
                  Archive document template
                </label>
              )}
            </>
          ) : (
            <>
              <p>
                Started from {selected?.name} · version {selected?.version}.
                Your edits become the initial document revision. Later template
                changes do not update this document.
              </p>
              <label>
                Linked asset
                <select
                  autoFocus
                  required
                  value={assetId}
                  disabled={locked}
                  onChange={(e) => setAssetId(e.target.value)}
                >
                  <option value="">Choose an asset</option>
                  {assets.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                    </option>
                  ))}
                </select>
              </label>
              {assetCursor && (
                <button
                  className="secondary"
                  type="button"
                  disabled={locked}
                  onClick={() =>
                    void run(
                      async (signal) => {
                        const page = await api<Page<Asset>>(
                          `${base}/assets?after=${assetCursor}`,
                          { signal },
                        );
                        if (!signal.aborted) {
                          setAssets((old) => merge(old, page.items));
                          setAssetCursor(page.nextCursor);
                        }
                      },
                      (e) => {
                        setAssets([]);
                        setAssetId("");
                        setAssetCursor(null);
                        setAssetFailed(true);
                        setReviewed(false);
                        fail(e);
                      },
                    )
                  }
                >
                  More linked assets
                </button>
              )}
              {assetFailed && (
                <button
                  type="button"
                  className="secondary"
                  disabled={locked}
                  onClick={() =>
                    void run(async (signal) => {
                      const page = await api<Page<Asset>>(`${base}/assets`, {
                        signal,
                      });
                      if (!signal.aborted) {
                        setAssets(page.items);
                        setAssetCursor(page.nextCursor);
                        setAssetFailed(false);
                        setError("");
                      }
                    }, fail)
                  }
                >
                  Reload linked assets
                </button>
              )}
              {!assets.length && (
                <p>
                  No loaded assets. Cancel, create an asset in the workspace and
                  retry.
                </p>
              )}
            </>
          )}
          <label>
            Starter document title
            <input
              required
              maxLength={160}
              value={title}
              disabled={locked}
              onChange={(e) => setTitle(e.target.value)}
            />
          </label>
          <label>
            Starter content
            <textarea
              rows={12}
              required
              maxLength={50000}
              value={content}
              disabled={locked}
              onChange={(e) => setContent(e.target.value)}
            />
          </label>
          {mode === "document" && (
            <label>
              <input
                type="checkbox"
                checked={reviewed}
                disabled={locked || conflict || selected?.isArchived}
                onChange={(e) => setReviewed(e.target.checked)}
              />{" "}
              I reviewed this template version, linked asset and final document
              text.
            </label>
          )}
          {current && (
            <aside
              className="folder-current-review"
              aria-label="Reloaded current template"
            >
              <h4>
                Current saved template · version {current.version}
                {current.isArchived ? " · Archived" : ""}
              </h4>
              <p>{current.name}</p>
              <strong>{current.title}</strong>
              <p className="document-text">{current.content}</p>
              <p>
                Your draft is retained separately. Review the current template
                before retrying.
              </p>
            </aside>
          )}
          {conflict && (
            <p role="status">
              The template changed. Your draft is retained; reload and review
              the current version before retrying.
            </p>
          )}
          {mode === "document" && selected?.isArchived && (
            <p role="status">
              This template is archived. Cancel or reload after it is
              reactivated.
            </p>
          )}
          <div className="actions">
            <button
              className="primary"
              type="submit"
              disabled={
                locked ||
                conflict ||
                (mode === "document" &&
                  (!reviewed || !assetId || selected?.isArchived))
              }
            >
              {mode === "publish"
                ? "Publish template version"
                : "Create reviewed document"}
            </button>
            {selected && (
              <button
                type="button"
                className="secondary"
                disabled={locked}
                onClick={reload}
              >
                Reload current template
              </button>
            )}
            <button
              type="button"
              className="secondary"
              disabled={locked}
              onClick={cancel}
            >
              Cancel template action
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

export function DocumentOrigin({
  organizationId,
  documentId,
  disabled,
  onBusyChange,
  onDenied,
}: {
  organizationId: string;
  documentId: string;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onDenied: (value: string) => void;
}) {
  const [origin, setOrigin] = useState<DocumentTemplateOrigin | null>(null),
    [loading, setLoading] = useState(true),
    [error, setError] = useState("");
  const callbacks = useRef({ onDenied });
  callbacks.current = { onDenied };
  useEffect(() => {
    const c = new AbortController();
    setOrigin(null);
    setError("");
    setLoading(true);
    api<DocumentTemplateOrigin>(
      `/organizations/${organizationId}/documents/${documentId}/template-origin`,
      { signal: c.signal },
    )
      .then((o) => {
        if (!c.signal.aborted) setOrigin(o);
      })
      .catch((e) => {
        if (!c.signal.aborted) {
          setError(message(e));
          if (denied(e)) callbacks.current.onDenied(message(e));
        }
      })
      .finally(() => {
        if (!c.signal.aborted) setLoading(false);
      });
    return () => c.abort();
  }, [organizationId, documentId]);
  const { busy, run } = useOwnedRequest(onBusyChange);
  return (
    <aside aria-label="Document starter provenance">
      <p>
        {loading
          ? "Loading starter provenance…"
          : origin
            ? origin.templateId
              ? `Started from ${origin.templateName} · template version ${origin.templateVersion}. Initial text may include reviewed edits.`
              : "No recorded template origin."
            : "Starter provenance unavailable."}
      </p>
      {error && <p role="alert">{error}</p>}
      <button
        type="button"
        className="text-button"
        disabled={disabled || busy || loading}
        onClick={() => {
          setOrigin(null);
          setError("");
          void run(
            async (signal) => {
              const o = await api<DocumentTemplateOrigin>(
                `/organizations/${organizationId}/documents/${documentId}/template-origin`,
                { signal },
              );
              if (!signal.aborted) setOrigin(o);
            },
            (e) => {
              setError(message(e));
              if (denied(e)) callbacks.current.onDenied(message(e));
            },
          );
        }}
      >
        Reload starter provenance
      </button>
    </aside>
  );
}
