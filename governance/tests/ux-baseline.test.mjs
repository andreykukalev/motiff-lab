// Tests of the UI/UX Baseline (governance/ux-baseline.md, #582 item 18): node --test "governance/tests/*.test.mjs".
// Every rule Andrei approved has its fields and at least one test that exists and carries the rule's tag; no test carries
// the tag of a rule that does not exist. The parser is checked on a hand-made document first, since the real one may have
// no rules yet.

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const FIELDS = ['Rule', 'Approved', 'Applies to', 'Tests'];
const TAG = /\[UXB-(\d+)\]/g;

/** The rules of a baseline document: the level-3 headings `### UXB-<n>: name` under `## Rules`, with their fields. */
export function parseBaseline(text) {
  const section = text.split(/^## Rules\s*$/m)[1] ?? '';
  const body = section.split(/^## /m)[0];
  const parts = body.split(/^### /m).slice(1);
  return parts.map((part) => {
    const [heading, ...lines] = part.split('\n');
    const id = /^UXB-(\d+):\s*\S/.exec(heading)?.[1];
    const text = lines.join('\n');
    const fields = Object.fromEntries(
      [...text.matchAll(/^- \*\*([^*]+):\*\*\s*(.*)$/gm)].map((match) => [match[1].trim(), match[2].trim()]));
    const tests = [...text.matchAll(/`([^`]+)`\s+—\s+`([^`]+)`/g)].map((match) => ({ file: match[1], name: match[2] }));
    return { heading: heading.trim(), id: id && Number(id), fields, tests, removed: 'Removed' in fields };
  });
}

/** What is wrong with the rules against the repository (an empty list when all holds). */
export function baselineProblems(rules, readTestFile) {
  const problems = [];
  const seen = new Set();
  for (const rule of rules) {
    if (!rule.id) { problems.push(`heading "${rule.heading}" is not "UXB-<n>: name"`); continue; }
    if (seen.has(rule.id)) problems.push(`UXB-${rule.id} appears twice`);
    seen.add(rule.id);
    if (rule.removed) continue;
    for (const field of FIELDS) {
      if (!(field in rule.fields)) problems.push(`UXB-${rule.id} has no **${field}:**`);
    }
    if (rule.tests.length === 0) problems.push(`UXB-${rule.id} lists no test`);
    for (const ref of rule.tests) {
      const content = readTestFile(ref.file);
      if (content === undefined) { problems.push(`UXB-${rule.id}: ${ref.file} does not exist`); continue; }
      if (!ref.name.includes(`[UXB-${rule.id}]`)) problems.push(`UXB-${rule.id}: test "${ref.name}" lacks the tag [UXB-${rule.id}]`);
      if (!content.includes(ref.name)) problems.push(`UXB-${rule.id}: ${ref.file} has no test "${ref.name}"`);
    }
  }
  return problems;
}

/** The baseline test files of the repository (governance/ux-baseline.md, "Tests"). */
function baselineTestFiles() {
  const pick = (dir, match) => (existsSync(path.join(root, dir))
    ? readdirSync(path.join(root, dir)).filter((name) => match.test(name)).map((name) => `${dir}/${name}`) : []);
  return [
    ...pick('tests/web', /^baseline-.*\.test\.mjs$/),
    ...pick('tests/e2e', /^baseline-.*\.spec\.mjs$/),
    ...pick('ipad/App/MotiffLabTests', /^Baseline.*\.swift$/),
  ];
}

function readRepoFile(file) {
  const full = path.join(root, file);
  return existsSync(full) ? readFileSync(full, 'utf8') : undefined;
}

const SAMPLE = `# UI/UX Baseline

## Rules

### UXB-1: Long press selects a bar

- **Rule:** A long press on a bar selects it.
- **Approved:** 2026-11-15, [Andrei](https://example.org/1)
- **Applies to:** iPad
- **Tests:** \`tests/web/baseline-selection.test.mjs\` — \`long press selects the bar [UXB-1]\`

### UXB-2: Old rule

- **Removed:** 2026-11-16, [Andrei](https://example.org/2)

### Something else
`;

test('the parser reads rules, fields and test references', () => {
  const rules = parseBaseline(SAMPLE);
  assert.equal(rules.length, 3);
  assert.equal(rules[0].id, 1);
  assert.deepEqual(Object.keys(rules[0].fields), FIELDS);
  assert.deepEqual(rules[0].tests, [{ file: 'tests/web/baseline-selection.test.mjs', name: 'long press selects the bar [UXB-1]' }]);
  assert.equal(rules[1].removed, true);
  assert.equal(rules[2].id, undefined);
});

test('a rule whose test is missing, untagged or absent from its file is reported', () => {
  const rules = parseBaseline(SAMPLE).slice(0, 2);
  assert.deepEqual(baselineProblems(rules, () => "test('long press selects the bar [UXB-1]')"), []);
  assert.match(baselineProblems(rules, () => undefined)[0], /does not exist/);
  assert.match(baselineProblems(rules, () => 'nothing')[0], /has no test/);
  const untagged = parseBaseline(SAMPLE.replace('bar [UXB-1]', 'bar'));
  assert.ok(baselineProblems(untagged.slice(0, 1), () => 'long press selects the bar').some((p) => /lacks the tag/.test(p)));
  assert.ok(baselineProblems(parseBaseline(SAMPLE), () => '').some((p) => /is not "UXB-<n>: name"/.test(p)));
});

test('every rule of governance/ux-baseline.md has its fields and existing, tagged tests', () => {
  const rules = parseBaseline(readFileSync(path.join(root, 'governance', 'ux-baseline.md'), 'utf8'));
  assert.deepEqual(baselineProblems(rules, readRepoFile), []);
});

test('no baseline test carries the tag of a rule that does not exist', () => {
  const rules = parseBaseline(readFileSync(path.join(root, 'governance', 'ux-baseline.md'), 'utf8'));
  const live = new Set(rules.filter((rule) => rule.id && !rule.removed).map((rule) => rule.id));
  const stray = baselineTestFiles().flatMap((file) => [...readRepoFile(file).matchAll(TAG)]
    .map((match) => Number(match[1])).filter((id) => !live.has(id)).map((id) => `${file}: [UXB-${id}]`));
  assert.deepEqual(stray, []);
});
