"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type DirectoryEntry, type Page } from "../lib/api";
import DirectoryHistory from "./directory-history";

const empty = {
  name: "",
  details: "",
  email: "",
  phone: "",
  notes: "",
  isArchived: false,
};
export default function DirectoryPanel({
  organizationId,
  canEdit,
  disabled,
  onBusyChange,
}: {
  organizationId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
}) {
  const [resource, setResource] = useState("locations");
  const [archived, setArchived] = useState(false);
  const [items, setItems] = useState<DirectoryEntry[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [form, setForm] = useState(empty);
  const [selected, setSelected] = useState<DirectoryEntry | null>(null);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [conflict, setConflict] = useState(false);
  const [notice, setNotice] = useState("");
  const [reload, setReload] = useState(0);
  const [historyRecord, setHistoryRecord] = useState<DirectoryEntry | null>(
    null,
  );
  const request = useRef<AbortController | null>(null);
  const base = `/organizations/${organizationId}/${resource}`;
  const singular = resource === "locations" ? "location" : "contact";
  useEffect(() => {
    const abort = new AbortController();
    setItems([]);
    setCursor(null);
    setSelected(null);
    setHistoryRecord(null);
    setEditing(false);
    setError("");
    setNotice("");
    setConflict(false);
    setLoading(true);
    api<Page<DirectoryEntry>>(`${base}?includeArchived=${archived}`, {
      signal: abort.signal,
    })
      .then((page) => {
        if (!abort.signal.aborted) {
          setItems(page.items);
          setCursor(page.nextCursor);
        }
      })
      .catch((err) => {
        if (!abort.signal.aborted)
          setError(
            err instanceof Error
              ? err.message
              : "Directory could not be loaded.",
          );
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => {
      abort.abort();
    };
  }, [base, archived, reload, onBusyChange]);
  useEffect(
    () => () => {
      if (request.current) {
        request.current.abort();
        onBusyChange(false);
      }
    },
    [onBusyChange],
  );
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (disabled || busy || loading) return;
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    setNotice("");
    try {
      await action(abort.signal);
    } catch (err) {
      if (!abort.signal.aborted) {
        setError(
          err instanceof Error
            ? err.message
            : "The change could not be completed.",
        );
        setConflict(err instanceof ApiError && err.status === 409);
        if (err instanceof ApiError && [401, 403, 404].includes(err.status)) {
          setItems([]);
          setCursor(null);
          setSelected(null);
          setHistoryRecord(null);
          setEditing(false);
        }
      }
    } finally {
      if (!abort.signal.aborted) {
        request.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (conflict || historyRecord) return;
    await perform(async (signal) => {
      const saved = await api<DirectoryEntry>(
        selected ? `${base}/${selected.id}` : base,
        {
          method: selected ? "PUT" : "POST",
          signal,
          body: JSON.stringify({
            ...form,
            expectedVersion: selected?.version ?? 0,
          }),
        },
      );
      if (signal.aborted) return;
      setItems((previous) =>
        [...previous.filter((x) => x.id !== saved.id), saved].filter(
          (x) => archived || !x.isArchived,
        ),
      );
      setSelected(saved);
      setEditing(false);
      setConflict(false);
      setNotice(
        `${singular === "location" ? "Location" : "Contact"} saved as version ${saved.version}.`,
      );
    });
  }
  return (
    <section
      className="panel directory-panel"
      aria-label="Locations and contacts"
      aria-busy={loading || busy}
    >
      <div className="panel-top">
        <h2>Locations & contacts</h2>
        {canEdit && (
          <button
            className="primary"
            disabled={disabled || busy || loading || !!historyRecord}
            onClick={() => {
              setSelected(null);
              setForm(empty);
              setEditing(true);
              setError("");
              setConflict(false);
            }}
          >
            New {singular}
          </button>
        )}
      </div>
      <div className="directory-controls">
        <label>
          Directory
          <select
            value={resource}
            disabled={disabled || busy}
            onChange={(e) => setResource(e.target.value)}
          >
            <option value="locations">Locations</option>
            <option value="contacts">Contacts</option>
          </select>
        </label>
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={archived}
            disabled={disabled || busy}
            onChange={(e) => setArchived(e.target.checked)}
          />
          Include archived records
        </label>
      </div>
      {loading && <p role="status">Loading {resource}…</p>}
      {error && (
        <div className="message error" role="alert">
          {error}
          {conflict && (
            <p>
              Your entered fields are preserved. Reload the record to review the
              current version before retrying.
            </p>
          )}
          <button
            disabled={disabled || busy}
            onClick={() => setReload((x) => x + 1)}
          >
            Reload directory
          </button>
        </div>
      )}
      {notice && (
        <p className="message success" role="status">
          {notice}
        </p>
      )}
      {!loading && !error && !items.length && (
        <p className="empty">No {resource} yet.</p>
      )}
      <ul
        className="directory-list"
        aria-label={
          resource === "locations" ? "Location records" : "Contact records"
        }
      >
        {items.map((item) => (
          <li key={item.id}>
            <div>
              <strong>{item.name}</strong>
              {item.isArchived && <span className="tag">Archived</span>}
              <p>{item.details}</p>
              {item.email && <p>{item.email}</p>}
              {item.phone && <p>{item.phone}</p>}
              <p className="document-text">{item.notes}</p>
              <small>
                Version {item.version} ·{" "}
                {new Date(item.updatedAt).toLocaleString()}
              </small>
            </div>
            <div className="actions">
              <button
                className="secondary"
                disabled={
                  disabled || busy || loading || editing || !!historyRecord
                }
                aria-label={`View ${singular} history ${item.name}`}
                onClick={() => setHistoryRecord(item)}
              >
                View history
              </button>
              {canEdit && (
                <button
                  className="secondary"
                  disabled={disabled || busy || loading || !!historyRecord}
                  aria-label={`Edit ${singular} ${item.name}`}
                  onClick={() => {
                    setSelected(item);
                    setForm({
                      name: item.name,
                      details: item.details,
                      email: item.email,
                      phone: item.phone,
                      notes: item.notes,
                      isArchived: item.isArchived,
                    });
                    setEditing(true);
                    setError("");
                    setConflict(false);
                  }}
                >
                  Edit
                </button>
              )}
            </div>
          </li>
        ))}
      </ul>
      {cursor && (
        <button
          className="load-more"
          disabled={disabled || busy || !!historyRecord}
          onClick={() =>
            perform(async (signal) => {
              const page = await api<Page<DirectoryEntry>>(
                `${base}?includeArchived=${archived}&after=${cursor}`,
                { signal },
              );
              if (!signal.aborted) {
                setItems((previous) => [
                  ...new Map(
                    [...previous, ...page.items].map((x) => [x.id, x]),
                  ).values(),
                ]);
                setCursor(page.nextCursor);
              }
            })
          }
        >
          Load more {resource}
        </button>
      )}
      {historyRecord && (
        <DirectoryHistory
          key={`${base}/${historyRecord.id}`}
          base={base}
          record={historyRecord}
          canEdit={canEdit}
          disabled={disabled || busy || loading || editing}
          onBusyChange={onBusyChange}
          onClose={() => setHistoryRecord(null)}
          onRestored={(saved) => {
            setHistoryRecord(saved);
            setItems((previous) =>
              [...previous.filter((x) => x.id !== saved.id), saved].filter(
                (x) => archived || !x.isArchived,
              ),
            );
          }}
          onDenied={(message) => {
            setItems([]);
            setCursor(null);
            setHistoryRecord(null);
            setSelected(null);
            setEditing(false);
            setError(message);
          }}
        />
      )}
      {editing && (
        <form
          className="directory-form"
          aria-label={`Edit ${singular} record`}
          onSubmit={save}
        >
          <h3>
            {selected
              ? `Edit ${singular} · version ${selected.version}`
              : `New ${singular}`}
          </h3>
          <label>
            {singular === "location" ? "Location name" : "Contact name"}
            <input
              required
              maxLength={160}
              value={form.name}
              disabled={disabled || busy}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
            />
          </label>
          <label>
            {singular === "location" ? "Address" : "Role or department"}
            <input
              maxLength={500}
              value={form.details}
              disabled={disabled || busy}
              onChange={(e) => setForm({ ...form, details: e.target.value })}
            />
          </label>
          {singular === "contact" && (
            <>
              <label>
                Email
                <input
                  type="email"
                  maxLength={320}
                  value={form.email}
                  disabled={disabled || busy}
                  onChange={(e) => setForm({ ...form, email: e.target.value })}
                />
              </label>
              <label>
                Phone
                <input
                  type="tel"
                  maxLength={80}
                  value={form.phone}
                  disabled={disabled || busy}
                  onChange={(e) => setForm({ ...form, phone: e.target.value })}
                />
              </label>
            </>
          )}
          <label>
            Notes
            <textarea
              maxLength={4000}
              rows={4}
              value={form.notes}
              disabled={disabled || busy}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
            />
          </label>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={form.isArchived}
              disabled={disabled || busy}
              onChange={(e) =>
                setForm({ ...form, isArchived: e.target.checked })
              }
            />
            Archived — retained and hidden from active lists
          </label>
          <div className="actions">
            <button
              className="primary"
              disabled={disabled || busy || conflict}
              type="submit"
            >
              Save {singular}
            </button>
            <button
              className="secondary"
              disabled={disabled || busy}
              type="button"
              onClick={() => setEditing(false)}
            >
              Cancel directory edit
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
