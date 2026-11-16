// Tests of the dependency check (CLAUDE.md, "Writing code"): node --test "tools/quality/*.test.mjs"
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { bundledFolders, coreViolations, ipadPageViolations, runtimeImports } from './dependencies.mjs';

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
