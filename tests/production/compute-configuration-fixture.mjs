// Synthetic private payloads for the isolated fake provider only. Never customer secrets.
import { readFile } from "node:fs/promises";
import { createHash, X509Certificate } from "node:crypto";
export async function computeConfigurationFixture(directory, imageFixture) {
  const hash = (bytes) => createHash("sha256").update(bytes).digest("hex");
  const pfx = async (name) =>
    (await readFile(directory + "/" + name + ".pfx")).toString("base64");
  const pin = async (name) =>
    hash(
      new X509Certificate(await readFile(directory + "/" + name + ".crt")).raw,
    );
  const ca = hash(await readFile(directory + "/ca.crt")),
    clientCa = hash(await readFile(directory + "/client-ca.crt")),
    clientPin = await pin("client-valid");
  const selected = structuredClone(imageFixture.variants["images-valid"]);
  Object.assign(selected.contract.runtime, {
    databaseCaSha256: ca,
    relayCaSha256: ca,
    clientCaSha256: clientCa,
    clientLeafSha256: [clientPin],
  });
  for (const process of ["api", "worker", "web"]) {
    for (const env of selected.tasks[process].taskDefinition
      .containerDefinitions[0].environment) {
      if (
        env.name === "Deployment__Trust__DatabaseCaSha256" ||
        env.name === "SAVEIT_API_CA_SHA256"
      )
        env.value = ca;
      if (env.name === "Deployment__ApiTls__ClientAuthentication__CaSha256")
        env.value = clientCa;
      if (
        env.name === "Deployment__ApiTls__ClientAuthentication__LeafSha256__0"
      )
        env.value = clientPin;
    }
  }
  imageFixture.variants["configuration-valid"] = selected;
  return {
    api: {
      ConnectionStrings__App:
        "Host=db.synthetic.invalid;Database=saveit;Username=saveit_app;Password=synthetic-private-cohort-marker;SSL Mode=VerifyFull",
      Identity__Okta__ClientSecret: "synthetic-private-cohort-marker",
      Identity__KeyCertificateBase64: await pfx("protection"),
      Identity__KeyCertificatePassword: "synthetic-container-only",
      Deployment__ApiTls__CertificateBase64: await pfx("compute-api"),
      Deployment__ApiTls__CertificatePassword: "synthetic-cohort-only",
    },
    worker: {
      ConnectionStrings__SearchWorker:
        "Host=db.synthetic.invalid;Database=saveit;Username=saveit_search_worker;Password=synthetic-private-cohort-marker;SSL Mode=VerifyFull",
    },
    relay: {
      CertificateBase64: await pfx("client-valid"),
      CertificatePassword: "synthetic-peer-only",
      ClientLeafSha256: clientPin,
      ApiCaSha256: ca,
      ServerCertificateBase64: await pfx("compute-relay"),
      ServerCertificatePassword: "synthetic-cohort-only",
      ServerLeafSha256: await pin("compute-relay"),
    },
  };
}
