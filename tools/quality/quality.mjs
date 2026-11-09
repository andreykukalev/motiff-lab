#!/usr/bin/env node
// Code-quality checks (#332): complexity, size and duplication in C#, TypeScript, Swift and Python, against the
// ratchet baseline. The rules are in governance/quality/README.md; thresholds and the soft/blocking switch in
// governance/quality/settings.json.
//
//   node tools/quality/quality.mjs                    all checks (CA rules need a quality build: about a minute)
//   node tools/quality/quality.mjs --only eslint,jscpd
//   node tools/quality/quality.mjs --update-baseline  drop fixed entries, add new ones with an empty "why"
//   node tools/quality/quality.mjs --write-config     rewrite CodeMetricsConfig.txt and ipad/.swiftlint.yml
//
// CI (.github/workflows/ci.yml) passes --ci, the log of its own build (--dotnet-log) and the pull request's base
// (--base), and the result goes to the job summary. Checks: config, dotnet, csharp, eslint, swiftlint, jscpd.
// In soft mode the command reports and exits 0; in blocking mode it exits 1 on a violation not in the baseline,
// a baseline entry no longer needed, an entry without a reason, or a check that could not run.
import { spawnSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, isAbsolute, join, relative as relativePath } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  addedEntries,
  compare,
  formatBaseline,
  parseDotnetLog,
  swiftSymbol,
  updateBaseline,
  valueIn,
} from './ratchet.mjs';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const WEB = join(ROOT, 'src', 'MotiffLab.Web');
const SETTINGS_PATH = join(ROOT, 'governance', 'quality', 'settings.json');
const BASELINE_PATH = join(ROOT, 'tools', 'quality', 'baseline.json');
const OUT = join(ROOT, 'artifacts', 'quality');
const SWIFTLINT_IMAGE =
  'ghcr.io/realm/swiftlint:0.62.2@sha256:2bf40eee4ea663c431c38950c7aa9bddf40407aad2c700eb00fc71830969606e';
const BASELINE_NOTE =
  'Known violations of the code-quality thresholds (governance/quality/README.md). Only shrinks: fixing code '
  + 'removes its entry; a new entry needs a reason in "why" and an explanation in the pull request.';
const CHECKS = ['config', 'dotnet', 'csharp', 'eslint', 'swiftlint', 'jscpd'];

const settings = JSON.parse(readFileSync(SETTINGS_PATH, 'utf8'));
const limits = settings.thresholds;

// ---- Arguments ----------------------------------------------------------------------------------------------

function parseArgs(argv) {
  const args = { only: CHECKS, ci: false, update: false, writeConfig: false, why: '', dotnetLog: '', base: '' };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => argv[++i] ?? '';
    if (a === '--only') {
      args.only = next().split(',').map((s) => s.trim()).filter(Boolean);
    } else if (a === '--ci') {
      args.ci = true;
    } else if (a === '--update-baseline') {
      args.update = true;
    } else if (a === '--write-config') {
      args.writeConfig = true;
    } else if (a === '--why') {
      args.why = next();
    } else if (a === '--dotnet-log') {
      args.dotnetLog = next();
    } else if (a === '--base') {
      args.base = next();
    } else {
      throw new UsageError(`Unknown argument ${a}; see the top of tools/quality/quality.mjs.`);
    }
  }
  const unknown = args.only.filter((c) => !CHECKS.includes(c));
  if (unknown.length) {
    throw new UsageError(`Unknown check ${unknown.join(', ')}; the checks are ${CHECKS.join(', ')}.`);
  }
  return args;
}

// ---- Helpers ------------------------------------------------------------------------------------------------

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    encoding: 'utf8',
    maxBuffer: 256 * 1024 * 1024,
    shell: false,
    ...options,
  });
  if (result.error) {
    throw new Error(`${command} could not start: ${result.error.message}`);
  }
  return result;
}

function toRepoPath(path) {
  const absolute = isAbsolute(path) ? path : join(ROOT, path);
  return relativePath(ROOT, absolute).split('\\').join('/');
}

function gitFiles(...patterns) {
  const result = run('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', ...patterns]);
  if (result.status !== 0) {
    throw new Error(`git ls-files failed: ${result.stderr}`);
  }
  return result.stdout.split('\0').filter(Boolean);
}

