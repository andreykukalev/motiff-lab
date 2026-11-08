// Tests of governance/metrics/collect.mjs on hand-made GitHub data: node --test "governance/tests/*.test.mjs".

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  computeTask, formatRecord, guardLines, minimumRisk, parseRecord, parseRiskPaths, parseVerdicts, summarize, taskFields,
} from '../metrics/collect.mjs';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const table = parseRiskPaths(readFileSync(path.join(root, 'governance', 'risk-paths.yml'), 'utf8'));

const issueBody = [
  '### Цель', '', 'Пустая библиотека подсказывает, что делать.', '',
  '### Критерии приёмки', '', '1. Видна кнопка.', '2. Оба языка.', '3. Старые проекты открываются.', '',
  '### Класс риска', '', 'Standard', '',
  '### Контур', '', 'Product Delivery', '',
  '### allowed_paths', '', '```text', 'src/MotiffLab.Web/Pages/Library*', 'tests/web/**', '```', '',
  '### Зависит от', '', '_No response_',
].join('\n');

const prBody = '## Задача\n\nCloses #12\n\n## Срабатывания ограждений\n\nНет. <!-- или по строке -->\n\n## Как проверить Андрею\n\n1. Открыть';

function data(overrides = {}) {
  const head = 'b'.repeat(40);
  return {
    issue: 12, createdAt: '2026-11-10T10:00:00Z', approvedAt: '2026-11-10T11:00:00Z', criteriaEditedAfterApproval: false,
    fields: taskFields(issueBody), riskTable: table,
    pr: { number: 34, merged: true, closed: true, mergedAt: '2026-11-11T11:00:00Z', headSha: head },
    commits: [{ sha: 'a'.repeat(40), message: 'Library empty state\n\nClaude-Session: https://claude.ai/code/x', parents: 1 },
      { sha: head, message: 'Fix review\n\nCo-Authored-By: Claude Opus <noreply@anthropic.com>', parents: 1 }],
    files: ['src/MotiffLab.Web/Pages/Library.razor', 'tests/web/library.test.mjs'],
    checks: [{ sha: 'a'.repeat(40), conclusions: ['failure'] }, { sha: head, conclusions: ['success'] }],
    mainRegression: false, defects: { total: 0, serious: 0 }, ruleset: { fail: 1, bypass: 0 },
    comments: {
      reviews: parseVerdicts([{ created_at: '1', body: `<!-- aom:review -->\nverdict: changes-requested\nsha: ${'a'.repeat(40)}\nblocking: 1` },
        { created_at: '2', body: `<!-- aom:review -->\nverdict: approve\nsha: ${head}\nblocking: 0` }], 'review'),
      qa: parseVerdicts([{ created_at: '3', body: `<!-- aom:qa -->\nverdict: pass\nsha: ${head}\ncriteria: 3 / pass 3` }], 'qa'),
    },
    guardSection: guardLines(prBody), drill: undefined, andreiMinutes: 20,
    ...overrides,
  };
}

test('risk minimum by paths takes the highest match, and unknown paths are standard', () => {
  assert.equal(minimumRisk(['docs/README.md'], table), 'low');
  assert.equal(minimumRisk(['docs/project-file.md'], table), 'high');
  assert.equal(minimumRisk(['docs/a.md', 'governance/roles/common.md'], table), 'guardrail');
  assert.equal(minimumRisk(['tools/icons/build.mjs'], table), 'standard');
  assert.equal(minimumRisk(['src/MotiffLab.Web/Pages/PlaybackLab.razor'], table), 'high');
});

test('the Issue Form is read: risk, scope and the number of criteria', () => {
  assert.deepEqual(taskFields(issueBody), {
    risk: 'standard', kind: 'Product Delivery', allowedPaths: ['src/MotiffLab.Web/Pages/Library*', 'tests/web/**'], criteria: 3,
  });
});

test('a clean task: no manual edit, returned once, complete evidence, one flaky-free run', () => {
  const r = computeTask(data());
  assert.equal(r.m1_no_manual_edit, true);
  assert.equal(r.m4_returned, true);
  assert.equal(r.m5_flaky_shas, 0);
  assert.equal(r.m5_merged_without_green_check, false);
  assert.equal(r.m7_evidence_complete, true);
  assert.equal(r.m9_outside_allowed_paths, 0);
  assert.equal(r.m11_hours_approved_to_merged, 24);
  assert.equal(r.guard_hook_incidents, 0);
  assert.equal(r.guard_ruleset_refusals, 1);
  assert.equal(r.risk_below_minimum, false);
});

test('owner commits, files out of scope, a stale verdict and a low class are all caught', () => {
  const d = data();
  const r = computeTask({
    ...d,
    commits: [...d.commits, { sha: 'c'.repeat(40), message: 'quick fix', parents: 1 }],
    files: [...d.files, 'src/MotiffLab.MusicCore/Facade/Facade.cs'],
    pr: { ...d.pr, headSha: 'c'.repeat(40) },
    checks: [...d.checks, { sha: 'c'.repeat(40), conclusions: ['failure', 'success'] }],
  });
  assert.equal(r.m1_no_manual_edit, false);
  assert.equal(r.m9_outside_allowed_paths, 1);
  assert.equal(r.m7_evidence_complete, false); // review and QA were for the earlier SHA
  assert.equal(r.m5_flaky_shas, 1);
  assert.equal(r.risk_minimum_by_paths, 'high');
  assert.equal(r.risk_below_minimum, true);
});

test('guard lines count incidents, and a missing section is not zero', () => {
  assert.equal(guardLines('## Срабатывания ограждений\n\n- push-guard deny: git push origin main\n- scope-guard ask: CLAUDE.md\n'), 2);
  assert.equal(guardLines('## Что сделано\n\n-'), null);
});

test('a record survives the comment and the summary reads it', () => {
  const record = computeTask(data({ drill: 'passed' }));
  const back = parseRecord(formatRecord(record));
  assert.equal(back.task, 12);
  assert.equal(back.m1_no_manual_edit, true);
  assert.equal(back.m8_drill, 'passed');
  const table = summarize([back]);
  assert.match(table, /M1 без ручной правки Андрея, % \| 100 \|/);
  assert.match(table, /M8 учения: остановился \| 1 из 1/);
});
