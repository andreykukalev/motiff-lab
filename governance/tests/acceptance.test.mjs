// Tests of the protection of the acceptance scenarios (Task Record #310, criterion 3): node --test "governance/tests/*.test.mjs".
// tests/acceptance/** is Guardrail by path, and CI's build-test fails a pull request that changes it without the label
// protected-scenarios-ok. The CI steps are run as written in ci.yml, with bash and jq, against a stand-in for gh that answers
// from hand-made GitHub data.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { minimumRisk, parseRiskPaths } from '../metrics/collect.mjs';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const workflow = readFileSync(path.join(root, '.github', 'workflows', 'ci.yml'), 'utf8');
const APPROVAL = 'Check approval of the protected scenarios';
const REPORT = 'Report the build of this commit';
const tools = ['bash', 'jq'].every((tool) => spawnSync(tool, ['--version']).status === 0);
const needsTools = { skip: !tools && 'needs bash and jq' };

/** A step of the build-test job by its name: its own lines, from "- name:" up to the next step. */
function step(name) {
  const lines = workflow.split('\n');
  const start = lines.findIndex((line) => line.trim() === `- name: ${name}`);
  assert.ok(start >= 0, `ci.yml has the step "${name}"`);
  const indent = lines[start].indexOf('-');
  let end = start + 1;
  while (end < lines.length && !(lines[end].trim() !== '' && lines[end].search(/\S/) <= indent)) end++;
  return lines.slice(start, end);
}

/** The step's `run: |` script, dedented. */
function script(name) {
  const lines = step(name);
  const at = lines.findIndex((line) => /^\s+run: \|\s*$/.test(line));
  assert.ok(at >= 0, `the step "${name}" runs a script`);
  const keyIndent = lines[at].search(/\S/);
  const end = lines.findIndex((line, i) => i > at && line.trim() !== '' && line.search(/\S/) <= keyIndent);
  const body = lines.slice(at + 1, end < 0 ? lines.length : end);
  const indent = Math.min(...body.filter((line) => line.trim() !== '').map((line) => line.search(/\S/)));
  return body.map((line) => line.slice(indent)).join('\n');
}

/**
 * Runs a step's script with a stand-in for gh: `gh api [--paginate] <path> [--jq <filter>]` answers the JSON in
 * `answers[path]` through jq, as gh does with --jq. Returns the exit code and what the step printed.
 */
function run(name, answers, env = {}) {
  const dir = mkdtempSync(path.join(tmpdir(), 'acceptance-guard-'));
  const key = (route) => route.replace(/[^A-Za-z0-9]/g, '_');
  for (const [route, answer] of Object.entries(answers)) writeFileSync(path.join(dir, `${key(route)}.json`), JSON.stringify(answer));
  const gh = path.join(dir, 'gh');
  writeFileSync(gh, [
    '#!/usr/bin/env bash',
    'route=""; filter="."',
    'while (( $# )); do case "$1" in api|--paginate) ;; --jq) shift; filter="$1" ;; *) route="$1" ;; esac; shift; done',
    'file="$FAKE_GH/$(printf "%s" "$route" | sed "s/[^A-Za-z0-9]/_/g").json"',
    '[[ -f "$file" ]] || { echo "gh stand-in: no answer for $route" >&2; exit 1; }',
    'jq -r "$filter" "$file"',
  ].join('\n'));
  chmodSync(gh, 0o755);
  const summary = path.join(dir, 'summary.md');
  writeFileSync(summary, '');
  const result = spawnSync('bash', ['-e', '-o', 'pipefail', '-c', script(name)], {
    encoding: 'utf8',
    env: {
      ...process.env, PATH: `${dir}${path.delimiter}${process.env.PATH}`, FAKE_GH: dir, GITHUB_STEP_SUMMARY: summary,
      GITHUB_REPOSITORY: 'o/r', GITHUB_SERVER_URL: 'https://github.com', ...env,
    },
  });
  return { code: result.status, out: `${result.stdout}${result.stderr}` };
}

const pull = 'repos/o/r/pulls/7';
const files = `${pull}/files?per_page=100`;
const approval = (changed, labels = [], count = changed.length) => run(APPROVAL, {
  [pull]: { changed_files: count, labels: labels.map((name) => ({ name })) },
  [files]: changed.map((file) => (typeof file === 'string' ? { filename: file } : file)),
}, { PR: '7' });

