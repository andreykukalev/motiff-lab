# Code-quality checks

Agents write code to a measurable spec: functions and files stay small and simple, and copy-paste is caught (#332).
The checks run in the repository, with no external service: locally with one command, in CI in `build-test` on every
pull request. Changing this folder is a Guardrail task (E3, `governance/risk-paths.yml`), so the thresholds and the
switch below change only when Andrei applies it.

## One command

```bash
node tools/quality/quality.mjs                    # all checks; the CA rules need a quality build (about a minute)
node tools/quality/quality.mjs --only eslint,jscpd,csharp,swiftlint   # quick, no build
node tools/quality/quality.mjs --update-baseline  # after fixing code, or to list a new deviation (then write its why)
node --test "tools/quality/*.test.mjs"            # the ratchet's own tests
```

It installs the tools in `src/MotiffLab.Web/node_modules` on the first run (`npm ci`). SwiftLint needs Docker; without
it the command says it skipped SwiftLint, and CI runs it. The report is Markdown on standard output; in CI it goes to the
job summary, and each new violation is an annotation on its line.

## What is checked

The thresholds are kept in one place, [`settings.json`](settings.json): function complexity 15, function 80 lines, file
600 lines, nesting 4, duplicates from 10 lines. `CodeMetricsConfig.txt` and `ipad/.swiftlint.yml` are written from it
(`--write-config`), and the check `config` fails when they differ; ESLint reads it directly.

| Check | Tool | Rules (threshold) |
| --- | --- | --- |
| `dotnet` | .NET code-metrics analyzers, every project of `MotiffLab.slnx` | CA1502 cyclomatic complexity (15), CA1505 maintainability index (below 10), CA1506 class coupling (95 types per type, 40 per member) |
| `csharp` | `tools/quality/csharp-length.cs` (Roslyn syntax) and a line count | method, accessor and local function length (80); `.cs` and `.razor` file length (600) |
| `eslint` | ESLint with `eslint-plugin-sonarjs`, `src/MotiffLab.Web/Scripts` | `sonarjs/cognitive-complexity` (15), `max-lines-per-function` (80), `max-depth` (4), `max-lines` (600) |
| `swiftlint` | SwiftLint in Docker on Linux, `ipad/App` | `cyclomatic_complexity` (15), `function_body_length` (80), `file_length` (600), `type_body_length` (600), `nesting` (4) |
| `jscpd` | jscpd, C#, Razor, TypeScript, Swift and Python (`.jscpd.json`) | duplicated blocks from 10 lines |
| `config` | `tools/quality/quality.mjs` | the generated configuration matches `settings.json` |

The CA rules report only in a quality build (`-p:QualityMetrics=true`, `Directory.Build.props`), and there as warnings
that never fail the build, although warnings are otherwise errors: the ratchet decides. An everyday `dotnet build`
does not run them. CI's own Release build is the quality build, so CI builds once.

## The ratchet

[`tools/quality/baseline.json`](../../tools/quality/baseline.json) lists every violation known when the checks were
added, one per line. An entry is identified by check, rule, file and symbol (the function, type or the other file of a
duplicate), never by line number, so moving code keeps its entry; `value` is what was measured then.

- A violation not in the baseline is **new**: fix the code. If it cannot be fixed in this task, run
  `--update-baseline`, write the reason in the entry's `why` (with the issue number) and explain it in the pull request.
- An entry without a reason in `why` fails.
- An entry no violation matches any more (the code was fixed, renamed or moved to another file) must be removed:
  `--update-baseline` removes it. The baseline only shrinks unless a pull request justifies an entry.
- A baselined violation that got worse (a longer function, a bigger file) is listed in the report but does not fail.
- In CI the report lists the entries the pull request adds to the baseline, with their reasons, for the Reviewer.

## Soft mode and the switch

`"mode"` in [`settings.json`](settings.json) is the one switch:

- `"soft"` (from the merge of #332, for two weeks): the report goes to the job summary and annotations, and the checks
  never fail `build-test`, whatever they find, even when a check cannot run.
- `"blocking"`: `build-test` fails on a new violation, an entry to remove, an entry without a reason, a configuration
  that differs from `settings.json`, or a check that could not run. Old code in the baseline does not fail: only new
  code is held to the thresholds.

Andrei flips it: a pull request that changes `"soft"` to `"blocking"`, an E3 change he applies. Raising or lowering a
threshold works the same way, followed by `--write-config` and `--update-baseline`.
