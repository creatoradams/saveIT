"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  type AssetComparison,
  type AssetComparisonSide,
} from "../lib/api";
import type { FieldDefinition } from "./schema-fields";

function scalar(value: unknown): string {
  if (typeof value === "string")
    return value === "" ? 'Empty text ("")' : value;
  if (typeof value === "boolean" || typeof value === "number")
    return String(value);
  return "Not set";
}
function Side({ side, field }: { side: AssetComparisonSide; field: boolean }) {
  const definition = side.definition as FieldDefinition | null;
  if (field && !definition) return <p className="subtle">Field not defined</p>;
  return (
    <>
      {definition && (
        <>
          <strong>{definition.label}</strong>
          <p className="subtle">
            {definition.type} · {definition.required ? "required" : "optional"}
          </p>
        </>
      )}
      <p className="comparison-value">
        {side.present ? scalar(side.value) : field ? "Not set" : "Unassigned"}
      </p>
      {definition && (
        <details className="field-rules">
          <summary>Historical field rules</summary>
          {definition.options && (
            <p>Choices: {definition.options.join(" · ")}</p>
          )}
          <p>
            Default:{" "}
            {definition.defaultValue != null
              ? scalar(definition.defaultValue)
              : "No default"}
          </p>
        </details>
      )}
    </>
  );
}

