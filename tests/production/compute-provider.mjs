// INTERNAL-network synthetic AWS, with no upstream forwarding or real credentials.
import { createServer as httpServer } from "node:http";
import { createServer as httpsServer } from "node:https";
import { createSocket } from "node:dgram";
import { readFileSync } from "node:fs";
import { createSecureContext } from "node:tls";
import { isDeepStrictEqual } from "node:util";
import { verifySyntheticSigV4 } from "./synthetic-sigv4.mjs";
const root = "/run/compute/";
const fixtures = JSON.parse(readFileSync(root + "observations.json", "utf8"));
const parked = JSON.parse(readFileSync(root + "parked.json", "utf8"));
const imageFixture = JSON.parse(readFileSync(root + "images.json", "utf8"));
const configurations = JSON.parse(readFileSync(root + "private.json", "utf8"));
const groupXml = readFileSync(root + "groups.xml", "utf8");
const routeObservations = Object.fromEntries(
  Object.entries(JSON.parse(readFileSync(root + "routes.json", "utf8"))).map(
    ([name, lines]) => [name, lines.join("\n")],
  ),
);
const endpointObservations = JSON.parse(
  readFileSync(root + "endpoints.json", "utf8"),
);
const resolverObservations = JSON.parse(
  readFileSync(root + "resolver.json", "utf8"),
);
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
  parkedTasks: 0,
  discovery: 0,
  dns: 0,
  rds: 0,
  ecr: 0,
  authorization: 0,
  registry: 0,
  configuration: 0,
  groups: 0,
  routes: 0,
  endpoints: 0,
  prefix: 0,
  dhcp: 0,
  interfaces: 0,
  resolver: 0,
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
      const family = mode.split("-")[0];
      const networkBound = [
        "groups",
        "routes",
        "endpoints",
        "network",
        "resolver",
        "rds",
      ].includes(family);
      const packagedTasks =
        networkBound || family === "configuration" || family === "images";
      const fallback = networkBound
        ? "groups-valid"
        : family === "configuration"
          ? "configuration-valid"
          : "images-valid";
      const selected =
        imageFixture.variants[mode] ?? imageFixture.variants[fallback];
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
      const zonePath = "/2013-04-01/hostedzone/Z0000000000000001";
      const records = request.url === zonePath + "/rrset?maxitems=10";
      verifySyntheticSigV4(request, body, {
        access,
        secret,
        token,
        service: kind === "api" ? "ecr" : kind,
        ...(kind === "route53"
          ? {
              host: "route53.amazonaws.com",
              method: "GET",
              path: records ? zonePath + "/rrset" : zonePath,
              query: records ? "maxitems=10" : "",
            }
          : {
              host:
                kind === "api"
                  ? "api.ecr.us-east-1.amazonaws.com"
                  : kind + ".us-east-1.amazonaws.com",
            }),
      });
      if (kind === "route53") {
        if (family !== "dns" || body !== "") throw new Error();
        const zone = "pilot.staging.saveit.internal.";
        const id = "Z0000000000000001";
        counts.dns++;
        const ns = ' xmlns="https://route53.amazonaws.com/doc/2013-04-01/"';
        const entry = (name, type, values) =>
          `<ResourceRecordSet><Name>${name}</Name><Type>${type}</Type><TTL>900</TTL><ResourceRecords>${values.map((value) => `<ResourceRecord><Value>${value}</Value></ResourceRecord>`).join("")}</ResourceRecords></ResourceRecordSet>`;
        const soa = entry(zone, "SOA", [
          "ns-0.awsdns-00.com. hostmaster.awsdns.com. 1 7200 900 1209600 86400",
        ]);
        const names = entry(zone, "NS", [
          "ns-0.awsdns-00.com.",
          "ns-512.awsdns-00.net.",
          "ns-1024.awsdns-00.org.",
          "ns-1536.awsdns-00.co.uk.",
        ]);
        const stale =
          mode === "dns-stale" ? entry("api." + zone, "A", ["192.0.2.1"]) : "";
        const vpc =
          mode === "dns-foreign"
            ? "vpc-00000000000000002"
            : "vpc-00000000000000001";
        response.setHeader("Content-Type", "text/xml");
        response.end(
          records
            ? `<ListResourceRecordSetsResponse${ns}><ResourceRecordSets>${soa}${names}${stale}</ResourceRecordSets><IsTruncated>false</IsTruncated><MaxItems>10</MaxItems></ListResourceRecordSetsResponse>`
            : `<GetHostedZoneResponse${ns}><HostedZone><Id>/hostedzone/${id}</Id><Name>${zone}</Name><CallerReference>synthetic</CallerReference><Config><PrivateZone>true</PrivateZone></Config><ResourceRecordSetCount>2</ResourceRecordSetCount></HostedZone><VPCs><VPC><VPCId>${vpc}</VPCId><VPCRegion>us-east-1</VPCRegion></VPC></VPCs></GetHostedZoneResponse>`,
        );
        return;
      }
      if (
        ![
          "sts",
          "ecs",
          "ec2",
          "api",
          "secretsmanager",
          "route53resolver",
          "servicediscovery",
          "rds",
        ].includes(kind)
      )
        throw new Error();
      const action =
        kind === "sts"
          ? "identity"
          : ["ec2", "rds"].includes(kind)
            ? new URLSearchParams(body).get("Action")
            : request.headers["x-amz-target"]?.split(".")[1];
      if (
        !{
          sts: ["identity"],
          ec2: [
            "DescribeSecurityGroups",
            "DescribeVpcs",
            "DescribeVpcAttribute",
            "DescribeSubnets",
            "DescribeRouteTables",
            "DescribeVpcEndpoints",
            "DescribeManagedPrefixLists",
            "DescribeDhcpOptions",
            "DescribeNetworkInterfaces",
          ],
          ecs: [
            "DescribeTaskDefinition",
            "DescribeClusters",
            "DescribeServices",
            "ListTasks",
          ],
          api: ["BatchGetImage", "GetAuthorizationToken"],
          secretsmanager: ["GetSecretValue"],
          route53resolver: Object.keys(resolverObservations),
          servicediscovery: ["GetNamespace", "GetService", "ListInstances"],
          rds: ["DescribeDBInstances"],
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
      const counter = {
        identity: "sts",
        DescribeTaskDefinition: "ecs",
        DescribeClusters: "cluster",
        DescribeServices: "services",
        ListTasks: "parkedTasks",
        BatchGetImage: "ecr",
        GetAuthorizationToken: "authorization",
        GetSecretValue: "configuration",
        DescribeSecurityGroups: "groups",
        DescribeVpcs: "routes",
        DescribeVpcAttribute: "routes",
        DescribeSubnets: "routes",
        DescribeRouteTables: "routes",
        DescribeVpcEndpoints: "endpoints",
        DescribeManagedPrefixLists: "prefix",
        DescribeDhcpOptions: "dhcp",
        DescribeNetworkInterfaces: "interfaces",
        ListResolverRuleAssociations: "resolver",
        ListFirewallRuleGroupAssociations: "resolver",
        GetFirewallConfig: "resolver",
        GetNamespace: "discovery",
        GetService: "discovery",
        ListInstances: "discovery",
        DescribeDBInstances: "rds",
      }[action];
      counts[counter]++;
      if (kind === "rds") {
        const query = new URLSearchParams(body);
        if (
          family !== "rds" ||
          [...query].length !== 3 ||
          request.headers["x-amz-target"] ||
          query.get("Version") !== "2014-10-31" ||
          query.get("DBInstanceIdentifier") !==
            "arn:aws:rds:us-east-1:111122223333:db:saveit-pilot-staging"
        )
          throw new Error();
        let xml = readFileSync(root + "rds.xml", "utf8");
        if (mode === "rds-public")
          xml = xml.replace(
            "<PubliclyAccessible>false",
            "<PubliclyAccessible>true",
          );
        if (mode === "rds-target")
          xml = xml.replace(
            "db.synthetic.invalid",
            "foreign.synthetic.invalid",
          );
        if (mode === "rds-subnet")
          xml = xml.replace(
            "subnet-00000000000000003",
            "subnet-00000000000000001",
          );
        response.setHeader("Content-Type", "text/xml");
        response.end(xml);
        return;
      }
      if (kind === "servicediscovery") {
        if (
          family !== "dns" ||
          request.headers["x-amz-target"] !==
            "Route53AutoNaming_v20170314." + action
        )
          throw new Error();
        const namespaceId = "ns-0000000000000001";
        const base = "arn:aws:servicediscovery:us-east-1:111122223333:";
        const query = JSON.parse(body);
        let result;
        if (action === "GetNamespace") {
          const arn = base + "namespace/" + namespaceId;
          if (!isDeepStrictEqual(query, { Id: arn })) throw new Error();
          result = {
            Namespace: {
              Arn: arn,
              Id: namespaceId,
              Name: "pilot.staging.saveit.internal",
              Type: "DNS_PRIVATE",
              Properties: {
                DnsProperties: { HostedZoneId: "Z0000000000000001" },
              },
            },
          };
        } else {
          const arn = query.Id ?? query.ServiceId;
          const process = ["api", "relay"].find(
            (name) =>
              parked.services.services.find(
                (s) => s.serviceName === "saveit-pilot-staging-" + name,
              ).serviceRegistries[0].registryArn === arn,
          );
          if (!process) throw new Error();
          if (action === "ListInstances") {
            if (!isDeepStrictEqual(query, { ServiceId: arn, MaxResults: 100 }))
              throw new Error();
            result = {
              Instances:
                mode === "dns-instance" ? [{ Id: "synthetic-instance" }] : [],
            };
          } else {
            if (!isDeepStrictEqual(query, { Id: arn })) throw new Error();
            result = {
              Service: {
                Arn: arn,
                Id: arn.split("/")[1],
                Name: process,
                NamespaceId: namespaceId,
                DnsConfig: {
                  RoutingPolicy: "MULTIVALUE",
                  DnsRecords: [{ Type: "A", TTL: 10 }],
                },
                HealthCheckCustomConfig: { FailureThreshold: 1 },
              },
            };
          }
        }
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(JSON.stringify(result));
        return;
      }
      if (action === "ListTasks") {
        const expected = {
          cluster:
            "arn:aws:ecs:us-east-1:111122223333:cluster/saveit-pilot-staging",
          desiredStatus: "RUNNING",
          maxResults: 100,
        };
        if (
          !["inventory", "dns"].includes(family) ||
          request.headers["x-amz-target"] !==
            "AmazonEC2ContainerServiceV20141113.ListTasks" ||
          !isDeepStrictEqual(JSON.parse(body), expected)
        )
          throw new Error();
        const result = { taskArns: [] };
        if (mode === "inventory-active")
          result.taskArns.push(
            "arn:aws:ecs:us-east-1:111122223333:task/saveit-pilot-staging/00000000000000000000000000000001",
          );
        if (mode === "inventory-page") result.nextToken = "synthetic-page";
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(JSON.stringify(result));
        return;
      }
      if (kind === "route53resolver") {
        const vpc = "vpc-00000000000000001";
        const expected = {
          ListResolverRuleAssociations: {
            Filters: [{ Name: "VPCId", Values: [vpc] }],
            MaxResults: 100,
          },
          ListFirewallRuleGroupAssociations: { VpcId: vpc, MaxResults: 100 },
          GetFirewallConfig: { ResourceId: vpc },
        };
        if (
          family !== "resolver" ||
          request.headers["x-amz-target"] !== "Route53Resolver." + action ||
          !isDeepStrictEqual(JSON.parse(body), expected[action])
        )
          throw new Error();
        const result = structuredClone(resolverObservations[action]);
        if (
          action === "ListResolverRuleAssociations" &&
          mode === "resolver-forward"
        )
          result.ResolverRuleAssociations.push({
            ResolverRuleId: "rslvr-rr-synthetic",
          });
        if (
          action === "ListFirewallRuleGroupAssociations" &&
          mode === "resolver-group"
        )
          result.FirewallRuleGroupAssociations.push({ Priority: 100 });
        if (
          action === "ListFirewallRuleGroupAssociations" &&
          mode === "resolver-page"
        )
          result.NextToken = "synthetic-next-page";
        if (action === "GetFirewallConfig" && mode === "resolver-owner")
          result.FirewallConfig.OwnerId = "444455556666";
        if (action === "GetFirewallConfig" && mode === "resolver-open")
          result.FirewallConfig.FirewallFailOpen = "ENABLED";
        response.setHeader("Content-Type", "application/x-amz-json-1.1");
        response.end(JSON.stringify(result));
        return;
      }
      if (kind === "ec2") {
        const query = new URLSearchParams(body);
        const vpc = "vpc-00000000000000001";
        const expected = { Action: action, Version: "2016-11-15" };
        const attribute = query.get("Attribute");
        if (action === "DescribeManagedPrefixLists")
          expected["PrefixListId.1"] = "pl-00000001";
        else if (action === "DescribeDhcpOptions")
          expected["DhcpOptionsId.1"] = "dopt-00000001";
        else if (action === "DescribeVpcs") expected["VpcId.1"] = vpc;
        else if (action === "DescribeVpcAttribute") {
          if (!["enableDnsSupport", "enableDnsHostnames"].includes(attribute))
            throw new Error();
          Object.assign(expected, { VpcId: vpc, Attribute: attribute });
        } else
          Object.assign(expected, {
            "Filter.1.Name": "vpc-id",
            "Filter.1.Value.1": vpc,
            MaxResults: [
              "DescribeRouteTables",
              "DescribeVpcEndpoints",
              "DescribeNetworkInterfaces",
            ].includes(action)
              ? "100"
              : "1000",
          });
        if (action === "DescribeNetworkInterfaces")
          Object.assign(expected, {
            "Filter.2.Name": "interface-type",
            "Filter.2.Value.1": "vpc_endpoint",
          });
        if (
          request.headers["x-amz-target"] ||
          [...query].length !== Object.keys(expected).length ||
          Object.entries(expected).some(
            ([key, value]) => query.get(key) !== value,
          )
        )
          throw new Error();
        if (["endpoints", "prefix", "dhcp", "interfaces"].includes(counter)) {
          if (
            !["network", "resolver"].includes(family) &&
            (family !== "endpoints" ||
              !["endpoints", "prefix"].includes(counter))
          )
            throw new Error();
          if (mode === "endpoints-stall" && counter === "prefix") {
            stall(response, true);
            return;
          }
          let xml = endpointObservations[counter].join("\n");
          if (mode === "endpoints-policy" && counter === "endpoints")
            xml = xml.replace("RejectInsecureTransport", "DifferentPolicy");
          if (mode === "endpoints-group" && counter === "endpoints")
            xml = xml.replace(
              "<groupId>sg-00000000000000008</groupId>",
              "<groupId>sg-00000000000000009</groupId>",
            );
          if (mode === "endpoints-next-page" && counter === "endpoints")
            xml = xml.replace(
              "</DescribeVpcEndpointsResponse>",
              "<nextToken>synthetic-next</nextToken></DescribeVpcEndpointsResponse>",
            );
          if (mode === "endpoints-prefix-owner" && counter === "prefix")
            xml = xml.replace(
              "<ownerId>AWS</ownerId>",
              "<ownerId>111122223333</ownerId>",
            );
          if (mode === "network-custom-dns" && counter === "dhcp")
            xml = xml.replace(
              "<value>AmazonProvidedDNS</value>",
              "<value>10.84.0.10</value>",
            );
          if (mode === "network-foreign-interface" && counter === "interfaces")
            xml = xml.replace(
              "<ownerId>111122223333</ownerId>",
              "<ownerId>444455556666</ownerId>",
            );
          if (mode === "network-interface-page" && counter === "interfaces")
            xml = xml.replace(
              "</DescribeNetworkInterfacesResponse>",
              "<nextToken>synthetic-next</nextToken></DescribeNetworkInterfacesResponse>",
            );
          response.setHeader("Content-Type", "text/xml");
          response.end(xml);
          return;
        }
        if (action !== "DescribeSecurityGroups") {
          if (
            !["routes", "endpoints", "network", "resolver", "rds"].includes(
              family,
            )
          )
            throw new Error();
          const stage = {
            DescribeVpcs: "vpc",
            DescribeVpcAttribute: attribute,
            DescribeSubnets: "subnets",
            DescribeRouteTables: "routes",
          }[action];
          if (mode === "routes-stall" && stage === "routes") {
            stall(response, true);
            return;
          }
          let xml = routeObservations[stage];
          if (mode === "routes-vpc-ipv6" && stage === "vpc")
            xml = xml.replace(
              "</ipv6CidrBlockAssociationSet>",
              "<item><ipv6CidrBlock>2001:db8::/56</ipv6CidrBlock></item></ipv6CidrBlockAssociationSet>",
            );
          if (mode === "routes-disabled-dns" && stage === "enableDnsSupport")
            xml = xml.replace("<value>true</value>", "<value>false</value>");
          if (mode === "routes-subnet-public" && stage === "subnets")
            xml = xml.replace(
              "<mapPublicIpOnLaunch>false",
              "<mapPublicIpOnLaunch>true",
            );
          if (mode === "routes-main-public" && stage === "routes") {
            const closing = xml.lastIndexOf("</routeSet>");
            xml =
              xml.slice(0, closing) +
              "<item><gatewayId>igw-00000000000000001</gatewayId><destinationCidrBlock>0.0.0.0/0</destinationCidrBlock><state>active</state><origin>CreateRoute</origin></item>" +
              xml.slice(closing);
          }
          if (mode === "routes-next-page" && stage === "routes")
            xml = xml.replace(
              "</DescribeRouteTablesResponse>",
              "<nextToken>synthetic-next-page</nextToken></DescribeRouteTablesResponse>",
            );
          if (mode === "routes-association" && stage === "routes")
            xml = xml.replace(
              "<subnetId>subnet-00000000000000001</subnetId>",
              "",
            );
          response.setHeader("Content-Type", "text/xml");
          response.end(xml);
          return;
        }
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
        packagedTasks ? selected.tasks : fixtures,
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
          "routes-valid",
          "routes-vpc-ipv6",
          "routes-disabled-dns",
          "routes-subnet-public",
          "routes-main-public",
          "routes-next-page",
          "routes-association",
          "routes-stall",
          "endpoints-valid",
          "endpoints-policy",
          "endpoints-group",
          "endpoints-next-page",
          "endpoints-prefix-owner",
          "endpoints-stall",
          "network-valid",
          "network-custom-dns",
          "network-foreign-interface",
          "network-interface-page",
          "resolver-valid",
          "resolver-forward",
          "resolver-group",
          "resolver-page",
          "resolver-owner",
          "resolver-open",
          "inventory-valid",
          "inventory-active",
          "inventory-page",
          "dns-valid",
          "dns-foreign",
          "dns-stale",
          "dns-instance",
          "rds-valid",
          "rds-public",
          "rds-target",
          "rds-subnet",
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
