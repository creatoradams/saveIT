import { apiOrigin, publicOrigin } from "../../../lib/deployment";
export const dynamic = "force-dynamic";
export async function GET() {
  try {
    publicOrigin();
    const result = await fetch(new URL("/health/ready", apiOrigin()), {
      cache: "no-store",
      signal: AbortSignal.timeout(3000),
    });
    if (!result.ok) throw new Error("Upstream is unavailable.");
    return Response.json(
      { status: "ready" },
      { headers: { "Cache-Control": "no-store" } },
    );
  } catch {
    return Response.json(
      { status: "unavailable" },
      { status: 503, headers: { "Cache-Control": "no-store" } },
    );
  }
}
