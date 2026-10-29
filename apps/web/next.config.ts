import type { NextConfig } from "next";
const issuer = process.env.SAVEIT_OKTA_ISSUER
  ? new URL(process.env.SAVEIT_OKTA_ISSUER)
  : null;
if (issuer && issuer.protocol !== "https:")
  throw new Error("The configured Okta issuer must use HTTPS.");
const config: NextConfig = {
  devIndicators: false,
  poweredByHeader: false,
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${process.env.SAVEIT_API_ORIGIN || "http://127.0.0.1:5080"}/api/:path*`,
      },
    ];
  },
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "same-origin" },
          {
            key: "Content-Security-Policy",
            value: `default-src 'self'; script-src 'self' 'unsafe-inline'${process.env.NODE_ENV === "development" ? " 'unsafe-eval'" : ""}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'${issuer ? ` ${issuer.origin}` : ""}`,
          },
        ],
      },
    ];
  },
};
export default config;