function lineCount(text) {
  if (text.length === 0) {
    return 0;
  }
  const lines = text.split(/\r?\n/);
  return lines[lines.length - 1] === '' ? lines.length - 1 : lines.length;
}

function readLines(file) {
  return readFileSync(join(ROOT, file), 'utf8').split(/\r?\n/);
}

class Skipped extends Error {}

class UsageError extends Error {}

// ---- Generated configuration --------------------------------------------------------------------------------

function codeMetricsConfig() {
  return [
    `CA1502: ${limits.functionComplexity}`,
    `CA1505: ${settings.dotnet.maintainabilityIndex}`,
    `CA1506(Type): ${settings.dotnet.typeCoupling}`,
    `CA1506(Method): ${settings.dotnet.memberCoupling}`,
    '',
  ].join('\n');
}

function swiftlintConfig() {
  const never = 1000000;
  return `# Code-quality checks for the iPad app (#332): only size and complexity rules, no style rules.
# Written by \`node tools/quality/quality.mjs --write-config\` from governance/quality/settings.json, the one
# place the thresholds are kept; the check "config" fails when this file differs. Every rule only warns: the
# ratchet in tools/quality decides what fails (governance/quality/README.md). CI runs SwiftLint on Linux, in Docker.
only_rules:
  - cyclomatic_complexity
  - function_body_length
  - file_length
  - type_body_length
  - nesting
included:
  - App
excluded:
  - App/Web
cyclomatic_complexity:
  warning: ${limits.functionComplexity}
  error: ${never}
function_body_length:
  warning: ${limits.functionLines}
  error: ${never}
file_length:
  warning: ${limits.fileLines}
  error: ${never}
type_body_length:
  warning: ${limits.fileLines}
  error: ${never}
nesting:
  type_level:
    warning: ${limits.nesting}
    error: ${never}
  function_level:
    warning: ${limits.nesting}
    error: ${never}
`;
}

const GENERATED = [
  { path: 'CodeMetricsConfig.txt', content: codeMetricsConfig },
  { path: 'ipad/.swiftlint.yml', content: swiftlintConfig },
];

function checkConfig(write) {
  const problems = [];
  for (const { path, content } of GENERATED) {
    const file = join(ROOT, path);
    const want = content();
    const have = existsSync(file) ? readFileSync(file, 'utf8').replace(/\r\n/g, '\n') : '';
    if (have !== want) {
      if (write) {
        writeFileSync(file, want);
      } else {
        problems.push(`${path} differs from governance/quality/settings.json: run node tools/quality/quality.mjs --write-config`);
      }
    }
  }
  return problems;
}

// ---- Checks -------------------------------------------------------------------------------------------------

/** CA1502, CA1505 and CA1506 from a quality build's log: CI's own build, or a build this command starts. */
function dotnetCheck(args) {
  let log = args.dotnetLog;
  if (!log) {
    mkdirSync(OUT, { recursive: true });
    log = join(OUT, 'dotnet-build.log');
    const result = run('dotnet', [
      'build', 'MotiffLab.slnx', '--no-incremental', '-p:QualityMetrics=true', `-flp1:logfile=${log};warningsonly`,
    ], { stdio: ['ignore', 'pipe', 'pipe'] });
    if (result.status !== 0) {
      throw new Error(`dotnet build failed:\n${(result.stdout + result.stderr).slice(-4000)}`);
    }
  }
  return parseDotnetLog(readFileSync(log, 'utf8'), toRepoPath);
}

/** Method length (Roslyn syntax, tools/quality/csharp-length.cs) and file length of C# and Razor files. */
function csharpCheck() {
  const out = [];
  for (const file of gitFiles('*.cs', '*.razor')) {
    const lines = lineCount(readFileSync(join(ROOT, file), 'utf8'));
    if (lines > limits.fileLines) {
      out.push({ tool: 'csharp', rule: 'file-length', file, symbol: '', line: 1, value: lines, limit: limits.fileLines,
        message: `File has ${lines} lines; at most ${limits.fileLines}.` });
    }
  }
  const sources = gitFiles('*.cs');
  const result = run('dotnet', ['run', 'tools/quality/csharp-length.cs', '--', String(limits.functionLines)], {
    input: sources.join('\n') + '\n',
  });
  if (result.status !== 0) {
    throw new Error(`csharp-length.cs failed:\n${(result.stdout + result.stderr).slice(-4000)}`);
  }
  for (const line of result.stdout.split(/\r?\n/).filter((l) => l.startsWith('{'))) {
    const m = JSON.parse(line);
    out.push({ tool: 'csharp', rule: 'method-length', file: toRepoPath(m.file), symbol: m.symbol, line: m.line,
      value: m.lines, limit: limits.functionLines, message: `'${m.symbol}' has ${m.lines} lines; at most ${limits.functionLines}.` });
  }
  return out;
}

