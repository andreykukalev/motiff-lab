// Tests of the code-quality ratchet (#332): node --test "tools/quality/*.test.mjs"
import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  addedEntries,
  compare,
  formatBaseline,
  parseDotnetLog,
  swiftSymbol,
  updateBaseline,
  valueIn,
} from './ratchet.mjs';

const v = (over) => ({ tool: 'eslint', rule: 'max-lines-per-function', file: 'a.ts', symbol: 'f', line: 10, value: 90, ...over });
const e = (over) => ({ tool: 'eslint', rule: 'max-lines-per-function', file: 'a.ts', symbol: 'f', value: 90, why: 'old', ...over });

test('a violation in the baseline is not new, wherever its line moved', () => {
  const r = compare([v({ line: 200 })], [e()], ['eslint']);
  assert.deepEqual([r.added, r.stale, r.grew, r.unjustified], [[], [], [], []]);
});

test('a violation not in the baseline is new', () => {
  const r = compare([v(), v({ symbol: 'g' })], [e()], ['eslint']);
  assert.deepEqual(r.added.map((x) => x.symbol), ['g']);
});

test('a key is counted: a second violation with the same key is new', () => {
  const r = compare([v({ value: 95 }), v({ value: 85 })], [e({ value: 95 })], ['eslint']);
  assert.equal(r.added.length, 1);
  assert.equal(r.added[0].value, 85);
});

test('fixed code leaves a stale entry to remove', () => {
  const r = compare([], [e()], ['eslint']);
  assert.equal(r.stale.length, 1);
});

test('entries of a tool that did not run are neither matched nor stale', () => {
  const r = compare([], [e({ tool: 'swiftlint' })], ['eslint']);
  assert.deepEqual(r.stale, []);
});

test('an entry without a reason is unjustified', () => {
  const r = compare([v()], [e({ why: '  ' })], ['eslint']);
  assert.equal(r.unjustified.length, 1);
});

test('a value that got worse is reported, lower being worse for maintainability', () => {
  assert.equal(compare([v({ value: 120 })], [e()], ['eslint']).grew.length, 1);
  assert.equal(compare([v({ value: 85 })], [e()], ['eslint']).grew.length, 0);
  const mi = { tool: 'dotnet', rule: 'CA1505' };
  assert.equal(compare([v({ ...mi, value: 5 })], [e({ ...mi, value: 8 })], ['dotnet']).grew.length, 1);
  assert.equal(compare([v({ ...mi, value: 9 })], [e({ ...mi, value: 8 })], ['dotnet']).grew.length, 0);
});

test('updating drops stale entries, adds new ones without a reason and keeps the rest', () => {
  const baseline = [e(), e({ symbol: 'gone' }), e({ tool: 'swiftlint', symbol: 'other' })];
  const next = updateBaseline(baseline, [v(), v({ symbol: 'h' })], ['eslint']);
  assert.deepEqual(next.map((x) => `${x.tool}:${x.symbol}:${x.why}`), ['eslint:f:old', 'eslint:h:', 'swiftlint:other:old']);
  assert.equal(compare([v(), v({ symbol: 'h' })], next, ['eslint']).unjustified.length, 1);
});

test('the entries a pull request adds to the baseline', () => {
  assert.deepEqual(addedEntries([e(), e(), e({ symbol: 'g' })], [e()]).map((x) => x.symbol), ['f', 'g']);
});

test('the baseline file has one entry per line and reads back', () => {
  const text = formatBaseline([e({ symbol: 'z' }), e()], 'note');
  assert.equal(text.split('\n').filter((l) => l.includes('"tool"')).length, 2);
  assert.deepEqual(JSON.parse(text).entries.map((x) => x.symbol), ['f', 'z']);
});

