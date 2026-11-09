export const dynamic = "force-dynamic";
export const GET = () =>
  Response.json(
    { status: "live" },
    { headers: { "Cache-Control": "no-store" } },
  );
