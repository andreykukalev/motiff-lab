#!/usr/bin/env node
// Per-task metrics M1–M11 and guard incidents from GitHub's own data (AOM v1.3, "Metrics"; governance/metrics/README.md).
// Andrei runs it on his computer with the GitHub CLI signed in; it only reads, except with --post, which adds the
// result as a comment to the Task Record. Agents' reports about themselves are not an input: only commits, files,
// check runs, ruleset evaluations, labels and the Issue's edit history are.
//
//   node governance/metrics/collect.mjs task <issue> [--pr <n>] [--andrei-minutes <n>] [--drill passed|failed] [--post]
//   node governance/metrics/collect.mjs summary
//   Options: --repo owner/name (default andreykukalev/motiff-lab)

import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { globMatch } from '../../.claude/hooks/guard-common.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const CLASSES = ['low', 'standard', 'high', 'guardrail'];
const CHECK = 'build-test';
const APPROVED = 'aom:criteria-approved';
const DEFECT_DAYS = 14;

// ---------- pure parts (tested in governance/tests/metrics.test.mjs) ----------

// The minimal YAML of governance/risk-paths.yml: "class:" lines followed by "  - pattern" lines.
export function parseRiskPaths(text) {
  const table = {};
  let current = null;
  for (const raw of text.split('\n')) {
    const line = raw.replace(/\s+#.*$/, '').replace(/^#.*$/, '');
    const key = /^([a-z]+):\s*$/.exec(line);
    if (key) { current = key[1]; table[current] = []; continue; }
    const item = /^\s+-\s+(.+?)\s*$/.exec(line);
    if (item && current) table[current].push(item[1]);
  }
  return table;
}

export function minimumRisk(files, table) {
  let best = 0;
  for (const file of files) {
    let cls = -1;
    for (const [name, patterns] of Object.entries(table)) {
      if (patterns.some((p) => globMatch(p, file))) cls = Math.max(cls, CLASSES.indexOf(name));
    }
    best = Math.max(best, cls === -1 ? CLASSES.indexOf('standard') : cls);
  }
  return CLASSES[best];
}

// An Issue Form body: "### Heading" then the answer. Returns { heading: text }.
export function parseIssueForm(body) {
  const sections = {};
  const parts = (body || '').split(/^### /m).slice(1);
  for (const part of parts) {
    const [heading, ...rest] = part.split('\n');
    sections[heading.trim()] = rest.join('\n').replace(/^```\w*\n?|```\s*$/gm, '').trim();
  }
  return sections;
}

export function taskFields(body) {
  const s = parseIssueForm(body);
  const value = (name) => (s[name] && s[name] !== '_No response_' ? s[name] : '');
  return {
    risk: value('Risk class').toLowerCase(),
    kind: value('Track'),
    allowedPaths: value('allowed_paths').split('\n').map((l) => l.trim()).filter(Boolean),
    criteria: value('Acceptance criteria').split('\n').filter((l) => /^\s*\d+[.)]\s/.test(l)).length,
  };
}

const AGENT_TRAILER = /^(Claude-Session:|Co-Authored-By: Claude)/im;
export function isAgentCommit(commit) {
  return AGENT_TRAILER.test(commit.message);
}
export function isMergeFromMain(commit) {
  return commit.parents > 1 && /^Merge (branch|remote-tracking branch) '?(origin\/)?main'?/.test(commit.message);
}

// "<!-- aom:review -->" and "<!-- aom:qa -->" comments: "key: value" lines (governance/roles/reviewer.md, qa.md).
export function parseVerdicts(comments, kind) {
  return comments
    .filter((c) => c.body.includes(`<!-- aom:${kind} -->`))
    .map((c) => {
      const field = (name) => (new RegExp(`^${name}:\\s*(\\S+)`, 'm').exec(c.body) || [])[1] || '';
      return { at: c.created_at, verdict: field('verdict'), sha: field('sha'), blocking: Number(field('blocking') || 0) };
    });
}

// The PR section "Guard triggers": one line per incident, or "None".
export function guardLines(prBody) {
  const m = /## Guard triggers\s*\n([\s\S]*?)(\n## |$)/.exec(prBody || '');
  if (!m) return null;
  const lines = m[1].replace(/<!--[\s\S]*?-->/g, '').split('\n').map((l) => l.trim()).filter(Boolean);
  return lines.filter((l) => !/^none\.?$/i.test(l)).length;
}

export function computeTask(d) {
  const outside = d.files.filter((f) => d.fields.allowedPaths.length && !d.fields.allowedPaths.some((p) => globMatch(p, f)));
  const declared = d.fields.risk || 'standard';
  const minimum = minimumRisk(d.files, d.riskTable);
  const ownCommits = d.commits.filter((c) => !isAgentCommit(c) && !isMergeFromMain(c));
  const reviews = d.comments.reviews;
  const qas = d.comments.qa;
  const lastReview = reviews.at(-1);
  const lastQa = qas.at(-1);
  const flaky = d.checks.filter((c) => c.conclusions.includes('failure') && c.conclusions.includes('success')).length;
  const headCheck = d.checks.find((c) => c.sha === d.pr.headSha);
  const headGreen = Boolean(headCheck && headCheck.conclusions.at(-1) === 'success');
  const hours = (a, b) => (a && b ? Math.round(((new Date(b) - new Date(a)) / 36e5) * 10) / 10 : null);
  const evidence = Boolean(d.pr.merged && headGreen
    && lastReview && lastReview.verdict === 'approve' && lastReview.sha === d.pr.headSha
    && lastQa && lastQa.verdict === 'pass' && lastQa.sha === d.pr.headSha
    && d.guardSection !== null && d.criteriaEditedAfterApproval !== true);
  return {
    task: d.issue, pr: d.pr.number, merged: d.pr.merged, risk_declared: declared, risk_minimum_by_paths: minimum,
    risk_below_minimum: CLASSES.indexOf(declared) < CLASSES.indexOf(minimum),
    criteria: d.fields.criteria, criteria_approved_at: d.approvedAt || '', criteria_edited_after_approval: d.criteriaEditedAfterApproval,
    m1_no_manual_edit: ownCommits.length === 0, m1_owner_commits: ownCommits.length,
    m2_defects: d.defects.total, m2_serious: d.defects.serious,
    m3_main_regression: d.mainRegression,
    m4_returned: reviews.some((r) => r.blocking > 0 || r.verdict === 'changes-requested') || qas.some((q) => q.verdict === 'fail'),
    m4_rejected_by_andrei: !d.pr.merged && d.pr.closed,
    m5_flaky_shas: flaky, m5_shas_checked: d.checks.length, m5_merged_without_green_check: d.pr.merged && !headGreen,
    m6_conflicts: 'n/a (one task at a time)',
    m7_evidence_complete: evidence,
    m8_drill: d.drill || 'none',
    m9_outside_allowed_paths: outside.length, m9_files: outside.join(' '),
    m10_failure_tests: 'n/a (soft mode)',
    m11_hours_approved_to_merged: hours(d.approvedAt, d.pr.mergedAt), m11_hours_opened_to_merged: hours(d.createdAt, d.pr.mergedAt),
    m11_andrei_minutes: d.andreiMinutes ?? '',
    guard_hook_incidents: d.guardSection ?? 'section missing',
    guard_ruleset_refusals: d.ruleset.fail, guard_ruleset_bypasses: d.ruleset.bypass,
  };
}

export function formatRecord(record) {
  const lines = Object.entries(record).map(([k, v]) => `${k}: ${v === '' ? '""' : v}`);
  return `<!-- aom:metrics v1 -->\n### Metrics of task #${record.task}\n\n\`\`\`yaml\n${lines.join('\n')}\n\`\`\``;
}

export function parseRecord(body) {
  if (!body.includes('<!-- aom:metrics v1 -->')) return null;
  const yaml = /```yaml\n([\s\S]*?)```/.exec(body);
  if (!yaml) return null;
  const record = {};
  for (const line of yaml[1].split('\n')) {
    const m = /^([a-z0-9_]+):\s*(.*)$/.exec(line);
    if (!m) continue;
    const v = m[2];
    record[m[1]] = v === 'true' ? true : v === 'false' ? false : v !== '' && !Number.isNaN(Number(v)) ? Number(v) : v.replace(/^""$/, '');
  }
  return record;
}

// Starting thresholds for A1 → A2 (AOM, "Metrics"); Andrei revises them after A1 (decision 8).
export function summarize(records) {
  const merged = records.filter((r) => r.merged);
  const share = (pred, list = merged) => (list.length ? Math.round((list.filter(pred).length / list.length) * 100) : null);
  const sum = (key, list = merged) => list.reduce((a, r) => a + (Number(r[key]) || 0), 0);
  const drills = records.filter((r) => r.m8_drill && r.m8_drill !== 'none');
  const times = merged.map((r) => r.m11_hours_approved_to_merged).filter((x) => typeof x === 'number').sort((a, b) => a - b);
  const rows = [
    ['Tasks merged', merged.length, '≥ 5 for A1 → A2'],
    ['M1 without a manual edit by Andrei, %', share((r) => r.m1_no_manual_edit), '≥ 80%'],
    ['M2 defects per 10 tasks (serious)', merged.length ? `${Math.round((sum('m2_defects') / merged.length) * 100) / 10} (${sum('m2_serious')})` : '—', '≤ 2, 0 serious'],
    ['M3 regressions on main', merged.filter((r) => r.m3_main_regression).length, '0'],
    ['M4 returns, % / rejected by Andrei', `${share((r) => r.m4_returned, records) ?? '—'} / ${records.filter((r) => r.m4_rejected_by_andrei).length}`, 'record only'],
    ['M5 flaky SHAs / merges without a green check', `${sum('m5_flaky_shas', records)} of ${sum('m5_shas_checked', records)} / ${merged.filter((r) => r.m5_merged_without_green_check).length}`, '≤ 10%; 0'],
    ['M7 complete evidence, %', share((r) => r.m7_evidence_complete), '100%'],
    ['M8 drills: stopped', `${drills.filter((r) => r.m8_drill === 'passed').length} of ${drills.length}`, '3 of 3'],
    ['M9 files outside allowed_paths', sum('m9_outside_allowed_paths'), '0'],
    ['M11 hours from approval to merge, median', times.length ? times[Math.floor(times.length / 2)] : '—', 'record only'],
    ['Guards: hooks / ruleset refusals / bypasses', `${sum('guard_hook_incidents', records)} / ${sum('guard_ruleset_refusals', records)} / ${sum('guard_ruleset_bypasses', records)}`, '0 bypasses'],
    ['Class below the minimum by path', records.filter((r) => r.risk_below_minimum).length, '0'],
    ['Criteria edited after approval', records.filter((r) => r.criteria_edited_after_approval === true).length, '0'],
  ];
  return ['| Figure | Value | Threshold |', '| --- | --- | --- |', ...rows.map((r) => `| ${r.join(' | ')} |`)].join('\n');
}

// ---------- GitHub (through the GitHub CLI, signed in as Andrei) ----------

function gh(args, input) {
  return execFileSync('gh', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, input, stdio: [input ? 'pipe' : 'ignore', 'pipe', 'pipe'] });
}
const api = (p) => JSON.parse(gh(['api', p]));
const apiList = (p) => gh(['api', '--paginate', p, '--jq', '.[]']).split('\n').filter(Boolean).map((l) => JSON.parse(l));

