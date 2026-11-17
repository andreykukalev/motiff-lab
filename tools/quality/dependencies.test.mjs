// Tests of the dependency check (CLAUDE.md, "Writing code"): node --test "tools/quality/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import {
  IPAD_FOLDER_USES, bundledFolders, coreViolations, ipadFolderViolations, ipadPageViolations, runtimeImports, swiftCode,
} from './dependencies.mjs';
import { compare } from './ratchet.mjs';

const reader = (files) => (file) => files[file];
const rules = (list) => list.map((v) => `${v.rule} ${v.symbol}`);

test('music-core may use its own namespaces and plain .NET, not the web, the cloud or AI', () => {
  const files = {
    'src/backend/MotiffLab.MusicCore/A.cs': 'using System.Text.Json;\nusing MotiffLab.MusicCore.Time;\nusing Azure.Storage;\n',
    'src/backend/MotiffLab.MusicCore/B.cs': 'global using static Microsoft.AspNetCore.Http.Results;\n',
  };
  const out = coreViolations(Object.keys(files), reader(files));
  assert.deepEqual(rules(out), ['core-using Azure.Storage', 'core-using Microsoft.AspNetCore.Http.Results']);
  assert.equal(out[0].line, 3);
});

test('music-core references no project and no forbidden package', () => {
  const csproj = '<ItemGroup>\n  <PackageReference Include="PdfPig" Version="1" />\n'
    + '  <PackageReference Include="OpenAI" Version="2" />\n  <ProjectReference Include="..\\Web\\Web.csproj" />\n</ItemGroup>';
  const out = coreViolations(['src/backend/MotiffLab.MusicCore/M.csproj'], reader({ 'src/backend/MotiffLab.MusicCore/M.csproj': csproj }));
  assert.deepEqual(rules(out), ['core-reference ..\\Web\\Web.csproj', 'core-package OpenAI']);
});

test('the bundled folders are the wwwroot/js paths of the iPad project', () => {
  const yml = '      - path: ../../shared/wwwroot/js/score\n        type: folder\n'
    + '      - path: ../../shared/wwwroot/js/layout\n      - path: ../../shared/wwwroot/audio/piano\n';
  assert.deepEqual([...bundledFolders(yml)], ['score', 'layout']);
});

test('type-only imports are erased, value and side-effect imports load', () => {
  const text = 'import type { A } from "./a.js";\nimport { b,\n  c } from "./b.js";\nexport { d } from "./d.js";\n'
    + 'export type { E } from "./e.js";\nimport "./f.js";\nconst g = await import("./g.js");\nimport { x } from "lib";\n';
  assert.deepEqual(runtimeImports(text).map((i) => `${i.spec}:${i.line}`).sort(),
    ['./b.js:3', './d.js:4', './f.js:6', './g.js:7']);
});

test('the iPad page may reach only the bundled folders, through every module it loads', () => {
  const files = {
    'src/ipad/App/Web/score.js': 'import { s } from "../score/s.js";\n',
    'src/shared/Scripts/score/s.ts': 'import type { T } from "../import/t.js";\nimport { p } from "../playback/p.js";\n',
    'src/shared/Scripts/playback/p.ts': 'import { r } from "../import/recording.js";\nimport { q } from "./q.js";\n',
  };
  const exists = (file) => file in files;
  const out = ipadPageViolations(['src/ipad/App/Web/score.js'], reader(files), exists, new Set(['score', 'playback']));
  assert.deepEqual(out.map((v) => `${v.rule} ${v.file} ${v.symbol}`), [
    'ipad-unbundled src/shared/Scripts/playback/p.ts ../import/recording.js',
    'ipad-missing src/shared/Scripts/playback/p.ts ./q.js',
  ]);
});

const app = (files) => Object.fromEntries(Object.entries(files).map(([path, text]) => [`src/ipad/App/MotiffLab/${path}`, text]));
const folderEdges = (files) => ipadFolderViolations(Object.keys(files), reader(files)).map((v) => `${v.file.slice(23)} ${v.symbol}`);
const declarations = {
  'Platform/Core.swift': 'struct PieceCore {}\n',
  'Selection/Place.swift': 'final class PlaceSelection {}\nenum SelectionSpan { case bar }\n',
  'Marks/Marks.swift': '@MainActor final class MarkStore {}\n',
};

