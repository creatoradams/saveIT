"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  type Organization,
  type SearchHit,
  type SearchPage,
} from "../lib/api";

export default function SearchPanel({
  organizationId,
  organizations,
  onOpen,
}: {
  organizationId: string;
  organizations: Organization[];
  onOpen: (hit: SearchHit) => void;
}) {
  const [query, setQuery] = useState("");
  const [scope, setScope] = useState("current");
  const [type, setType] = useState("");
  const [items, setItems] = useState<SearchHit[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [searched, setSearched] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const request = useRef<AbortController | null>(null);

  function reset() {
    request.current?.abort();
    setItems([]);
    setCursor(null);
    setSearched(false);
    setLoading(false);
    setError("");
  }
  useEffect(() => {
    reset();
    return () => request.current?.abort();
  }, [organizationId]);

  async function search(after?: string) {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setLoading(true);
    setError("");
    if (!after) {
      setItems([]);
      setCursor(null);
      setSearched(false);
    }
    const params = new URLSearchParams({ q: query.trim() });
    if (scope === "current") params.set("organizationId", organizationId);
    if (type) params.set("type", type);
    if (after) params.set("after", after);
    try {
      const page = await api<SearchPage>(`/search?${params}`, {
        signal: abort.signal,
      });
      if (abort.signal.aborted) return;
      setItems((previous) =>
        after
          ? [
              ...new Map(
                [...previous, ...page.items].map((hit) => [
                  `${hit.type}:${hit.id}`,
                  hit,
                ]),
              ).values(),
            ]
          : page.items,
      );
      setCursor(page.nextCursor);
      setSearched(true);
    } catch (err) {
      if (!abort.signal.aborted) {
        // Clear previous pages on denial/outage; do not keep showing stale previews.
        setItems([]);
        setCursor(null);
        setSearched(false);
        setError(
          err instanceof Error ? err.message : "Search could not be completed.",
        );
      }
    } finally {
      if (!abort.signal.aborted) setLoading(false);
    }
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    search();
  }
  const currentName =
    organizations.find((org) => org.id === organizationId)?.name ||
    "Current organization";

  return (
    <section className="search-workspace" aria-label="Authorized search">
      <form
        className="panel search-form"
        role="search"
        aria-label="Find assets and documents"
        onSubmit={submit}
      >
        <label className="search-query">
          Search assets and documents
          <input
            type="search"
            value={query}
            minLength={2}
            maxLength={120}
            required
            placeholder="Try an asset name or a procedure keyword"
            onChange={(event) => {
              setQuery(event.target.value);
              reset();
            }}
          />
        </label>
        <label>
          Search scope
          <select
            value={scope}
            onChange={(event) => {
              setScope(event.target.value);
              reset();
            }}
          >
            <option value="current">{currentName}</option>
            <option value="all">All authorized organizations</option>
          </select>
        </label>
        <label>
          Record type
          <select
            value={type}
            onChange={(event) => {
              setType(event.target.value);
              reset();
            }}
          >
            <option value="">Assets and documents</option>
            <option value="asset">Assets</option>
            <option value="document">Documents</option>
          </select>
        </label>
        <button type="submit" className="primary" disabled={loading}>
          {loading ? "Searching…" : "Search"}
        </button>
        <p className="search-hint">
          Match complete words in names, titles, and document content. New
          changes may take a moment to appear.
        </p>
      </form>
      {error && (
        <div className="message error" role="alert">
          {error}
          <button onClick={() => search()} disabled={loading}>
            Retry search
          </button>
        </div>
      )}
      <div className="search-results-status" role="status" aria-live="polite">
        {loading
          ? "Loading search results…"
          : searched
            ? `${items.length} result${items.length === 1 ? "" : "s"} loaded${cursor ? "; more available" : ""}.`
            : "Search the knowledge your account can access."}
      </div>
      {searched && items.length === 0 && (
        <div className="panel empty">
          <h2>No matching records</h2>
          <p>
            Try another word or search all authorized organizations. Recent
            changes may still be indexing.
          </p>
        </div>
      )}
      {items.length > 0 && (
        <ul className="search-results" aria-label="Search results">
          {items.map((hit) => (
            <li className="panel search-result" key={`${hit.type}:${hit.id}`}>
              <div className="search-result-top">
                <span className="tag">
                  {hit.type === "document" ? "Document" : "Asset"}
                </span>
                <span>{hit.organizationName}</span>
                {hit.type === "document" && <span>Revision {hit.version}</span>}
              </div>
              <h2>
                <button onClick={() => onOpen(hit)}>
                  {hit.title}
                  <span aria-hidden="true"> ↗</span>
                </button>
              </h2>
              <p className="search-preview">{hit.preview}</p>
              <button className="text-button" onClick={() => onOpen(hit)}>
                Open {hit.type === "document" ? "document" : "asset"}
              </button>
            </li>
          ))}
        </ul>
      )}
      {cursor && (
        <button
          className="load-more"
          disabled={loading}
          onClick={() => search(cursor)}
        >
          Load more results
        </button>
      )}
    </section>
  );
}
