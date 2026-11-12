// Original query XML from the independently frozen producer graph; synthetic only.
import assert from "node:assert/strict";
export function computeNetworkFixture(imageFixture, fixture) {
  const selected = structuredClone(
    imageFixture.variants["configuration-valid"],
  );
  selected.contract.network.bindings = structuredClone(fixture.bindings);
  imageFixture.variants["groups-valid"] = selected;
  const ids = {
    ...selected.contract.network.securityGroups,
    ...fixture.bindings.securityGroups,
    default: fixture.defaultGroupId,
  };
  assert.equal(new Set(Object.values(ids)).size, 13);
  const permissions = (role, inbound) => {
    const buckets = new Map();
    for (const edge of fixture.edges.filter(
      (e) => e[inbound ? "to" : "from"] === role,
    )) {
      const bucket = buckets.get(edge.port) ?? { groups: [], prefixes: [] };
      if (edge.to === "layers")
        bucket.prefixes.push(
          `<item><prefixListId>${fixture.bindings.s3PrefixListId}</prefixListId></item>`,
        );
      else
        bucket.groups.push(
          `<item><userId>111122223333</userId><groupId>${ids[edge[inbound ? "from" : "to"]]}</groupId></item>`,
        );
      buckets.set(edge.port, bucket);
    }
    return [...buckets]
      .map(
        ([port, values]) =>
          `<item><ipProtocol>tcp</ipProtocol><fromPort>${port}</fromPort><toPort>${port}</toPort><groups>${values.groups.join("")}</groups><ipRanges/><ipv6Ranges/><prefixListIds>${values.prefixes.join("")}</prefixListIds></item>`,
      )
      .join("");
  };
  const groups = Object.entries(ids)
    .map(([role, id]) => {
      const suffix = [
        "ecr-api",
        "ecr-dkr",
        "logs",
        "configuration",
        "web-token",
      ].includes(role)
        ? role + "-endpoint"
        : role;
      const tags = { Project: "SaveIT", Cell: "pilot", Environment: "staging" };
      if (
        ["api", "web", "worker", "deployment", "relay", "web-token"].includes(
          role,
        )
      )
        tags.Process = role;
      return `<item><ownerId>111122223333</ownerId><groupId>${id}</groupId><groupName>${role === "default" ? "default" : "saveit-pilot-staging-" + suffix + "-synthetic"}</groupName><groupDescription>synthetic graph</groupDescription><vpcId>vpc-00000000000000001</vpcId><ipPermissions>${permissions(role, true)}</ipPermissions><ipPermissionsEgress>${permissions(role, false)}</ipPermissionsEgress><tagSet>${Object.entries(
        tags,
      )
        .map(
          ([key, value]) =>
            `<item><key>${key}</key><value>${value}</value></item>`,
        )
        .join("")}</tagSet></item>`;
    })
    .join("");
  return `<DescribeSecurityGroupsResponse xmlns="http://ec2.amazonaws.com/doc/2016-11-15/"><requestId>synthetic-request</requestId><securityGroupInfo>${groups}</securityGroupInfo></DescribeSecurityGroupsResponse>`;
}
