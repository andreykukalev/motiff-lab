// push-guard (PreToolUse, Bash): an agent writes to GitHub only by pushing its own branch.
// Refused: a push to main or to any branch but the one checked out, a push from main, force, delete, tags, --all,
// --mirror, another remote; gh commands that merge, run workflows, change releases, secrets, variables or the
// repository, any gh api call that is not a read, and writes to api.github.com through curl or PowerShell; the Azure
// CLI and the infrastructure scripts (Andrei runs those). Rules: governance/roles/common.md, "Ограждения".
// This is the second line, not the boundary: the ruleset on main refuses what slips through.

import path from 'node:path';
import { pathToFileURL } from 'node:url';
import {
  AGENT_BRANCH, currentBranch, logIncident, programName, programWords, readEvent, repoRoot, respond, splitCommands,
} from './guard-common.mjs';

const GIT_OPTIONS_WITH_VALUE = new Set(['-C', '-c', '--git-dir', '--work-tree', '--namespace', '--exec-path', '--config-env']);
const PUSH_OPTIONS_WITH_VALUE = new Set(['-o', '--push-option', '--repo', '--receive-pack', '--exec', '--signed']);
const PUSH_REFUSED = /^(--force|--force-with-lease|--force-if-includes|--mirror|--all|--branches|--tags|--follow-tags|--delete|--prune)(=|$)/;

const GH_REFUSED = {
  pr: ['merge'],
  workflow: ['run', 'enable', 'disable'],
  run: ['rerun', 'cancel', 'delete'],
  release: ['create', 'delete', 'edit', 'upload', 'delete-asset'],
  repo: ['create', 'delete', 'edit', 'rename', 'archive', 'unarchive', 'fork', 'sync', 'deploy-key'],
  auth: ['token', 'login', 'logout', 'refresh', 'setup-git', 'switch'],
  issue: ['delete', 'transfer'],
  cache: ['delete'],
  gist: ['create', 'edit', 'delete'],
  extension: ['install', 'upgrade', 'exec'],
};
const SHELLS = new Set(['bash', 'sh', 'zsh', 'dash', 'eval', 'cmd', 'pwsh', 'powershell', 'ssh', 'source', '.']);
const GH_REFUSED_ALL =new Set(['secret', 'variable', 'ruleset', 'codespace', 'attestation', 'agent-task']);

function refuse(reason) {
  return { decision: 'deny', reason };
}

function checkPush(args, cwd, branchOf) {
  const positional = [];
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--') { positional.push(...args.slice(i + 1)); break; }
    if (PUSH_REFUSED.test(a)) return refuse(`git push ${a} запрещён: агент пушит только свою ветку, без force, удаления и тегов.`);
    if (/^-[A-Za-z]+$/.test(a) && /[fd]/.test(a)) return refuse(`git push ${a} запрещён: force (-f) и удаление (-d) не разрешены.`);
    if (PUSH_OPTIONS_WITH_VALUE.has(a)) { i++; continue; }
    if (a.startsWith('-')) continue;
    positional.push(a);
  }
  const branch = branchOf(cwd);
  if (!branch || branch === 'HEAD') return refuse('Не удалось определить текущую ветку; push не разрешён.');
  if (!AGENT_BRANCH.test(branch)) {
    return refuse(`Текущая ветка «${branch}» не ветка агента (claude/*, task/*, env/*). Push из main и чужих веток запрещён.`);
  }
  const [remote, ...refspecs] = positional;
  if (remote && remote !== 'origin') return refuse(`Push только в origin, не в «${remote}».`);
  for (const spec of refspecs.length ? refspecs : [branch]) {
    if (spec.startsWith('+')) return refuse(`Refspec «${spec}» с + — это force push; запрещён.`);
    if (spec.startsWith(':')) return refuse(`Refspec «${spec}» удаляет ветку; запрещён.`);
    const parts = spec.split(':');
    const target = (parts.length > 1 ? parts[1] : (parts[0] === 'HEAD' ? branch : parts[0])).replace(/^refs\/heads\//, '');
    if (target.startsWith('refs/')) return refuse(`Push в «${target}» запрещён: только ветки, не теги и другие ссылки.`);
    if (target !== branch) {
      return refuse(`Push в «${target}» запрещён: агент пушит только текущую ветку «${branch}». В main попадает только PR, который сливает Андрей.`);
    }
  }
  return null;
}

