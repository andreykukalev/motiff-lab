// archive-guard (PreToolUse: Read, Grep, Glob, Edit, Write, MultiEdit, NotebookEdit, Bash): asks before an agent reads,
// searches or changes archive/, the archived web (docs/repository-structure.md, 0033). Agents work only on active code,
// so archived code never enters their context. The one exception is a task whose allowed_paths (.aom/task.json) name
// archive/ (reactivating the web, docs/web-archive.md): Andrei's decision. Search tools also skip archive/ by .ignore.
// Rules: governance/roles/common.md, "Platform focus" and "Guards".

import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { globMatch, logIncident, programWords, readEvent, repoRoot, respond, splitCommands } from './guard-common.mjs';
import { contextFor, relativeToRoot } from './scope-guard.mjs';

export const ARCHIVE = 'archive';

const REASON = 'archive/ is the archived web (docs/web-archive.md): agents do not read, search or change it, so it stays out of '
  + 'their context. Work from src/, tests/ and docs/; what the web did is in docs/web-archive.md and the tag v0.6.5. '
  + 'Only a task whose allowed_paths name archive/ (reactivation, Andrei\'s decision) opens it.';

/** Whether a path relative to the repository root is in the archive. */
export function inArchive(rel) {
  return rel !== null && globMatch(ARCHIVE, rel);
}

/** Whether the task's scope opens the archive: its allowed_paths name a path in it. */
export function archiveOpen(scope) {
  return Boolean(scope?.allowed_paths?.some((p) => inArchive(p.replace(/^\.?\//, ''))));
}

/** The paths a tool call reads or writes, relative to the root (null outside it), or pattern text that names the archive. */
export function touchedPaths(tool, input, root, cwd) {
  const rel = (file) => relativeToRoot(root, path.resolve(cwd, file));
  switch (tool) {
    case 'Read': case 'Edit': case 'Write': case 'MultiEdit': return [rel(input.file_path || '')];
    case 'NotebookEdit': return [rel(input.notebook_path || '')];
    case 'Grep': case 'Glob': {
      const base = input.path ? rel(input.path) : null;
      const glob = (input.glob || (tool === 'Glob' ? input.pattern : '') || '').replace(/^\.?\//, '');
      return [base, base === null || base === '' ? glob : null];
    }
    case 'Bash': return bashPaths(input.command || '', root, cwd);
    default: return [];
  }
}

// The words of a shell command that name a path in the archive: archive/…, ./archive/…, or an absolute path under the
// root. An exclusion (git's :(exclude)archive/**, :!archive) is not a use.
function bashPaths(command, root, cwd) {
  const paths = [];
  for (const part of splitCommands(command)) {
    for (const word of programWords(part)) {
      const bare = word.replace(/^['"]|['"]$/g, '').replace(/^--?[A-Za-z-]+=/, '');
      if (!bare || bare.startsWith(':') || bare.startsWith('$') || bare.startsWith('~')) continue;
      if (/^(?:\.\/)?archive(?:\/|$)/.test(bare) || path.isAbsolute(bare)) paths.push(relativeToRoot(root, path.resolve(cwd, bare)));
    }
  }
  return paths;
}

export function decideArchive(tool, input, root, cwd, scope) {
  if (archiveOpen(scope)) return { decision: 'allow' };
  return touchedPaths(tool, input, root, cwd).some(inArchive) ? { decision: 'ask', reason: REASON } : { decision: 'allow' };
}

async function main() {
  const event = await readEvent();
  const cwd = event.cwd || process.cwd();
  const root = repoRoot(cwd);
  const input = event.tool_input || {};
  const result = decideArchive(event.tool_name, input, root, cwd, contextFor(root, cwd).scope);
  if (result.decision !== 'allow') {
    const detail = (input.command || input.file_path || input.path || input.pattern || '').slice(0, 300);
    logIncident(root, { hook: 'archive-guard', decision: result.decision, tool: event.tool_name, detail, reason: result.reason });
    respond(result.decision, result.reason);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) await main();
