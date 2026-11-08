// scope-guard (PreToolUse: Edit, Write, MultiEdit, NotebookEdit, Bash): asks before a change outside the task.
//  1. The agents' own environment (.claude/, governance/, .github/, CLAUDE.md, tests/acceptance/) changes only in an
//     environment task: on a branch env/*, or when AOM_ENV_TASK=1 is set for the session by Andrei. Elsewhere: ask.
//  2. When the task's scope is written to .aom/task.json ({"task": 12, "allowed_paths": ["src/MotiffLab.Web/Pages/**"]}),
//     a file outside allowed_paths: ask. The file itself is changed only by Andrei's decision: ask.
// It asks rather than refuses, because the person may agree; where nobody can answer, the platform treats it as a
// refusal. Rules: governance/roles/common.md, "Ограждения" and "Остановка".

import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import {
  currentBranch, globMatch, isProtected, logIncident, programName, programWords, PROTECTED_PATHS, readEvent, repoRoot,
  respond, splitCommands,
} from './guard-common.mjs';

const EDIT_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);
// Shell commands that change files. A heuristic: it only has to notice the usual ways, not every one.
const WRITE_PROGRAMS = new Set(['tee', 'mv', 'cp', 'rm', 'touch', 'mkdir', 'chmod', 'ln', 'truncate', 'install', 'rsync', 'patch',
  'set-content', 'out-file', 'add-content', 'remove-item', 'copy-item', 'move-item', 'new-item', 'rename-item']);
const GIT_WRITES = new Set(['checkout', 'restore', 'rm', 'mv']);

// The files a simple shell command writes to: redirection targets, and the arguments of commands that change files.
export function bashTargets(command) {
  const targets = [];
  for (const all of splitCommands(command)) {
    const words = programWords(all);
    if (!words.length) continue;
    const name = programName(words[0]);
    for (let i = 0; i < words.length; i++) {
      const redirect = /^(?:\d|&)?>>?(.*)$/.exec(words[i]);
      if (redirect) targets.push(redirect[1] || words[i + 1] || '');
    }
    const args = words.slice(1).filter((w) => !w.startsWith('-') && !/^(?:\d|&)?>/.test(w));
    if (WRITE_PROGRAMS.has(name)) targets.push(...args);
    else if ((name === 'sed' || name === 'perl') && words.some((w) => /^-[A-Za-z]*i|^--in-place/.test(w))) targets.push(...args);
    else if (name === 'git' && GIT_WRITES.has(words[1])) targets.push(...args.slice(1));
  }
  return targets.filter((t) => t && t !== '/dev/null' && !t.startsWith('$'));
}

function readScope(root) {
  const file = path.join(root, '.aom', 'task.json');
  if (!existsSync(file)) return null;
  try {
    const scope = JSON.parse(readFileSync(file, 'utf8'));
    return Array.isArray(scope.allowed_paths) && scope.allowed_paths.length ? scope : null;
  } catch {
    return { invalid: true };
  }
}

function relativeToRoot(root, file) {
  const abs = path.resolve(root, file);
  const rel = path.relative(root, abs).split(path.sep).join('/');
  return rel.startsWith('..') || path.isAbsolute(rel) ? null : rel;
}

export function decideFile(rel, { envTask, scope, scopeExists }) {
  if (rel === null) return { decision: 'allow' }; // outside the repository: scratch files
  if (rel === '.aom/task.json') {
    return scopeExists
      ? { decision: 'ask', reason: 'Правка .aom/task.json меняет рамки задачи: allowed_paths меняются только решением Андрея в Task Record.' }
      : { decision: 'allow' };
  }
  if (rel.startsWith('.aom/')) return { decision: 'allow' };
  if (isProtected(rel) && !envTask) {
    return {
      decision: 'ask',
      reason: `«${rel}» — часть среды агентов (${PROTECTED_PATHS.join(', ')}). Её меняет только задача среды (E3), которую применяет Андрей.`,
    };
  }
  if (scope?.invalid) return { decision: 'ask', reason: '.aom/task.json не читается как JSON; рамки задачи неизвестны.' };
  if (scope && !isProtected(rel) && !scope.allowed_paths.some((p) => globMatch(p, rel))) {
    return {
      decision: 'ask',
      reason: `«${rel}» вне allowed_paths задачи #${scope.task ?? '?'} (${scope.allowed_paths.join(', ')}). Это условие остановки: опиши, зачем нужен файл, и спроси Андрея.`,
    };
  }
  return { decision: 'allow' };
}

export function decideBash(command, root, cwd, context) {
  for (const target of bashTargets(command)) {
    if (target.startsWith('~')) continue;
    const rel = relativeToRoot(root, path.resolve(cwd, target));
    if (rel === null) continue;
    const result = decideFile(rel, context);
    if (result.decision !== 'allow' && (isProtected(rel) || rel === '.aom/task.json')) return result;
  }
  return { decision: 'allow' };
}

export function contextFor(root, cwd, env = process.env) {
  const branch = currentBranch(cwd);
  const scope = readScope(root);
  return {
    envTask: /^env\//.test(branch) || env.AOM_ENV_TASK === '1',
    scope,
    scopeExists: existsSync(path.join(root, '.aom', 'task.json')),
  };
}

async function main() {
  const event = await readEvent();
  const tool = event.tool_name;
  const cwd = event.cwd || process.cwd();
  const root = repoRoot(cwd);
  const context = contextFor(root, cwd);
  let result = { decision: 'allow' };
  let detail = '';
  if (EDIT_TOOLS.has(tool)) {
    const file = event.tool_input?.file_path || event.tool_input?.notebook_path || '';
    detail = file;
    result = decideFile(relativeToRoot(root, path.resolve(cwd, file)), context);
  } else if (tool === 'Bash') {
    detail = (event.tool_input?.command || '').slice(0, 300);
    result = decideBash(event.tool_input?.command || '', root, cwd, context);
  }
  if (result.decision !== 'allow') {
    logIncident(root, { hook: 'scope-guard', decision: result.decision, tool, detail, reason: result.reason });
    respond(result.decision, result.reason);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) await main();
