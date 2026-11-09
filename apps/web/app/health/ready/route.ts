import { upstreamHealth } from "../../../lib/health.server";
export const dynamic = "force-dynamic";
export const GET = () => upstreamHealth("/health/ready");
