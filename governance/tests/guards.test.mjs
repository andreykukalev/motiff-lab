// Tests of the agent guards in .claude/hooks: node --test "governance/tests/*.test.mjs" (Node 22+).
// Each failure test is a promise the guard makes in governance/roles/common.md ("Guards").

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { globMatch, splitCommands } from '../../.claude/hooks/guard-common.mjs';
import { check } from '../../.claude/hooks/push-guard.mjs';
import { decideArchive } from '../../.claude/hooks/archive-guard.mjs';
import { bashTargets, decideBash, decideFile } from '../../.claude/hooks/scope-guard.mjs';

const hooks = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '.claude', 'hooks');
const onBranch = (name) => () => name;
const push = (command, branch = 'claude/project-thread-abc') => check(command, '/repo', onBranch(branch)).decision;

test('push of the own agent branch is allowed', () => {
  assert.equal(push('git push -u origin claude/project-thread-abc'), 'allow');
  assert.equal(push('git push'), 'allow');
  assert.equal(push('git push origin HEAD'), 'allow');
  assert.equal(push('git add -A && git commit -m "fix" && git push -u origin claude/project-thread-abc'), 'allow');
  assert.equal(push('git -C /repo push origin HEAD:claude/project-thread-abc'), 'allow');
});

test('push to main or another branch is refused', () => {
  assert.equal(push('git push origin main'), 'deny');
  assert.equal(push('git push origin HEAD:main'), 'deny');
  assert.equal(push('git push origin HEAD:refs/heads/main'), 'deny');
  assert.equal(push('git push origin claude/project-thread-other'), 'deny');
  assert.equal(push('git push', 'main'), 'deny');
  assert.equal(push('git push origin feature', 'feature'), 'deny');
  assert.equal(push('git push upstream claude/project-thread-abc'), 'deny');
});

test('force, delete, tags and mirror are refused', () => {
  for (const command of [
    'git push --force', 'git push -f origin claude/project-thread-abc', 'git push -uf origin claude/project-thread-abc',
    'git push --force-with-lease', 'git push origin +claude/project-thread-abc', 'git push origin :claude/project-thread-abc',
    'git push --delete origin claude/project-thread-abc', 'git push --tags', 'git push origin refs/tags/ipad-v1.0',
    'git push --mirror', 'git push --all',
  ]) assert.equal(push(command), 'deny', command);
});

test('a push hidden in another shell is refused, a commit message about pushes is not', () => {
  assert.equal(push('bash -c "git push origin main"'), 'deny');
  assert.equal(push('git commit -m "push guard: refuse pushes to main"'), 'allow');
});

test('gh: merges, workflow runs, settings and API writes are refused; reads are allowed', () => {
  for (const command of [
    'gh pr merge 12 --squash', 'gh workflow run ci.yml', 'gh run rerun 123', 'gh secret set X', 'gh variable set X',
    'gh api -X PUT repos/o/r/rulesets/1', 'gh api repos/o/r/issues/1/comments -f body=hi', 'gh api --method=DELETE repos/o/r',
    'gh auth token', 'gh repo edit --visibility private', 'gh release create v1',
  ]) assert.equal(push(command), 'deny', command);
  for (const command of [
    'gh pr view 12', 'gh pr checks 12', 'gh api repos/o/r/pulls/12', "gh api -X GET search/code -f q='push guard'",
    'gh run view 123 --log-failed', 'gh pr create --fill',
  ]) assert.equal(push(command), 'allow', command);
});

test('curl writes to the GitHub API and the Azure CLI are refused', () => {
  assert.equal(push('curl -X POST https://api.github.com/repos/o/r/merges -d @x.json'), 'deny');
  assert.equal(push('curl -s https://api.github.com/repos/o/r'), 'allow');
  assert.equal(push('az deployment group create -g rg'), 'deny');
  assert.equal(push('pwsh ./infra/github/protect.ps1'), 'deny');
});

test('environment files ask outside an environment task, and pass in one', () => {
  const plain = { envTask: false, scope: null, scopeExists: false };
  for (const file of ['.claude/settings.json', 'governance/roles/common.md', '.github/workflows/ci.yml', 'CLAUDE.md', 'tests/backend/Acceptance/MotiffLab.Acceptance.Tests/a.md',
    'docs/ux-baseline.md', 'tests/ipad/Baseline/SelectionTests.swift', 'tests/contracts/page-scenarios/sel-grid.json']) {
    assert.equal(decideFile(file, plain).decision, 'ask', file);
    assert.equal(decideFile(file, { ...plain, envTask: true }).decision, 'allow', file);
  }
  assert.equal(decideFile('src/ipad/App/MotiffLab/LibraryView.swift', plain).decision, 'allow');
  assert.equal(decideFile('docs/ui-conventions.md', plain).decision, 'allow');
  assert.equal(decideFile(null, plain).decision, 'allow');
});

test('allowed_paths: a file outside the task asks, and so does changing the scope', () => {
  const scope = { task: 12, allowed_paths: ['src/ipad/App/MotiffLab/Library*', 'tests/shared/**'] };
  const context = { envTask: false, scope, scopeExists: true };
  assert.equal(decideFile('src/ipad/App/MotiffLab/LibraryView.swift', context).decision, 'allow');
  assert.equal(decideFile('tests/shared/library/empty.test.mjs', context).decision, 'allow');
  assert.equal(decideFile('src/backend/MotiffLab.MusicCore/Facade/Facade.cs', context).decision, 'ask');
  assert.equal(decideFile('.aom/task.json', context).decision, 'ask');
  assert.equal(decideFile('.aom/task.json', { envTask: false, scope: null, scopeExists: false }).decision, 'allow');
});

