// Tests of the agent definitions in .claude/agents: node --test "governance/tests/*.test.mjs" (Node 22+).
// Each test is a promise governance/roles/council.md makes: council seats only read, every role names its model, and
// the council skill dispatches only roles that exist.

import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const agentsDir = path.join(root, '.claude', 'agents');

// The frontmatter between the first two '---' lines, as flat key: value pairs.
export function frontmatter(text) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(text);
  if (!match) return null;
  const fields = {};
  for (const line of match[1].split(/\r?\n/)) {
    const pair = /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line);
    if (pair) fields[pair[1]] = pair[2].trim();
  }
  return { fields, body: text.slice(match[0].length) };
}

const agents = readdirSync(agentsDir)
  .filter((f) => f.endsWith('.md'))
  .map((file) => ({ file, ...frontmatter(readFileSync(path.join(agentsDir, file), 'utf8')) }));

const tools = (agent) => (agent.fields.tools || '').split(',').map((t) => t.trim()).filter(Boolean);
const READ_ONLY = new Set(['Read', 'Grep', 'Glob']);
const WRITERS = ['Edit', 'Write', 'MultiEdit', 'NotebookEdit', 'Agent', 'Task'];

test('every agent has a name matching its file, a description, an explicit tool list and a model', () => {
  assert.ok(agents.length > 0);
  for (const agent of agents) {
    assert.ok(agent.fields, `${agent.file}: no frontmatter`);
    assert.equal(agent.fields.name, agent.file.replace(/\.md$/, ''), agent.file);
    assert.ok(agent.fields.description, `${agent.file}: no description`);
    assert.ok(tools(agent).length > 0, `${agent.file}: no tools list (an agent without one inherits every tool)`);
    assert.match(agent.fields.model || '', /^(opus|sonnet|haiku)$/, `${agent.file}: model`);
    assert.ok(agent.body.trim().length > 200, `${agent.file}: empty prompt`);
  }
});

test('council seats only read', () => {
  const seats = agents.filter((a) => a.fields.name.startsWith('council-'));
  assert.ok(seats.length >= 6);
  for (const seat of seats) {
    for (const tool of tools(seat)) assert.ok(READ_ONLY.has(tool), `${seat.file}: ${tool} is not read-only`);
  }
});

// Bash can still write; that limit is an instruction (governance/roles/council.md, "Researcher").
test('the researcher has no edit or agent tools', () => {
  const researcher = agents.find((a) => a.fields.name === 'researcher');
  assert.ok(researcher);
  for (const tool of WRITERS) assert.ok(!tools(researcher).includes(tool), `researcher: ${tool}`);
});

test('the council skill dispatches only agents that exist', () => {
  const skill = readFileSync(path.join(root, '.claude', 'skills', 'council', 'SKILL.md'), 'utf8');
  assert.ok(frontmatter(skill)?.fields.name === 'council');
  const named = [...skill.matchAll(/`((?:council-[a-z-]+)|researcher)`/g)].map((m) => m[1]);
  assert.ok(named.length >= 7);
  const existing = new Set(agents.map((a) => a.fields.name));
  for (const name of named) assert.ok(existing.has(name), `SKILL.md names ${name}, which has no .claude/agents file`);
});
