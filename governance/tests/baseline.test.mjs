// The guard of the UX baseline (docs/ux-baseline.md, "Fields"): node --test "governance/tests/*.test.mjs".
// Every entry has a valid id, status and «Protected by»; a test it names exists under the protected folders and names
// the entry, or a journey of the walk it names exists and lists the entry; every file under tests/ipad/Baseline/ and
// tests/contracts/page-scenarios/ names an entry of the list. The folders may not exist yet.

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { swiftCode } from '../../tools/quality/dependencies.mjs';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const DOCUMENT = 'docs/ux-baseline.md';
const STATUSES = ['approved', 'provisional', 'known defect'];
/** Where a protecting test lives: the folders the label protected-scenarios-ok guards. */
const PROTECTED = ['tests/backend/Acceptance/', 'tests/ipad/Baseline/', 'tests/contracts/page-scenarios/'];
/** The folders whose every file names an entry (a README and fixture folders aside). */
const NAMING = ['tests/ipad/Baseline/', 'tests/contracts/page-scenarios/'];
/** The walk of the screens: a journey there protects the entries its `protects:` lists; no label guards the folder. */
const WALK = 'tests/ipad/MotiffLabScreens/';
/** How the walk declares a journey (ScreensUITests.swift): its name, the ids it protects, and up to its `run:` (last). */
const JOURNEY = /\bJourney\(\s*name:\s*"([^"]+)",\s*protects:\s*\[([^\]]*)\]([^]*?)\brun:/g;
const ID = /^[A-Z]{2,5}-\d{2}$/;
const STEP = /^([A-D]\d+|baseline 0\.\d+)$/;

/** The rows of the table under "## Entries": { id, struck, behaviour, approved, status, protectedBy }. */
export function entries(markdown) {
  const section = markdown.split(/^## /m).find((part) => part.startsWith('Entries\n')) ?? '';
  return section.split('\n').filter((line) => line.startsWith('|')).slice(2).map((line) => {
    const cells = line.split(/(?<!\\)\|/).slice(1, -1).map((cell) => cell.trim());
    const struck = /^~~.+~~$/.test(cells[0] ?? '');
    const [, behaviour, approved, status, protectedBy] = cells;
    return { id: struck ? cells[0].slice(2, -2) : cells[0], struck, cells: cells.length, behaviour, approved, status, protectedBy };
  });
}

/** What is wrong with the list, given the files under the protected folders (repository path → text). */
export function problems(markdown, files) {
  const found = [];
  const rows = entries(markdown);
  if (rows.length === 0) found.push(`${DOCUMENT} has no entries`);
  const seen = new Set();
  for (const row of rows) {
    if (!ID.test(row.id ?? '')) found.push(`bad id "${row.id}"`);
    if (seen.has(row.id)) found.push(`${row.id}: id used twice`);
    seen.add(row.id);
    if (row.struck) continue;
    if (row.cells !== 5) found.push(`${row.id}: ${row.cells} cells, not 5`);
    if (!row.behaviour) found.push(`${row.id}: no behaviour`);
    if (!row.approved) found.push(`${row.id}: no approval`);
    if (!STATUSES.includes(row.status)) found.push(`${row.id}: status "${row.status}" is not one of ${STATUSES.join(', ')}`);
    const items = (row.protectedBy ?? '').split(';').map((item) => item.trim()).filter(Boolean);
    if (items.length === 0) found.push(`${row.id}: nothing in «Protected by»`);
    for (const item of items) found.push(...protection(row.id, item, files));
  }
  const live = new Set(rows.filter((row) => !row.struck).map((row) => row.id));
  const prefixes = [...new Set([...live].map((id) => id.split('-')[0]))];
  const token = new RegExp(`\\b(?:${prefixes.join('|') || '(?!)'})-\\d{2}\\b`, 'g');
  for (const [file, text] of files) {
    if (!NAMING.some((folder) => file.startsWith(folder)) || exempt(file)) continue;
    const named = text.match(token) ?? [];
    if (named.length === 0) found.push(`${file} names no entry of ${DOCUMENT}`);
    for (const id of named) if (!live.has(id)) found.push(`${file} names ${id}, which ${DOCUMENT} does not list`);
  }
  return found;
}

function protection(id, item, files) {
  if (item.startsWith('pending:')) {
    const step = /^(.+?)(\s+\(.+\))?$/.exec(item.slice(8).trim())?.[1] ?? '';
    return STEP.test(step) ? [] : [`${id}: "${item}" names no step (A4, A5, B4, … or baseline 0.<n>)`];
  }
  if (item.startsWith('device:')) return item.slice(7).trim() ? [] : [`${id}: "device:" without a checklist line`];
  const [, file, journey] = /^`([^`]+)`(?:\s+journey\s+`([^`]+)`)?(\s+\(.+\))?$/.exec(item) ?? [];
  if (!file) return [`${id}: "${item}" is no test path, device: or pending:`];
  if (file.startsWith(WALK)) return walkProtection(id, file, journey, files);
  if (journey) return [`${id}: ${file} is no walk file: a journey is named only in ${WALK}`];
  if (!PROTECTED.some((folder) => file.startsWith(folder))) return [`${id}: ${file} is not under ${PROTECTED.join(', ')}, ${WALK}`];
  if (!files.has(file)) return [`${id}: ${file} does not exist`];
  return files.get(file).includes(id) ? [] : [`${id}: ${file} does not name ${id}`];
}