function checkGit(words, cwd, branchOf) {
  let i = 1;
  let gitCwd = cwd;
  while (i < words.length && words[i].startsWith('-')) {
    const w = words[i];
    if (w === '-C') gitCwd = path.resolve(gitCwd, words[i + 1] || '.');
    if (GIT_OPTIONS_WITH_VALUE.has(w)) i += 2; else i += 1;
  }
  if (words[i] !== 'push') return null;
  return checkPush(words.slice(i + 1), gitCwd, branchOf);
}

function checkGh(words) {
  const sub = words[1];
  const action = words[2];
  if (GH_REFUSED_ALL.has(sub)) return refuse(`gh ${sub} запрещён агентам: настройки репозитория меняет только Андрей.`);
  if (GH_REFUSED[sub] && GH_REFUSED[sub].includes(action)) {
    return refuse(`gh ${sub} ${action} запрещён агентам (сливает, запускает workflow, выпускает и меняет настройки только Андрей).`);
  }
  if (sub === 'api') {
    let method = null;
    let hasBody = false;
    for (let i = 2; i < words.length; i++) {
      const w = words[i];
      const m = /^(?:-X|--method)(?:=(.+))?$/.exec(w);
      if (m) { method = (m[1] || words[i + 1] || '').toUpperCase(); continue; }
      if (/^-X[A-Za-z]+$/.test(w)) { method = w.slice(2).toUpperCase(); continue; }
      if (/^(-f|-F|--field|--raw-field|--input)(=|$)/.test(w) || /^-[fF].+/.test(w)) hasBody = true;
    }
    if (!method) method = hasBody ? 'POST' : 'GET';
    if (method !== 'GET' && method !== 'HEAD') {
      return refuse(`gh api с методом ${method} запрещён: агенты только читают API, а пишут через свою ветку и PR.`);
    }
  }
  return null;
}

function checkHttp(words, raw) {
  const text = words.join(' ');
  if (!/(api|uploads)\.github\.com|github\.com\/repos\//i.test(text)) return null;
  const write = /(^|\s)(-X|--request|-Method)\s*(POST|PUT|PATCH|DELETE)\b/i.test(text)
    || /(^|\s)(-d|--data|--data-raw|--data-binary|--data-urlencode|-F|--form|-T|--upload-file|-Body|-InFile)(\s|=|$)/i.test(text)
    || /-Method\s+(Post|Put|Patch|Delete)/i.test(raw);
  return write ? refuse('Запись в GitHub API через curl или PowerShell запрещена агентам.') : null;
}

export function check(command, cwd, branchOf = currentBranch) {
  const commands = splitCommands(command || '');
  let sawPush = false;
  let sawShell = false;
  for (const all of commands) {
    const words = programWords(all);
    if (!words.length) continue;
    const name = programName(words[0]);
    let result = null;
    if (SHELLS.has(name)) sawShell = true;
    if (name === 'cd' && words[1]) { cwd = path.resolve(cwd, words[1]); continue; }
    if (name === 'git') {
      if (words.includes('push')) sawPush = true;
      result = checkGit(words, cwd, branchOf);
    } else if (name === 'gh') result = checkGh(words);
    else if (['curl', 'wget', 'http', 'https', 'invoke-webrequest', 'invoke-restmethod', 'iwr', 'irm'].includes(name)) result = checkHttp(words, command);
    else if (name === 'az' || name === 'azd') result = refuse('Azure CLI агентам запрещён: в Azure пишет только workflow после одобрения Андрея.');
    else if (['pwsh', 'powershell'].includes(name) && /infra[\\/](azure|github)/i.test(words.join(' '))) {
      result = refuse('Скрипты infra/azure и infra/github запускает только Андрей.');
    }
    if (result) return result;
  }
  // A push the parser could not see (inside bash -c '...', eval) is refused rather than guessed at.
  if (!sawPush && sawShell && /\bgit\b[^\n;&|]*\bpush\b/.test(command || '')) {
    return refuse('Не удалось разобрать git push в этой команде; запусти git push отдельной простой командой.');
  }
  return { decision: 'allow' };
}

async function main() {
  const event = await readEvent();
  if (event.tool_name !== 'Bash') return;
  const cwd = event.cwd || process.cwd();
  const command = event.tool_input?.command || '';
  const result = check(command, cwd);
  if (result.decision !== 'allow') {
    logIncident(repoRoot(cwd), { hook: 'push-guard', decision: result.decision, tool: 'Bash', command: command.slice(0, 300), reason: result.reason });
    respond(result.decision, `${result.reason} Не обходи ограждение: остановись и опиши в PR или треде, что было нужно (governance/roles/common.md).`);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) await main();
