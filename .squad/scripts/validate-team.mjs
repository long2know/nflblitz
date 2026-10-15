import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
process.chdir(root);
const read = (file) => fs.readFileSync(file, 'utf8');
const json = (file) => JSON.parse(read(file));
const expected = [
  ['lead', 'Lead', 'Architect'],
  ['unity', 'Unity', 'Unity Gameplay Engineer'],
  ['unity-systems', 'Unity Systems', 'Unity Rendering/Systems Engineer'],
  ['frontend', 'Frontend', 'Web/Build Engineer'],
  ['backend', 'Backend', 'Backend/Storage Engineer'],
  ['quality', 'Quality', 'QA Engineer'],
  ['scribe', 'Scribe', 'Decision Keeper'],
  ['ralph', 'Ralph', 'Work Monitor'],
  ['rai', 'Rai', 'Responsible-AI Reviewer'],
  ['fact-checker', 'Fact Checker', 'Verifier'],
];
const specialistIds = expected.slice(0, 6).map(([id]) => id);
const team = read('.squad/team.md');
const routing = read('.squad/routing.md');
const registry = json('.squad/casting/registry.json');
const policy = json('.squad/casting/policy.json');
const history = json('.squad/casting/history.json');
const manifest = json('.squad/casting/registry-history.commit.json');
const rows = team.split('## Members\n')[1].split('\n## ')[0]
  .split('\n').filter((line) => /^\| /.test(line) && !line.startsWith('| Name |'));
assert.equal(rows.length, 10, 'exactly ten approved members');
const charters = {};
for (const [id, name, role] of expected) {
  const row = rows.find((line) => line.startsWith(`| ${name} |`));
  assert.ok(row, `missing roster member ${name}`);
  assert.ok(row.includes(`| ${role} |`), `wrong role for ${name}`);
  assert.ok(row.includes(`(agents/${id}/charter.md)`), `wrong charter link for ${name}`);
  charters[id] = read(`.squad/agents/${id}/charter.md`);
  const agentHistory = read(`.squad/agents/${id}/history.md`);
  assert.ok(charters[id].includes(`**Name:** ${name}`));
  assert.ok(charters[id].includes(`**Role:** ${role}`));
  assert.ok(agentHistory.includes('2026-10-15') && agentHistory.includes('nflblitz'));
  assert.ok(!/\{(?:project_name|project_description|Name|Role)\}/.test(charters[id]));
  assert.ok(routing.includes(name) && routing.includes(id), `missing routing context for ${id}`);
}
assert.deepEqual(fs.readdirSync('.squad/agents').sort(), expected.map(([id]) => id).sort());
assert.ok(team.includes('<!-- copilot-auto-assign: false -->'));
for (const tier of ['🟢 Good fit', '🟡 Needs review', '🔴 Not suitable']) {
  assert.ok(team.includes(tier), `missing @copilot capability tier: ${tier}`);
}
const routingRows = routing.split('## Routing Table\n')[1].split('\n## ')[0]
  .split('\n').filter((line) => /^\| /.test(line) && !line.startsWith('| Work Type |'));
assert.equal(routingRows.length, 6);
assert.deepEqual(routingRows.map((row) => row.split('|')[2].trim()), expected.slice(0, 6).map(([, name]) => name));
assert.deepEqual(Object.keys(registry.agents).sort(), [...specialistIds].sort());
for (const [id, name, role] of expected.slice(0, 6)) {
  const agent = registry.agents[id];
  assert.equal(agent.display_name, name);
  assert.equal(agent.persistent_name, name);
  assert.equal(agent.role, role);
  assert.equal(agent.universe, 'descriptive');
  assert.equal(agent.status, 'active');
  assert.equal(agent.legacy_named, false);
  assert.equal(agent.created_at.slice(0, 10), '2026-10-15');
}
assert.equal(policy.default_naming, 'descriptive');
assert.ok(policy.allowlist_universes.includes('descriptive'));
assert.equal(policy.universe_capacity.descriptive, 6);
assert.equal(history.registry_revision, registry.revision);
assert.equal(manifest.registry_revision, registry.revision);
assert.equal(history.transaction_id, registry.transaction_id);
assert.equal(manifest.transaction_id, registry.transaction_id);
for (const kind of ['registry', 'history']) {
  assert.equal(createHash('sha256').update(read(`.squad/casting/${kind}.json`)).digest('hex'), manifest[`${kind}_sha256`]);
}
const snapshot = history.assignment_cast_snapshots['nflblitz-initial-team-2026-10-15'];
assert.deepEqual(snapshot.agents, specialistIds);
assert.equal(snapshot.universe, 'descriptive');
assert.equal(snapshot.created_at, registry.generated_at);
assert.ok(history.universe_usage_history.some((entry) => entry.universe === 'descriptive' && entry.used_at === snapshot.created_at));

const squadBin = process.env.PATH.split(path.delimiter)
  .map((dir) => path.join(dir, 'squad')).find((file) => fs.existsSync(file));
assert.ok(squadBin, 'installed squad CLI required for SDK schema validation');
const cliRoot = path.dirname(path.dirname(fs.realpathSync(squadBin)));
const sdk = path.join(cliRoot, 'node_modules/@bradygaster/squad-sdk/dist');
const load = (file) => import(pathToFileURL(path.join(sdk, file)));
const casting = await load('casting/index.js');
const capabilities = await load('config/team-capabilities.js');
const parsed = casting.parseAgentProvenanceRegistry(registry);
assert.equal(parsed.completeness, 'complete', JSON.stringify(parsed.diagnostics));
assert.deepEqual(parsed.diagnostics, []);
casting.readCastingRegistryPair(path.resolve('.squad/casting'));
casting.validateCastingRegistryPairForCommit(registry, history, registry.revision);