export default function AssetComparisonPanel({
  organizationId,
  assetId,
  assetVersion,
  disabled,
  onBusyChange,
  onReload,
  onClose,
}: {
  organizationId: string;
  assetId: string;
  assetVersion: number;
  disabled: boolean;
  onBusyChange: (busy: boolean) => void;
  onReload: () => Promise<void>;
  onClose: () => void;
}) {
  const [from, setFrom] = useState(String(Math.max(1, assetVersion - 1))),
    [to, setTo] = useState(String(assetVersion));
  const [showUnchanged, setShowUnchanged] = useState(false),
    [result, setResult] = useState<AssetComparison | null>(null);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const request = useRef<AbortController | null>(null);
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  function clear() {
    setResult(null);
    setError("");
  }
  async function compare(offset = 0) {
    if (disabled || busy) return;
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setResult(null);
    setError("");
    setBusy(true);
    onBusyChange(true);
    try {
      const next = await api<AssetComparison>(
        `/organizations/${organizationId}/assets/${assetId}/compare?fromVersion=${Number(from)}&toVersion=${Number(to)}&changesOnly=${!showUnchanged}&offset=${offset}`,
        { signal: abort.signal },
      );
      if (!abort.signal.aborted) setResult(next);
    } catch (e) {
      if (!abort.signal.aborted)
        setError(
          e instanceof Error ? e.message : "Asset comparison is unavailable.",
        );
    } finally {
      if (!abort.signal.aborted) {
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  function submit(e: FormEvent) {
    e.preventDefault();
    void compare();
  }
  const locked = disabled || busy;
  const pageOffset = Number(result?.offset ?? 0);
  return (
    <section
      className="asset-comparison"
      aria-label="Asset revision comparison"
    >
      <div className="actions">
        <h3>Compare asset revisions</h3>
        <button
          type="button"
          className="text-button"
          disabled={locked}
          onClick={onClose}
        >
          Close asset comparison
        </button>
      </div>
      <p className="subtle">
        Compare stored values and pinned field definitions. Locations use IDs;
        historical location names are unavailable. Restore through asset history
        after reviewing the current asset version.
      </p>
      <form
        className="asset-comparison-form"
        aria-label="Choose asset revisions"
        onSubmit={submit}
      >
        <label>
          From asset revision
          <input
            type="number"
            min={1}
            max={2147483647}
            step={1}
            required
            disabled={locked}
            value={from}
            onChange={(e) => {
              setFrom(e.target.value);
              clear();
            }}
          />
        </label>
        <label>
          To asset revision
          <input
            type="number"
            min={1}
            max={2147483647}
            step={1}
            required
            disabled={locked}
            value={to}
            onChange={(e) => {
              setTo(e.target.value);
              clear();
            }}
          />
        </label>
        <label className="template-toggle">
          <input
            type="checkbox"
            disabled={locked}
            checked={showUnchanged}
            onChange={(e) => {
              setShowUnchanged(e.target.checked);
              clear();
            }}
          />{" "}
          Show unchanged rows
        </label>
        <button className="secondary" type="submit" disabled={locked}>
          Compare asset versions
        </button>
      </form>
      {busy && <p role="status">Loading asset comparison…</p>}
      {error && (
        <div className="message error" role="alert">
          {error}
          <p>
            Comparison content has been cleared. Check the revision numbers and
            retry.
          </p>
          <button
            className="secondary"
            disabled={locked}
            onClick={() => compare()}
          >
            Retry asset comparison
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
                  index === 0 ? "From asset snapshot" : "To asset snapshot"
                }
              >
                <h4>
                  {index === 0 ? "From" : "To"}: asset revision{" "}
                  {revision.version}
                </h4>
                <p className="comparison-value">{revision.name}</p>
                <p className="subtle">
                  {revision.actor} ·{" "}
                  {new Date(revision.createdAt).toLocaleString()}
                </p>
                <p className="comparison-value">
                  {revision.template
                    ? `${revision.template.name} · schema v${revision.template.version} · ${revision.template.id}`
                    : "Untyped asset"}
                </p>
                {revision.restoredFromVersion != null && (
                  <p>
                    Restored from asset revision {revision.restoredFromVersion}
                  </p>
                )}
              </section>
            ))}
          </div>
          <p className="subtle">
            Current asset version: {result.currentVersion}. {result.changedRows}{" "}
            changed rows. Field keys align across templates; definitions
            describe each historical side.
          </p>
          {Number(result.currentVersion) !== assetVersion && (
            <div className="message" role="status">
              The current asset changed. Reload its details before restoring.
              <button
                className="secondary"
                disabled={locked}
                onClick={() => void onReload()}
              >
                Refresh asset details
              </button>
            </div>
          )}
          {result.rows.length === 0 ? (
            <p role="status">
              No differences in asset values or field definitions.
            </p>
          ) : (
            <>
              <p className="comparison-pagination" aria-live="polite">
                Asset comparison rows {pageOffset + 1}–
                {pageOffset + result.rows.length} of {result.totalRows}
              </p>
              <div
                className="comparison-table-scroll"
                tabIndex={0}
                role="region"
                aria-label="Asset comparison table"
              >
                <table className="asset-comparison-table">
                  <caption>Historical asset values and definitions</caption>
                  <thead>
                    <tr>
                      <th scope="col">Field / change</th>
                      <th scope="col">From revision {result.from.version}</th>
                      <th scope="col">To revision {result.to.version}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.rows.map((row) => (
                      <tr key={`${row.section}:${row.key}`}>
                        <th scope="row">
                          <span className="comparison-value">{row.label}</span>
                          <p>{row.change}</p>
                          {row.definitionChanged && <p>Definition changed</p>}
                          {row.valueChanged && <p>Value changed</p>}
                        </th>
                        <td>
                          <Side
                            side={row.from}
                            field={row.section === "field"}
                          />
                        </td>
                        <td>
                          <Side side={row.to} field={row.section === "field"} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="actions comparison-pagination">
                <button
                  className="secondary"
                  disabled={locked || pageOffset === 0}
                  onClick={() => compare(Math.max(0, pageOffset - 20))}
                >
                  Previous asset changes
                </button>
                <button
                  className="secondary"
                  disabled={locked || result.nextOffset == null}
                  onClick={() => compare(Number(result.nextOffset!))}
                >
                  Next asset changes
                </button>
              </div>
            </>
          )}
        </>
      )}
    </section>
  );
}