test('tests/acceptance/** is Guardrail by path; other tests stay Standard', () => {
  const table = parseRiskPaths(readFileSync(path.join(root, 'governance', 'risk-paths.yml'), 'utf8'));
  assert.equal(minimumRisk(['tests/acceptance/README.md'], table), 'guardrail');
  assert.equal(minimumRisk(['tests/acceptance/Fixtures/scale-type0.mid'], table), 'guardrail');
  assert.equal(minimumRisk(['tests/MotiffLab.MusicCore.Tests/Facade/MusicCoreFacadeTests.cs'], table), 'standard');
});

test('the approval check runs on every pull request run, whatever changed, and label events start a run', () => {
  assert.ok(step(APPROVAL).some((line) => line.trim() === "if: github.event_name == 'pull_request'"), 'runs on pull requests, not only when code changed');
  assert.match(workflow, /types: \[[^\]]*\blabeled\b[^\]]*\bunlabeled\b[^\]]*\]/, 'adding and removing a label start a run');
  assert.ok(workflow.indexOf(`- name: ${APPROVAL}`) > workflow.indexOf('- name: Test\n'), 'it runs after the build and the tests');
});

test('a pull request that changes no scenario passes without the label', needsTools, () => {
  const result = approval(['src/MotiffLab.Web/Pages/Home.razor.cs', 'tests/web/home.test.mjs']);
  assert.equal(result.code, 0, result.out);
});

test('a pull request that changes a scenario fails without protected-scenarios-ok', needsTools, () => {
  for (const changed of [['tests/acceptance/OpeningAPiece.cs'], ['README.md', 'tests/acceptance/Fixtures/new.musicxml']]) {
    const result = approval(changed, ['agent', 'risk:guardrail', 'protected-scenarios-ok-later']);
    assert.equal(result.code, 1, result.out);
    assert.match(result.out, /::error title=protected-scenarios-ok::.*tests\/acceptance\//);
  }
});

test('a pull request that changes a scenario passes with protected-scenarios-ok', needsTools, () => {
  const result = approval(['tests/acceptance/README.md', 'MotiffLab.slnx'], ['agent', 'protected-scenarios-ok']);
  assert.equal(result.code, 0, result.out);
});

test('moving a scenario out of tests/acceptance counts as changing it', needsTools, () => {
  const result = approval([{ filename: 'tests/old/OpeningAPiece.cs', previous_filename: 'tests/acceptance/OpeningAPiece.cs' }]);
  assert.equal(result.code, 1, result.out);
});

test('a file list GitHub cut short counts as changing the scenarios', needsTools, () => {
  assert.equal(approval(['src/a.cs'], [], 3001).code, 1);
  assert.equal(approval(['src/a.cs'], ['protected-scenarios-ok'], 3001).code, 0);
});

// A label run reports the build before it: built and approved, built but not approved (checked again), or not built.
const report = (job) => run(REPORT, {
  'repos/o/r/actions/runs/200': { workflow_id: 5 },
  'repos/o/r/actions/workflows/5/runs?head_sha=abc&event=pull_request&per_page=100': {
    workflow_runs: [{ id: 199, run_number: 41, status: 'completed' }, { id: 200, run_number: 42, status: 'in_progress' }],
  },
  'repos/o/r/actions/runs/199/jobs': { jobs: [{ name: 'build-test', ...job }] },
}, { GITHUB_RUN_ID: '200', GITHUB_RUN_NUMBER: '42', HEAD_SHA: 'abc' });
const steps = (failing, skipped = []) => ['Set up job', 'Find what changed', 'Build', 'Test', APPROVAL, 'Complete job'].map((name) => ({
  name, conclusion: name === failing ? 'failure' : skipped.includes(name) ? 'skipped' : 'success',
}));

test('a label run passes the build of a pull request that failed only for want of the label, and checks it again', needsTools, () => {
  const result = report({ conclusion: 'failure', steps: steps(APPROVAL) });
  assert.equal(result.code, 0, result.out);
  assert.match(result.out, /built, not approved/);
});

test('a label run fails on a build that failed, and passes one that passed', needsTools, () => {
  assert.equal(report({ conclusion: 'failure', steps: steps('Test', [APPROVAL]) }).code, 1);
  assert.equal(report({ conclusion: 'cancelled', steps: steps(null) }).code, 1);
  assert.equal(report({ conclusion: 'success', steps: steps(null) }).code, 0);
});
