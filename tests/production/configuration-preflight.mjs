// Synthetic payloads only. Supply private fixture bytes over stdin, never argv,
// environment, logs, Terraform, an output payload file or image build input.
import { readFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';

const failure = 'SaveIT protected configuration preflight failed.';
const success = 'SaveIT protected configuration preflight passed (local validation only).';
const fixture = new URL('../../.local/container-fixture/', import.meta.url);
const session = (await readFile(new URL('protection.pfx', fixture))).toString('base64');
const transport = (await readFile(new URL('api.pfx', fixture))).toString('base64');
const connection = user => `Host=db;Database=saveit;Username=${user};Password=synthetic-preflight-only;SSL Mode=VerifyFull`;
const payload = (workload, retired = 0) => {
  if (workload !== 'api') return {
    [({ worker: 'ConnectionStrings__SearchWorker', migration: 'SAVEIT_DEPLOYMENT_CONNECTION', access: 'SAVEIT_ACCESS_CONNECTION' })[workload]]:
      connection(({ worker: 'saveit_search_worker', migration: 'synthetic_deployer', access: 'synthetic_access' })[workload])
  };
  const value = {
    ConnectionStrings__App: connection('saveit_app'), Identity__Okta__ClientSecret: 'synthetic-only',
    Identity__KeyCertificateBase64: session, Identity__KeyCertificatePassword: 'synthetic-container-only',
    Deployment__ApiTls__CertificateBase64: transport, Deployment__ApiTls__CertificatePassword: 'synthetic-transport-only'
  };
  for (let i = 0; i < retired; i++) {
    value[`Identity__PreviousKeyCertificates__${i}__Base64`] = session;
    value[`Identity__PreviousKeyCertificates__${i}__Password`] = 'synthetic-container-only';
  }
  return value;
};

let sequence = 0;
async function within(promise, milliseconds) {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error('Synthetic preflight process deadline exceeded.')), milliseconds);
    })]);
  } finally { clearTimeout(timer); }
}
async function boundedClose(child, completion, milliseconds) {
  try { return await within(completion, milliseconds); }
  catch {
    child.kill('SIGKILL');
    try { await within(completion, 5000); }
    catch {
      child.stdin?.destroy(); child.stdout?.destroy(); child.stderr?.destroy();
      child.unref();
    }
    throw new Error('Synthetic preflight process failed or exceeded its deadline.');
  }
}
async function run(workload, retired, bytes, expected, label, holdOpen = false) {
  const owner = `saveit-preflight-${process.pid}`;
  const name = `${owner}-${++sequence}`;
  const child = spawn('docker', ['run', '--rm', '--network', 'none', '--read-only', '--name', name,
    '--label', `com.saveit.preflight-owner=${owner}`, '-i', 'saveit-deployment:test', 'check-configuration', workload, String(retired)],
  { stdio: ['pipe', 'pipe', 'pipe'] });
  let output = '', errors = '', exceeded = false, closed = false;
  child.stdout.on('data', data => { output += data; if (output.length > 4096) { output = ''; exceeded = true; } });
  child.stderr.on('data', data => { errors += data; if (errors.length > 4096) { errors = ''; exceeded = true; } });
  child.stdin.on('error', () => {}); // A refused command may close stdin before consuming it.
  const completion = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('close', code => { closed = true; resolve(code); });
  });
  try {
    if (holdOpen) child.stdin.write(bytes);
    else child.stdin.end(bytes);
    const code = await boundedClose(child, completion, 20_000);
    if (exceeded || code !== (expected ? 0 : 1) || output.trim() !== (expected ? success : '') || errors.trim() !== (expected ? '' : failure))
      throw new Error('Synthetic protected configuration canary failed.');
    console.log(`Packaged configuration preflight passed: ${label}`);
  } finally {
    if (!closed) {
      child.kill('SIGKILL');
      try { await within(completion, 5000); }
      catch { child.stdin?.destroy(); child.stdout?.destroy(); child.stderr?.destroy(); child.unref(); }
    }
    // Docker can outlive a killed client. Inspect this exact container's owner
    // label before removing it; never enumerate/delete unrelated resources.
    const inspect = spawn('docker', ['inspect', '--format', '{{index .Config.Labels "com.saveit.preflight-owner"}}', name], { stdio: ['ignore', 'pipe', 'ignore'] });
    let found = ''; inspect.stdout.on('data', data => { found = (found + data).slice(-256); });
    const inspection = new Promise((resolve, reject) => { inspect.once('error', reject); inspect.once('close', resolve); });
    {
      const code = await boundedClose(inspect, inspection, 5000);
      if (code === 0 && found.trim() === owner) {
        const remove = spawn('docker', ['rm', '--force', name], { stdio: 'ignore' });
        const removed = new Promise((resolve, reject) => { remove.once('error', reject); remove.once('close', resolve); });
        if (await boundedClose(remove, removed, 5000) !== 0) throw new Error('Synthetic preflight cleanup failed.');
      } else if (code !== 1) throw new Error('Synthetic preflight ownership check failed.');
    }
  }
}

try {
  for (const workload of ['api', 'worker', 'migration', 'access']) await run(workload, 0, JSON.stringify(payload(workload)), true, `${workload} exact fields`);
  await run('api', 4, JSON.stringify(payload('api', 4)), true, 'four retired protectors');
  const valid = JSON.stringify(payload('api'));
  await run('api', 0, valid + ' '.repeat(65536 - Buffer.byteLength(valid)), true, 'exact 64 KiB original bytes');
  await run('api', 0, valid + ' '.repeat(65537 - Buffer.byteLength(valid)), false, 'aggregate carrier overflow');
  await run('api', 0, valid.slice(0, -1) + ',"Identity__Okta__ClientSecret":"synthetic-duplicate"}', false, 'duplicate selector');
  await run('api', 0, JSON.stringify({ ...payload('api'), Deployment__ApiTls__CertificatePassword: 'synthetic-wrong' }), false, 'wrong PFX password');
  await run('api', 0, JSON.stringify({ ...payload('api'), Deployment__ApiTls__CertificateBase64: session, Deployment__ApiTls__CertificatePassword: 'synthetic-container-only' }), false, 'transport and session key reuse');
  await run('api', 1, valid, false, 'pinned retired-count mismatch');
  await run('web', 0, '{}', false, 'web has no secret contract');
  await run('worker', 0, '', false, 'empty stdin deadline', true);
  await run('worker', 0, JSON.stringify(payload('worker')), false, 'valid JSON still requires bounded EOF', true);
} catch {
  console.error('Synthetic protected configuration verification failed.');
  process.exitCode = 1;
}