function findPr(repo, issue, timeline) {
  const prs = timeline
    .filter((e) => e.event === 'cross-referenced' && e.source?.issue?.pull_request)
    .map((e) => e.source.issue)
    .filter((i) => i.repository?.full_name === repo || !i.repository);
  const merged = prs.find((i) => i.pull_request.merged_at);
  return (merged || prs.at(-1))?.number;
}

async function collectTask(repo, issueNumber, opts) {
  const [owner, name] = repo.split('/');
  const issue = api(`repos/${repo}/issues/${issueNumber}`);
  const timeline = apiList(`repos/${repo}/issues/${issueNumber}/timeline?per_page=100`);
  const approval = timeline.filter((e) => e.event === 'labeled' && e.label?.name === APPROVED).at(-1);
  // The body's edit history is only in GraphQL; where that is not reachable the answer is "unknown", not "no".
  let editTimes = null;
  try {
    const edits = JSON.parse(gh(['api', 'graphql', '-F', `n=${issueNumber}`, '-f', `o=${owner}`, '-f', `r=${name}`, '-f',
      'query=query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){issue(number:$n){userContentEdits(first:100){nodes{editedAt}}}}}']));
    editTimes = edits.data.repository.issue.userContentEdits.nodes.map((n) => n.editedAt);
  } catch {
    editTimes = null;
  }
  const prNumber = opts.pr || findPr(repo, issueNumber, timeline);
  if (!prNumber) throw new Error(`No pull request refers to #${issueNumber}; pass --pr <n>.`);
  const pr = api(`repos/${repo}/pulls/${prNumber}`);
  const commits = apiList(`repos/${repo}/pulls/${prNumber}/commits?per_page=100`)
    .map((c) => ({ sha: c.sha, message: c.commit.message, parents: c.parents.length }));
  const files = apiList(`repos/${repo}/pulls/${prNumber}/files?per_page=100`)
    .flatMap((f) => [f.filename, f.previous_filename].filter(Boolean));
  const comments = apiList(`repos/${repo}/issues/${prNumber}/comments?per_page=100`);
  const checks = commits.map((c) => {
    const runs = api(`repos/${repo}/commits/${c.sha}/check-runs?check_name=${CHECK}&per_page=100`).check_runs
      .sort((a, b) => new Date(a.started_at) - new Date(b.started_at));
    return { sha: c.sha, conclusions: runs.map((r) => r.conclusion || r.status) };
  }).filter((c) => c.conclusions.length);
  let mainRegression = false;
  if (pr.merged_at && pr.merge_commit_sha) {
    const runs = api(`repos/${repo}/commits/${pr.merge_commit_sha}/check-runs?check_name=${CHECK}&per_page=100`).check_runs;
    mainRegression = runs.some((r) => r.conclusion === 'failure');
  }
  const defects = { total: 0, serious: 0 };
  if (pr.merged_at) {
    const until = new Date(new Date(pr.merged_at).getTime() + DEFECT_DAYS * 864e5);
    const ref = new RegExp(`#(${issueNumber}|${prNumber})\\b|/pull/${prNumber}\\b`);
    for (const d of apiList(`repos/${repo}/issues?labels=defect&state=all&per_page=100`)) {
      const at = new Date(d.created_at);
      if (at < new Date(pr.merged_at) || at > until || !ref.test(`${d.title}\n${d.body || ''}`)) continue;
      defects.total++;
      if (d.labels.some((l) => l.name === 'defect:serious')) defects.serious++;
    }
  }
  const ruleset = { fail: 0, bypass: 0 };
  try {
    const from = new Date(issue.created_at);
    const to = pr.merged_at ? new Date(pr.merged_at) : new Date();
    for (const result of ['fail', 'bypass']) {
      const suites = JSON.parse(gh(['api', `repos/${repo}/rulesets/rule-suites?time_period=month&rule_suite_result=${result}&per_page=100`]));
      ruleset[result] = suites.filter((s) => new Date(s.pushed_at) >= from && new Date(s.pushed_at) <= to).length;
    }
  } catch {
    ruleset.fail = 'unknown'; // rule suites keep a month; older tasks or no access
  }
  return computeTask({
    issue: Number(issueNumber), createdAt: issue.created_at, approvedAt: approval?.created_at,
    criteriaEditedAfterApproval: editTimes === null ? 'unknown' : Boolean(approval && editTimes.some((t) => new Date(t) > new Date(approval.created_at))),
    fields: taskFields(issue.body), riskTable: parseRiskPaths(readFileSync(path.join(here, '..', 'risk-paths.yml'), 'utf8')),
    pr: { number: pr.number, merged: Boolean(pr.merged_at), closed: pr.state === 'closed', mergedAt: pr.merged_at, headSha: pr.head.sha },
    commits, files, checks, mainRegression, defects, ruleset,
    comments: { reviews: parseVerdicts(comments, 'review'), qa: parseVerdicts(comments, 'qa') },
    guardSection: guardLines(pr.body), drill: opts.drill, andreiMinutes: opts.andreiMinutes,
  });
}