test('CA warnings are read from an MSBuild log once each, with their symbol and value', () => {
  const line = "  2:10>/repo/src/A.cs(31,45): warning CA1502: 'Sections' has a cyclomatic complexity of '16'. "
    + "Rewrite or refactor the code to decrease its complexity below '16'. "
    + '(https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1502) [/repo/src/A.csproj]';
  const other = "C:\\repo\\src\\B.cs(3,1): warning CA1506: 'B' is coupled with '100' different types from '34' "
    + "different namespaces. Rewrite or refactor the code to decrease its class coupling below '96'. [C:\\repo\\B.csproj]";
  const found = parseDotnetLog([line, line, other, 'noise'].join('\n'), (p) => p.replace(/^.*[\\/]src[\\/]/, 'src/'));
  assert.deepEqual(found.map((f) => [f.rule, f.file, f.symbol, f.value]),
    [['CA1502', 'src/A.cs', 'Sections', 16], ['CA1506', 'src/B.cs', 'B', 100]]);
});

test('Swift symbols come from the declaration line', () => {
  assert.equal(swiftSymbol('    private func drawRoll(in rect: CGRect) {'), 'drawRoll');
  assert.equal(swiftSymbol('final class ScoreController: NSObject {'), 'class ScoreController');
  assert.equal(swiftSymbol('    init(model: Model) {'), 'init');
  assert.equal(swiftSymbol('    var body: some View {'), 'body');
});

test('values are read from the tools\' messages', () => {
  assert.equal(valueIn("Function 'draw2' has too many lines (186). Maximum allowed is 80."), 186);
  assert.equal(valueIn('Refactor this function to reduce its Cognitive Complexity from 22 to the 15 allowed.'), 22);
  assert.equal(valueIn('File should contain 600 lines or less: currently contains 655'), 655);
  assert.equal(valueIn('Function should have complexity 15 or less; currently complexity is 19'), 19);
});

test('a way to hide a violation is found in every language', async () => {
  const { findSuppression } = await import('./ratchet.mjs');
  const hidden = [
    ['src/MotiffLab.Web/Scripts/a.ts', '/* eslint-disable max-lines-per-function */'],
    ['src/MotiffLab.Web/Scripts/a.ts', '  // eslint-disable-next-line sonarjs/cognitive-complexity'],
    ['src/MotiffLab.Web/Scripts/a.ts', '/* eslint max-lines: off */'],
    ['src/MotiffLab.Web/eslint.config.mjs', '    linterOptions: { noInlineConfig: false },'],
    ['ipad/App/MotiffLab/A.swift', '// swiftlint:disable function_body_length'],
    ['ipad/App/MotiffLab/A.swift', 'func f() { // swiftlint:disable:this cyclomatic_complexity'],
    ['src/A.cs', '#pragma warning disable CA1502'],
    ['src/A.cs', '#pragma warning disable IDE0005, CA1506 // coupling'],
    ['src/A.cs', '#pragma warning disable'],
    ['src/A.cs', '[SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity")]'],
    ['src/A.razor', '    #pragma warning disable CA1505'],
    ['src/A.cs', '// jscpd:ignore-start'],
    ['services/omr/a.py', '# jscpd:ignore-start'],
    ['src/MotiffLab.Web/.editorconfig', 'dotnet_diagnostic.CA1502.severity = none'],
    ['.editorconfig', 'dotnet_analyzer_diagnostic.category-Maintainability.severity = none'],
    ['src/X/X.csproj', '    <NoWarn>$(NoWarn);CA1502</NoWarn>'],
    ['Directory.Build.props', '    <RunAnalyzers>false</RunAnalyzers>'],
  ];
  for (const [file, line] of hidden) {
    assert.ok(findSuppression(file, line), `${file}: ${line}`);
  }
  const fine = [
    ['src/A.cs', '#pragma warning disable CS0618 // obsolete API'],
    ['src/A.cs', '[SuppressMessage("Style", "IDE0060")]'],
    ['.editorconfig', 'dotnet_diagnostic.IDE0005.severity = warning'],
    ['src/X/X.csproj', '    <NoWarn>$(NoWarn);CS1591</NoWarn>'],
    ['src/MotiffLab.Web/Scripts/a.ts', 'const eslint = 1;'],
    ['ipad/App/MotiffLab/A.swift', 'let swiftlint = "lint"'],
    ['Directory.Build.props', '    <WarningsNotAsErrors>$(WarningsNotAsErrors);CA1502;CA1505;CA1506</WarningsNotAsErrors>'],
  ];
  for (const [file, line] of fine) {
    assert.equal(findSuppression(file, line), null, `${file}: ${line}`);
  }
});
