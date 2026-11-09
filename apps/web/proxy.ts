import { NextResponse, type NextRequest } from "next/server";
import { apiOrigin, publicOrigin, issuerOrigin } from "./lib/deployment";
import { workloadToken } from "./lib/workload.server";

export async function proxy(request: NextRequest) {
  try {
    const origin = publicOrigin();
    if (
      origin &&
      !["/health/ready", "/health/live", "/health/startup"].includes(
        request.nextUrl.pathname,
      ) &&
      request.headers.get("host")?.toLowerCase() !== origin.host.toLowerCase()
    )
      return NextResponse.json(
        { title: "Invalid request host." },
        { status: 400, headers: { "Cache-Control": "no-store" } },
      );
    // Reject known oversized requests before streaming them to Kestrel. Kestrel also
    // independently caps all bodies, including requests without Content-Length.
    const length = request.headers.get("content-length");
    if (length && /^\d+$/.test(length) && Number(length) > 1_048_576)
      return NextResponse.json(
        { status: 413, title: "The request body exceeds the 1 MiB limit." },
        {
          status: 413,
          headers: {
            "Content-Type": "application/problem+json",
            "Cache-Control": "no-store",
          },
        },
      );
    if (request.nextUrl.pathname.startsWith("/api/")) {
      const target = apiOrigin();
      target.pathname = request.nextUrl.pathname;
      target.search = request.nextUrl.search;
      const headers = new Headers(request.headers);
      headers.delete("x-saveit-workload");
      const token = await workloadToken(request.signal);
      if (token) headers.set("x-saveit-workload", token);
      headers.delete("x-forwarded-for");
      headers.set(
        "x-forwarded-proto",
        origin?.protocol.slice(0, -1) || request.nextUrl.protocol.slice(0, -1),
      );
      headers.set("x-forwarded-host", origin?.host || request.nextUrl.host);
      const response = NextResponse.rewrite(target, { request: { headers } });
      response.headers.set("Cache-Control", "no-store");
      return response;
    }
    const nonce = Buffer.from(
      crypto.getRandomValues(new Uint8Array(16)),
    ).toString("base64");
    const policy = `default-src 'self'; script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${process.env.NODE_ENV === "development" ? " 'unsafe-eval'" : ""}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'${issuerOrigin()}`;
    const headers = new Headers(request.headers);
    headers.set("x-nonce", nonce);
    headers.set("Content-Security-Policy", policy);
    const response = NextResponse.next({ request: { headers } });
    response.headers.set("Content-Security-Policy", policy);
    response.headers.set("Cache-Control", "no-store");
    if (origin)
      response.headers.set("Strict-Transport-Security", "max-age=31536000");
    return response;
  } catch {
    return NextResponse.json(
      { title: "SaveIT deployment configuration is unavailable." },
      { status: 503, headers: { "Cache-Control": "no-store" } },
    );
  }
}

export const config = {
  matcher: "/((?!_next/static|_next/image|favicon.ico).*)",
};
