// Tests of the dependency check (CLAUDE.md, "Writing code"): node --test "tools/quality/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';

import {
  CORE_LAYERS, IPAD_FOLDER_USES, SCRIPT_FOLDER_USES, bundledFolders, coreNamespaceViolations, coreViolations, csharpCode,
  ipadFolderViolations, ipadPageViolations, moduleImports, runtimeImports, scriptFolderViolations, swiftCode,
} from './dependencies.mjs';
import { compare, updateBaseline } from './ratchet.mjs';

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

const core = (files) => Object.fromEntries(Object.entries(files).map(([path, text]) => [`src/backend/MotiffLab.MusicCore/${path}`, text]));
const namespaceEdges = (files) => coreNamespaceViolations(Object.keys(files), reader(files)).map((v) => `${v.file.slice(32)}:${v.line} ${v.symbol}`);
const namespaces = {
  'Time/Tick.cs': 'namespace MotiffLab.MusicCore.Time;\npublic readonly record struct Tick(long Value);\n',
  'Pieces/ImportedPiece.cs': 'using MotiffLab.MusicCore.Time;\n\nnamespace MotiffLab.MusicCore.Pieces;\npublic sealed record ImportedPiece(Tick End);\n',
  'Import/Scan/ScanCheck.cs': 'namespace MotiffLab.MusicCore.Import.Scan;\npublic sealed class ScanCheck {}\n',
  'Projects/Project.cs': 'namespace MotiffLab.MusicCore.Projects;\npublic sealed partial record Project { public PieceAssembly Assembly { get; } }\n',
};

test('a music-core namespace uses the layers below its own, by a using line or a name written qualified', () => {
  const files = core({
    ...namespaces,
    'Pieces/Bookmark.cs': 'using MotiffLab.MusicCore.Projects;\n\nnamespace MotiffLab.MusicCore.Pieces;\n'
      + '/// <summary>Kept in <see cref="Projects.Project"/>, read by <c>Import.Scan.ScanCheck</c>.</summary>\n'
      + 'public sealed record Bookmark(Tick At) { const string S = "Import.Scan.ScanCheck"; }\n',
    'Time/Meter.cs': 'namespace MotiffLab.MusicCore.Time;\n// Pieces.ImportedPiece reads it.\n'
      + 'public sealed class Meter { Pieces.ImportedPiece? piece; MusicCore.Import.Scan.ScanCheck? check; }\n',
    'Projects/Open.cs': 'using MotiffLab.MusicCore.Import.Scan;\n\nnamespace MotiffLab.MusicCore.Projects;\n'
      + 'partial record Project { string A => Assembly.Steps; Pieces.ImportedPiece? P; string B => typeof(Project).Assembly.FullName; }\n',
  });
  assert.deepEqual(namespaceEdges(files), ['Pieces/Bookmark.cs:1 Pieces: Projects', 'Time/Meter.cs:3 Time: Pieces', 'Time/Meter.cs:3 Time: Import']);
});

test("a cref's name is code, other comment text and string literals are not", () => {
  const text = '/// <see cref="A.B"/> and C.D\n/* E.F */ var s = @"x ""G.H"" y" + $"{I.J}" + """\nK.L\n""" + \'"\' + "M.N";\n';
  const code = csharpCode(text);
  assert.deepEqual([...code.matchAll(/[A-Z]\.[A-Z]/g)].map((m) => m[0]), ['A.B']);
  assert.equal(code.length, text.length);
  assert.equal(code.split('\n').length, text.split('\n').length);
});

test('a file in a music-core namespace the layers do not list is reported', () => {
  const files = core({ 'Practice/Drill.cs': 'namespace MotiffLab.MusicCore.Practice;\npublic sealed class Drill {}\n' });
  assert.deepEqual(rules(coreNamespaceViolations(Object.keys(files), reader(files))), ['core-namespace-unlisted Practice']);
});

