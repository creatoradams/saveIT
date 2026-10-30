"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type DirectoryComparison } from "../lib/api";

function value(key: string, item: unknown) {
  if (key === "isArchived") return item === true ? "Archived" : "Active";
  return item === "" ? 'Empty text ("")' : String(item);
}

export default function DirectoryComparisonPanel({
  path,
  currentVersion,
  disabled,
  onBusyChange,
  onDenied,
  onClose,
}: {
  path: string;
  currentVersion: number;
  disabled: boolean;
  onBusyChange: (busy: boolean) => void;
  onDenied: (message: string) => void;
  onClose: () => void;
}) {
  const [from, setFrom] = useState(String(Math.max(1, currentVersion - 1))),
    [to, setTo] = useState(String(currentVersion)),
    [showUnchanged, setShowUnchanged] = useState(false);
  const [result, setResult] = useState<DirectoryComparison | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const request = useRef<AbortController | null>(null);
  const locked = disabled || busy;
  useEffect(
    () => () => {
      const owned = request.current;
      if (owned) {
        request.current = null;
        owned.abort();
        onBusyChange(false);
      }
    },
    [onBusyChange],
  );
  function clear() {
    setResult(null);
    setError("");
  }
  async function compare(offset = 0) {
    if (locked) return;
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    clear();
    try {
      const next = await api<DirectoryComparison>(
        `${path}/compare?fromVersion=${Number(from)}&toVersion=${Number(to)}&changesOnly=${!showUnchanged}&offset=${offset}`,
        { signal: abort.signal },
      );
      if (!abort.signal.aborted && request.current === abort) setResult(next);
    } catch (e) {
      if (!abort.signal.aborted && request.current === abort) {
        const message =
          e instanceof Error
            ? e.message
            : "Directory comparison is unavailable.";
        setError(message);
        if (e instanceof ApiError && [401, 403, 404].includes(e.status))
          onDenied(message);
      }
    } finally {
      if (!abort.signal.aborted && request.current === abort) {
        request.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  function submit(e: FormEvent) {
    e.preventDefault();
    void compare();
  }
  const pageOffset = Number(result?.offset ?? 0);
  return (
    <section
      className="asset-comparison"
      aria-label="Directory revision comparison"
      aria-busy={busy}
    >
      <div className="actions">
        <h4>Compare directory revisions</h4>
        <button className="text-button" disabled={locked} onClick={onClose}>
          Close directory comparison
        </button>
      </div>
      <p className="subtle">
        Compare exact saved fields and lifecycle, including empty text and line
        endings. Earlier migration history may be unavailable. Restore
        separately through historical review.
      </p>
      <form
        className="asset-comparison-form"
        aria-label="Choose directory revisions"
        onSubmit={submit}
      >
        <label>
          From directory revision
          <input
            type="number"
            min={1}
            max={2147483647}
            required
            value={from}
            disabled={locked}
            onChange={(e) => {
              setFrom(e.target.value);
              clear();
            }}
          />
        </label>
        <label>
          To directory revision
          <input
            type="number"
            min={1}
            max={2147483647}
            required
            value={to}
            disabled={locked}
            onChange={(e) => {
              setTo(e.target.value);
              clear();
            }}
          />
        </label>
        <label className="template-toggle">
          <input
            type="checkbox"
            checked={showUnchanged}
            disabled={locked}
            onChange={(e) => {
              setShowUnchanged(e.target.checked);
              clear();
            }}
          />
          Show unchanged directory fields
        </label>
        <button className="secondary" type="submit" disabled={locked}>
          Compare directory versions
        </button>
      </form>
      {busy && <p role="status">Loading directory comparison…</p>}
      {error && (
        <div className="message error" role="alert">
          {error}
          <button
            className="secondary"
            disabled={locked}
            onClick={() => void compare()}
          >
            Retry directory comparison
          </button>
        </div>
      )}
      {result && (
        <>
          <div className="asset-comparison-summaries">
            {[result.from, result.to].map((revision, index) => (
              <section
                key={index}
                aria-label={
                  index === 0
                    ? "From directory snapshot"
                    : "To directory snapshot"
                }
              >
                <h5>
                  {index === 0 ? "From" : "To"}: directory revision{" "}
                  {revision.version}
                </h5>
                <p className="comparison-value">{revision.name}</p>
                <p>{revision.isArchived ? "Archived" : "Active"}</p>
                <p className="subtle">
                  {revision.actor} ·{" "}
                  {new Date(revision.createdAt).toLocaleString()}
                </p>
                {revision.restoredFromVersion != null && (
                  <p>
                    Restored from directory revision{" "}
                    {revision.restoredFromVersion}
                  </p>
                )}
              </section>
            ))}
          </div>
          <p className="subtle">
            Observed current directory version: {result.currentVersion}.{" "}
            {result.changedRows} changed fields. Showing {result.totalRows}{" "}
            {result.changesOnly ? "changed" : "total"} fields.
          </p>
          {Number(result.currentVersion) !== currentVersion && (
            <p className="message" role="status">
              The current record has changed. Review the current record and
              historical snapshot separately before restoring.
            </p>
          )}
          {!result.rows.length ? (
            <p role="status">No differences in recorded fields or lifecycle.</p>
          ) : (
            <>
              <div
                className="comparison-table-scroll"
                role="region"
                aria-label="Directory comparison fields"
                tabIndex={0}
              >
                <table className="asset-comparison-table">
                  <caption>
                    Historical directory fields, from revision{" "}
                    {result.from.version} to {result.to.version}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">Field / change</th>
                      <th scope="col">From</th>
                      <th scope="col">To</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.rows.map((row) => (
                      <tr key={row.key}>
                        <th scope="row">
                          {row.label}
                          <p>{row.changed ? "Changed" : "Unchanged"}</p>
                          {row.lineEndingsOnly && <p>Line endings differ</p>}
                        </th>
                        <td>
                          <p className="comparison-value">
                            {value(row.key, row.from)}
                          </p>
                        </td>
                        <td>
                          <p className="comparison-value">
                            {value(row.key, row.to)}
                          </p>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="actions">
                {pageOffset > 0 && (
                  <button
                    className="secondary"
                    disabled={locked}
                    onClick={() => void compare(Math.max(0, pageOffset - 4))}
                  >
                    Previous directory differences
                  </button>
                )}
                {result.nextOffset != null && (
                  <button
                    className="secondary"
                    disabled={locked}
                    onClick={() => void compare(Number(result.nextOffset))}
                  >
                    Next directory differences
                  </button>
                )}
              </div>
            </>
          )}
        </>
      )}
    </section>
  );
}
