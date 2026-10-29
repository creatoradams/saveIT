"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type Organization } from "../lib/api";

export default function OrganizationPanel({
  organizations,
  hasMore,
  disabled,
  onBusyChange,
  onSaved,
  onReload,
  onLoadMore,
  onOpen,
}: {
  organizations: Organization[];
  hasMore: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onSaved: (row: Organization, created: boolean) => void;
  onReload: () => Promise<boolean>;
  onLoadMore: () => Promise<void>;
  onOpen: (row: Organization) => void;
}) {
  const [editing, setEditing] = useState<Organization | "new" | null>(null);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [blocked, setBlocked] = useState(false);
  const request = useRef<AbortController | null>(null);
  const input = useRef<HTMLInputElement>(null);
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  useEffect(() => {
    if (editing) input.current?.focus();
  }, [editing]);
  function start(row: Organization | "new") {
    setEditing(row);
    setName(row === "new" ? "" : row.name);
    setError("");
    setNotice("");
    setBlocked(false);
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing || busy || disabled || blocked) return;
    const creating = editing === "new";
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    setNotice("");
    try {
      const row = await api<Organization>(
        `/organizations${creating ? "" : "/" + editing.id}`,
        {
          method: creating ? "POST" : "PUT",
          signal: abort.signal,
          body: JSON.stringify(
            creating ? { name } : { name, expectedVersion: editing.version },
          ),
        },
      );
      if (!abort.signal.aborted) {
        onSaved(row, creating);
        setEditing(null);
        setNotice(
          creating
            ? "Organization created. Your membership has Editor access."
            : `Organization saved as version ${row.version}.`,
        );
      }
    } catch (err) {
      if (!abort.signal.aborted) {
        setError(
          err instanceof Error
            ? err.message
            : "The organization could not be saved.",
        );
        if (
          err instanceof ApiError &&
          [401, 403, 404, 409].includes(err.status)
        )
          setBlocked(true);
      }
    } finally {
      if (!abort.signal.aborted) {
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  return (
    <section
      className="panel directory-panel organization-panel"
      aria-label="Organization administration"
    >
      <div className="panel-top">
        <h2>Manage organizations</h2>
        <button
          className="primary"
          disabled={disabled || busy}
          onClick={() => start("new")}
        >
          New organization
        </button>
      </div>
      <p className="subtle">
        Create a managed organization or rename one where you have Editor
        access. Each change is audited.
      </p>
      {error && (
        <div className="message error" role="alert">
          {error}
          <button
            disabled={disabled || busy}
            onClick={async () => {
              if (await onReload()) {
                setEditing(null);
                setBlocked(false);
                setError("");
              }
            }}
          >
            Reload organizations
          </button>
        </div>
      )}
      {notice && (
        <div className="message notice" role="status">
          {notice}
        </div>
      )}
      {editing && (
        <form
          className="directory-form"
          aria-label={
            editing === "new" ? "Create organization" : "Rename organization"
          }
          onSubmit={save}
        >
          <label>
            Organization name
            <input
              ref={input}
              required
              maxLength={160}
              value={name}
              disabled={disabled || busy}
              onChange={(event) => setName(event.target.value)}
            />
          </label>
          <div className="actions">
            <button
              type="submit"
              className="primary"
              disabled={disabled || busy || blocked}
            >
              {editing === "new"
                ? "Create organization"
                : "Save organization name"}
            </button>
            <button
              type="button"
              className="secondary"
              disabled={disabled || busy}
              onClick={() => setEditing(null)}
            >
              Cancel organization edit
            </button>
          </div>
        </form>
      )}
      {organizations.length ? (
        <ul className="directory-list" aria-label="Managed organizations">
          {organizations.map((row) => (
            <li key={row.id}>
              <div>
                <strong>{row.name}</strong>
                <p className="subtle">
                  {row.role === "Editor" ? "Editor" : "Read only"} · Version{" "}
                  {row.version}
                </p>
              </div>
              <div className="actions">
                <button
                  className="secondary"
                  disabled={disabled || busy}
                  onClick={() => onOpen(row)}
                  aria-label={`Open organization ${row.name}`}
                >
                  Open workspace
                </button>
                {row.role === "Editor" && (
                  <button
                    className="secondary"
                    disabled={disabled || busy}
                    onClick={() => start(row)}
                    aria-label={`Rename organization ${row.name}`}
                  >
                    Rename
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <div className="empty">
          <h3>No organizations assigned</h3>
          <p>Create one to start documenting its infrastructure.</p>
        </div>
      )}
      {hasMore && (
        <button
          className="load-more"
          disabled={disabled || busy}
          onClick={() => void onLoadMore()}
        >
          Load more organizations
        </button>
      )}
    </section>
  );
}
