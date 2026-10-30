"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import DirectoryComparisonPanel from "./directory-comparison";
import {
  api,
  ApiError,
  type DirectoryEntry,
  type DirectoryRevision,
  type DirectoryRevisionSummary,
  type Page,
} from "../lib/api";

export default function DirectoryHistory({
  base,
  record,
  canEdit,
  disabled,
  onBusyChange,
  onRestored,
  onDenied,
  onClose,
}: {
  base: string;
  record: DirectoryEntry;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (value: boolean) => void;
  onRestored: (value: DirectoryEntry) => void;
  onDenied: (message: string) => void;
  onClose: () => void;
}) {
  const path = `${base}/${record.id}`,
    singular = base.endsWith("contacts") ? "contact" : "location";
  const [current, setCurrent] = useState(record),
    [rows, setRows] = useState<DirectoryRevisionSummary[]>([]);
  const [cursor, setCursor] = useState<string | null>(null),
    [snapshot, setSnapshot] = useState<DirectoryRevision | null>(null);
  const [sourceVersion, setSourceVersion] = useState<number | null>(null),
    [reason, setReason] = useState("");
  const [loading, setLoading] = useState(true),
    [busy, setBusy] = useState(false),
    [refresh, setRefresh] = useState(0);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [conflict, setConflict] = useState(false),
    [confirming, setConfirming] = useState(false);
  const request = useRef<AbortController | null>(null),
    dialog = useRef<HTMLDialogElement>(null);
  const restoreButton = useRef<HTMLButtonElement>(null),
    closeButton = useRef<HTMLButtonElement>(null);
  const focusAfterClose = useRef(false);
  const [comparing, setComparing] = useState(false);
  const comparisonButton = useRef<HTMLButtonElement>(null),
    focusAfterComparison = useRef(false);
  const locked = disabled || busy || loading;
  useEffect(() => {
    const abort = new AbortController();
    setLoading(true);
    setRows([]);
    setCursor(null);
    setSnapshot(null);
    setComparing(false);
    setError("");
    api<Page<DirectoryRevisionSummary>>(`${path}/revisions`, {
      signal: abort.signal,
    })
      .then((page) => {
        if (!abort.signal.aborted) {
          setRows(page.items);
          setCursor(page.nextCursor);
        }
      })
      .catch((e) => {
        if (!abort.signal.aborted) {
          setRows([]);
          setCursor(null);
          setSnapshot(null);
          setError(e.message);
          if (e instanceof ApiError && [401, 403, 404].includes(e.status))
            onDenied(e.message);
        }
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [path, refresh]);
  useEffect(
    () => () => {
      if (request.current) {
        request.current.abort();
        onBusyChange(false);
      }
    },
    [onBusyChange],
  );
  useEffect(() => {
    if (confirming) dialog.current?.showModal();
    else dialog.current?.close();
  }, [confirming]);
  useEffect(() => {
    if (!confirming && !locked && focusAfterClose.current) {
      focusAfterClose.current = false;
      (restoreButton.current?.isConnected
        ? restoreButton.current
        : closeButton.current
      )?.focus();
    }
  }, [confirming, locked]);
  useEffect(() => {
    if (!comparing && !locked && focusAfterComparison.current) {
      focusAfterComparison.current = false;
      comparisonButton.current?.focus();
    }
  }, [comparing, locked]);
  function closeConfirmation() {
    focusAfterClose.current = true;
    setConfirming(false);
    dialog.current?.close();
  }
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (locked) return;
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    setNotice("");
    try {
      await action(abort.signal);
    } catch (e) {
      if (!abort.signal.aborted) {
        const message =
          e instanceof Error ? e.message : "Directory history is unavailable.";
        setError(message);
        setRows([]);
        setCursor(null);
        setSnapshot(null);
        setComparing(false);
        if (e instanceof ApiError && e.status === 409) setConflict(true);
        if (e instanceof ApiError && [401, 403, 404].includes(e.status))
          onDenied(message);
      }
    } finally {
      if (!abort.signal.aborted) {
        request.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  async function inspect(version: number) {
    setComparing(false);
    setSnapshot(null);
    await perform(async (signal) => {
      const head = await api<DirectoryEntry>(path, { signal });
      const historical = await api<DirectoryRevision>(
        `${path}/revisions/${version}`,
        { signal },
      );
      if (signal.aborted) return;
      setCurrent(head);
      setSnapshot(historical);
      setSourceVersion(version);
      setReason("");
      setConflict(false);
    });
  }
  async function reloadCurrent() {
    setComparing(false);
    await perform(async (signal) => {
      const head = await api<DirectoryEntry>(path, { signal });
      const historical =
        sourceVersion == null
          ? null
          : await api<DirectoryRevision>(`${path}/revisions/${sourceVersion}`, {
              signal,
            });
      const history = await api<Page<DirectoryRevisionSummary>>(
        `${path}/revisions`,
        { signal },
      );
      if (signal.aborted) return;
      setCurrent(head);
      setSnapshot(historical);
      setRows(history.items);
      setCursor(history.nextCursor);
      onRestored(head);
      setConflict(false);
      closeConfirmation();
      setNotice(
        "Current record reloaded. Review the historical fields and confirm again; your reason is retained.",
      );
    });
  }
  async function restore(e: FormEvent) {
    e.preventDefault();
    if (!snapshot || conflict || !canEdit || !confirming) return;
    await perform(async (signal) => {
      const saved = await api<DirectoryEntry>(`${path}/restore`, {
        method: "POST",
        signal,
        body: JSON.stringify({
          sourceVersion: snapshot.version,
          expectedVersion: current.version,
          reason,
        }),
      });
      if (signal.aborted) return;
      setCurrent(saved);
      onRestored(saved);
      setSnapshot(null);
      setSourceVersion(null);
      setReason("");
      setConflict(false);
      setLoading(true);
      closeConfirmation();
      setRefresh((x) => x + 1);
      setNotice(
        `${singular === "location" ? "Location" : "Contact"} restored as version ${saved.version}.`,
      );
    });
  }
  const feedback = error && (
    <div className="message error" role="alert">
      {error}
      {conflict && (
        <p>
          The record changed. Your reason is retained. Reload the current record
          and review before confirming again.
        </p>
      )}
      <button
        type="button"
        className="secondary"
        disabled={locked}
        onClick={() => void reloadCurrent()}
      >
        Reload current {singular}
      </button>
      {!confirming && (
        <button
          type="button"
          className="secondary"
          disabled={locked}
          onClick={() => setRefresh((x) => x + 1)}
        >
          Retry directory history
        </button>
      )}
    </div>
  );
  return (
    <section
      className="directory-history"
      aria-label="Directory revision history"
      aria-busy={busy || loading}
    >
      <div className="actions">
        <h3>{singular === "location" ? "Location" : "Contact"} history</h3>
        <button
          ref={closeButton}
          className="text-button"
          disabled={locked}
          onClick={onClose}
        >
          Close directory history
        </button>
      </div>
      <p className="subtle">
        Current version {current.version}. Migration snapshots start at the
        version available when history was introduced; earlier changes may be
        unavailable.
      </p>
      {(loading || busy) && <p role="status">Loading directory history…</p>}
      {!confirming && feedback}
      {notice && (
        <p role="status" className="message success">
          {notice}
        </p>
      )}
      {!loading && !rows.length && !error && <p>No recorded revisions.</p>}
      {!!rows.length && (
        <button
          ref={comparisonButton}
          className="secondary"
          disabled={locked || confirming}
          onClick={() => setComparing(true)}
        >
          Compare directory revisions
        </button>
      )}
      {comparing && (
        <DirectoryComparisonPanel
          path={path}
          currentVersion={Number(current.version)}
          disabled={locked || confirming}
          onBusyChange={onBusyChange}
          onDenied={onDenied}
          onClose={() => {
            focusAfterComparison.current = true;
            setComparing(false);
          }}
        />
      )}
      <ol className="directory-history-list" aria-label="Directory revisions">
        {rows.map((row) => (
          <li key={row.version}>
            <button
              className="secondary"
              disabled={locked || confirming}
              onClick={() => void inspect(Number(row.version))}
            >
              Review directory revision {row.version}
            </button>
            <span className="comparison-value">
              {row.name} · {row.isArchived ? "Archived" : "Active"}
            </span>
            <small>
              {row.actor} · {new Date(row.createdAt).toLocaleString()}
              {row.restoredFromVersion != null
                ? ` · Restored from version ${row.restoredFromVersion}`
                : ""}
            </small>
          </li>
        ))}
      </ol>
      {cursor && (
        <button
          className="load-more"
          disabled={locked || confirming}
          onClick={() =>
            void perform(async (signal) => {
              const page = await api<Page<DirectoryRevisionSummary>>(
                `${path}/revisions?before=${cursor}`,
                { signal },
              );
              if (!signal.aborted) {
                setRows((previous) => [
                  ...new Map(
                    [...previous, ...page.items].map((x) => [x.version, x]),
                  ).values(),
                ]);
                setCursor(page.nextCursor);
              }
            })
          }
        >
          Load more directory revisions
        </button>
      )}
      {snapshot && (
        <section
          className="directory-snapshot"
          aria-label="Historical directory snapshot"
        >
          <h4>Directory revision {snapshot.version}</h4>
          <dl>
            <dt>Name</dt>
            <dd>{snapshot.name}</dd>
            <dt>
              {singular === "location" ? "Address" : "Role or department"}
            </dt>
            <dd>{snapshot.details || "Empty"}</dd>
            {singular === "contact" && (
              <>
                <dt>Email</dt>
                <dd>{snapshot.email || "Empty"}</dd>
                <dt>Phone</dt>
                <dd>{snapshot.phone || "Empty"}</dd>
              </>
            )}
            <dt>Notes</dt>
            <dd className="document-text">{snapshot.notes || "Empty"}</dd>
            <dt>Lifecycle</dt>
            <dd>{snapshot.isArchived ? "Archived" : "Active"}</dd>
            <dt>Recorded by</dt>
            <dd>
              {snapshot.actor} · {new Date(snapshot.createdAt).toLocaleString()}
            </dd>
            {snapshot.restoredFromVersion != null && (
              <>
                <dt>Restored from</dt>
                <dd>Version {snapshot.restoredFromVersion}</dd>
                <dt>Restore reason</dt>
                <dd className="document-text">{snapshot.restoreReason}</dd>
              </>
            )}
          </dl>
          {canEdit && Number(snapshot.version) < Number(current.version) && (
            <button
              ref={restoreButton}
              className="secondary"
              disabled={locked || conflict}
              onClick={() => {
                setComparing(false);
                setConfirming(true);
              }}
            >
              Restore directory revision {snapshot.version}
            </button>
          )}
        </section>
      )}
      <dialog
        ref={dialog}
        className="restore-dialog"
        aria-labelledby="directory-restore-title"
        onCancel={(e) => {
          e.preventDefault();
          if (!locked) closeConfirmation();
        }}
      >
        <form onSubmit={restore}>
          <h3 id="directory-restore-title">Confirm {singular} restoration</h3>
          <p>
            Copy revision {sourceVersion} into a new version after{" "}
            {current.version}.{" "}
            {snapshot
              ? `All historical fields and the ${snapshot.isArchived ? "Archived" : "Active"} state shown in the review will be copied.`
              : "Reload the historical snapshot before confirming."}{" "}
            Archived records disappear from active lists; linked assets keep
            their IDs and placement.
          </p>
          <p>
            Existing snapshots remain unchanged. This recovers an earlier record
            version.
          </p>
          <label>
            Reason for directory restoration
            <textarea
              required
              minLength={3}
              maxLength={500}
              rows={3}
              value={reason}
              disabled={locked}
              onChange={(e) => setReason(e.target.value)}
            />
          </label>
          {confirming && feedback}
          <div className="actions">
            <button
              className="primary"
              type="submit"
              disabled={
                locked || conflict || !snapshot || reason.trim().length < 3
              }
            >
              Confirm directory restore
            </button>
            <button
              type="button"
              className="secondary"
              disabled={locked}
              onClick={closeConfirmation}
            >
              Cancel directory restore
            </button>
          </div>
        </form>
      </dialog>
    </section>
  );
}
