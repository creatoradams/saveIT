# Public CA release inputs

ADR039 adds a separate client-anchor context: `node scripts/prepare-client-trust.mjs /reviewed/client-roots.crt .local/new-client-trust`, then `--build-context saveit-client-trust=.local/new-client-trust` for API builds. Only API receives client-ca.crt; web/worker/deployment receive none. Public metadata supplies its hash and exact permitted leaf hashes; private client delivery is unfinished. Both preparers now refuse **all existing output directories**, including empty paths, to exclude unrelated leftovers before a public build-context upload. Choose a new ignored path; never place a client key in these directories.

This is the account-free implementation in [ADR036](../../docs/adr/036-public-trust-release-delivery.md). The tracked `unconfigured` files are placeholders, not trusted certificates. Do not add private keys/PFX, secret configuration, real environment files or test evidence here.

Prepare reviewed **public-only** API root and regional RDS root bundles into a new ignored directory:

```sh
node scripts/prepare-public-trust.mjs /reviewed/api-roots.crt /reviewed/regional-rds-roots.crt .local/public-trust-release
docker build -f infra/containers/dotnet.Dockerfile --build-context saveit-trust=.local/public-trust-release --target api -t saveit-api:review .
docker build -f infra/containers/web.Dockerfile --build-context saveit-trust=.local/public-trust-release -t saveit-web:review .
```

Use the same named public context for worker/deployment builds. The helper permits only current public CA PEM (one-to-sixteen roots, at most128KiB, RSA2048+ or supported NIST EC); exact DER/Base64 checks exclude embedded trailing data. It copies validated bytes through exclusive creation and writes only filenames plus hash metadata. Existing output files cause failure; review/choose a new directory rather than deleting unrelated files. Do not use the private fixture directory itself as the named context.

Set the task root's `public_trust.api_ca_sha256`/`database_ca_sha256` from the prepared manifest. Web uses fixed `NODE_EXTRA_CA_CERTS` and `SAVEIT_API_CA_SHA256`; .NET uses `Deployment__Trust__DatabaseCaSha256`. Files are baked at fixed root-owned mode0444 paths, separate from writable scratch/EFS. Do not mount replacements over the trust directory. Runtime checks precede application work; ordinary Node platform roots remain enabled, so hashes are not exclusive trust-anchor pinning, signatures or promotion evidence. Private transport PFX injection remains API-only and separate from public image input.

The regional RDS roots must match the authorized region/engine and reviewed CA lifecycle. Do not install RDS intermediate certificates as roots. The real bundle, endpoint/hostname, managed roles, secret payload limits/injection and rotation must still be verified in an authorized environment. Images with placeholder files cannot satisfy the strict task hash contract. No AWS account or secret is needed for synthetic CI tests.
