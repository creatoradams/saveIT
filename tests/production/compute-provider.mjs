// INTERNAL-network synthetic AWS, with no upstream forwarding or real credentials.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { createSocket } from "node:dgram";
import { readFileSync } from "node:fs";
import { createSecureContext } from "node:tls";
import { verifySyntheticSigV4 } from "./synthetic-sigv4.mjs";
const root = "/run/compute/";
const fixtures = JSON.parse(readFileSync(root + "observations.json", "utf8"));
const parked = JSON.parse(readFileSync(root + "parked.json", "utf8"));
const imageFixture = JSON.parse(readFileSync(root + "images.json", "utf8"));
const configurations = JSON.parse(readFileSync(root + "private.json", "utf8"));
const groupXml = readFileSync(root + "groups.xml", "utf8");
const registryHost = "111122223333.dkr.ecr.us-east-1.amazonaws.com";
const registryToken = Buffer.from(
  "AWS:synthetic-private-registry-password",
).toString("base64");
const access = "ASIA" + "0".repeat(16),
  secret = "a".repeat(40),
  token = "synthetic-temporary-session-token";
const counts = {
  sts: 0,
  ecs: 0,
  cluster: 0,
  services: 0,
  ecr: 0,
  authorization: 0,
  registry: 0,
  configuration: 0,
  groups: 0,
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
const goodContext = createSecureContext(options),
  wrongContext = createSecureContext({
    ...options,
    cert: readFileSync(root + "compute-wrong.crt"),
  });
options.SNICallback = (name, callback) =>
  callback(
    null,
    mode === "wrong-name" ||
      (mode === "images-registry-wrong-name" && name === registryHost) ||
      (mode === "groups-wrong-name" &&
        name === "ec2.us-east-1.amazonaws.com") ||
      (mode === "configuration-wrong-name" &&
        name === "secretsmanager.us-east-1.amazonaws.com")
      ? wrongContext
      : goodContext,
  );
function stall(response, body = false) {
  if (body) {
    response.writeHead(200, { "Content-Type": "application/octet-stream" });
    response.write("<");
  }
  const started = performance.now();
  const timer = setTimeout(() => {
    counts.stallFallbacks++;
    response.destroy();
  }, 30000);
  response.once("close", () => {
    clearTimeout(timer);
    if (performance.now() - started < 9000) counts.deadlineAborts++;
  });
}
const service = httpsServer(options, (request, response) => {
  let body = "";
  request.setTimeout(30000, () => request.destroy());
  request.on("data", (chunk) => {
    body += chunk;
    if (body.length > 2048) request.destroy();
  });
  request.on("end", () => {
    try {
      const selected =
        imageFixture.variants[mode] ??
        imageFixture.variants[
          mode.startsWith("groups-")
            ? "groups-valid"
            : mode.startsWith("configuration-")
              ? "configuration-valid"
              : "images-valid"
        ];
      if (request.headers.host === registryHost) {
        if (
          request.method !== "GET" ||
          body !== "" ||
          request.headers.authorization !== "Basic " + registryToken ||
          request.headers["x-amz-security-token"]
        )
          throw new Error();
        const image = Object.values(selected.images).find(
          (x) =>
            request.url ===
            "/v2/" +
              x.envelope.images[0].repositoryName +
              "/blobs/" +
              x.configDigest,
        );
        if (!image) throw new Error();
        counts.registry++;
        if (mode === "images-registry-redirect") {
          response.writeHead(302, {
            Location: "https://foreign-control.invalid/forbidden",
          });
          response.end();
          return;
        }
        if (mode === "images-registry-stall") {
          stall(response, true);
          return;
        }
        response.setHeader("Content-Type", "application/octet-stream");
        response.setHeader("Docker-Content-Digest", image.configDigest);
        response.end(image.body + (mode === "images-config-hash" ? " " : ""));
        return;
      }
      const kind = request.headers.host?.split(".")[0];
      if (!["sts", "ecs", "ec2", "api", "secretsmanager"].includes(kind))
        throw new Error();
      verifySyntheticSigV4(request, body, {
        access,
        secret,
        token,
        service: kind === "api" ? "ecr" : kind,
        host:
          kind === "api"
            ? "api.ecr.us-east-1.amazonaws.com"
            : kind + ".us-east-1.amazonaws.com",
      });
      const action =
        kind === "sts"
          ? "identity"
          : kind === "ec2"
            ? new URLSearchParams(body).get("Action")
            : request.headers["x-amz-target"]?.split(".")[1];
      if (
        ![
          "identity",
          "DescribeTaskDefinition",
          "DescribeClusters",
          "DescribeServices",
          "BatchGetImage",
          "GetAuthorizationToken",
          "GetSecretValue",
          "DescribeSecurityGroups",
        ].includes(action)
      )
        throw new Error();
      if (kind === "secretsmanager" && action !== "GetSecretValue")
        throw new Error();
      if (
        !{
          sts: ["identity"],
          ec2: ["DescribeSecurityGroups"],
          ecs: [
            "DescribeTaskDefinition",
            "DescribeClusters",
            "DescribeServices",
          ],
          api: ["BatchGetImage", "GetAuthorizationToken"],
          secretsmanager: ["GetSecretValue"],
        }[kind].includes(action)
      )
        throw new Error();
      if (
        kind === "api" &&
        !request.headers["x-amz-target"]?.startsWith(
          "AmazonEC2ContainerRegistry_V20150921.",
        )
      )
        throw new Error();
      counts[
        kind === "sts"
          ? "sts"
          : action === "DescribeTaskDefinition"
            ? "ecs"
            : action === "DescribeClusters"
              ? "cluster"
              : action === "DescribeServices"
                ? "services"
                : action === "BatchGetImage"
                  ? "ecr"
                  : action === "DescribeSecurityGroups"
                    ? "groups"
                    : action === "GetSecretValue"
                      ? "configuration"
                      : "authorization"
      ]++;
      if (kind === "ec2") {
        const query = new URLSearchParams(body);
        const expected = {
          Action: "DescribeSecurityGroups",
          Version: "2016-11-15",
          "Filter.1.Name": "vpc-id",
          "Filter.1.Value.1": "vpc-00000000000000001",
          MaxResults: "1000",
        };
        if (
          request.headers["x-amz-target"] ||
          [...query].length !== 5 ||
          Object.entries(expected).some(
            ([key, value]) => query.get(key) !== value,
          )
        )
          throw new Error();
        if (mode === "groups-redirect") {
          response.writeHead(302, {
            Location: "https://foreign-control.invalid/forbidden",
          });
          response.end();
          return;
        }
        if (mode === "groups-stall") {
          stall(response, true);
          return;
        }
        let xml = groupXml;
        if (mode === "groups-unknown")
          xml = xml.replace(
            "</DescribeSecurityGroupsResponse>",
            "<extraAuthority>private-marker</extraAuthority></DescribeSecurityGroupsResponse>",
          );
        if (mode === "groups-duplicate")
          xml = xml.replace(
            "</requestId>",
            "</requestId><requestId>duplicate</requestId>",
          );
        if (mode === "groups-next-page")
          xml = xml.replace(
            "</DescribeSecurityGroupsResponse>",
            "<nextToken>private-next-page</nextToken></DescribeSecurityGroupsResponse>",
          );
        if (mode === "groups-foreign-owner")
          xml = xml.replace(
            "<ownerId>111122223333</ownerId>",
            "<ownerId>444455556666</ownerId>",
          );
        response.setHeader("Content-Type", "text/xml");
        response.end(xml);
        return;
      }
      if (kind === "secretsmanager") {
        if (request.headers["x-amz-target"] !== "secretsmanager.GetSecretValue")
          throw new Error();
        const query = JSON.parse(body);
        const process = ["api", "worker", "relay"].find(
          (p) => selected.contract.tasks[p].configurationArn === query.SecretId,
        );
        if (
          !process ||
          Object.keys(query).sort().join(",") !== "SecretId,VersionId" ||
          query.VersionId !==
            selected.contract.tasks[process].configurationVersionId
        )
          throw new Error();
        if (mode === "configuration-redirect") {
          response.writeHead(302, {
            Location: "https://foreign-control.invalid/forbidden",
          });
          response.end();
          return;
        }
        if (mode === "configuration-stall") {
          stall(response, true);
          return;
        }
        const payload = structuredClone(configurations[process]);
        if (mode === "configuration-extra" && process === "api")
          payload.Identity__KeyCertificatePath =
            "synthetic-private-cohort-marker";
        if (mode === "configuration-api-name" && process === "api")
          payload.Deployment__ApiTls__CertificateBase64 =
            configurations.relay.ServerCertificateBase64;
        if (mode === "configuration-db" && process === "worker")
          payload.ConnectionStrings__SearchWorker =
            payload.ConnectionStrings__SearchWorker.replace(
              "db.synthetic.invalid",
              "foreign.synthetic.invalid",
            );
        if (mode === "configuration-key-reuse" && process === "api") {
          payload.Identity__KeyCertificateBase64 =
            configurations.relay.CertificateBase64;
          payload.Identity__KeyCertificatePassword =
            configurations.relay.CertificatePassword;
        }
        const result = {
          ARN: query.SecretId,
          Name: `saveit/pilot/staging/${process}/config`,
          VersionId: query.VersionId,
          SecretString: JSON.stringify(payload),
          VersionStages: ["AWSPREVIOUS"],
        };
        if (mode === "configuration-version")
          result.VersionId = "90000000-0000-0000-0000-000000000001";
        if (mode === "configuration-binary") result.SecretBinary = null;
        if (mode === "configuration-unknown")
          result.privateAuthority = "synthetic-private-cohort-marker";
        let serialized = JSON.stringify(result);
        if (mode === "configuration-duplicate")
          serialized = serialized.replace(
            '"VersionId":',
            '"VersionId":"foreign","VersionI\\u0064":',
          );
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(serialized);
        return;
      }
      if (mode === "redirect") {
        response.writeHead(302, {
          Location: "https://foreign-control.invalid/forbidden",
        });
        response.end();
        return;
      }
      if (mode === "stall") {
        stall(response);
        return;
      }
      if (mode === "stall-body") {
        stall(response, true);
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
      if (action === "BatchGetImage" || action === "GetAuthorizationToken") {
        const query = JSON.parse(body);
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        if (action === "GetAuthorizationToken") {
          if (Object.keys(query).length !== 0) throw new Error();
          response.end(
            JSON.stringify({
              authorizationData: [
                {
                  authorizationToken: registryToken,
                  expiresAt: Math.floor(Date.now() / 1000) + 43000,
                  proxyEndpoint:
                    mode === "images-auth-foreign"
                      ? "https://foreign-control.invalid"
                      : "https://" + registryHost,
                },
              ],
            }),
          );
          return;
        }
        const image = Object.values(selected.images).find(
          (x) => x.envelope.images[0].repositoryName === query.repositoryName,
        );
        if (
          !image ||
          Object.keys(query).sort().join(",") !==
            "acceptedMediaTypes,imageIds,registryId,repositoryName" ||
          query.registryId !== "111122223333" ||
          JSON.stringify(query.imageIds) !==
            JSON.stringify([image.envelope.images[0].imageId]) ||
          JSON.stringify(query.acceptedMediaTypes) !==
            JSON.stringify([
              "application/vnd.docker.distribution.manifest.v2+json",
              "application/vnd.oci.image.manifest.v1+json",
            ])
        )
          throw new Error();
        const result = structuredClone(image.envelope);
        if (mode === "images-manifest-hash")
          result.images[0].imageManifest += " ";
        response.end(JSON.stringify(result));
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
      const match = Object.values(
        mode.startsWith("images-") ||
          mode.startsWith("configuration-") ||
          mode.startsWith("groups-")
          ? selected.tasks
          : fixtures,
      ).find(
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
          "images-valid",
          "images-root",
          "images-entrypoint",
          "images-env",
          "images-index",
          "images-manifest-hash",
          "images-auth-foreign",
          "images-config-hash",
          "images-registry-redirect",
          "images-registry-wrong-name",
          "images-registry-stall",
          "configuration-valid",
          "configuration-version",
          "configuration-binary",
          "configuration-unknown",
          "configuration-duplicate",
          "configuration-extra",
          "configuration-api-name",
          "configuration-db",
          "configuration-key-reuse",
          "configuration-redirect",
          "configuration-wrong-name",
          "configuration-stall",
          "groups-valid",
          "groups-unknown",
          "groups-duplicate",
          "groups-next-page",
          "groups-foreign-owner",
          "groups-redirect",
          "groups-wrong-name",
          "groups-stall",
        ].includes(body)
      ) {
        response.writeHead(400);
        response.end();
        return;
      }
      mode = body;
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
