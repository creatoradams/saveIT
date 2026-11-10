// INTERNAL-network synthetic AWS, with no upstream forwarding or real credentials.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { createSocket } from "node:dgram";
import { readFileSync } from "node:fs";
import { verifySyntheticSigV4 } from "./synthetic-sigv4.mjs";
const root = "/run/compute/";
const fixtures = JSON.parse(readFileSync(root + "observations.json", "utf8"));
const parked = JSON.parse(readFileSync(root + "parked.json", "utf8"));
const access = "ASIA" + "0".repeat(16),
  secret = "a".repeat(40),
  token = "synthetic-temporary-session-token";
const counts = {
  sts: 0,
  ecs: 0,
  cluster: 0,
  services: 0,
  proxy: 0,
  monitoring: 0,
  unexpected: 0,
  deadlineAborts: 0,
  stallFallbacks: 0,
};
let mode = "valid";
const options = {
  key: readFileSync(root + "compute.key"),
  cert: readFileSync(root + "compute.crt"),
  minVersion: "TLSv1.2",
};
const service = httpsServer(options, (request, response) => {
  let body = "";
  request.setTimeout(30000, () => request.destroy());
  request.on("data", (chunk) => {
    body += chunk;
    if (body.length > 2048) request.destroy();
  });
  request.on("end", () => {
    try {
      const kind = request.headers.host?.split(".")[0];
      if (!["sts", "ecs"].includes(kind)) throw new Error();
      verifySyntheticSigV4(request, body, {
        access,
        secret,
        token,
        service: kind,
      });
      const action =
        kind === "sts"
          ? "identity"
          : request.headers["x-amz-target"]?.split(".")[1];
      if (
        ![
          "identity",
          "DescribeTaskDefinition",
          "DescribeClusters",
          "DescribeServices",
        ].includes(action)
      )
        throw new Error();
      counts[
        kind === "sts"
          ? "sts"
          : action === "DescribeTaskDefinition"
            ? "ecs"
            : action === "DescribeClusters"
              ? "cluster"
              : "services"
      ]++;
      if (mode === "redirect") {
        response.writeHead(302, { Location: "https://foreign.invalid/" });
        response.end();
        return;
      }
      if (mode === "stall") {
        const started = performance.now();
        const timer = setTimeout(() => {
          counts.stallFallbacks++;
          response.destroy();
        }, 30000);
        response.once("close", () => {
          clearTimeout(timer);
          if (performance.now() - started < 9000) counts.deadlineAborts++;
        });
        return;
      }
      if (mode === "stall-body") {
        response.writeHead(200, { "Content-Type": "text/xml" });
        response.write("<");
        const started = performance.now();
        const timer = setTimeout(() => {
          counts.stallFallbacks++;
          response.destroy();
        }, 30000);
        response.once("close", () => {
          clearTimeout(timer);
          if (performance.now() - started < 9000) counts.deadlineAborts++;
        });
        return;
      }
      if (kind === "sts") {
        if (
          ![
            "Action=GetCallerIdentity&Version=2011-06-15",
            "Version=2011-06-15&Action=GetCallerIdentity",
          ].includes(body) ||
          request.headers["x-amz-target"]
        )
          throw new Error();
        const role =
          mode === "foreign-role" ? "api-task" : "compute-controller";
        response.setHeader("Content-Type", "text/xml");
        response.end(
          `<GetCallerIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/"><GetCallerIdentityResult><Arn>arn:aws:sts::111122223333:assumed-role/saveit-pilot-staging-${role}/synthetic</Arn><UserId>AROA00000000000000000:synthetic</UserId><Account>111122223333</Account></GetCallerIdentityResult><ResponseMetadata><RequestId>synthetic-request</RequestId></ResponseMetadata></GetCallerIdentityResponse>`,
        );
        return;
      }
      if (action === "DescribeClusters" || action === "DescribeServices") {
        const query = JSON.parse(body),
          clusterArn = parked.cluster.clusters[0].clusterArn;
        const expected =
          action === "DescribeClusters"
            ? {
                clusters: [clusterArn],
                include: ["ATTACHMENTS", "CONFIGURATIONS", "SETTINGS", "TAGS"],
              }
            : {
                cluster: clusterArn,
                services: parked.services.services.map((x) => x.serviceArn),
                include: ["TAGS"],
              };
        if (
          JSON.stringify(Object.keys(query).sort()) !==
            JSON.stringify(Object.keys(expected).sort()) ||
          Object.keys(expected).some(
            (k) => JSON.stringify(query[k]) !== JSON.stringify(expected[k]),
          )
        )
          throw new Error();
        const result = structuredClone(
          action === "DescribeClusters" ? parked.cluster : parked.services,
        );
        if (mode === "foreign-cluster" && action === "DescribeClusters")
          result.clusters[0].clusterArn =
            "arn:aws:ecs:us-east-1:111122223333:cluster/foreign";
        if (action === "DescribeServices") {
          const service = result.services[0];
          if (mode === "active-service") service.runningCount = 1;
          if (mode === "unknown-service")
            service.unknownAuthority = { unexpected: true };
          if (mode === "extra-group")
            service.networkConfiguration.awsvpcConfiguration.securityGroups.push(
              "sg-00000000000000004",
            );
          if (mode === "service-load-balancer")
            service.loadBalancers = [{ targetGroupArn: "foreign" }];
        }
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(JSON.stringify(result));
        return;
      }
      if (
        request.headers["x-amz-target"] !==
        "AmazonEC2ContainerServiceV20141113.DescribeTaskDefinition"
      )
        throw new Error();
      const query = JSON.parse(body);
      if (
        Object.keys(query).sort().join(",") !== "include,taskDefinition" ||
        JSON.stringify(query.include) !== '["TAGS"]'
      )
        throw new Error();
      const match = Object.values(fixtures).find(
        (x) => x.taskDefinition.taskDefinitionArn === query.taskDefinition,
      );
      if (!match) throw new Error();
      const result = structuredClone(match);
      if (mode === "unknown-task")
        result.taskDefinition.unknownAuthority = { unexpected: true };
      response.setHeader("Content-Type", "application/x-amz-json-1.1");
      response.end(JSON.stringify(result));
    } catch {
      counts.unexpected++;
      response.writeHead(400);
      response.end("{}");
    }
  });
  request.on("error", () => {});
  response.on("error", () => {});
});
service.on("tlsClientError", () => {});
httpServer((request, response) => {
  if (request.url === "/counts" && request.method === "GET") {
    response.end(JSON.stringify(counts));
    return;
  }
  if (request.url === "/mode" && request.method === "POST") {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
      if (body.length > 32) request.destroy();
    });
    request.on("end", () => {
      if (
        ![
          "valid",
          "foreign-role",
          "unknown-task",
          "wrong-name",
          "redirect",
          "stall",
          "stall-body",
          "foreign-cluster",
          "active-service",
          "unknown-service",
          "extra-group",
          "service-load-balancer",
        ].includes(body)
      ) {
        response.writeHead(400);
        response.end();
        return;
      }
      mode = body;
      service.setSecureContext({
        ...options,
        cert: readFileSync(
          root + (body === "wrong-name" ? "compute-wrong.crt" : "compute.crt"),
        ),
      });
      response.end("ok");
    });
    return;
  }
  counts.unexpected++;
  response.writeHead(400);
  response.end();
}).listen(80, "0.0.0.0");
const proxy = httpServer((request, response) => {
  counts.proxy++;
  response.end("active");
  request.resume();
});
proxy.on("connect", (_request, socket) => {
  counts.proxy++;
  socket.end(
    "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
  );
  socket.on("error", () => {});
});
proxy.listen(18080, "0.0.0.0");
const monitor = createSocket("udp4");
monitor.on("message", () => counts.monitoring++);
monitor.bind(31000, "0.0.0.0");
service.listen(443, "0.0.0.0");