/**
 * A journey of the walk: `<file>` journey `<name>`, declared in that file's code (not in a comment) with the entry among
 * its `protects:`, and walked in the asserting walk (not `picturesOnly`).
 */
function walkProtection(id, file, journey, files) {
  if (!journey) return [`${id}: ${file} is the walk: name its journey (\`${file}\` journey \`<name>\`)`];
  if (!files.has(file)) return [`${id}: ${file} does not exist`];
  // Comments blanked, strings kept: a declaration or an id in a comment is no journey and protects nothing.
  const declared = [...swiftCode(files.get(file), { strings: false }).matchAll(JOURNEY)].find((match) => match[1] === journey);
  if (!declared) return [`${id}: ${file} has no journey ${journey}`];
  if (/\bpicturesOnly:\s*true\b/.test(declared[3])) {
    return [`${id}: journey ${journey} in ${file} is walked only for the pictures (picturesOnly), not in the asserting walk`];
  }
  return declared[2].includes(`"${id}"`) ? [] : [`${id}: journey ${journey} in ${file} does not list ${id}`];
}

function exempt(file) {
  return path.posix.basename(file) === 'README.md' || file.split('/').some((part) => part.toLowerCase() === 'fixtures');
}

/** The files under the protected folders and the walk, by repository path; build output skipped. */
function protectedFiles() {
  const files = new Map();
  const walk = (folder) => {
    if (!existsSync(path.join(root, folder))) return;
    for (const entry of readdirSync(path.join(root, folder), { withFileTypes: true })) {
      const at = `${folder}${entry.name}`;
      if (entry.isDirectory()) {
        if (!['bin', 'obj'].includes(entry.name)) walk(`${at}/`);
      } else {
        files.set(at, readFileSync(path.join(root, at), 'utf8'));
      }
    }
  };
  [...PROTECTED, WALK].forEach(walk);
  return files;
}

test('every entry of the UX baseline has an id, a status and a protection that holds', () => {
  assert.deepEqual(problems(readFileSync(path.join(root, DOCUMENT), 'utf8'), protectedFiles()), []);
});

const table = (...rows) => ['## Entries', '', '| Id | Behaviour | Approved | Status | Protected by |', '| --- | --- | --- | --- | --- |', ...rows, ''].join('\n');
const row = (id, status, protectedBy) => `| ${id} | PDF \\| Notes keeps its order | v0.6.1 spec item 2 | ${status} | ${protectedBy} |`;

test('the guard accepts tests that name their entry, device lines and pending steps', () => {
  const files = new Map([
    ['tests/contracts/page-scenarios/sel-grid.json', '{ "entry": "SEL-01" }'],
    ['tests/contracts/page-scenarios/README.md', 'How the scenarios run'],
    ['tests/contracts/page-scenarios/fixtures/piece.musicxml', '<score/>'],
  ]);
  const markdown = table(
    row('SEL-01', 'approved', '`tests/contracts/page-scenarios/sel-grid.json` (T3 page); device: drag with the Pencil'),
    row('PLAY-02', 'provisional', 'pending: B4'),
    row('SCAN-01', 'known defect', 'pending: baseline 0.7'),
    '| ~~SEL-02~~ | replaced | 2026-11-16 | by SEL-01 | — |',
  );
  assert.deepEqual(problems(markdown, files), []);
});

test('the guard refuses a bad id, status or protection, and a test that names no entry or an unknown one', () => {
  const files = new Map([
    ['tests/ipad/Baseline/SelectionTests.swift', '// SEL-01 SEL-09'],
    ['tests/ipad/Baseline/PlaybackTests.swift', '// plays'],
    ['tests/backend/Unit/Loop.cs', 'PLAY-01'],
  ]);
  const found = problems(table(
    row('SEL-01', 'approved', '`tests/ipad/Baseline/PlaybackTests.swift`'),
    row('SEL-1', 'approved', 'pending: A5'),
    row('PLAY-01', 'done', '`tests/backend/Unit/Loop.cs`'),
    row('VIEW-01', 'approved', 'pending: later; device:'),
    row('VIEW-01', 'approved', '`tests/ipad/Baseline/Missing.swift`'),
    row('ROLL-01', 'approved', ''),
  ), files).join('\n');
  for (const expected of [
    'SEL-01: tests/ipad/Baseline/PlaybackTests.swift does not name SEL-01', 'bad id "SEL-1"', 'PLAY-01: status "done"',
    'PLAY-01: tests/backend/Unit/Loop.cs is not under', 'VIEW-01: "pending: later" names no step', 'VIEW-01: "device:" without',
    'VIEW-01: id used twice', 'tests/ipad/Baseline/Missing.swift does not exist', 'ROLL-01: nothing in «Protected by»',
    'tests/ipad/Baseline/SelectionTests.swift names SEL-09', 'tests/ipad/Baseline/PlaybackTests.swift names no entry',
  ]) assert.ok(found.includes(expected), `expected "${expected}" in:\n${found}`);
});

