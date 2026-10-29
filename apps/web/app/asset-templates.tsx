"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  ApiError,
  type Asset,
  type AssetType,
  type AssetTypeRevision,
  type AssetTypeRevisionSummary,
  type Page,
} from "../lib/api";
import SchemaFields, {
  defaults,
  type FieldDefinition,
  type FieldValues,
} from "./schema-fields";

const emptyField = (): FieldDefinition => ({
  key: "",
  label: "",
  type: "text",
  required: false,
});
export default function AssetTemplates({
  organizationId,
  canEdit,
  disabled,
  onBusyChange,
  onCreated,
}: {
  organizationId: string;
  canEdit: boolean;
  disabled: boolean;
  onBusyChange: (busy: boolean) => void;
  onCreated: (asset: Asset) => void;
}) {
  const [rows, setRows] = useState<AssetType[]>([]),
    [cursor, setCursor] = useState<string | null>(null),
    [archived, setArchived] = useState(false);
  const [selected, setSelected] = useState<AssetType | null>(null),
    [schema, setSchema] = useState<AssetTypeRevision | null>(null);
  const [name, setName] = useState(""),
    [definition, setDefinition] = useState<FieldDefinition[]>([emptyField()]),
    [editing, setEditing] = useState(false),
    [isArchived, setIsArchived] = useState(false);
  const [creating, setCreating] = useState(false),
    [assetName, setAssetName] = useState(""),
    [kind, setKind] = useState(""),
    [values, setValues] = useState<FieldValues>({});
  const [history, setHistory] = useState<AssetTypeRevisionSummary[]>([]),
    [historyCursor, setHistoryCursor] = useState<string | null>(null),
    [historical, setHistorical] = useState<AssetTypeRevision | null>(null);
  const [busy, setBusy] = useState(false),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [conflict, setConflict] = useState(false);
  const request = useRef<AbortController | null>(null),
    base = `/organizations/${organizationId}/asset-types`;
  const [retryList, setRetryList] = useState(0);
  useEffect(() => {
    const abort = new AbortController();
    setLoading(true);
    setRows([]);
    setCursor(null);
    api<Page<AssetType>>(`${base}?includeArchived=${archived}`, {
      signal: abort.signal,
    })
      .then((p) => {
        if (!abort.signal.aborted) {
          setRows(p.items);
          setCursor(p.nextCursor);
          setError("");
        }
      })
      .catch((e) => {
        if (!abort.signal.aborted) {
          setRows([]);
          setCursor(null);
          setError(e.message);
        }
      })
      .finally(() => {
        if (!abort.signal.aborted) setLoading(false);
      });
    return () => abort.abort();
  }, [base, archived, retryList]);
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (disabled || busy || loading) return;
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    try {
      await action(abort.signal);
    } catch (e) {
      if (!abort.signal.aborted) {
        setError(e instanceof Error ? e.message : "Templates unavailable.");
        if (e instanceof ApiError) {
          setConflict(e.status === 409);
          if ([401, 403, 404].includes(e.status)) {
            setSchema(null);
            setRows([]);
            setEditing(false);
            setCreating(false);
            setHistorical(null);
            setHistory([]);
          }
        }
      }
    } finally {
      if (!abort.signal.aborted) {
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  async function open(row: AssetType) {
    await perform(async (signal) => {
      const current = await api<AssetType>(`${base}/${row.id}`, { signal });
      const def = await api<AssetTypeRevision>(
        `${base}/${row.id}/revisions/${current.version}`,
        { signal },
      );
      if (!signal.aborted) {
        setSelected(current);
        setSchema(def);
        setName(current.name);
        setDefinition(def.definition as FieldDefinition[]);
        setIsArchived(!!current.isArchived);
        setEditing(false);
        setCreating(false);
        setConflict(false);
        setHistory([]);
        setHistorical(null);
        setNotice("");
      }
    });
  }
  async function save(e: FormEvent) {
    e.preventDefault();
    await perform(async (signal) => {
      const row = await api<AssetType>(
        `${base}${selected ? `/${selected.id}` : ""}`,
        {
          method: selected ? "PUT" : "POST",
          signal,
          body: JSON.stringify({
            name,
            definition,
            expectedVersion: selected?.version ?? 0,
            isArchived,
          }),
        },
      );
      if (!signal.aborted) {
        setRows((prev) => [row, ...prev.filter((x) => x.id !== row.id)]);
        setSelected(row);
        setSchema(null);
        setEditing(false);
        setNotice(
          `Template published as version ${row.version}. Existing assets keep their pinned version.`,
        );
      }
    });
  }
  async function create(e: FormEvent) {
    e.preventDefault();
    if (!schema) return;
    await perform(async (signal) => {
      const asset = await api<Asset>(
        `/organizations/${organizationId}/assets`,
        {
          method: "POST",
          signal,
          body: JSON.stringify({
            name: assetName,
            kind,
            assetTypeId: schema.assetTypeId,
            assetTypeVersion: schema.version,
            fields: values,
          }),
        },
      );
      if (!signal.aborted) onCreated(asset);
    });
  }
  function update(i: number, patch: Partial<FieldDefinition>) {
    setDefinition((d) => d.map((f, n) => (n === i ? { ...f, ...patch } : f)));
  }
  return (
    <section
      className="panel directory-panel template-panel"
      aria-label="Asset templates"
    >
      <div className="panel-top">
        <h2>Asset templates</h2>
        {canEdit && (
          <button
            className="primary"
            disabled={disabled || busy || loading}
            onClick={() => {
              setSelected(null);
              setSchema(null);
              setName("");
              setDefinition([emptyField()]);
              setIsArchived(false);
              setEditing(true);
              setCreating(false);
              setConflict(false);
              setHistory([]);
              setHistorical(null);
              setError("");
            }}
          >
            New template
          </button>
        )}
      </div>
      <p className="template-intro">
        Versioned fields for repeatable inventory. Publishing keeps existing
        assets on their original schema. Fields are excluded from search. Keep
        passwords and secrets out of these fields.
      </p>
      <label className="template-toggle">
        <input
          type="checkbox"
          checked={archived}
          disabled={disabled || busy || loading}
          onChange={(e) => {
            setArchived(e.target.checked);
            setSelected(null);
            setSchema(null);
            setEditing(false);
            setCreating(false);
            setHistory([]);
            setHistorical(null);
          }}
        />{" "}
        Include archived templates
      </label>
      <button
        disabled={disabled || busy || loading}
        onClick={() => setRetryList((n) => n + 1)}
      >
        Reload template list
      </button>
      {(error || conflict) && (
        <div className="message error" role="alert">
          {error ||
            "This template changed. Reload the current version before publishing again."}
          {selected && (
            <button disabled={disabled || busy} onClick={() => open(selected)}>
              Reload template
            </button>
          )}
        </div>
      )}
      {notice && (
        <div className="message" role="status">
          {notice}
        </div>
      )}
      {loading ? (
        <p>Loading templates…</p>
      ) : (
        <ul className="template-list">
          {rows
            .filter((r) => archived || !r.isArchived)
            .map((row) => (
              <li key={row.id}>
                <span>
                  {row.name} · v{row.version}
                  {row.isArchived ? " · archived" : ""}
                </span>
                <button disabled={disabled || busy} onClick={() => open(row)}>
                  Open template {row.name}
                </button>
              </li>
            ))}
        </ul>
      )}
      {cursor && (
        <button
          disabled={disabled || busy || loading}
          onClick={() =>
            perform(async (signal) => {
              const p = await api<Page<AssetType>>(
                `${base}?includeArchived=${archived}&after=${cursor}`,
                { signal },
              );
              if (!signal.aborted) {
                setRows((prev) => [
                  ...new Map(
                    [...prev, ...p.items].map((row) => [row.id, row]),
                  ).values(),
                ]);
                setCursor(p.nextCursor);
              }
            })
          }
        >
          Load more templates
        </button>
      )}
      {schema && !editing && !creating && (
        <section className="template-detail" aria-label="Template definition">
          <h3>
            {schema.name} · version {schema.version}
          </h3>
          <p>
            {(schema.definition as FieldDefinition[]).length} fields ·{" "}
            {schema.actor}
          </p>
          <ul>
            {(schema.definition as FieldDefinition[]).map((f) => (
              <li key={f.key}>
                {f.label} ({f.key}) · {f.type}
                {f.required ? " · required" : ""}
                {f.options ? ` · ${f.options.join(", ")}` : ""}
                {f.defaultValue != null
                  ? ` · default: ${String(f.defaultValue)}`
                  : ""}
              </li>
            ))}
          </ul>
          <div className="actions">
            {canEdit && (
              <>
                <button
                  disabled={disabled || busy}
                  onClick={() => {
                    setEditing(true);
                    setConflict(false);
                  }}
                >
                  Edit template
                </button>
                {!selected?.isArchived && (
                  <button
                    disabled={disabled || busy}
                    onClick={() => {
                      setCreating(true);
                      setAssetName("");
                      setKind(schema.name.slice(0, 80));
                      setValues(
                        defaults(schema.definition as FieldDefinition[]),
                      );
                    }}
                  >
                    Create asset from template
                  </button>
                )}
              </>
            )}
            <button
              disabled={disabled || busy}
              onClick={() =>
                perform(async (signal) => {
                  const p = await api<Page<AssetTypeRevisionSummary>>(
                    `${base}/${selected!.id}/revisions`,
                    { signal },
                  );
                  if (!signal.aborted) {
                    setHistory(p.items);
                    setHistoryCursor(p.nextCursor);
                  }
                })
              }
            >
              View template history
            </button>
          </div>
        </section>
      )}
      {!!history.length && !editing && !creating && (
        <section aria-label="Template history">
          <h3>Published versions</h3>
          {history.map((r) => (
            <p key={r.version}>
              <button
                disabled={disabled || busy}
                onClick={() =>
                  perform(async (signal) => {
                    const d = await api<AssetTypeRevision>(
                      `${base}/${selected!.id}/revisions/${r.version}`,
                      { signal },
                    );
                    if (!signal.aborted) setHistorical(d);
                  })
                }
              >
                Inspect template version {r.version}
              </button>{" "}
              · {r.name} · {r.actor}
            </p>
          ))}
          {historyCursor && (
            <button
              disabled={disabled || busy}
              onClick={() =>
                perform(async (signal) => {
                  const p = await api<Page<AssetTypeRevisionSummary>>(
                    `${base}/${selected!.id}/revisions?before=${historyCursor}`,
                    { signal },
                  );
                  if (!signal.aborted) {
                    setHistory((prev) => [...prev, ...p.items]);
                    setHistoryCursor(p.nextCursor);
                  }
                })
              }
            >
              Older template versions
            </button>
          )}
          {historical && (
            <pre className="schema-snapshot">
              {JSON.stringify(historical.definition, null, 2)}
            </pre>
          )}
        </section>
      )}
      {editing && (
        <form
          className="directory-form"
          aria-label="Template editor"
          onSubmit={save}
        >
          <label>
            Template name
            <input
              required
              maxLength={160}
              value={name}
              disabled={disabled || busy}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          {definition.map((f, i) => (
            <fieldset
              className="schema-row"
              key={i}
              disabled={disabled || busy}
            >
              <legend>Field {i + 1}</legend>
              <div className="schema-grid">
                <label>
                  Field key
                  <input
                    required
                    pattern="[a-z][a-z0-9_]{0,31}"
                    maxLength={32}
                    value={f.key}
                    onChange={(e) => update(i, { key: e.target.value })}
                  />
                </label>
                <label>
                  Field label
                  <input
                    required
                    maxLength={80}
                    value={f.label}
                    onChange={(e) => update(i, { label: e.target.value })}
                  />
                </label>
                <label>
                  Field type
                  <select
                    value={f.type}
                    onChange={(e) =>
                      update(i, {
                        type: e.target.value as FieldDefinition["type"],
                        options: e.target.value === "choice" ? [] : null,
                        defaultValue: null,
                      })
                    }
                  >
                    {["text", "number", "boolean", "date", "choice"].map(
                      (t) => (
                        <option key={t}>{t}</option>
                      ),
                    )}
                  </select>
                </label>
                <label className="template-toggle">
                  <input
                    type="checkbox"
                    checked={f.required}
                    onChange={(e) => update(i, { required: e.target.checked })}
                  />{" "}
                  Required field
                </label>
                {f.type === "choice" && (
                  <label>
                    Choices (one per line)
                    <textarea
                      required
                      value={f.options?.join("\n") ?? ""}
                      onChange={(e) =>
                        update(i, { options: e.target.value.split("\n") })
                      }
                    />
                  </label>
                )}
                <label>
                  Default value (optional)
                  {f.type === "boolean" ? (
                    <select
                      value={
                        f.defaultValue == null ? "" : String(f.defaultValue)
                      }
                      onChange={(e) =>
                        update(i, {
                          defaultValue:
                            e.target.value === ""
                              ? null
                              : e.target.value === "true",
                        })
                      }
                    >
                      <option value="">No default</option>
                      <option value="true">Yes</option>
                      <option value="false">No</option>
                    </select>
                  ) : (
                    <input
                      type={
                        f.type === "number"
                          ? "number"
                          : f.type === "date"
                            ? "date"
                            : "text"
                      }
                      step={f.type === "number" ? "0.001" : undefined}
                      maxLength={f.type === "text" ? 500 : 80}
                      value={
                        f.defaultValue == null ? "" : String(f.defaultValue)
                      }
                      onChange={(e) =>
                        update(i, {
                          defaultValue:
                            e.target.value === ""
                              ? null
                              : f.type === "number"
                                ? Number(e.target.value)
                                : e.target.value,
                        })
                      }
                    />
                  )}
                </label>
              </div>
              <button
                type="button"
                disabled={disabled || busy || definition.length === 1}
                onClick={() =>
                  setDefinition((d) => d.filter((_, n) => n !== i))
                }
              >
                Remove field {i + 1}
              </button>
            </fieldset>
          ))}
          <button
            type="button"
            disabled={disabled || busy || definition.length >= 20}
            onClick={() => setDefinition((d) => [...d, emptyField()])}
          >
            Add field
          </button>
          {selected && (
            <label className="template-toggle">
              <input
                type="checkbox"
                checked={isArchived}
                disabled={disabled || busy}
                onChange={(e) => setIsArchived(e.target.checked)}
              />{" "}
              Archive template (existing pins remain usable)
            </label>
          )}
          <div className="actions">
            <button className="primary" disabled={disabled || busy || conflict}>
              Publish template version
            </button>
            <button
              type="button"
              disabled={disabled || busy}
              onClick={() => setEditing(false)}
            >
              Cancel template edit
            </button>
          </div>
        </form>
      )}
      {creating && schema && (
        <form
          className="directory-form"
          aria-label="Create typed asset"
          onSubmit={create}
        >
          <h3>
            New asset · {schema.name} v{schema.version}
          </h3>
          <p>
            Defaults are prefilled for review and saved only when you create
            this asset.
          </p>
          <label>
            Asset name
            <input
              required
              maxLength={160}
              value={assetName}
              disabled={disabled || busy}
              onChange={(e) => setAssetName(e.target.value)}
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
          <SchemaFields
            definition={schema.definition as FieldDefinition[]}
            values={values}
            disabled={disabled || busy}
            onChange={setValues}
          />
          <div className="actions">
            <button className="primary" disabled={disabled || busy || conflict}>
              Create typed asset
            </button>
            <button
              type="button"
              disabled={disabled || busy}
              onClick={() => setCreating(false)}
            >
              Cancel typed asset
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
