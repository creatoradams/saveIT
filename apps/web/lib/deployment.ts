export function apiOrigin(): URL {
  const value =
    process.env.SAVEIT_API_ORIGIN ||
    (process.env.NODE_ENV === "development" ? "http://127.0.0.1:5080" : "");
  const url = new URL(value);
  if (
    !(process.env.NODE_ENV === "development"
      ? ["http:", "https:"].includes(url.protocol)
      : url.protocol === "https:") ||
    process.env.NODE_TLS_REJECT_UNAUTHORIZED === "0" ||
    url.username ||
    url.password ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  )
    throw new Error("Invalid API origin configuration.");
  return url;
}
export function publicOrigin(): URL | null {
  const value = process.env.SAVEIT_PUBLIC_ORIGIN;
  if (!value && process.env.NODE_ENV === "development") return null;
  const url = new URL(value || "");
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  )
    throw new Error("Invalid public origin configuration.");
  return url;
}
export function issuerOrigin(): string {
  if (!process.env.SAVEIT_OKTA_ISSUER) return "";
  const url = new URL(process.env.SAVEIT_OKTA_ISSUER);
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.search ||
    url.hash
  )
    throw new Error("Invalid identity issuer configuration.");
  return ` ${url.origin}`;
}