test('shell writes to environment files ask, reads and other writes do not', () => {
  const context = { envTask: false, scope: null, scopeExists: false };
  assert.deepEqual(bashTargets('echo x > CLAUDE.md && cat a 2>/dev/null'), ['CLAUDE.md']);
  assert.equal(decideBash("sed -i 's/a/b/' .github/workflows/ci.yml", '/repo', '/repo', context).decision, 'ask');
  assert.equal(decideBash('rm -rf governance', '/repo', '/repo', context).decision, 'ask');
  assert.equal(decideBash('rm -rf tests', '/repo', '/repo', context).decision, 'ask');
  assert.equal(decideBash('rm -rf src/shared/bin', '/repo', '/repo', context).decision, 'allow');
  assert.equal(decideBash('cat .github/workflows/ci.yml 2>/dev/null | head', '/repo', '/repo', context).decision, 'allow');
  assert.equal(decideBash('echo hi > notes.txt', '/repo', '/repo', context).decision, 'allow');
});

test('globs: * stays in a folder, ** crosses folders, a plain folder name covers what is inside', () => {
  assert.ok(globMatch('src/*.cs', 'src/a.cs'));
  assert.ok(!globMatch('src/*.cs', 'src/x/a.cs'));
  assert.ok(globMatch('src/**', 'src/x/a.cs'));
  assert.ok(globMatch('src/**/a.cs', 'src/a.cs'));
  assert.ok(globMatch('docs', 'docs/README.md'));
  assert.ok(!globMatch('docs', 'docsx/README.md'));
});

test('the shell splitter keeps quoted words together', () => {
  assert.deepEqual(splitCommands('git commit -m "a; b" && git push'), [['git', 'commit', '-m', 'a; b'], ['git', 'push']]);
});

test('the hooks answer the platform in its format, end to end', () => {
  const repo = mkdtempSync(path.join(tmpdir(), 'guards-'));
  execFileSync('git', ['init', '-q', '-b', 'main', repo]);
  const run = (hook, event) => execFileSync('node', [path.join(hooks, hook)], { input: JSON.stringify({ cwd: repo, ...event }), encoding: 'utf8' });
  const denied = JSON.parse(run('push-guard.mjs', { tool_name: 'Bash', tool_input: { command: 'git push origin main' } }));
  assert.equal(denied.hookSpecificOutput.permissionDecision, 'deny');
  assert.equal(run('push-guard.mjs', { tool_name: 'Bash', tool_input: { command: 'ls' } }), '');
  mkdirSync(path.join(repo, '.claude'));
  writeFileSync(path.join(repo, '.claude', 'settings.json'), '{}');
  const asked = JSON.parse(run('scope-guard.mjs', { tool_name: 'Edit', tool_input: { file_path: path.join(repo, '.claude', 'settings.json') } }));
  assert.equal(asked.hookSpecificOutput.permissionDecision, 'ask');
});

test('the archive stays out of the agents\' context: reads, searches and changes there ask', () => {
  const archive = (tool, input, scope = null) => decideArchive(tool, input, '/repo', '/repo', scope).decision;
  assert.equal(archive('Read', { file_path: '/repo/archive/web/src/MotiffLab.Web/Pages/PlaybackLab.razor' }), 'ask');
  assert.equal(archive('Edit', { file_path: 'archive/web/README.md' }), 'ask');
  assert.equal(archive('Grep', { pattern: 'Playback', path: '/repo/archive' }), 'ask');
  assert.equal(archive('Grep', { pattern: 'Playback', glob: 'archive/**/*.cs' }), 'ask');
  assert.equal(archive('Glob', { pattern: 'archive/web/**/*.razor' }), 'ask');
  assert.equal(archive('Bash', { command: 'cat archive/web/src/MotiffLab.Web/Program.cs | head' }), 'ask');
  assert.equal(archive('Bash', { command: 'grep -rn Playback ./archive/web' }), 'ask');
  for (const [tool, input] of [
    ['Read', { file_path: '/repo/src/shared/Scripts/score/score-page.ts' }],
    ['Read', { file_path: '/repo/docs/web-archive.md' }],
    ['Grep', { pattern: 'archive', path: '/repo/src' }],
    ['Glob', { pattern: '**/*.ts', path: '/repo/src/shared' }],
    ['Bash', { command: "git grep -n score -- ':(exclude)archive/**'" }],
    ['Bash', { command: 'cat /tmp/archive/notes.txt' }],
  ]) assert.equal(archive(tool, input), 'allow', JSON.stringify(input));
  // A reactivation task, whose allowed_paths Andrei set to name the archive, opens it.
  assert.equal(archive('Read', { file_path: 'archive/web/README.md' }, { task: 9, allowed_paths: ['archive/web/**', 'src/web/**'] }), 'allow');
  assert.equal(archive('Read', { file_path: 'archive/web/README.md' }, { task: 9, allowed_paths: ['src/ipad/**'] }), 'ask');
});

test('search tools skip the archive by .ignore', () => {
  const ignore = readFileSync(new URL('../../.ignore', import.meta.url), 'utf8').split('\n').map((line) => line.trim());
  assert.ok(ignore.includes('archive/'));
});

