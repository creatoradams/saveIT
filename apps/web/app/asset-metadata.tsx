"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { Values, type FieldValues } from "./schema-fields";
import AssetComparisonPanel from "./asset-comparison";
import {
  api,
  ApiError,
  type Asset,
  type AssetRevision,
  type DirectoryEntry,
  type Page,
} from "../lib/api";

export default function AssetMetadata({
  asset,
  organizationId,
  canEdit,
  disabled,
  onBusyChange,
  onUpdated,
}: {
  asset: Asset;
  organizationId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onUpdated: (asset: Asset) => void;
}) {
  const [editing, setEditing] = useState(false),
    [busy, setBusy] = useState(false);
  const [name, setName] = useState(asset.name),
    [kind, setKind] = useState(asset.kind),
    [location, setLocation] = useState(asset.locationId || "");
  const [locations, setLocations] = useState<DirectoryEntry[]>([]),
    [locationCursor, setLocationCursor] = useState<string | null>(null);
  const [placement, setPlacement] = useState<DirectoryEntry | null>(null);
  const [placementError, setPlacementError] = useState("");
  const [placementRetry, setPlacementRetry] = useState(0);
  const [history, setHistory] = useState<AssetRevision[]>([]),
    [historyCursor, setHistoryCursor] = useState<string | null>(null);
  const [error, setError] = useState(""),
    [conflict, setConflict] = useState(false);
  const [source, setSource] = useState<AssetRevision | null>(null),
    [reason, setReason] = useState("");
  const [comparing, setComparing] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null),
    request = useRef<AbortController | null>(null);
  const base = `/organizations/${organizationId}`,
    path = `${base}/assets/${asset.id}`;
  useEffect(() => {
    const abort = new AbortController();
    setPlacement(null);
    setPlacementError("");
    if (asset.locationId)
      api<DirectoryEntry>(`${base}/locations/${asset.locationId}`, {
        signal: abort.signal,
      })
        .then((row) => {
          if (!abort.signal.aborted) setPlacement(row);
        })
        .catch((err) => {
          if (!abort.signal.aborted)
            setPlacementError(
              err instanceof Error
                ? err.message
                : "Location could not be loaded.",
            );
        });
    return () => {
      abort.abort();
    };
  }, [asset.locationId, asset.version, base, placementRetry]);
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  useEffect(() => {
    if (source && dialog.current && !dialog.current.open)
      dialog.current.showModal();
    else if (!source && dialog.current?.open) dialog.current.close();
  }, [source]);
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (disabled || busy) return;
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    try {
      await action(abort.signal);
    } catch (err) {
      if (!abort.signal.aborted) {
        setError(
          err instanceof Error
            ? err.message
            : "The asset could not be updated.",
        );
        setConflict(err instanceof ApiError && err.status === 409);
        if (err instanceof ApiError && [401, 403, 404].includes(err.status)) {
          setSource(null);
          setHistory([]);
          setLocations([]);
          setPlacement(null);
          setEditing(false);
          setComparing(false);
        }
      }
    } finally {
      if (!abort.signal.aborted) {
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  async function reload() {
    setSource(null);
    await perform(async (signal) => {
      const row = await api<Asset>(path, { signal });
      if (!signal.aborted) {
        setName(row.name);
        setKind(row.kind);
        setLocation(row.locationId || "");
        setEditing(false);
        setHistory([]);
        setConflict(false);
        onUpdated(row);
      }
    });
  }
  async function edit() {
    await perform(async (signal) => {
      const page = await api<Page<DirectoryEntry>>(`${base}/locations`, {
        signal,
      });
      if (!signal.aborted) {
        setLocations(page.items);
        setLocationCursor(page.nextCursor);
        setEditing(true);
        setConflict(false);
      }
    });
  }
  async function revisions(after: string | null = null) {
    await perform(async (signal) => {
      try {
        const page = await api<Page<AssetRevision>>(
          `${path}/revisions${after ? `?before=${after}` : ""}`,
          { signal },
        );
        if (!signal.aborted) {
          setHistory((previous) =>
            after ? [...previous, ...page.items] : page.items,
          );
          setHistoryCursor(page.nextCursor);
        }
      } catch (err) {
        if (!signal.aborted) {
          setHistory([]);
          setHistoryCursor(null);
          setSource(null);
          setComparing(false);
        }
        throw err;
      }
    });
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await perform(async (signal) => {
      const row = await api<Asset>(path, {
        method: "PUT",
        signal,
        body: JSON.stringify({
          name,
          kind,
          locationId: location || null,
          expectedVersion: asset.version ?? 1,
        }),
      });
      if (!signal.aborted) onUpdated(row);
    });
  }
  async function restore(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!source || conflict) return;
    await perform(async (signal) => {
      const row = await api<Asset>(`${path}/restore`, {
        method: "POST",
        signal,
        body: JSON.stringify({
          sourceVersion: source.version,
          expectedVersion: asset.version ?? 1,
          reason,
        }),
      });
      if (!signal.aborted) {
        setSource(null);
        onUpdated(row);
      }
    });
  }
  return (
    <section
      className="asset-metadata"
      aria-label="Asset placement and history"
    >
      <p className="subtle">
        Asset version {asset.version ?? 1} ·{" "}
        {placement
          ? `Location: ${placement.name}${placement.isArchived ? " (archived)" : ""}`
          : asset.locationId
            ? placementError
              ? "Location unavailable"
              : "Loading location…"
            : "No location assigned"}
      </p>
      {placementError && (
        <div className="message error" role="alert">
          {placementError}
          <button
            disabled={disabled || busy}
            onClick={() => setPlacementRetry((x) => x + 1)}
          >
            Retry asset location
          </button>
        </div>
      )}
      {!source && error && (
        <div className="message error" role="alert">
          {error}
          <button disabled={disabled || busy} onClick={reload}>
            Reload asset details
          </button>
        </div>
      )}
      <div className="actions">
        {canEdit && !editing && (
          <button
            className="secondary"
            disabled={disabled || busy}
            onClick={edit}
          >
            Edit asset details
          </button>
        )}
        <button
          className="text-button"
          disabled={disabled || busy}
          onClick={() => revisions()}
        >
          View asset history
        </button>
      </div>
      {editing && (
        <form
          className="directory-form"
          aria-label="Edit asset details"
          onSubmit={save}
        >
          <label>
            Asset name
            <input
              required
              maxLength={160}
              value={name}
              disabled={disabled || busy}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label>
            Asset type
            <input
              required
              maxLength={80}
              value={kind}
              disabled={disabled || busy}
              onChange={(e) => setKind(e.target.value)}
            />
          </label>
          <label>
            Asset location
            <select
              value={location}
              disabled={disabled || busy}
              onChange={(e) => setLocation(e.target.value)}
            >
              <option value="">Unassigned</option>
              {asset.locationId &&
                !placement &&
                !locations.some((x) => x.id === asset.locationId) && (
                  <option value={asset.locationId}>
                    Current location ({asset.locationId.slice(0, 8)})
                  </option>
                )}
              {placement && !locations.some((x) => x.id === placement.id) && (
                <option value={placement.id}>
                  {placement.name}
                  {placement.isArchived ? " (archived)" : ""}
                </option>
              )}
              {locations.map((row) => (
                <option key={row.id} value={row.id}>
                  {row.name} · {row.id.slice(0, 8)}
                </option>
              ))}
            </select>
          </label>
          {locationCursor && (
            <button
              className="secondary"
              type="button"
              disabled={disabled || busy}
              onClick={() =>
                perform(async (signal) => {
                  const page = await api<Page<DirectoryEntry>>(
                    `${base}/locations?after=${locationCursor}`,
                    { signal },
                  );
                  if (!signal.aborted) {
                    setLocations((previous) => [...previous, ...page.items]);
                    setLocationCursor(page.nextCursor);
                  }
                })
              }
            >
              Load more locations
            </button>
          )}
          <div className="actions">
            <button
              className="primary"
              type="submit"
              disabled={disabled || busy || conflict}
            >
              Save asset details
            </button>
            <button
              className="secondary"
              type="button"
              disabled={disabled || busy}
              onClick={() => setEditing(false)}
            >
              Cancel asset edit
            </button>
          </div>
        </form>
      )}
      {!!history.length && (
        <section className="revisions" aria-label="Asset revisions">
          <h3>Asset history</h3>
          <button
            className="secondary"
            disabled={disabled || busy || editing || !!source}
            onClick={() => setComparing(true)}
          >
            Compare asset revisions
          </button>
          {comparing && (
            <AssetComparisonPanel
              organizationId={organizationId}
              assetId={asset.id}
              assetVersion={Number(asset.version ?? 1)}
              disabled={disabled || busy || editing || !!source}
              onBusyChange={onBusyChange}
              onReload={reload}
              onClose={() => setComparing(false)}
            />
          )}
          {history.map((row) => (
            <details key={row.version}>
              <summary>
                Asset revision {row.version} · {row.actor}
              </summary>
              <p>
                {row.name} · {row.kind}
              </p>
              <p className="comparison-value">
                Snapshot location:{" "}
                {row.locationId
                  ? (row.locationName ?? "Historical name unavailable")
                  : "Unassigned"}
              </p>
              {row.locationId && <p>Location ID: {row.locationId}</p>}
              <p>
                Template:{" "}
                {row.assetTypeId
                  ? `${row.assetTypeId} · version ${row.assetTypeVersion}`
                  : "Unassigned"}
              </p>
              <Values values={(row.fields as FieldValues) ?? {}} />
              {row.restoredFromVersion && (
                <p>
                  Restored from revision {row.restoredFromVersion} ·{" "}
                  {row.restoreReason}
                </p>
              )}
              {canEdit && Number(row.version) < Number(asset.version ?? 1) && (
                <button
                  className="secondary"
                  disabled={disabled || busy}
                  onClick={() => {
                    setReason("");
                    setConflict(false);
                    setError("");
                    setSource(row);
                  }}
                >
                  Restore asset revision {row.version}…
                </button>
              )}
            </details>
          ))}
          {historyCursor && (
            <button
              className="load-more"
              disabled={disabled || busy}
              onClick={() => revisions(historyCursor)}
            >
              Older asset revisions
            </button>
          )}
        </section>
      )}
      <dialog
        ref={dialog}
        className="restore-dialog"
        aria-labelledby="asset-restore-title"
        onCancel={(e) => {
          if (busy || disabled) e.preventDefault();
          else setSource(null);
        }}
        onClose={() => setSource(null)}
      >
        {source && (
          <form onSubmit={restore}>
            <h2 id="asset-restore-title">
              Restore asset revision {source.version}?
            </h2>
            <p>
              Create version {Number(asset.version ?? 1) + 1} with {source.name}
              , type {source.kind}, the historical location, template version
              and custom values. Documents and relationships remain linked to
              this asset; earlier history is preserved.
            </p>
            <p className="comparison-value">
              Snapshot location:{" "}
              {source.locationId
                ? (source.locationName ?? "Historical name unavailable")
                : "Unassigned"}
              {source.locationId && <> · ID: {source.locationId}</>}. The
              current location keeps its current name. Unavailable historical
              names remain unavailable.
            </p>
            <label>
              Reason for asset restore
              <textarea
                required
                minLength={3}
                maxLength={500}
                rows={3}
                value={reason}
                autoFocus
                disabled={disabled || busy}
                onChange={(e) => setReason(e.target.value)}
              />
            </label>
            {error && (
              <div className="message error" role="alert">
                {error}
                {conflict && (
                  <button
                    type="button"
                    disabled={disabled || busy}
                    onClick={reload}
                  >
                    Reload asset details
                  </button>
                )}
              </div>
            )}
            <div className="actions">
              <button
                className="primary"
                disabled={
                  disabled || busy || conflict || reason.trim().length < 3
                }
                type="submit"
              >
                Create restored asset version
              </button>
              <button
                className="secondary"
                disabled={disabled || busy}
                type="button"
                onClick={() => setSource(null)}
              >
                Cancel asset restore
              </button>
            </div>
          </form>
        )}
      </dialog>
    </section>
  );
}
