import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
test('build artifacts and secrets are ignored, reproducibility inputs are trackable', () => {
  const ignored = ['services/api/bin/Debug/api.dll','services/api/obj/project.assets.json','apps/web/node_modules/next/index.js','apps/web/.next/cache/data','apps/web/.env.local','.env','.env.production','infra/.terraform/providers/test','infra/terraform.tfstate','infra/terraform.tfstate.backup','infra/plan.tfplan','infra/local.tfvars','infra/terraform/bootstrap/local.backend.hcl','infra/terraform/bootstrap/backend_override.tf','infra/terraform/bootstrap/terraform.tfstate','.idea/workspace.xml','.vscode/settings.json','test-results/result.png','playwright-report/index.html','coverage/lcov.info','private/session.pfx','private/session.p12','reports/csharp.sarif','operator.access.local.json','tenant.provision.local.json','.local/operator/access.json'];
  const tracked = ['package-lock.json','pnpm-lock.yaml','yarn.lock','services/api/packages.lock.json','project.lock.json','infra/.terraform.lock.hcl','services/api/Migrations/Initial.cs','.env.example','apps/web/.env.local.example','infra/dev.tfvars.example','infra/terraform/bootstrap/.terraform.lock.hcl','infra/terraform/bootstrap/bootstrap.tfvars.example','infra/terraform/bootstrap/staging.backend.hcl.example','infra/postgres/access-create.json.example','infra/postgres/access-change.json.example','infra/postgres/access-inspect.json.example'];
  for (const [paths, expected] of [[ignored, true], [tracked, false]]) {
    for (const path of paths) {
      const result = spawnSync('git', ['check-ignore', '--no-index', '-q', path]);
      assert.equal(result.status === 0, expected, path);
    }
  }
});
