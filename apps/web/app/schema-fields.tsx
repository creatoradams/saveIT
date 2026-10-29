"use client";

export type FieldDefinition = {
  key: string;
  label: string;
  type: "text" | "number" | "boolean" | "date" | "choice";
  required: boolean;
  options?: string[] | null;
  defaultValue?: string | number | boolean | null;
};
export type FieldValues = Record<string, string | number | boolean>;
export function defaults(definition: FieldDefinition[]): FieldValues {
  return Object.fromEntries(
    definition
      .filter((f) => f.defaultValue != null)
      .map((f) => [f.key, f.defaultValue!]),
  );
}
export function Values({ values }: { values: FieldValues }) {
  return (
    <dl className="field-values">
      {Object.entries(values).map(([key, value]) => (
        <div key={key}>
          <dt>{key}</dt>
          <dd>{String(value)}</dd>
        </div>
      ))}
    </dl>
  );
}
export default function SchemaFields({
  definition,
  values,
  disabled,
  onChange,
}: {
  definition: FieldDefinition[];
  values: FieldValues;
  disabled: boolean;
  onChange: (values: FieldValues) => void;
}) {
  function change(field: FieldDefinition, text: string) {
    const next = { ...values };
    if (text === "") delete next[field.key];
    else
      next[field.key] =
        field.type === "number"
          ? Number(text)
          : field.type === "boolean"
            ? text === "true"
            : text;
    onChange(next);
  }
  return (
    <div className="schema-grid">
      {definition.map((f) => {
        const current = Object.hasOwn(values, f.key)
          ? values[f.key]
          : undefined;
        return (
          <label key={f.key}>
            {f.label}
            {f.required ? " (required)" : ""}
            {f.type === "choice" || f.type === "boolean" ? (
              <select
                disabled={disabled}
                required={f.required}
                value={current == null ? "" : String(current)}
                onChange={(e) => change(f, e.target.value)}
              >
                <option value="">Not set</option>
                {f.type === "boolean" ? (
                  <>
                    <option value="true">Yes</option>
                    <option value="false">No</option>
                  </>
                ) : (
                  f.options?.map((o) => (
                    <option key={o} value={o}>
                      {o}
                    </option>
                  ))
                )}
              </select>
            ) : (
              <input
                disabled={disabled}
                required={f.required}
                type={f.type === "text" ? "text" : f.type}
                maxLength={f.type === "text" ? 500 : undefined}
                min={
                  f.type === "number"
                    ? -1000000000
                    : f.type === "date"
                      ? "0001-01-01"
                      : undefined
                }
                max={
                  f.type === "number"
                    ? 1000000000
                    : f.type === "date"
                      ? "9999-12-31"
                      : undefined
                }
                step={f.type === "number" ? "0.001" : undefined}
                value={current == null ? "" : String(current)}
                onChange={(e) => change(f, e.target.value)}
              />
            )}
          </label>
        );
      })}
    </div>
  );
}
