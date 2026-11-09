import { apiOrigin, publicOrigin } from "../../../lib/deployment";
import { workloadToken } from "../../../lib/workload.server";
export const dynamic = "force-dynamic";
export async function GET() {
  try {
    publicOrigin();
    const signal = AbortSignal.timeout(3000);
    const token = await workloadToken(signal);
    const result = await fetch(new URL("/health/ready", apiOrigin()), {
      headers: token ? { "X-SaveIT-Workload": token } : {},
      cache: "no-store",
      redirect: "error",
      signal,
    });
    if (result.status !== 200) throw new Error("Upstream is unavailable.");
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