const WALK_FILE = 'tests/ipad/MotiffLabScreens/ScreensUITests.swift';
const walkFiles = (...more) => new Map([[WALK_FILE, [
  '        Journey(name: "open-and-play", protects: [], start: .fresh, end: .example, run: { try $0.openExampleAndPlay() }),',
  '        Journey(name: "pdf-and-notes", protects: ["VIEW-01", "VIEW-04"], start: .notesAndPdf, run: { try $0.pdfAndNotes() }),',
  ...more,
].join('\n')]]);

test('the guard accepts a journey of the walk that lists its entry, and a pending step with the part it leaves', () => {
  const markdown = table(
    row('VIEW-01', 'approved', `\`${WALK_FILE}\` journey \`pdf-and-notes\` (PDF to Notes); pending: A4 (Notes back to the PDF)`),
    row('VIEW-04', 'approved', `\`${WALK_FILE}\` journey \`pdf-and-notes\``),
  );
  assert.deepEqual(problems(markdown, walkFiles()), []);
});

test('the guard refuses a journey or a walk file that does not exist, a journey that does not list the entry, and a walk path without its journey', () => {
  const found = problems(table(
    row('VIEW-01', 'approved', `\`${WALK_FILE}\` journey \`notes-and-pdf\``),
    row('VIEW-02', 'approved', '`tests/ipad/MotiffLabScreens/Missing.swift` journey `pdf-and-notes`'),
    row('VIEW-03', 'approved', `\`${WALK_FILE}\` journey \`open-and-play\``),
    row('VIEW-04', 'approved', `\`${WALK_FILE}\``),
    row('PLAY-01', 'approved', '`tests/ipad/Baseline/PlaybackTests.swift` journey `pdf-and-notes`'),
  ), walkFiles()).join('\n');
  for (const expected of [
    `VIEW-01: ${WALK_FILE} has no journey notes-and-pdf`, 'VIEW-02: tests/ipad/MotiffLabScreens/Missing.swift does not exist',
    `VIEW-03: journey open-and-play in ${WALK_FILE} does not list VIEW-03`, `VIEW-04: ${WALK_FILE} is the walk: name its journey`,
    'PLAY-01: tests/ipad/Baseline/PlaybackTests.swift is no walk file: a journey is named only in tests/ipad/MotiffLabScreens/',
  ]) assert.ok(found.includes(expected), `expected "${expected}" in:\n${found}`);
});

test('the guard refuses a journey that is commented out or walked only for the pictures, and an id in a comment', () => {
  const found = problems(table(
    row('SEL-01', 'approved', `\`${WALK_FILE}\` journey \`select-by-drag\``),
    row('SEL-02', 'approved', `\`${WALK_FILE}\` journey \`select-loop-change\``),
    row('SEL-03', 'approved', `\`${WALK_FILE}\` journey \`other-places\``),
    row('SEL-04', 'approved', `\`${WALK_FILE}\` journey \`roll-and-expand\``),
    row('VIEW-03', 'approved', `\`${WALK_FILE}\` journey \`a-//-b\``),
  ), walkFiles(
    '        // Journey(name: "select-by-drag", protects: ["SEL-01"], start: .example, run: { try $0.selectByDragAndOver() }),',
    '        /* Journey(name: "select-loop-change", protects: ["SEL-02"], start: .example,',
    '                   run: { try $0.selectLoopAndChange() }), */',
    '        Journey(name: "other-places", protects: ["SEL-03"], start: .example, end: .library, picturesOnly: true,',
    '                run: { try $0.otherPlaces() }),',
    '        Journey(name: "roll-and-expand", protects: [/* "SEL-04" */], start: .example, run: { try $0.rollAndExpand() }),',
    '        Journey(name: "a-//-b", protects: ["VIEW-03"], start: .example, run: { try $0.insightsAndTry() }),',
  )).join('\n');
  for (const expected of [
    `SEL-01: ${WALK_FILE} has no journey select-by-drag`, `SEL-02: ${WALK_FILE} has no journey select-loop-change`,
    `SEL-03: journey other-places in ${WALK_FILE} is walked only for the pictures (picturesOnly), not in the asserting walk`,
    `SEL-04: journey roll-and-expand in ${WALK_FILE} does not list SEL-04`,
  ]) assert.ok(found.includes(expected), `expected "${expected}" in:\n${found}`);
  assert.ok(!found.includes('VIEW-03'), `a comment sign in a string starts no comment:\n${found}`);
});