test("governance/quality/README.md shows music-core's layers as CORE_LAYERS has them", () => {
  const readme = readFileSync(new URL('../../governance/quality/README.md', import.meta.url), 'utf8');
  const rows = [...readme.matchAll(/^\| (\d+) \| (.+?) \|$/gm)].map((m) => [Number(m[1]), m[2]]);
  assert.deepEqual(rows, CORE_LAYERS.map((layer, index) => [index + 1, layer.map((name) => `\`${name}\``).join(', ')]));
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

test('a dynamic import by a template literal without an interpolation loads; one with an interpolation is not named', () => {
  const text = 'const a = await import(`./a.js`);\nconst b = await import( `../roll/b.js` );\nconst c = await import(`./${name}.js`);\n';
  assert.deepEqual(runtimeImports(text).map((i) => `${i.spec}:${i.line}`), ['./a.js:1', '../roll/b.js:2']);
});

test('an import after another statement on the same line counts', () => {
  const text = 'const n = 1; import { a } from "./a.js";\nexport const m = 2; import "./b.js";\nlet t; import type { T } from "./t.js";\n';
  assert.deepEqual(runtimeImports(text).map((i) => `${i.spec}:${i.line}`).sort(), ['./a.js:1', './b.js:2']);
  assert.deepEqual(moduleImports(text).map((i) => `${i.spec}:${i.line}`).sort(), ['./a.js:1', './b.js:2', './t.js:3']);
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

test('a module names its runtime imports and its type-only ones', () => {
  const text = 'import type { A } from "./a.js";\nimport { type B, b } from "../b/b.js";\nexport type { E } from "./e.js";\n';
  assert.deepEqual(moduleImports(text).map((i) => `${i.spec}:${i.line}`).sort(), ['../b/b.js:2', './a.js:1', './e.js:3']);
});

const scripts = (files) => Object.fromEntries(Object.entries(files).map(([path, text]) => [`src/shared/Scripts/${path}`, text]));

test('a folder of the shared scripts imports only the folders the table allows, type-only imports included', () => {
  const files = scripts({
    'playback/hold.ts': 'import { HOLD_MS } from "../selection/gesture.js";\nimport type { Bar } from "../score/geometry.js";\n',
    'score/ipad-page/page.ts': 'import { a } from "../a.js";\nimport { b } from "../../playback/hold.js";\nimport { r } from "../../roll/r.js";\n',
    'selection/gesture.ts': 'import { c } from "./c.js";\n',
    'import/scan.ts': 'export const s = 1;\n',
  });
  assert.deepEqual(scriptFolderViolations(Object.keys(files), reader(files)).map((v) => `${v.rule} ${v.file.slice(19)}:${v.line} ${v.symbol}`), [
    'script-folder playback/hold.ts:2 playback: score',
    'script-folder score/ipad-page/page.ts:3 score: roll',
    'script-folder-unlisted import/scan.ts:1 import',
  ]);
});

test('no two folders of the shared scripts may import each other, directly or round a loop', () => {
  const reaches = (from, seen = new Set()) => {
    for (const to of SCRIPT_FOLDER_USES[from]) {
      if (!seen.has(to)) {
        seen.add(to);
        reaches(to, seen);
      }
    }
    return seen;
  };
  for (const folder of Object.keys(SCRIPT_FOLDER_USES)) {
    assert.ok(SCRIPT_FOLDER_USES[folder].every((to) => to in SCRIPT_FOLDER_USES), `${folder} names a folder the table lacks`);
    assert.ok(!reaches(folder).has(folder), `${folder} reaches itself`);
  }
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
  assert.equal(swiftCode('a // b "c"\nd "e // f" /* g */ h', { strings: false }), 'a         \nd "e // f"         h');
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

test("a baseline merged from two branches keeps both sides' reasons when it is updated", () => {
  const files = app({ ...declarations, 'Ink/Pen.swift': 'let a: MarkStore\n', 'Roll/Drawer.swift': 'let b: MarkStore\n' });
  const current = ipadFolderViolations(Object.keys(files), reader(files));
  const ours = { ...current[0], why: 'ours (#1)' };
  const theirs = { ...current[1], why: 'theirs (#2)' };
  const gone = { ...current[0], file: 'src/ipad/App/MotiffLab/Ink/Old.swift', why: 'gone (#3)' };
  const next = updateBaseline([ours, theirs, gone], current, ['dependencies']);
  assert.deepEqual(next.map((e) => `${e.file.slice(23)} ${e.why}`), ['Ink/Pen.swift ours (#1)', 'Roll/Drawer.swift theirs (#2)']);
});
