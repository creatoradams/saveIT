using Microsoft.Extensions.Hosting;

// Deliberately no dispatcher until a real consumer and idempotent delivery exist.
// Unpublished events remain durable in PostgreSQL; this host is a scaffold only.
var builder = Host.CreateApplicationBuilder(args);
await builder.Build().RunAsync();