function ensureNodeTools(ci) {
  if (existsSync(join(WEB, 'node_modules', 'eslint')) && existsSync(join(WEB, 'node_modules', 'jscpd'))) {
    return;
  }
  if (ci) {
    throw new Error('The quality tools are not installed: npm ci --prefix src/MotiffLab.Web');
  }
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  const result = run(npm, ['ci', '--no-audit', '--no-fund'], { cwd: WEB, shell: process.platform === 'win32' });
  if (result.status !== 0) {
    throw new Error(`npm ci in src/MotiffLab.Web failed:\n${result.stderr}`);
  }
}

/** The names of the functions around a position, outermost first, from the TypeScript syntax tree. */
function tsSymbolFinder() {
  const ts = createRequire(join(WEB, 'package.json'))('typescript');
  const trees = new Map();
  const nameOf = (node, sf) => {
    const named = node.name && (ts.isIdentifier(node.name) || ts.isPrivateIdentifier(node.name)
      || ts.isStringLiteral(node.name)) ? node.name.text : '';
    if (ts.isConstructorDeclaration(node)) {
      return 'constructor';
    }
    if (named) {
      return named;
    }
    const parent = node.parent;
    if (parent && (ts.isVariableDeclaration(parent) || ts.isPropertyAssignment(parent)
      || ts.isPropertyDeclaration(parent)) && parent.name) {
      return parent.name.getText(sf);
    }
    return '(anonymous)';
  };
  return (file, line, column) => {
    if (!trees.has(file)) {
      const text = readFileSync(join(ROOT, file), 'utf8');
      trees.set(file, ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true));
    }
    const sf = trees.get(file);
    const position = sf.getPositionOfLineAndCharacter(Math.max(0, line - 1), Math.max(0, column - 1));
    const names = [];
    const visit = (node) => {
      if (node.getStart(sf) > position || node.end <= position) {
        return;
      }
      if (ts.isFunctionLike(node) && node.body !== undefined) {
        const owner = node.parent && ts.isClassLike(node.parent) && node.parent.name ? `${node.parent.name.text}.` : '';
        names.push(owner + nameOf(node, sf));
      }
      ts.forEachChild(node, visit);
    };
    visit(sf);
    return names.join('/');
  };
}

function eslintCheck(args) {
  ensureNodeTools(args.ci);
  const result = run(process.execPath, [join(WEB, 'node_modules', 'eslint', 'bin', 'eslint.js'), '--format', 'json',
    'Scripts'], { cwd: WEB });
  if (result.status !== 0 && result.status !== 1) {
    throw new Error(`eslint failed:\n${result.stderr || result.stdout}`);
  }
  const symbolAt = tsSymbolFinder();
  const out = [];
  for (const report of JSON.parse(result.stdout)) {
    const file = toRepoPath(report.filePath);
    for (const m of report.messages) {
      if (!m.ruleId) {
        throw new Error(`eslint could not check ${file}: ${m.message}`);
      }
      const symbol = m.ruleId === 'max-lines' ? '' : symbolAt(file, m.line, m.column);
      out.push({ tool: 'eslint', rule: m.ruleId, file, symbol, line: m.line, value: valueIn(m.message), message: m.message });
    }
  }
  return out;
}

function swiftlintCheck(args) {
  const docker = run('docker', ['version', '--format', '{{.Server.Version}}']);
  if (docker.status !== 0) {
    if (args.ci) {
      throw new Error(`Docker is not available: ${docker.stderr.trim()}`);
    }
    throw new Skipped('Docker is not running here; CI runs SwiftLint.');
  }
  const result = run('docker', ['run', '--rm', '-v', `${join(ROOT, 'ipad')}:/work`, '-w', '/work', SWIFTLINT_IMAGE,
    'swiftlint', 'lint', '--quiet', '--no-cache', '--reporter', 'json', '--config', '.swiftlint.yml']);
  if (!result.stdout.trim().startsWith('[')) {
    throw new Error(`SwiftLint failed (exit ${result.status}):\n${result.stderr.slice(-4000)}`);
  }
  const seen = new Set();
  const out = [];
  for (const m of JSON.parse(result.stdout)) {
    const file = toRepoPath(join(ROOT, 'ipad', relativePath('/work', m.file)));
    const id = `${file}:${m.line}:${m.rule_id}:${m.reason}`;
    if (seen.has(id)) {
      continue; // SwiftLint 0.62 on Linux reports each violation more than once
    }
    seen.add(id);
    const symbol = m.rule_id === 'file_length' ? '' : swiftSymbol(readLines(file)[m.line - 1]);
    out.push({ tool: 'swiftlint', rule: m.rule_id, file, symbol, line: m.line, value: valueIn(m.reason), message: m.reason });
  }
  return out;
}

