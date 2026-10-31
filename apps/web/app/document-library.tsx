"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  ApiError,
  type Folder,
  type FolderDetail,
  type LibraryDocument,
  type DocumentPlacement,
  type Page,
} from "../lib/api";

type Snapshot = {
  folder: Folder | null;
  path: Folder[];
  items: Folder[];
  nextCursor: string | null;
};
const message = (e: unknown) =>
  e instanceof Error ? e.message : "The document library is unavailable.";
const denied = (e: unknown) =>
  e instanceof ApiError && [401, 403, 404].includes(e.status);
function query(key: string, value: string | null, after?: string | null) {
  const parameters = new URLSearchParams();
  if (value) parameters.set(key, value);
  if (after) parameters.set("after", after);
  return parameters.size ? `?${parameters}` : "";
}
async function folderPage(
  base: string,
  id: string | null,
  signal: AbortSignal,
  after?: string | null,
): Promise<Snapshot> {
  const [page, detail] = await Promise.all([
    api<Page<Folder>>(`${base}/folders${query("parentId", id, after)}`, {
      signal,
    }),
    id
      ? api<FolderDetail>(`${base}/folders/${id}`, { signal })
      : Promise.resolve(null),
  ]);
  return { folder: detail?.folder ?? null, path: detail?.path ?? [], ...page };
}
function merge<T extends { id: string }>(old: T[], added: T[]) {
  return [...new Map([...old, ...added].map((x) => [x.id, x])).values()];
}

