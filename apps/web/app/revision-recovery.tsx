"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type Doc, type DocumentComparison } from "../lib/api";

export default function RevisionRecovery({
  document,
  organizationId,
  canEdit,
  selection,
  onBusyChange,
  onRestored,
  onReload,
}: {
  document: Doc;
  organizationId: string;
  canEdit: boolean;
  selection: { version: number; nonce: number } | null;
  onBusyChange: (value: boolean) => void;
  onRestored: (document: Doc, sourceVersion: number) => void;
  onReload: () => void;
}) {
  const currentVersion = Number(document.version);
  const [from, setFrom] = useState(Math.max(1, currentVersion - 1));
  const [to, setTo] = useState(currentVersion);
  const [comparison, setComparison] = useState<DocumentComparison | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [conflict, setConflict] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState("");
  const request = useRef<AbortController | null>(null);
  const dialog = useRef<HTMLDialogElement | null>(null);
  const path = `/organizations/${organizationId}/documents/${document.id}`;
  const current =
    comparison?.currentVersion === document.version &&
    comparison.to.version === document.version;
  const canRestore =
    !!comparison && current && comparison.from.version < document.version;
  const date = (value: string) => new Date(value).toLocaleString();

  function markBusy(value: boolean) {
    setBusy(value);
    onBusyChange(value);
  }
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  useEffect(() => {
    if (selection) compare(selection.version, currentVersion);
  }, [selection]);
  useEffect(() => {
    if (confirming && dialog.current && !dialog.current.open)
      dialog.current.showModal();
    else if (!confirming && dialog.current?.open) dialog.current.close();
  }, [confirming]);

  async function compare(source: number, target: number, offset = 0) {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setFrom(source);
    setTo(target);
    setError("");
    setConflict(false);
    markBusy(true);
    try {
      const result = await api<DocumentComparison>(
        `${path}/compare?fromVersion=${source}&toVersion=${target}&offset=${offset}`,
        { signal: abort.signal },
      );
      if (!abort.signal.aborted) setComparison(result);
    } catch (err) {
      if (!abort.signal.aborted) {
        setComparison(null);
        setError(
          err instanceof Error
            ? err.message
            : "The comparison could not be loaded.",
        );
      }
    } finally {
      if (!abort.signal.aborted) markBusy(false);
    }
  }
  function submitComparison(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    compare(from, to);
  }
  async function restore(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!comparison || !canRestore || conflict) return;
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    markBusy(true);
    setError("");
    try {
      const restored = await api<Doc>(`${path}/restore`, {
        method: "POST",
        signal: abort.signal,
        body: JSON.stringify({
          sourceVersion: comparison.from.version,
          expectedVersion: document.version,
          reason,
        }),
      });
      if (!abort.signal.aborted) {
        setConfirming(false);
        onRestored(restored, Number(comparison.from.version));
      }
    } catch (err) {
      if (!abort.signal.aborted) {
        setError(
          err instanceof Error
            ? err.message
            : "The restore could not be completed. Reload the document before retrying.",
        );
        setConflict(err instanceof ApiError && err.status === 409);
        if (err instanceof ApiError && [401, 403, 404].includes(err.status)) {
          setConfirming(false);
          setComparison(null);
        }
      }
    } finally {
      if (!abort.signal.aborted) markBusy(false);
    }
  }
  function reload() {
    setConfirming(false);
    onReload();
  }

  return (
    <div className="revision-recovery">
      <form
        className="comparison-form"
        aria-label="Compare document revisions"
        onSubmit={submitComparison}
      >
        <label>
          From revision
          <input
            type="number"
            min={1}
            max={document.version}
            required
            value={from}
            disabled={busy}
            onChange={(event) => {
              setFrom(Number(event.target.value));
              setComparison(null);
              setError("");
            }}
          />
        </label>
        <label>
          To revision
          <input
            type="number"
            min={1}
            max={document.version}
            required
            value={to}
            disabled={busy}
            onChange={(event) => {
              setTo(Number(event.target.value));
              setComparison(null);
              setError("");
            }}
          />
        </label>
        <button className="secondary" disabled={busy} type="submit">
          Compare revisions
        </button>
      </form>
      {busy && (
        <p role="status" className="subtle">
          {confirming
            ? "Creating the restored revision…"
            : "Loading comparison…"}
        </p>
      )}
      {error && !confirming && (
        <div className="message error" role="alert">
          {error}
          <button onClick={reload} disabled={busy}>
            Reload current document
          </button>
        </div>
      )}
      {comparison && (
        <section className="comparison" aria-label="Revision comparison">
          <h4>
            Revision {comparison.from.version} → Revision{" "}
            {comparison.to.version}
          </h4>
          <div className="comparison-summaries">
            <div>
              <strong>Before · {comparison.from.title}</strong>
              <small>
                {comparison.from.actor} · {date(comparison.from.createdAt)}
              </small>
            </div>
            <div>
              <strong>After · {comparison.to.title}</strong>
              <small>
                {comparison.to.actor} · {date(comparison.to.createdAt)}
              </small>
            </div>
          </div>
          <p className="comparison-counts">
            {comparison.addedLines} lines added · {comparison.removedLines}{" "}
            lines removed{comparison.titleChanged ? " · Title changed" : ""}
          </p>
          {!comparison.contentChanged && !comparison.titleChanged && (
            <p className="subtle">
              These revisions have identical title and content.
            </p>
          )}
          {comparison.lineEndingsOnly && (
            <p className="subtle">Only the content’s line endings changed.</p>
          )}
          {comparison.simplified && (
            <p className="subtle">
              Large change: showing replaced sections in full.
            </p>
          )}
          <div className="table-scroll diff-scroll">
            <table className="diff-table">
              <caption className="sr-only">
                Changes from revision {comparison.from.version} to revision{" "}
                {comparison.to.version}
              </caption>
              <thead>
                <tr>
                  <th>Before</th>
                  <th>After</th>
                  <th>Change</th>
                  <th>Text</th>
                </tr>
              </thead>
              <tbody>
                {comparison.lines.map((line, index) => (
                  <tr
                    className={`diff-${line.kind}`}
                    key={`${comparison.offset}:${index}`}
                  >
                    <td>{line.oldLine ?? "—"}</td>
                    <td>{line.newLine ?? "—"}</td>
                    <td>
                      {line.kind === "added"
                        ? "Added"
                        : line.kind === "removed"
                          ? "Removed"
                          : "Unchanged"}
                    </td>
                    <td className="diff-text">{line.text || " "}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="comparison-pagination">
            <button
              className="secondary"
              disabled={busy || comparison.offset === 0}
              onClick={() =>
                compare(
                  Number(comparison.from.version),
                  Number(comparison.to.version),
                  Math.max(0, Number(comparison.offset) - 200),
                )
              }
            >
              Previous changes
            </button>
            <span>
              {comparison.totalRows
                ? `${Number(comparison.offset) + 1}–${Math.min(Number(comparison.offset) + comparison.lines.length, Number(comparison.totalRows))}`
                : "0"}{" "}
              of {comparison.totalRows} rows
            </span>
            <button
              className="secondary"
              disabled={busy || comparison.nextOffset === null}
              onClick={() =>
                compare(
                  Number(comparison.from.version),
                  Number(comparison.to.version),
                  Number(comparison.nextOffset),
                )
              }
            >
              Next changes
            </button>
          </div>
          {!current && (
            <div className="message error" role="status">
              Compare with the current revision before restoring. Reload if the
              document has changed.
              <button disabled={busy} onClick={reload}>
                Reload current document
              </button>
            </div>
          )}
          {canEdit && (
            <button
              className="primary"
              disabled={busy || !canRestore}
              onClick={() => {
                setReason("");
                setError("");
                setConflict(false);
                setConfirming(true);
              }}
            >
              Restore revision {comparison.from.version}…
            </button>
          )}
          <dialog
            ref={dialog}
            className="restore-dialog"
            aria-labelledby="restore-heading"
            onCancel={(event) => {
              if (busy) event.preventDefault();
              else setConfirming(false);
            }}
            onClose={() => setConfirming(false)}
          >
            <form onSubmit={restore}>
              <h2 id="restore-heading">
                Restore revision {comparison.from.version}?
              </h2>
              <p>
                This will create <strong>revision {currentVersion + 1}</strong>{" "}
                using the title and content from revision{" "}
                {comparison.from.version}. All existing revisions will remain in
                history.
              </p>
              <dl>
                <dt>Title to restore</dt>
                <dd>{comparison.from.title}</dd>
                <dt>Current title</dt>
                <dd>{comparison.to.title}</dd>
              </dl>
              <label>
                Reason for restore
                <textarea
                  required
                  minLength={3}
                  maxLength={500}
                  rows={3}
                  value={reason}
                  autoFocus
                  disabled={busy}
                  onChange={(event) => setReason(event.target.value)}
                />
              </label>
              <p className="subtle">
                Your reason and the source revision will be recorded in revision
                history.
              </p>
              {error && (
                <div className="message error" role="alert">
                  {error}
                  {conflict && (
                    <button type="button" disabled={busy} onClick={reload}>
                      Reload current document
                    </button>
                  )}
                </div>
              )}
              <div className="actions">
                <button
                  type="submit"
                  className="primary"
                  disabled={busy || conflict || reason.trim().length < 3}
                >
                  Create restored revision
                </button>
                <button
                  type="button"
                  className="secondary"
                  disabled={busy}
                  onClick={() => setConfirming(false)}
                >
                  Cancel restore
                </button>
              </div>
            </form>
          </dialog>
        </section>
      )}
    </div>
  );
}
