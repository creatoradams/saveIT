// Separate unauthenticated synthetic sink: no registry/SDK data or authority.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { readFileSync } from "node:fs";
let requests = 0;
httpsServer(
  {
    key: readFileSync("/run/compute/compute.key"),
    cert: readFileSync("/run/compute/compute.crt"),
    minVersion: "TLSv1.2",
  },
  (request, response) => {
    requests++;
    request.resume();
    response.end("active");
  },
).listen(443, "0.0.0.0");
httpServer((request, response) => {
  if (request.method === "GET" && request.url === "/counts")
    response.end(JSON.stringify({ requests }));
  else {
    response.writeHead(400);
    response.end();
  }
}).listen(80, "0.0.0.0");
