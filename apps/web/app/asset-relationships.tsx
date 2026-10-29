"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  ApiError,
  type Asset,
  type AssetRelationship,
  type Page,
} from "../lib/api";

export default function AssetRelationships({
  organizationId,
  assetId,
  canEdit,
  disabled,
  onBusyChange,
  onNavigate,
}: {
  organizationId: string;
  assetId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onNavigate: (id: string) => void;
}) {
  const base = `/organizations/${organizationId}/assets`;
  const path = `${base}/${assetId}/relationships`;
  const [items, setItems] = useState<AssetRelationship[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [adding, setAdding] = useState(false);
  const [targets, setTargets] = useState<Asset[]>([]);
  const [targetCursor, setTargetCursor] = useState<string | null>(null);
  const [targetId, setTargetId] = useState("");
  const [kind, setKind] = useState("DependsOn");
  const [removing, setRemoving] = useState<AssetRelationship | null>(null);
  const [conflict, setConflict] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const request = useRef<AbortController | null>(null);
  const label = (edge: AssetRelationship) =>
    edge.kind === "DependsOn"
      ? edge.source.id === assetId
        ? "Depends on"
        : "Required by"
      : edge.source.id === assetId
        ? "Contains"
        : "Part of";
  const other = (edge: AssetRelationship) =>
    edge.source.id === assetId ? edge.target : edge.source;
  const message = (err: unknown) =>
    err instanceof Error
      ? err.message
      : "Relationships could not be loaded. Try again.";
  function markBusy(value: boolean) {
    setBusy(value);
    onBusyChange(value);
  }

  useEffect(() => {
    const abort = new AbortController();
    request.current = abort;
    api<Page<AssetRelationship>>(path, { signal: abort.signal })
      .then((page) => {
        if (!abort.signal.aborted) {
          setItems(page.items);
          setCursor(page.nextCursor);
        }
      })
      .catch((err) => {
        if (!abort.signal.aborted) setError(message(err));
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => {
      request.current?.abort();
      onBusyChange(false);
    };
  }, [path, onBusyChange]);
  useEffect(() => {
    if (removing && dialog.current && !dialog.current.open)
      dialog.current.showModal();
    else if (!removing && dialog.current?.open) dialog.current.close();
  }, [removing]);

  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    markBusy(true);
    setError("");
    setNotice("");
    try {
      await action(abort.signal);
    } catch (err) {
      if (!abort.signal.aborted) {
        setError(message(err));
        setConflict(err instanceof ApiError && err.status === 409);
        if (err instanceof ApiError && [401, 403, 404].includes(err.status)) {
          setItems([]);
          setCursor(null);
          setTargets([]);
          setAdding(false);
          setRemoving(null);
        }
      }
    } finally {
      if (!abort.signal.aborted) {
        setLoading(false);
        markBusy(false);
      }
    }
  }
  async function loadMore(after: string | null = null) {
    setRemoving(null);
    setConflict(false);
    await perform(async (signal) => {
      const page = await api<Page<AssetRelationship>>(
        `${path}${after ? `?after=${after}` : ""}`,
        { signal },
      );
      if (signal.aborted) return;
      setItems((previous) =>
        after
          ? [
              ...new Map(
                [...previous, ...page.items].map((x) => [x.id, x]),
              ).values(),
            ]
          : page.items,
      );
      setCursor(page.nextCursor);
    });
  }
  async function loadTargets(after: string | null = null) {
    await perform(async (signal) => {
      const page = await api<Page<Asset>>(
        `${base}${after ? `?after=${after}` : ""}`,
        { signal },
      );
      if (signal.aborted) return;
      const available = page.items.filter((x) => x.id !== assetId);
      setTargets((previous) =>
        after
          ? [
              ...new Map(
                [...previous, ...available].map((x) => [x.id, x]),
              ).values(),
            ]
          : available,
      );
      setTargetCursor(page.nextCursor);
      setAdding(true);
    });
  }
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await perform(async (signal) => {
      await api<AssetRelationship>(path, {
        method: "POST",
        signal,
        body: JSON.stringify({ targetAssetId: targetId, kind }),
      });
      if (signal.aborted) return;
      // Close the form before reloading: a successful commit must never be presented as an unsaved duplicate.
      setAdding(false);
      setTargetId("");
      setTargets([]);
      setTargetCursor(null);
      setConflict(false);
      setItems([]);
      setCursor(null);
      setNotice("Relationship created. Both assets are preserved.");
      const page = await api<Page<AssetRelationship>>(path, { signal });
      if (!signal.aborted) {
        setItems(page.items);
        setCursor(page.nextCursor);
      }
    });
  }
  async function remove(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!removing || conflict) return;
    await perform(async (signal) => {
      await api(`${path}/${removing.id}/remove`, {
        method: "POST",
        signal,
        body: JSON.stringify({ expectedVersion: removing.version }),
      });
      if (signal.aborted) return;
      setItems((previous) => previous.filter((x) => x.id !== removing.id));
      setRemoving(null);
      setConflict(false);
      setNotice(
        "Relationship removed. Assets, documents and the audit trail are preserved.",
      );
    });
  }
  return (
    <section
      className="asset-relationships"
      aria-label="Asset relationships"
      aria-busy={loading || busy}
    >
      <div className="documents-heading">
        <h3>Relationships</h3>
        {canEdit && !adding && (
          <button
            className="text-button"
            disabled={disabled || busy || loading}
            onClick={() => {
              setTargetId("");
              setConflict(false);
              loadTargets();
            }}
          >
            ＋ Add relationship
          </button>
        )}
      </div>
      {loading && <p>Loading relationships…</p>}
      {notice && (
        <p className="message success" role="status">
          {notice}
        </p>
      )}
      {error && !removing && (
        <div role="alert" className="message error">
          {error}
          <button disabled={disabled || busy} onClick={() => loadMore()}>
            Reload relationships
          </button>
        </div>
      )}
      {!loading && !error && !items.length && (
        <p className="subtle">
          No relationships yet. Link assets to show dependencies or containment.
        </p>
      )}
      {!!items.length && (
        <ul className="relationship-list" aria-label="Linked assets">
          {items.map((edge) => (
            <li key={edge.id}>
              <div>
                <span className="relationship-label">{label(edge)}</span>
                <button
                  className="text-button"
                  disabled={disabled || busy}
                  onClick={() => onNavigate(other(edge).id)}
                  aria-label={`Open linked asset ${other(edge).name}`}
                >
                  {other(edge).name} <span aria-hidden="true">↗</span>
                </button>
                <small>
                  {other(edge).kind} · {other(edge).id.slice(0, 8)}
                </small>
              </div>
              {canEdit && (
                <button
                  className="secondary"
                  disabled={disabled || busy}
                  onClick={() => {
                    setError("");
                    setNotice("");
                    setConflict(false);
                    setRemoving(edge);
                  }}
                  aria-label={`Remove ${label(edge).toLowerCase()} relationship with ${other(edge).name}`}
                >
                  Remove
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {cursor && (
        <button
          className="load-more"
          disabled={disabled || busy}
          onClick={() => loadMore(cursor)}
        >
          Load more relationships
        </button>
      )}
      {adding && (
        <form
          className="relationship-form"
          aria-label="Create asset relationship"
          onSubmit={create}
        >
          <label>
            Relationship type
            <select
              value={kind}
              disabled={disabled || busy}
              onChange={(event) => {
                setKind(event.target.value);
                setConflict(false);
              }}
            >
              <option value="DependsOn">Depends on</option>
              <option value="Contains">Contains</option>
            </select>
          </label>
          <label>
            Linked asset
            <select
              required
              value={targetId}
              disabled={disabled || busy}
              onChange={(event) => {
                setTargetId(event.target.value);
                setConflict(false);
              }}
            >
              <option value="">Choose an asset in this organization</option>
              {targets.map((target) => (
                <option key={target.id} value={target.id}>
                  {target.name} · {target.kind} · {target.id.slice(0, 8)}
                </option>
              ))}
            </select>
          </label>
          {!targets.length && (
            <p className="subtle">
              No other assets on this page. Create another asset or load the
              next page.
            </p>
          )}
          {targetCursor && (
            <button
              type="button"
              className="secondary"
              disabled={disabled || busy}
              onClick={() => loadTargets(targetCursor)}
            >
              Load more link targets
            </button>
          )}
          <p className="subtle">
            The selected asset{" "}
            {kind === "DependsOn" ? "is required by" : "will be part of"} this
            asset. Links stay inside this organization.
          </p>
          <div className="actions">
            <button
              type="submit"
              className="primary"
              disabled={disabled || busy || !targetId || conflict}
            >
              Create relationship
            </button>
            <button
              type="button"
              className="secondary"
              disabled={disabled || busy}
              onClick={() => {
                setAdding(false);
                setTargets([]);
                setError("");
              }}
            >
              Cancel relationship
            </button>
          </div>
        </form>
      )}
      <dialog
        ref={dialog}
        className="restore-dialog"
        aria-labelledby="remove-relationship-heading"
        onCancel={(event) => {
          if (busy) event.preventDefault();
          else setRemoving(null);
        }}
        onClose={() => setRemoving(null)}
      >
        {removing && (
          <form onSubmit={remove}>
            <h2 id="remove-relationship-heading">Remove relationship?</h2>
            <p>
              {label(removing)} <strong>{other(removing).name}</strong>
            </p>
            <p>
              Both assets and their documents will remain. The relationship
              record and audit trail will be retained.
            </p>
            {error && (
              <div role="alert" className="message error">
                {error}
                {conflict && (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => loadMore()}
                  >
                    Reload relationships
                  </button>
                )}
              </div>
            )}
            <div className="actions">
              <button
                className="primary"
                type="submit"
                disabled={busy || conflict}
                autoFocus
              >
                Remove relationship
              </button>
              <button
                type="button"
                className="secondary"
                disabled={busy}
                onClick={() => setRemoving(null)}
              >
                Keep relationship
              </button>
            </div>
          </form>
        )}
      </dialog>
    </section>
  );
}
