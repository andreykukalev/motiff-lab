// The check aom-verdicts (Task Record #300): a pull request's head commit gets the commit status `aom-verdicts`,
// success only when the Reviewer's `approve` and QA's `pass` exist for that exact commit. The verdicts are the
// comments the roles write (governance/roles/reviewer.md, qa.md, "Output"). Run by .github/workflows/aom-verdicts.yml
// on comment events; `evaluate` is pure and tested in governance/tests/verdicts.test.mjs.

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
  const pr = await (await api(token, 'GET', `${apiUrl}/repos/${repo}/pulls/${number}`)).json();
  const comments = [];
  for (let url = `${apiUrl}/repos/${repo}/issues/${number}/comments?per_page=100`; url;) {
    const response = await api(token, 'GET', url);
    comments.push(...(await response.json()));
    url = /<([^>]+)>;\s*rel="next"/.exec(response.headers.get('link') || '')?.[1];
  }
  const status = evaluate(comments, pr.head.sha);
  const runUrl = `${process.env.GITHUB_SERVER_URL}/${repo}/actions/runs/${process.env.GITHUB_RUN_ID}`;
  await api(token, 'POST', `${apiUrl}/repos/${repo}/statuses/${pr.head.sha}`, {
    state: status.state, context: CONTEXT, description: status.description, target_url: runUrl,
  });
  console.log(`#${number} ${pr.head.sha}: ${status.state} (${status.description})`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((error) => { console.error(error.message); process.exit(1); });
}
