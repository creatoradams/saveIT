"use client";

import { useEffect, useRef, useState } from "react";
import {
  api,
  ApiError,
  type OrganizationRevision,
  type Page,
} from "../lib/api";

export default function OrganizationHistory({
  organizationId,
  disabled,
  onBusyChange,
  onDenied,
}: {
  organizationId: string;
  disabled: boolean;
  onBusyChange: (busy: boolean) => void;
  onDenied: (message: string) => void;
}) {
  const [rows, setRows] = useState<OrganizationRevision[]>([]),
    [cursor, setCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const request = useRef<AbortController | null>(null);
  const path = `/organizations/${organizationId}/revisions`,
    locked = disabled || busy || loading;
  function fail(e: unknown) {
    const message =
      e instanceof Error ? e.message : "Organization history is unavailable.";
    setRows([]);
    setCursor(null);
    setError(message);
    if (e instanceof ApiError && [401, 403, 404].includes(e.status))
      onDenied(message);
  }
  useEffect(() => {
    const abort = new AbortController();
    setRows([]);
    setCursor(null);
    setError("");
    setLoading(true);
    api<Page<OrganizationRevision>>(path, { signal: abort.signal })
      .then((page) => {
        if (!abort.signal.aborted) {
          setRows(page.items);
          setCursor(page.nextCursor);
        }
      })
      .catch((e) => {
        if (!abort.signal.aborted) fail(e);
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [path]);
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
  async function read(before: string | null = null) {
    if (locked) return;
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    if (!before) {
      setRows([]);
      setCursor(null);
    }
    try {
      const page = await api<Page<OrganizationRevision>>(
        `${path}${before ? `?before=${encodeURIComponent(before)}` : ""}`,
        { signal: abort.signal },
      );
      if (!abort.signal.aborted && request.current === abort) {
        setRows((previous) =>
          before
            ? [
                ...new Map(
                  [...previous, ...page.items].map((x) => [x.version, x]),
                ).values(),
              ]
            : page.items,
        );
        setCursor(page.nextCursor);
      }
    } catch (e) {
      if (!abort.signal.aborted && request.current === abort) fail(e);
    } finally {
      if (!abort.signal.aborted && request.current === abort) {
        request.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  return (
    <section
      className="panel directory-panel"
      aria-label="Organization name history"
      aria-busy={loading || busy}
    >
      <div className="panel-top">
        <h2>Organization name history</h2>
        <button
          className="secondary"
          disabled={locked}
          onClick={() => void read()}
        >
          Refresh organization history
        </button>
      </div>
      <p className="subtle">
        Saved names remain unchanged after a rename. Migration records begin at
        the version available when history was introduced; earlier changes may
        be unavailable.
      </p>
      {(loading || busy) && <p role="status">Loading organization history…</p>}
      {error && (
        <div className="message error" role="alert">
          {error}
          <button
            className="secondary"
            disabled={locked}
            onClick={() => void read()}
          >
            Retry organization history
          </button>
        </div>
      )}
      {!loading && !busy && !error && !rows.length && (
        <p>No recorded organization revisions.</p>
      )}
      <ol
        className="directory-history-list"
        aria-label="Organization revisions"
      >
        {rows.map((row) => (
          <li key={row.version}>
            <strong>Organization revision {row.version}</strong>
            <span className="comparison-value">{row.name}</span>
            <small>
              {row.origin} · {row.actor} ·{" "}
              {new Date(row.createdAt).toLocaleString()}
            </small>
          </li>
        ))}
      </ol>
      {cursor && (
        <button
          className="load-more"
          disabled={locked}
          onClick={() => void read(cursor)}
        >
          Older organization revisions
        </button>
      )}
    </section>
  );
}