// CLI 1.0.0's roster parser misclassifies Rai and Fact Checker as specialists.
// Render from the registry-backed roster view without changing the ten-member roster.
const specialistNames = new Set(expected.slice(0, 6).map(([, name]) => name));
const specialistTeam = team.split('\n').filter((line) => {
  if (!line.startsWith('| ')) return true;
  const name = line.split('|')[1].trim();
  return !expected.some(([, member]) => member === name) || specialistNames.has(name);
}).join('\n');
const profile = capabilities.buildTeamCapabilityProfile({
  teamMarkdown: specialistTeam, routingMarkdown: routing, charters, registry,
});
assert.deepEqual(profile.specialists.map((entry) => entry.name), [...specialistNames]);
assert.equal(profile.routingHints.length, 6);
const agentFile = '.github/agents/squad.agent.md';
const before = read(agentFile);
const rendered = capabilities.applyTeamCapabilitiesBlock(before, capabilities.renderTeamCapabilitiesBlock(profile));
if (process.argv.includes('--write-capabilities')) fs.writeFileSync(agentFile, rendered);
assert.equal(read(agentFile), rendered, 'stale capabilities: rerun with --write-capabilities');
for (const canary of ['SQUAD_COORDINATOR_CANARY_HEAD_b7d2', 'SQUAD_COORDINATOR_CANARY_a8f3']) {
  assert.equal(read(agentFile).split(canary).length - 1, 1);
}
assert.equal(capabilities.stripTeamCapabilitiesBlock(before), capabilities.stripTeamCapabilitiesBlock(rendered));

const docs = ['README.md', '.squad/team.md', '.squad/routing.md', '.squad/governance.md',
  '.squad/decisions.md', ...expected.map(([id]) => `.squad/agents/${id}/charter.md`)];
let references = 0;
for (const file of docs) {
  for (const [, target] of read(file).matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
    if (/^(?:https?:|#)/.test(target)) continue;
    assert.ok(fs.existsSync(path.resolve(path.dirname(file), target.split('#')[0])), `broken link ${file}: ${target}`);
    references++;
  }
}
const attributes = read('.gitattributes');
for (const entry of ['decisions.md', 'agents/*/history.md', 'log/**', 'orchestration-log/**', 'rai/audit-trail.md', 'fact-checker/audit-trail.md']) {
  assert.ok(attributes.includes(`.squad/${entry} merge=union`));
}
const ignored = ['Library/test', 'Temp/test', 'obj/test', 'bin/test', 'Logs/test', 'UserSettings/test',
  'Builds/test', '.env', '.env.local', 'activation.ulf', 'request.alf', 'private.key',
  '.squad/sessions/example.json', '.squad/memory/index.json', '.squad/identity/now.md',
  '.squad/.first-run', '.copilot/session-state/example.json',
  '.squad/casting/registry.lock/owner.json', '.squad/casting/registry.lock.recovery/owner.json',
  '.squad/casting/registry.lock.candidate-example/owner.json',
  '.squad/casting/registry-history.transaction.json',
  '.squad/casting/registry-history.transaction.example.payload/registry.json'];
const checked = execFileSync('git', ['check-ignore', '--no-index', '--stdin'], {input: ignored.join('\n') + '\n', encoding: 'utf8'}).trim().split('\n');
assert.deepEqual(checked, ignored);
for (const file of ['Assets/Example.meta', 'Packages/manifest.json', 'Packages/packages-lock.json',
  '.env.example', '.squad/config.json', '.squad/memory/config.json', '.mcp.json', '.copilot/mcp-config.json',
  '.squad/casting/registry.json', '.squad/casting/history.json', '.squad/casting/registry-history.commit.json']) {
  try {
    execFileSync('git', ['check-ignore', '--no-index', '-q', file]);
    assert.fail(`source unexpectedly ignored: ${file}`);
  } catch (error) {
    if (error.status !== 1) throw error;
  }
}
let jsonCount = 0;
for (const dir of ['.squad/casting', '.squad/templates']) {
  for (const entry of fs.readdirSync(dir, {recursive: true})) {
    const file = path.join(dir, entry);
    if (entry.endsWith('.json') && fs.statSync(file).isFile()) { json(file); jsonCount++; }
  }
}
for (const file of ['.squad/config.json', '.squad/memory/config.json', '.mcp.json', '.copilot/mcp-config.json', '.vscode/settings.json']) {
  json(file); jsonCount++;
}
assert.deepEqual(json('.squad/config.json'), {version: 1});
const heartbeatTriggers = read('.github/workflows/squad-heartbeat.yml').split('\non:\n')[1].split('\npermissions:')[0];
assert.ok(heartbeatTriggers.includes('workflow_dispatch:'));
assert.ok(!/^\s{2}(?:issues|pull_request_target|schedule):/m.test(heartbeatTriggers));
execFileSync('git', ['diff', '--cached', '--quiet']);
console.log(`PASS: ${jsonCount} JSON files; 10 exact members/charters/histories; 6 specialist routes and provenance records.`);
console.log(`PASS: SDK complete schema + atomic pair/manifest hashes; ${references} documentation links; merge rules and ${ignored.length} ignore probes.`);
console.log('PASS: generated capabilities (6 specialists/6 hints), both canaries, manual-only heartbeat, local backend, no staged changes.');
