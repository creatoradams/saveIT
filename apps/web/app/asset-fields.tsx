"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  api,
  ApiError,
  type Asset,
  type AssetFields,
  type AssetType,
  type AssetTypeRevision,
  type Page,
} from "../lib/api";
import SchemaFields, {
  type FieldDefinition,
  type FieldValues,
  Values,
} from "./schema-fields";

export default function AssetFieldsPanel({
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
  onBusyChange: (busy: boolean) => void;
  onUpdated: (asset: Asset) => void;
}) {
  const [data, setData] = useState<AssetFields | null>(null),
    [schema, setSchema] = useState<AssetTypeRevision | null>(null),
    [values, setValues] = useState<FieldValues>({});
  const [editing, setEditing] = useState(false),
    [types, setTypes] = useState<AssetType[]>([]),
    [cursor, setCursor] = useState<string | null>(null),
    [choice, setChoice] = useState("");
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [conflict, setConflict] = useState(false),
    [loaded, setLoaded] = useState(false),
    [confirmSwitch, setConfirmSwitch] = useState(false);
  const request = useRef<AbortController | null>(null),
    path = `/organizations/${organizationId}/assets/${asset.id}`,
    base = `/organizations/${organizationId}/asset-types`;
  useEffect(
    () => () => {
      request.current?.abort();
      onBusyChange(false);
    },
    [onBusyChange],
  );
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (busy || disabled) return;
    const abort = new AbortController();
    request.current = abort;
    setBusy(true);
    onBusyChange(true);
    setError("");
    try {
      await action(abort.signal);
    } catch (e) {
      if (!abort.signal.aborted) {
        setError(e instanceof Error ? e.message : "Fields unavailable.");
        if (e instanceof ApiError) {
          if (e.status === 409) setConflict(true);
          if ([401, 403, 404].includes(e.status)) {
            setData(null);
            setSchema(null);
            setValues({});
            setTypes([]);
            setEditing(false);
            setLoaded(false);
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
  async function load() {
    await perform(async (signal) => {
      const current = await api<AssetFields>(`${path}/fields`, { signal });
      if (!signal.aborted) {
        if (current.asset.version !== asset.version) {
          onUpdated(current.asset);
          return;
        }
        setData(current);
        setSchema(current.schema ?? null);
        setValues(current.values as FieldValues);
        setLoaded(true);
        setEditing(false);
        setConflict(false);
        setConfirmSwitch(false);
      }
    });
  }
  async function edit() {
    await perform(async (signal) => {
      const page = await api<Page<AssetType>>(base, { signal });
      if (!signal.aborted) {
        setTypes(page.items);
        setCursor(page.nextCursor);
        setChoice("");
        setEditing(true);
      }
    });
  }
  async function apply() {
    await perform(async (signal) => {
      const latest = await api<AssetType>(`${base}/${choice}`, { signal });
      if (latest.isArchived)
        throw new Error("Reactivate this template before assigning it.");
      const next = await api<AssetTypeRevision>(
        `${base}/${choice}/revisions/${latest.version}`,
        { signal },
      );
      if (!signal.aborted) {
        const defs = next.definition as FieldDefinition[];
        setSchema(next);
        setValues((prev) =>
          Object.fromEntries(
            Object.entries(prev).filter(([key]) =>
              defs.some((f) => f.key === key),
            ),
          ),
        );
        setConfirmSwitch(false);
      }
    });
  }
  async function save(e: FormEvent) {
    e.preventDefault();
    await perform(async (signal) => {
      const updated = await api<Asset>(`${path}/fields`, {
        method: "PUT",
        signal,
        body: JSON.stringify({
          expectedVersion: asset.version,
          assetTypeId: schema?.assetTypeId ?? null,
          assetTypeVersion: schema?.version ?? null,
          fields: values,
        }),
      });
      if (!signal.aborted) onUpdated(updated);
    });
  }
  return (
    <section
      className="asset-metadata asset-fields"
      aria-label="Asset custom fields"
    >
      <h3>Custom fields</h3>
      <p className="subtle">
        {asset.assetTypeId
          ? `Pinned template version ${asset.assetTypeVersion}`
          : "No template assigned"}{" "}
        · excluded from search
      </p>
      {(error || conflict) && (
        <div className="message error" role="alert">
          {error ||
            "This asset changed. Reload custom fields before saving again."}
          <button disabled={disabled || busy} onClick={load}>
            Reload custom fields
          </button>
        </div>
      )}
      {!loaded && (
        <button disabled={disabled || busy} onClick={load}>
          View custom fields
        </button>
      )}
      {loaded && !editing && (
        <>
          <p>
            {data?.schema?.name ?? "Untyped asset"}
            {data?.schema ? ` · schema v${data.schema.version}` : ""}
          </p>
          <Values values={(data?.values as FieldValues) ?? {}} />
          {canEdit && (
            <button disabled={disabled || busy} onClick={edit}>
              Edit custom fields
            </button>
          )}
        </>
      )}
      {editing && (
        <form
          className="directory-form"
          aria-label="Edit custom fields"
          onSubmit={save}
        >
          <p>
            Editing {schema?.name ?? "an untyped asset"}
            {schema ? ` · schema v${schema.version}` : ""}. Publishing a
            template never upgrades this asset automatically.
          </p>
          <label>
            Assign or upgrade template
            <select
              value={choice}
              disabled={disabled || busy}
              onChange={(e) => {
                setChoice(e.target.value);
                setConfirmSwitch(false);
              }}
            >
              <option value="">Choose an active template</option>
              {types.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name} · v{t.version}
                </option>
              ))}
            </select>
          </label>
          {cursor && (
            <button
              type="button"
              disabled={disabled || busy}
              onClick={() =>
                perform(async (signal) => {
                  const p = await api<Page<AssetType>>(
                    `${base}?after=${cursor}`,
                    { signal },
                  );
                  if (!signal.aborted) {
                    setTypes((prev) => [...prev, ...p.items]);
                    setCursor(p.nextCursor);
                  }
                })
              }
            >
              Load more field templates
            </button>
          )}
          {choice && (
            <>
              <label className="template-toggle">
                <input
                  type="checkbox"
                  checked={confirmSwitch}
                  disabled={disabled || busy}
                  onChange={(e) => setConfirmSwitch(e.target.checked)}
                />{" "}
                Review this schema change; remove keys absent from the selected
                schema.
              </label>
              <button
                type="button"
                disabled={disabled || busy || !confirmSwitch}
                onClick={apply}
              >
                Review selected template version
              </button>
            </>
          )}
          {schema && (
            <SchemaFields
              definition={schema.definition as FieldDefinition[]}
              values={values}
              disabled={disabled || busy}
              onChange={setValues}
            />
          )}
          <p className="subtle">
            Matching values are retained for validation. New required fields
            must be supplied; template defaults are never applied during an
            upgrade.
          </p>
          <div className="actions">
            <button className="primary" disabled={disabled || busy || conflict}>
              Save custom fields
            </button>
            <button
              type="button"
              disabled={disabled || busy}
              onClick={() => {
                setEditing(false);
                setSchema(data?.schema ?? null);
                setValues((data?.values as FieldValues) ?? {});
                setConfirmSwitch(false);
              }}
            >
              Cancel field edit
            </button>
          </div>
        </form>
      )}
    </section>
  );
}