test('an iPad folder may use Platform and the folders the table allows; Workspace uses every folder', () => {
  const files = app({
    ...declarations,
    'Playback/Player.swift': 'struct Player { let core: PieceCore; let place: PlaceSelection; var marks: [MarkStore] }\n',
    'Platform/OpenPiece.swift': 'struct OpenPiece {\n  let span = SelectionSpan.bar\n}\n',
    'Workspace/Screen.swift': 'struct Screen { let a: PlaceSelection; let b: MarkStore; let c: Player; let d: PieceCore }\n',
  });
  assert.deepEqual(folderEdges(files), ['Playback/Player.swift Marks: MarkStore', 'Platform/OpenPiece.swift Selection: SelectionSpan']);
  const [, platform] = ipadFolderViolations(Object.keys(files), reader(files));
  assert.equal(platform.line, 2);
  assert.match(platform.message, /Platform\/ may use nothing above it/);
});

test('names in comments and string literals are no use; an interpolation is code', () => {
  const files = app({
    ...declarations,
    'Ink/Pen.swift': [
      '// MarkStore in a comment',
      '/* a block /* nested MarkStore */ still a comment: PlaceSelection */',
      'let a = "MarkStore \\"PlaceSelection\\""',
      'let b = #"MarkStore "PlaceSelection" \\(MarkStore)"#',
      'let c = """',
      '  MarkStore "PlaceSelection"',
      '  """',
      'let d = "seen \\(SelectionSpan.bar) here"',
      '',
    ].join('\n'),
  });
  assert.deepEqual(folderEdges(files), ['Ink/Pen.swift Selection: SelectionSpan']);
  assert.equal(swiftCode('a /* b */ "c\\(d)" e').length, 'a /* b */ "c\\(d)" e'.length);
});

test("a private type, a file's own nested type and a member after a dot are no other folder's type", () => {
  const files = app({
    ...declarations,
    'Marks/Check.swift': 'private struct Hidden {}\nextension MarkStore { enum Kind { case a } }\n',
    'Ink/Pen.swift': 'struct Pen {\n  struct Hidden {}\n  let h: Hidden\n  let k = tool.MarkStore\n  let e: MarkStore.Kind\n}\n',
  });
  assert.deepEqual(folderEdges(files), ['Ink/Pen.swift Marks: MarkStore.Kind']);
});

test('a file in a folder the table does not list is reported', () => {
  const files = app({ 'Practice/Drill.swift': 'struct Drill {}\n' });
  assert.deepEqual(ipadFolderViolations(Object.keys(files), reader(files)).map((v) => `${v.rule} ${v.symbol}`),
    ['ipad-folder-unlisted Practice']);
});

test('a baselined folder edge passes, a new one fails, a gone one is to remove', () => {
  const before = app({ ...declarations, 'Ink/Pen.swift': 'let a: MarkStore\n' });
  const baseline = ipadFolderViolations(Object.keys(before), reader(before)).map((v) => ({ ...v, why: 'known' }));
  assert.deepEqual(compare(baseline, baseline, ['dependencies']).added, []);
  const after = app({ ...declarations, 'Ink/Pen.swift': 'let b: PlaceSelection\n' });
  const { added, stale } = compare(ipadFolderViolations(Object.keys(after), reader(after)), baseline, ['dependencies']);
  assert.deepEqual(added.map((v) => v.symbol), ['Selection: PlaceSelection']);
  assert.deepEqual(stale.map((e) => e.symbol), ['Marks: MarkStore']);
});

test("governance/quality/README.md shows the folders' table as IPAD_FOLDER_USES has it", () => {
  const readme = readFileSync(new URL('../../governance/quality/README.md', import.meta.url), 'utf8');
  const rows = [...readme.matchAll(/^\| `(\w+)\/` \| (.+?) \|$/gm)].map((m) => [m[1], m[2]]);
  const want = Object.entries(IPAD_FOLDER_USES).map(([folder, uses]) =>
    [folder, uses === '*' ? 'every folder' : uses.length ? uses.map((u) => `\`${u}\``).join(', ') : 'nothing']);
  assert.deepEqual(rows, want);
});
