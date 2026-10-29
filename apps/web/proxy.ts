import { NextResponse, type NextRequest } from "next/server";

export function proxy(request: NextRequest) {
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
  return NextResponse.next();
}

export const config = { matcher: "/api/:path*" };