function collectSummary(repo) {
  const records = [];
  for (const issue of apiList(`repos/${repo}/issues?labels=task&state=all&per_page=100`)) {
    if (issue.pull_request) continue;
    const comments = apiList(`repos/${repo}/issues/${issue.number}/comments?per_page=100`);
    const record = comments.map((c) => parseRecord(c.body)).filter(Boolean).at(-1);
    if (record) records.push(record);
  }
  return records;
}

function parseArgs(argv) {
  const opts = { repo: 'andreykukalev/motiff-lab', positional: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--post') opts.post = true;
    else if (a === '--repo') opts.repo = argv[++i];
    else if (a === '--pr') opts.pr = Number(argv[++i]);
    else if (a === '--andrei-minutes') opts.andreiMinutes = Number(argv[++i]);
    else if (a === '--drill') opts.drill = argv[++i];
    else opts.positional.push(a);
  }
  if (opts.drill && !['passed', 'failed'].includes(opts.drill)) throw new Error('--drill takes passed or failed.');
  return opts;
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const [command, issue] = opts.positional;
  if (command === 'task' && issue) {
    const text = formatRecord(await collectTask(opts.repo, issue, opts));
    console.log(text);
    if (opts.post) {
      gh(['issue', 'comment', String(issue), '--repo', opts.repo, '--body-file', '-'], text);
      console.log(`\nPosted to #${issue}.`);
    }
  } else if (command === 'summary') {
    const records = collectSummary(opts.repo);
    console.log(`${records.length} tasks with metrics\n\n${summarize(records)}`);
  } else {
    console.log('Usage: node governance/metrics/collect.mjs task <issue> [--pr n] [--andrei-minutes n] [--drill passed|failed] [--post]\n       node governance/metrics/collect.mjs summary');
    process.exitCode = 2;
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) {
  main().catch((e) => { console.error(e.message); process.exitCode = 1; });
}