function jscpdCheck(args) {
  ensureNodeTools(args.ci);
  mkdirSync(OUT, { recursive: true });
  const result = run(process.execPath, [join(WEB, 'node_modules', 'jscpd', 'run-jscpd.js'), '--config', '.jscpd.json',
    '--min-lines', String(limits.duplicateLines), '--reporters', 'json,silent', '--output', OUT, '--no-tips', '.']);
  const report = join(OUT, 'jscpd-report.json');
  if (result.status !== 0 || !existsSync(report)) {
    throw new Error(`jscpd failed (exit ${result.status}):\n${(result.stderr || result.stdout).slice(-4000)}`);
  }
  return JSON.parse(readFileSync(report, 'utf8')).duplicates.map((d) => {
    const [a, b] = [d.firstFile, d.secondFile].sort((x, y) => (x.name < y.name ? -1 : x.name > y.name ? 1 : x.start - y.start));
    const first = toRepoPath(a.name);
    const second = toRepoPath(b.name);
    return { tool: 'jscpd', rule: 'duplicate', file: first, symbol: first === second ? 'same file' : `with ${second}`,
      line: a.start, value: d.lines,
      message: `${d.lines} duplicated lines: ${first}:${a.start}-${a.end} and ${second}:${b.start}-${b.end}` };
  });
}

const RUNNERS = { dotnet: dotnetCheck, csharp: csharpCheck, eslint: eslintCheck, swiftlint: swiftlintCheck, jscpd: jscpdCheck };

// ---- Report -------------------------------------------------------------------------------------------------

function readBaseline() {
  return existsSync(BASELINE_PATH) ? JSON.parse(readFileSync(BASELINE_PATH, 'utf8')).entries : [];
}

function baseBaseline(ref) {
  if (!ref || /^0+$/.test(ref)) {
    return null;
  }
  const result = run('git', ['show', `${ref}:tools/quality/baseline.json`]);
  return result.status === 0 ? JSON.parse(result.stdout).entries : null;
}