// A component releases only its own workspace busy reservation, including on unmount.
function useOwnedRequest(onBusyChange: (value: boolean) => void) {
  const owned = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(
    () => () => {
      const current = owned.current;
      if (current) {
        owned.current = null;
        current.abort();
        onBusyChange(false);
      }
    },
    [onBusyChange],
  );
  async function run(
    action: (signal: AbortSignal) => Promise<void>,
    fail: (e: unknown) => void,
  ) {
    if (owned.current) return;
    const controller = new AbortController();
    owned.current = controller;
    setBusy(true);
    onBusyChange(true);
    try {
      await action(controller.signal);
    } catch (e) {
      if (!controller.signal.aborted && owned.current === controller) fail(e);
    } finally {
      if (!controller.signal.aborted && owned.current === controller) {
        owned.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  return { busy, run };
}

function Breadcrumbs({
  path,
  disabled,
  onChoose,
  label,
}: {
  path: Folder[];
  disabled: boolean;
  onChoose: (id: string | null) => void;
  label: string;
}) {
  return (
    <nav className="folder-breadcrumbs" aria-label={label}>
      <button
        type="button"
        className="secondary"
        disabled={disabled}
        onClick={() => onChoose(null)}
      >
        Library root
      </button>
      {path.map((row) => (
        <button
          type="button"
          className="secondary"
          key={row.id}
          disabled={disabled}
          onClick={() => onChoose(row.id)}
        >
          {row.name}
        </button>
      ))}
    </nav>
  );
}

function FolderPicker({
  base,
  initial,
  disabled,
  onBusyChange,
  onSelect,
  onDenied,
}: {
  base: string;
  initial: string | null;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onSelect: (id: string | null, label: string, ready: boolean) => void;
  onDenied: (value: string) => void;
}) {
  const [id, setId] = useState(initial),
    [snapshot, setSnapshot] = useState<Snapshot | null>(null),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [refresh, setRefresh] = useState(0);
  const callbacks = useRef({ onSelect, onDenied });
  callbacks.current = { onSelect, onDenied };
  const { busy, run } = useOwnedRequest(onBusyChange);
  const locked = disabled || busy || loading;
  function fail(e: unknown) {
    setSnapshot(null);
    setError(message(e));
    callbacks.current.onSelect(id, "", false);
    if (denied(e)) callbacks.current.onDenied(message(e));
  }
  useEffect(() => {
    const abort = new AbortController();
    setSnapshot(null);
    setLoading(true);
    setError("");
    callbacks.current.onSelect(id, "", false);
    folderPage(base, id, abort.signal)
      .then((data) => {
        if (!abort.signal.aborted) {
          setSnapshot(data);
          callbacks.current.onSelect(
            id,
            data.folder?.name ?? "Library root (unfiled)",
            true,
          );
        }
      })
      .catch((e) => {
        if (!abort.signal.aborted) fail(e);
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [base, id, refresh]);
  function choose(next: string | null) {
    if (next === id) return;
    setSnapshot(null);
    setError("");
    setLoading(true);
    callbacks.current.onSelect(next, "", false);
    setId(next);
  }
  async function more() {
    if (locked || !snapshot?.nextCursor) return;
    await run(async (signal) => {
      const data = await folderPage(base, id, signal, snapshot.nextCursor);
      if (!signal.aborted)
        setSnapshot({ ...data, items: merge(snapshot.items, data.items) });
    }, fail);
  }
  return (
    <section
      className="folder-destination"
      aria-label="Destination folder"
      aria-busy={loading || busy}
    >
      <h3>Destination folder</h3>
      <p className="subtle">
        Choose a folder by opening it below. Library root leaves a document
        unfiled.
      </p>
      <Breadcrumbs
        path={snapshot?.path ?? []}
        disabled={locked}
        onChoose={choose}
        label="Destination path"
      />
      {loading && <p role="status">Loading destination folders…</p>}
      {error && (
        <div className="message error" role="alert">
          {error}
          <button
            type="button"
            className="secondary"
            disabled={locked}
            onClick={() => setRefresh((x) => x + 1)}
          >
            Retry destination folders
          </button>
        </div>
      )}
      {snapshot && (
        <>
          <p>
            Selected destination:{" "}
            <strong>{snapshot.folder?.name ?? "Library root (unfiled)"}</strong>
          </p>
          <ul className="folder-list" aria-label="Destination choices">
            {snapshot.items.map((row) => (
              <li key={row.id}>
                <button
                  type="button"
                  className="secondary"
                  disabled={locked}
                  onClick={() => choose(row.id)}
                >
                  Choose folder {row.name}
                </button>
              </li>
            ))}
          </ul>
          {!snapshot.items.length && <p>No child folders.</p>}
          {snapshot.nextCursor && (
            <button
              type="button"
              className="load-more"
              disabled={locked}
              onClick={() => void more()}
            >
              More destination folders
            </button>
          )}
        </>
      )}
    </section>
  );
}

type Edit = {
  kind: "create" | "folder" | "file";
  id: string;
  name: string;
  expectedVersion: number;
  initial: string | null;
};
export default function DocumentLibrary({
  organizationId,
  canEdit,
  disabled,
  onBusyChange,
  onDenied,
  onOpen,
}: {
  organizationId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onDenied: (value: string) => void;
  onOpen: (document: LibraryDocument) => Promise<void>;
}) {
  const base = `/organizations/${organizationId}`;
  const [location, setLocation] = useState<string | null>(null),
    [snapshot, setSnapshot] = useState<Snapshot | null>(null),
    [documents, setDocuments] = useState<LibraryDocument[]>([]),
    [documentCursor, setDocumentCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [refresh, setRefresh] = useState(0),
    [edit, setEdit] = useState<Edit | null>(null),
    [conflict, setConflict] = useState(false);
  const [destination, setDestination] = useState<string | null>(null),
    [destinationReady, setDestinationReady] = useState(false);
  const [currentReview, setCurrentReview] = useState<{
    name: string | null;
    path: string[];
    version: number;
  } | null>(null);
  const formRef = useRef<HTMLFormElement>(null),
    opener = useRef<HTMLElement | null>(null),
    heading = useRef<HTMLHeadingElement>(null),
    wasEditing = useRef(false),
    returnToOpener = useRef(false);
  const callbacks = useRef({ onDenied });
  callbacks.current = { onDenied };
  const { busy, run } = useOwnedRequest(onBusyChange);
  const locked = disabled || busy || loading;
  function clear() {
    setSnapshot(null);
    setDocuments([]);
    setDocumentCursor(null);
  }
  function fail(e: unknown, clearRows = true) {
    if (clearRows || denied(e)) clear();
    setError(message(e));
    if (e instanceof ApiError && e.status === 409) {
      setConflict(true);
      setCurrentReview(null);
    }
    if (denied(e)) callbacks.current.onDenied(message(e));
  }
  useEffect(() => {
    const abort = new AbortController();
    clear();
    setLoading(true);
    setError("");
    Promise.all([
      folderPage(base, location, abort.signal),
      api<Page<LibraryDocument>>(
        `${base}/library/documents${query("folderId", location)}`,
        { signal: abort.signal },
      ),
    ])
      .then(([folders, docs]) => {
        if (!abort.signal.aborted) {
          setSnapshot(folders);
          setDocuments(docs.items);
          setDocumentCursor(docs.nextCursor);
        }
      })
      .catch((e) => {
        if (!abort.signal.aborted) fail(e);
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [base, location, refresh]);
  useEffect(() => {
    if (edit) {
      wasEditing.current = true;
      const input = formRef.current?.querySelector<HTMLInputElement>(
        "input:not(:disabled)",
      );
      (input ?? formRef.current?.querySelector<HTMLElement>("h3"))?.focus();
    } else if (wasEditing.current) {
      wasEditing.current = false;
      if (returnToOpener.current && opener.current?.isConnected)
        opener.current.focus();
      else heading.current?.focus();
      returnToOpener.current = false;
    }
  }, [edit?.id, edit?.kind]);
  function navigate(id: string | null) {
    if (locked || edit || id === location) return;
    clear();
    setLoading(true);
    setError("");
    setNotice("");
    setLocation(id);
  }
  function start(value: Edit) {
    if (locked) return;
    opener.current = globalThis.document.activeElement as HTMLElement;
    setEdit(value);
    setDestination(value.initial);
    setDestinationReady(false);
    setConflict(false);
    setCurrentReview(null);
    setError("");
    setNotice("");
  }
  function cancel() {
    if (locked) return;
    returnToOpener.current = true;
    setEdit(null);
    setConflict(false);
    setCurrentReview(null);
    setError("");
  }
  async function read(kind: "all" | "folders" | "documents") {
    if (locked) return;
    setError("");
    if (kind === "all") clear();
    await run(async (signal) => {
      if (kind === "all") {
        const [folders, docs] = await Promise.all([
          folderPage(base, location, signal),
          api<Page<LibraryDocument>>(
            `${base}/library/documents${query("folderId", location)}`,
            { signal },
          ),
        ]);
        if (!signal.aborted) {
          setSnapshot(folders);
          setDocuments(docs.items);
          setDocumentCursor(docs.nextCursor);
        }
      } else if (kind === "folders" && snapshot?.nextCursor) {
        const data = await folderPage(
          base,
          location,
          signal,
          snapshot.nextCursor,
        );
        if (!signal.aborted)
          setSnapshot({ ...data, items: merge(snapshot.items, data.items) });
      } else if (kind === "documents" && documentCursor) {
        const data = await api<Page<LibraryDocument>>(
          `${base}/library/documents${query("folderId", location, documentCursor)}`,
          { signal },
        );
        if (!signal.aborted) {
          setDocuments((old) => merge(old, data.items));
          setDocumentCursor(data.nextCursor);
        }
      }
    }, fail);
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (locked || !edit || !destinationReady || conflict) return;
    setError("");
    await run(
      async (signal) => {
        const url =
          edit.kind === "file"
            ? `${base}/documents/${edit.id}/placement`
            : `${base}/folders${edit.kind === "folder" ? `/${edit.id}` : ""}`;
        await api(url, {
          method: edit.kind === "create" ? "POST" : "PUT",
          body: JSON.stringify(
            edit.kind === "file"
              ? { folderId: destination, expectedVersion: edit.expectedVersion }
              : {
                  name: edit.name,
                  parentId: destination,
                  expectedVersion: edit.expectedVersion,
                },
          ),
          signal,
        });
        if (!signal.aborted) {
          setEdit(null);
          clear();
          setLoading(true);
          setConflict(false);
          setCurrentReview(null);
          setNotice(
            edit.kind === "file"
              ? "Document location saved. Content history and linked asset are preserved."
              : "Folder saved.",
          );
          setRefresh((x) => x + 1);
        }
      },
      (e) => fail(e, false),
    );
  }
  async function reloadVersion() {
    if (locked || !edit || edit.kind === "create") return;
    setError("");
    setCurrentReview(null);
    await run(
      async (signal) => {
        let version: number, name: string | null, path: string[];
        if (edit.kind === "file") {
          const placement = await api<DocumentPlacement>(
            `${base}/documents/${edit.id}/placement`,
            { signal },
          );
          const detail = placement.folderId
            ? await api<FolderDetail>(`${base}/folders/${placement.folderId}`, {
                signal,
              })
            : null;
          version = Number(placement.version);
          name = null;
          path = detail?.path.map((x) => x.name) ?? [];
        } else {
          const detail = await api<FolderDetail>(`${base}/folders/${edit.id}`, {
            signal,
          });
          version = Number(detail.folder.version);
          name = detail.folder.name;
          path = detail.path.slice(0, -1).map((x) => x.name);
        }
        if (!signal.aborted) {
          setEdit((old) => (old ? { ...old, expectedVersion: version } : null));
          setConflict(false);
          setCurrentReview({ name, path, version });
          setNotice(
            `Reloaded current version ${version}. Your destination and name are retained; review them before saving again.`,
          );
        }
      },
      (e) => fail(e, false),
    );
  }
  return (
    <section
      className="panel document-library"
      aria-label="Document library"
      aria-busy={loading || busy}
    >
      <div className="panel-top">
        <h2 ref={heading} tabIndex={-1}>
          Document library
        </h2>
        <button
          className="secondary"
          disabled={locked || !!edit}
          onClick={() => void read("all")}
        >
          Refresh document library
        </button>
      </div>
      <p className="subtle">
        Folders organize this organization’s documents. Existing access and
        linked assets apply throughout the library.
      </p>
      {notice && (
        <p role="status" className="message">
          {notice}
        </p>
      )}
      {error && (
        <div role="alert" className="message error">
          {error}
          {!edit && (
            <button
              className="secondary"
              disabled={locked}
              onClick={() => void read("all")}
            >
              Retry document library
            </button>
          )}
        </div>
      )}
      {loading && <p role="status">Loading document library…</p>}
      {snapshot && (
        <div hidden={!!edit}>
          <Breadcrumbs
            path={snapshot.path}
            disabled={locked}
            onChoose={navigate}
            label="Library path"
          />
          <h3>{snapshot.folder?.name ?? "Library root"}</h3>
          {canEdit && (
            <div className="actions">
              <button
                className="secondary"
                disabled={locked}
                onClick={() =>
                  start({
                    kind: "create",
                    id: "new",
                    name: "",
                    initial: location,
                    expectedVersion: 0,
                  })
                }
              >
                New folder
              </button>
              {snapshot.folder && (
                <button
                  className="secondary"
                  disabled={locked}
                  onClick={() =>
                    start({
                      kind: "folder",
                      id: snapshot.folder!.id,
                      name: snapshot.folder!.name,
                      initial: snapshot.folder!.parentId ?? null,
                      expectedVersion: Number(snapshot.folder!.version),
                    })
                  }
                >
                  Rename or move this folder
                </button>
              )}
            </div>
          )}
          <h3>Folders</h3>
          <ul className="folder-list" aria-label="Child folders">
            {snapshot.items.map((row) => (
              <li key={row.id}>
                <button
                  className="secondary"
                  disabled={locked}
                  onClick={() => navigate(row.id)}
                >
                  Open folder {row.name}
                </button>
              </li>
            ))}
          </ul>
          {!snapshot.items.length && <p>No child folders.</p>}
          {snapshot.nextCursor && (
            <button
              className="load-more"
              disabled={locked}
              onClick={() => void read("folders")}
            >
              More child folders
            </button>
          )}
          <h3>{location ? "Documents in this folder" : "Unfiled documents"}</h3>
          <ul className="directory-history-list" aria-label="Library documents">
            {documents.map((doc) => (
              <li key={doc.id}>
                <strong>{doc.title}</strong>
                <small>
                  Content revision {doc.version} · Placement version{" "}
                  {doc.placementVersion}
                </small>
                <div className="actions">
                  <button
                    className="secondary"
                    disabled={locked}
                    onClick={() => void onOpen(doc)}
                  >
                    Open document {doc.title}
                  </button>
                  {canEdit && (
                    <button
                      className="secondary"
                      disabled={locked}
                      onClick={() =>
                        start({
                          kind: "file",
                          id: doc.id,
                          name: doc.title,
                          initial: doc.folderId ?? null,
                          expectedVersion: Number(doc.placementVersion),
                        })
                      }
                    >
                      Move document {doc.title}
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
          {!documents.length && (
            <p>
              {location
                ? "No documents in this folder."
                : "No unfiled documents."}
            </p>
          )}
          {documentCursor && (
            <button
              className="load-more"
              disabled={locked}
              onClick={() => void read("documents")}
            >
              More library documents
            </button>
          )}
        </div>
      )}
      {edit && (
        <form
          className="folder-edit"
          ref={formRef}
          onSubmit={submit}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.preventDefault();
              cancel();
            }
          }}
          aria-label={edit.kind === "file" ? "Move document" : "Edit folder"}
        >
          <h3 tabIndex={-1}>
            {edit.kind === "file"
              ? `Move document: ${edit.name}`
              : edit.kind === "create"
                ? "New folder"
                : "Rename or move folder"}
          </h3>
          {edit.kind !== "file" && (
            <label>
              Folder name
              <input
                required
                maxLength={160}
                value={edit.name}
                disabled={locked}
                onChange={(event) =>
                  setEdit((old) =>
                    old ? { ...old, name: event.target.value } : null,
                  )
                }
              />
            </label>
          )}
          {edit.kind === "file" && (
            <p>
              Moving keeps the content revision and asset link. Content
              restoration keeps the current folder.
            </p>
          )}
          <FolderPicker
            key={`${edit.kind}:${edit.id}`}
            base={base}
            initial={edit.initial}
            disabled={locked}
            onBusyChange={onBusyChange}
            onDenied={onDenied}
            onSelect={(id, _label, ready) => {
              setDestination(id);
              setDestinationReady(ready);
            }}
          />
          {conflict && (
            <p>
              The record changed. Reload its current version, then review your
              retained choices before saving.
            </p>
          )}
          {currentReview && (
            <aside
              className="folder-current-review"
              aria-label="Reloaded current state"
            >
              <h4>Current saved state</h4>
              {currentReview.name !== null && (
                <p>
                  Saved name: <strong>{currentReview.name}</strong>
                </p>
              )}
              <p>
                {edit.kind === "file" ? "Saved location" : "Saved parent"}:{" "}
                {currentReview.path.length
                  ? currentReview.path.join(" / ")
                  : "Library root (unfiled)"}
              </p>
              <p>
                Version {currentReview.version}. Your form choices are retained;
                review them before saving.
              </p>
            </aside>
          )}
          <div className="actions">
            <button
              className="primary"
              disabled={locked || !destinationReady || conflict}
              type="submit"
            >
              {edit.kind === "file" ? "Save document location" : "Save folder"}
            </button>
            {edit.kind !== "create" && (
              <button
                className="secondary"
                disabled={locked}
                type="button"
                onClick={() => void reloadVersion()}
              >
                Reload current {edit.kind === "file" ? "placement" : "folder"}
              </button>
            )}
            <button
              className="secondary"
              disabled={locked}
              type="button"
              onClick={cancel}
            >
              Cancel folder changes
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
