import "server-only";
import { apiOrigin, publicOrigin } from "./deployment";
import { workloadToken } from "./workload.server";

export async function upstreamHealth(
  path: "/health/ready" | "/health/startup",
) {
  try {
    publicOrigin();
    const signal = AbortSignal.timeout(3000);
    const token = await workloadToken(signal);
    const result = await fetch(new URL(path, apiOrigin()), {
      headers: token ? { "X-SaveIT-Workload": token } : {},
      cache: "no-store",
      redirect: "error",
      signal,
    });
    if (result.status !== 200) throw new Error();
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