const cell = (s) => String(s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');

function where(v) {
  return `${v.file}:${v.line ?? 1}`;
}

function report({ mode, results, outcome, configProblems, prAdded, elapsed }) {
  const { added, stale, grew, unjustified } = outcome;
  const failing = added.length + stale.length + unjustified.length + configProblems.length
    + results.filter((r) => r.error).length;
  const md = [];
  md.push(`### Code quality (${mode} mode): ${failing === 0 ? 'clean against the baseline' : `${failing} to fix`}`);
  md.push('');
  md.push(mode === 'soft'
    ? 'Soft mode: this report does not fail build-test (governance/quality/README.md).'
    : 'Blocking mode: anything below under "to fix" fails build-test.');
  md.push('');
  md.push('| Check | Violations | New | Fixed (entry to remove) | Time |');
  md.push('| --- | --- | --- | --- | --- |');
  for (const r of results) {
    if (r.error || r.skipped) {
      md.push(`| ${r.name} | ${r.error ? `did not run: ${cell(r.error.split('\n')[0])}` : `skipped: ${cell(r.skipped)}`} | | | ${r.seconds}s |`);
      continue;
    }
    const n = (list) => list.filter((v) => v.tool === r.tool).length;
    md.push(`| ${r.name} | ${r.violations.length} | ${n(added)} | ${n(stale)} | ${r.seconds}s |`);
  }
  md.push('');
  const section = (title, rows, header, format) => {
    if (rows.length === 0) {
      return;
    }
    md.push(`#### ${title} (${rows.length})`, '', header, header.replace(/[^|]+/g, ' --- '));
    for (const row of rows.slice(0, 100)) {
      md.push(format(row));
    }
    if (rows.length > 100) {
      md.push(`| … ${rows.length - 100} more | | |`);
    }
    md.push('');
  };
  section('To fix: new violations, not in the baseline', added, '| Where | Rule | Symbol | Found |',
    (v) => `| ${cell(where(v))} | ${v.tool} ${v.rule} | ${cell(v.symbol)} | ${cell(v.message)} |`);
  section('To fix: baseline entries no longer needed (run --update-baseline)', stale, '| File | Rule | Symbol |',
    (e) => `| ${cell(e.file)} | ${e.tool} ${e.rule} | ${cell(e.symbol)} |`);
  section('To fix: baseline entries without a reason in "why"', unjustified, '| File | Rule | Symbol |',
    (e) => `| ${cell(e.file)} | ${e.tool} ${e.rule} | ${cell(e.symbol)} |`);
  if (configProblems.length) {
    md.push(`#### To fix: configuration (${configProblems.length})`, '', ...configProblems.map((p) => `- ${p}`), '');
  }
  for (const r of results.filter((x) => x.error)) {
    md.push(`#### ${r.name} did not run`, '', '```', r.error.slice(0, 3000), '```', '');
  }
  if (prAdded) {
    section('Baseline entries this pull request adds (the Reviewer checks each reason)', prAdded,
      '| File | Rule | Symbol | Why |', (e) => `| ${cell(e.file)} | ${e.tool} ${e.rule} | ${cell(e.symbol)} | ${cell(e.why)} |`);
  }
  section('Got worse but still in the baseline (does not fail)', grew, '| Where | Rule | Symbol | Was | Now |',
    ({ violation: v, entry: e }) => `| ${cell(where(v))} | ${v.tool} ${v.rule} | ${cell(v.symbol)} | ${e.value} | ${v.value} |`);
  md.push(`Total ${elapsed}s.`);
  return { markdown: md.join('\n') + '\n', failing };
}

// ---- Main ---------------------------------------------------------------------------------------------------

function main() {
  const args = parseArgs(process.argv.slice(2));
  const started = Date.now();
  const configProblems = args.only.includes('config') || args.writeConfig ? checkConfig(args.writeConfig) : [];
  const results = [];
  for (const name of args.only.filter((c) => c !== 'config')) {
    const t0 = Date.now();
    const result = { name, tool: name, violations: [] };
    try {
      result.violations = RUNNERS[name](args);
    } catch (error) {
      if (error instanceof Skipped) {
        result.skipped = error.message;
      } else {
        result.error = error.message;
      }
    }
    result.seconds = ((Date.now() - t0) / 1000).toFixed(1);
    results.push(result);
    console.error(`quality: ${name} ${result.error ? 'failed' : result.skipped ? 'skipped' : `${result.violations.length} violations`} in ${result.seconds}s`);
  }
  const ran = results.filter((r) => !r.error && !r.skipped).map((r) => r.tool);
  const current = results.flatMap((r) => r.violations);
  let baseline = readBaseline();
  if (args.update) {
    baseline = updateBaseline(baseline, current, ran, args.why);
    writeFileSync(BASELINE_PATH, formatBaseline(baseline, BASELINE_NOTE));
    console.error(`quality: baseline written, ${baseline.length} entries (${BASELINE_PATH})`);
  }
  const outcome = compare(current, baseline, ran);
  const base = baseBaseline(args.base);
  const prAdded = base ? addedEntries(baseline, base) : null;
  const mode = settings.mode === 'blocking' ? 'blocking' : 'soft';
  const elapsed = ((Date.now() - started) / 1000).toFixed(1);
  const { markdown, failing } = report({ mode, results, outcome, configProblems, prAdded, elapsed });
  process.stdout.write(markdown);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
  }
  if (args.ci) {
    const level = mode === 'blocking' ? 'error' : 'warning';
    for (const v of outcome.added.slice(0, 50)) {
      console.log(`::${level} file=${v.file},line=${v.line ?? 1},title=quality ${v.tool} ${v.rule}::${cell(v.message)}`);
    }
  }
  process.exitCode = mode === 'blocking' && failing > 0 ? 1 : 0;
}

try {
  main();
} catch (error) {
  console.error(`quality: ${error.message}`);
  process.exitCode = error instanceof UsageError ? 2 : settings.mode === 'blocking' ? 1 : 0;
}
