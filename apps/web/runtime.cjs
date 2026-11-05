// Pinned Next.js rewrite transport logs the complete target URL on error,
// including OIDC callback queries. Redact that specific framework diagnostic
// before loading Next, without changing its streaming/cancellation semantics.
// Packaged marker canaries guard this contract whenever Next is updated.
const originalError = console.error.bind(console);
console.error = (...args) => {
  if (typeof args[0] === "string" && args[0].startsWith("Failed to proxy ")) {
    originalError("SaveIT API upstream request failed.");
    return;
  }
  originalError(...args);
};
require("./public-trust.cjs").configure();
require("./apps/web/server.js");
