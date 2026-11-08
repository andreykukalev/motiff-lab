// Shared helpers for the agent guards (push-guard, scope-guard): governance/roles/common.md, "Guards".
// The guards are the second line: the ruleset on main and the Environment production are the boundary.
// Each guard reads the PreToolUse event as JSON on stdin and answers with a permission decision on stdout.
// Every deny or ask is appended to .aom/guard-log.jsonl (ignored by git), which the Developer copies into the PR
// (section "Guard triggers") and governance/metrics/collect.mjs counts.

import { execFileSync } from 'node:child_process';
import { appendFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';

export const AGENT_BRANCH = /^(claude|task|env)\/[A-Za-z0-9._\/-]+$/;

// Paths that only an environment task may change (E3): the rules and tools the agents themselves work under.
export const PROTECTED_PATHS = ['.claude/**', 'governance/**', '.github/**', 'CLAUDE.md', 'tests/acceptance/**'];

export async function readEvent() {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  const text = Buffer.concat(chunks).toString('utf8').trim();
  return text ? JSON.parse(text) : {};
}

export function respond(decision, reason) {
  // allow: say nothing, so the normal permission rules still apply.
  if (decision === 'allow') return;
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: decision, permissionDecisionReason: reason },
  }));
}

export function git(cwd, args) {
  try {
    return execFileSync('git', ['-C', cwd, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return '';
  }
}

export function repoRoot(cwd) {
  return git(cwd, ['rev-parse', '--show-toplevel']) || process.env.CLAUDE_PROJECT_DIR || cwd;
}

export function currentBranch(cwd) {
  return git(cwd, ['rev-parse', '--abbrev-ref', 'HEAD']);
}

export function logIncident(root, entry) {
  try {
    const dir = path.join(root, '.aom');
    mkdirSync(dir, { recursive: true });
    appendFileSync(path.join(dir, 'guard-log.jsonl'), JSON.stringify({ time: new Date().toISOString(), ...entry }) + '\n');
  } catch {
    // The log is a convenience; a guard never fails open or closed because of it.
  }
}

// Glob as in CODEOWNERS and governance/risk-paths: * stays within one folder, ** crosses folders.
// A pattern without wildcards also matches everything under it when it names a folder.
export function globMatch(pattern, file) {
  const p = pattern.replace(/^\.?\//, '').replace(/\/$/, '');
  const f = file.replace(/^\.?\//, '');
  if (!/[*?]/.test(p)) return f === p || f.startsWith(p + '/');
  let re = '';
  for (let i = 0; i < p.length; i++) {
    const c = p[i];
    if (c === '*' && p[i + 1] === '*') {
      i++;
      if (p[i + 1] === '/') { i++; re += '(?:.*/)?'; } else re += '.*';
    } else if (c === '*') re += '[^/]*';
    else if (c === '?') re += '[^/]';
    else re += c.replace(/[.+^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp('^' + re + '$').test(f);
}

// A file is protected when a pattern matches it, or when it is a folder that holds protected files (rm -rf tests).
export function isProtected(file) {
  const f = file.replace(/^\.?\//, '').replace(/\/$/, '');
  if (f === '' || f === '.') return true;
  return PROTECTED_PATHS.some((p) => globMatch(p, f) || globMatch(p, f + '/') || p.startsWith(f + '/'));
}

// Splits a shell command into simple commands (by ;, &&, ||, |, & and new lines) and each into words,
// honouring quotes. Good enough to find git, gh and curl calls; anything it cannot read is treated as unsafe
// by the callers.
export function splitCommands(command) {
  const commands = [];
  let words = [];
  let word = '';
  let inWord = false;
  let quote = null;
  const pushWord = () => { if (inWord) words.push(word); word = ''; inWord = false; };
  const pushCommand = () => { pushWord(); if (words.length) commands.push(words); words = []; };
  for (let i = 0; i < command.length; i++) {
    const c = command[i];
    if (quote) {
      if (c === quote) quote = null;
      else if (c === '\\' && quote === '"' && i + 1 < command.length) word += command[++i];
      else word += c;
      continue;
    }
    if (c === "'" || c === '"') { quote = c; inWord = true; continue; }
    if (c === '\\' && i + 1 < command.length) { word += command[++i]; inWord = true; continue; }
    if (c === ' ' || c === '\t') { pushWord(); continue; }
    if (c === '\n' || c === ';' || c === '|' || c === '&' || c === '(' || c === ')' || c === '`') { pushCommand(); continue; }
    if (c === '$' && command[i + 1] === '(') { pushCommand(); i++; continue; }
    word += c;
    inWord = true;
  }
  pushCommand();
  return commands;
}

// Drops what comes before the program itself: VAR=value assignments and wrappers such as sudo, env, time, nohup.
export function programWords(words) {
  let i = 0;
  while (i < words.length) {
    const w = words[i];
    if (/^[A-Za-z_][A-Za-z0-9_]*=/.test(w)) { i++; continue; }
    if (['sudo', 'env', 'time', 'nohup', 'command', 'exec', 'xargs'].includes(w)) { i++; continue; }
    break;
  }
  return words.slice(i);
}

export function programName(word) {
  return (word || '').split(/[\\/]/).pop().replace(/\.exe$/i, '').toLowerCase();
}
