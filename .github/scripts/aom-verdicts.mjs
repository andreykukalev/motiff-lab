// The check aom-verdicts (Task Records #300, #358): a pull request's head commit gets the commit status `aom-verdicts`,
// success only when the Reviewer's `approve` and QA's `pass` exist for that exact commit, or when the pull request is
// docs-only (`isDocsOnly`): then Andrei reviews it himself and no verdict is needed. The verdicts are the comments the
// roles write (governance/roles/reviewer.md, qa.md, "Output"). Run by .github/workflows/aom-verdicts.yml on comment
// and pull request events; `isDocsOnly`, `evaluate` and `decide` are pure and tested in
// governance/tests/verdicts.test.mjs.

export const CONTEXT = 'aom-verdicts';
export const TRUSTED = new Set(['OWNER', 'MEMBER', 'COLLABORATOR']);

const KINDS = {
  review: { role: 'Reviewer', good: 'approve', bad: 'changes-requested' },
  qa: { role: 'QA', good: 'pass', bad: 'fail' },
};

// A verdict comment: its first non-empty line is `<!-- aom:review -->` or `<!-- aom:qa -->`; the header is that line and
// the lines after it up to the first blank line; in the header, `verdict: <value>` and `sha: <full SHA>`.
// Returns { kind, verdict, sha } or null for any other comment.
export function parseVerdict(body) {
  const lines = String(body ?? '').replace(/\r\n?/g, '\n').split('\n');
  let i = lines.findIndex((l) => l.trim() !== '');
  if (i < 0) return null;
  const marker = /^<!--\s*aom:(review|qa)\s*-->$/.exec(lines[i].trim());
  if (!marker) return null;
  const header = [];
  for (i += 1; i < lines.length && lines[i].trim() !== ''; i += 1) header.push(lines[i].trim());
  const field = (name) => {
    const line = header.find((l) => l.toLowerCase().startsWith(`${name}:`));
    return line === undefined ? '' : line.slice(name.length + 1).trim();
  };
  return { kind: marker[1], verdict: field('verdict').toLowerCase(), sha: field('sha').toLowerCase() };
}

// comments: the pull request's comments as the REST API gives them ({ id, created_at, author_association, body }).
// Returns the status for headSha: { state: 'success' | 'pending' | 'failure', description }.
export function evaluate(comments, headSha) {
  const head = String(headSha).toLowerCase();
  const short = head.slice(0, 7);
  const latest = {};
  const ordered = [...comments].sort((a, b) => String(a.created_at).localeCompare(String(b.created_at)) || a.id - b.id);
  for (const c of ordered) {
    if (!TRUSTED.has(c.author_association)) continue;
    const v = parseVerdict(c.body);
    if (!v || !/^[0-9a-f]{40}$/.test(v.sha) || v.sha !== head) continue;
    latest[v.kind] = v.verdict;
  }
  const waiting = [];
  const problems = [];
  for (const [kind, k] of Object.entries(KINDS)) {
    const verdict = latest[kind];
    if (verdict === undefined) waiting.push(k.role);
    else if (verdict === k.bad) problems.push(`${k.role} ${k.bad}`);
    else if (verdict !== k.good) problems.push(`${k.role} verdict unreadable`);
  }
  if (!waiting.length && !problems.length) return { state: 'success', description: `Reviewer approve and QA pass on ${short}` };
  const parts = [...problems];
  if (waiting.length) parts.push(`waiting for ${waiting.join(' and ')}`);
  return { state: problems.length ? 'failure' : 'pending', description: `${parts.join('; ')} on ${short}` };
}

// The one docs-only rule (Task Record #358, criterion 1). files: the pull request's changed files as the REST API
// gives them ({ filename, previous_filename? }); total: the number of changed files the pull request reports, so a
// list cut short by the API is never read as complete. Docs-only when every path, old and new path of a rename alike,
// is under docs/ or is the root README.md, and none is docs/project-file.md (High, the data contract). Anything else,
// a Guardrail file such as CLAUDE.md or governance/*.md included, makes the pull request not docs-only.
export function isDocsOnly(files, total = files.length) {
  if (!files.length || files.length !== total) return false;
  const paths = files.flatMap((f) => [f.filename, ...(f.previous_filename == null ? [] : [f.previous_filename])]);
  return paths.every((p) => typeof p === 'string'
    && (p === 'README.md' || (p.startsWith('docs/') && p.length > 'docs/'.length))
    && p !== 'docs/project-file.md');
}

// The status to report for headSha: docs-only success, whatever the comments say, or the verdicts' status.
export function decide({ files, total, comments, headSha }) {
  if (isDocsOnly(files, total)) {
    const short = String(headSha).toLowerCase().slice(0, 7);
    return { state: 'success', description: `docs-only: no Reviewer or QA needed on ${short}` };
  }
  return evaluate(comments, headSha);
}

async function list(token, url) {
  const items = [];
  for (let next = url; next;) {
    const response = await api(token, 'GET', next);
    items.push(...(await response.json()));
    next = /<([^>]+)>;\s*rel="next"/.exec(response.headers.get('link') || '')?.[1];
  }
  return items;
}

async function api(token, method, url, body) {
  const response = await fetch(url, {
    method,
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response;
}

async function main() {
  const { GITHUB_TOKEN: token, GITHUB_REPOSITORY: repo, GITHUB_API_URL: apiUrl = 'https://api.github.com' } = process.env;
  const number = Number(process.env.PR_NUMBER);
  if (!token || !repo || !number) throw new Error('GITHUB_TOKEN, GITHUB_REPOSITORY and PR_NUMBER are required');
  const getPr = async () => (await api(token, 'GET', `${apiUrl}/repos/${repo}/pulls/${number}`)).json();
  const pr = await getPr();
  const files = await list(token, `${apiUrl}/repos/${repo}/pulls/${number}/files?per_page=100`);
  const comments = await list(token, `${apiUrl}/repos/${repo}/issues/${number}/comments?per_page=100`);
  // A push while this run read the files: the lists may be of the newer head, so report nothing for the old one; the
  // push's own run reports the new head.
  if ((await getPr()).head.sha !== pr.head.sha) {
    console.log(`#${number}: the head moved from ${pr.head.sha} while reading, nothing reported`);
    return;
  }
  const status = decide({ files, total: pr.changed_files, comments, headSha: pr.head.sha });
  const runUrl = `${process.env.GITHUB_SERVER_URL}/${repo}/actions/runs/${process.env.GITHUB_RUN_ID}`;
  await api(token, 'POST', `${apiUrl}/repos/${repo}/statuses/${pr.head.sha}`, {
    state: status.state, context: CONTEXT, description: status.description, target_url: runUrl,
  });
  console.log(`#${number} ${pr.head.sha}: ${status.state} (${status.description})`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((error) => { console.error(error.message); process.exit(1); });
}
