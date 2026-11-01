"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, type DocumentTemplateComparison } from "../lib/api";

export default function DocumentTemplateComparisonPanel({
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
  const [result, setResult] = useState<DocumentTemplateComparison | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const request = useRef<AbortController | null>(null);
  const resultsHeading = useRef<HTMLHeadingElement>(null),
    errorPanel = useRef<HTMLDivElement>(null);
  const pendingFocus = useRef(false);
  const locked = disabled || busy;
  useEffect(() => {
    if (!locked && pendingFocus.current && (result || error)) {
      pendingFocus.current = false;
      (result ? resultsHeading.current : errorPanel.current)?.focus();
    }
  }, [locked, result, error]);
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
    if (locked || request.current) return;
    const abort = new AbortController();
    request.current = abort;
    pendingFocus.current = true;
    setBusy(true);
    onBusyChange(true);
    clear();
    try {
      const next = await api<DocumentTemplateComparison>(
        `${path}/compare?fromVersion=${Number(from)}&toVersion=${Number(to)}&changesOnly=${!showUnchanged}&offset=${offset}`,
        { signal: abort.signal },
      );
      if (!abort.signal.aborted && request.current === abort) setResult(next);
    } catch (e) {
      if (!abort.signal.aborted && request.current === abort) {
        const message =
          e instanceof Error
            ? e.message
            : "Template comparison is unavailable.";
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
      aria-label="Template revision comparison"
      aria-busy={busy}
      onKeyDown={(e) => {
        if (e.key === "Escape" && !locked) {
          e.preventDefault();
          onClose();
        }
      }}
    >
      <div className="actions">
        <h4>Compare template revisions</h4>
        <button className="text-button" disabled={locked} onClick={onClose}>
          Close template comparison
        </button>
      </div>
      <p className="subtle">
        Compare exact saved names, titles, lifecycle and plain-text content.
        Comparison does not refresh the version used to publish or create a
        document.
      </p>
      <form
        className="asset-comparison-form"
        aria-label="Choose template revisions"
        onSubmit={submit}
      >
        <label>
          From template revision
          <input
            autoFocus
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
          To template revision
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
          Show unchanged template lines
        </label>
        <button className="secondary" type="submit" disabled={locked}>
          Compare template versions
        </button>
      </form>
      {busy && <p role="status">Loading template comparison…</p>}
      {error && (
        <div
          className="message error"
          role="alert"
          ref={errorPanel}
          tabIndex={-1}
        >
          {error}
          <button
            className="secondary"
            disabled={locked}
            onClick={() => void compare()}
          >
            Retry template comparison
          </button>
        </div>
      )}
      {result && (
        <>
          <h5 ref={resultsHeading} tabIndex={-1}>
            Template comparison results
          </h5>
          <div className="asset-comparison-summaries">
            {[result.from, result.to].map((revision, index) => (
              <section
                key={index}
                aria-label={
                  index === 0
                    ? "From template snapshot"
                    : "To template snapshot"
                }
              >
                <h5>
                  {index === 0 ? "From" : "To"}: template revision{" "}
                  {revision.version}
                </h5>
                <p className="comparison-value">Name: {revision.name}</p>
                <p className="comparison-value">Title: {revision.title}</p>
                <p>{revision.isArchived ? "Archived" : "Active"}</p>
                <p className="subtle">
                  {revision.actor} ·{" "}
                  {new Date(revision.createdAt).toLocaleString()}
                </p>
              </section>
            ))}
          </div>
          <p>
            Name: {result.nameChanged ? "changed" : "unchanged"}. Title:{" "}
            {result.titleChanged ? "changed" : "unchanged"}. Lifecycle:{" "}
            {result.lifecycleChanged ? "changed" : "unchanged"}. Content:{" "}
            {result.contentChanged ? "changed" : "unchanged"}.
          </p>
          <p className="subtle">
            Observed current template version: {result.currentVersion}.{" "}
            {result.addedLines} added lines, {result.removedLines} removed
            lines. Showing {result.totalRows}{" "}
            {result.changesOnly ? "changed" : "total"} lines.
          </p>
          {Number(result.currentVersion) !== currentVersion && (
            <p className="message" role="status">
              A newer template publication exists. Review the current template
              separately before publishing or creating a document.
            </p>
          )}
          {result.lineEndingsOnly && (
            <p role="status">
              Only content line endings differ. The saved snapshots retain their
              exact line endings.
            </p>
          )}
          {result.simplified && (
            <p className="subtle">
              Large replacement: unchanged prefix and suffix are preserved; the
              middle is shown as removed and added blocks.
            </p>
          )}
          {!result.lines.length ? (
            <p role="status">No line differences on this view.</p>
          ) : (
            <>
              <div
                className="comparison-table-scroll"
                role="region"
                aria-label="Template comparison lines"
                tabIndex={0}
              >
                <table className="asset-comparison-table">
                  <caption>
                    Template content, from revision {result.from.version} to{" "}
                    {result.to.version}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">Change</th>
                      <th scope="col">From line</th>
                      <th scope="col">To line</th>
                      <th scope="col">Text</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.lines.map((line, index) => (
                      <tr key={pageOffset + index}>
                        <th scope="row">{line.kind}</th>
                        <td>{line.oldLine ?? "—"}</td>
                        <td>{line.newLine ?? "—"}</td>
                        <td>
                          <p className="comparison-value">
                            {line.text === "" ? 'Empty line ("")' : line.text}
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
                    onClick={() => void compare(Math.max(0, pageOffset - 200))}
                  >
                    Previous template differences
                  </button>
                )}
                {result.nextOffset != null && (
                  <button
                    className="secondary"
                    disabled={locked}
                    onClick={() => void compare(Number(result.nextOffset))}
                  >
                    Next template differences
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
