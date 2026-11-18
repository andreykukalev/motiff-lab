# Code-quality checks

Agents write code to a measurable spec: functions and files stay small and simple, copy-paste is caught (#332), and each module uses only the modules it may
(the `dependencies` check, `CLAUDE.md` «Writing code» 4).
The checks run in the repository, with no external service: locally with one command, in CI in `build-test` on every
pull request. The thresholds and the switch below are the agents' own rules (decision 6 in `governance/README.md`):
they change only in a pull request Andrei merges.

## One command

```bash
node tools/quality/quality.mjs                    # all checks; the CA rules need a quality build (about a minute)
node tools/quality/quality.mjs --only eslint,jscpd,csharp,swiftlint,suppressions,dependencies   # quick, no build
node tools/quality/quality.mjs --update-baseline  # after fixing code, or to list a new deviation (then write its why)
node --test "tools/quality/*.test.mjs"            # the ratchet's own tests
```

It installs the tools in `src/shared/node_modules` on the first run (`npm ci`). It skips `archive/` (docs/repository-structure.md). SwiftLint needs Docker; without
it the command says it skipped SwiftLint, and CI runs it. A pull request that changes only the scan server
(`src/backend/omr`) is not built, so CI runs only `config`, `jscpd` and `suppressions` on it. The report is Markdown on standard output; in CI it goes to the
job summary, and each new violation is an annotation on its line.

## What is checked

The thresholds are kept in one place, [`settings.json`](settings.json): function complexity 15, function 80 lines, file
600 lines, nesting 4, duplicates from 10 lines. `CodeMetricsConfig.txt` and `src/ipad/.swiftlint.yml` are written from it
(`--write-config`), and the check `config` fails when they differ; ESLint reads it directly.

| Check | Tool | Rules (threshold) |
| --- | --- | --- |
| `dotnet` | .NET code-metrics analyzers, every project of `MotiffLab.slnx` | CA1502 cyclomatic complexity (15), CA1505 maintainability index (below 10), CA1506 class coupling (95 types per type, 40 per member) |
| `csharp` | `tools/quality/csharp-length.cs` (Roslyn syntax) and a line count | method, accessor and local function length (80); `.cs` and `.razor` file length (600) |
| `eslint` | ESLint with `eslint-plugin-sonarjs`, `src/shared/Scripts` | `sonarjs/cognitive-complexity` (15), `max-lines-per-function` (80), `max-depth` (4), `max-lines` (600) |
| `swiftlint` | SwiftLint in Docker on Linux, `src/ipad/App` and `tests/ipad` | `cyclomatic_complexity` (15), `function_body_length` (80), `file_length` (600), `type_body_length` (600), `nesting` (4) |
| `jscpd` | jscpd, C#, Razor, TypeScript, Swift and Python (`.jscpd.json`) | duplicated blocks from 10 lines |
| `suppressions` | `tools/quality/quality.mjs` (a scan of the tracked sources and build files) | each way to hide a violation from the checks above is a violation itself: `eslint-disable` and other ESLint directives (ignored anyway: `noInlineConfig`), `swiftlint:disable`, `jscpd:ignore`, `#pragma warning disable` (bare or naming CA1502/CA1505/CA1506), `SuppressMessage` for them, `dotnet_diagnostic.CA150x` or a Maintainability-wide severity in an `.editorconfig` or `.globalconfig`, `NoWarn` naming them, analyzers turned off |
| `dependencies` | `tools/quality/dependencies.mjs` | music-core references no project of the solution and no web, cloud or AI package or namespace (`Microsoft.AspNetCore`, `Microsoft.JSInterop`, `Microsoft.Extensions.AI`, `Microsoft.Identity`, `Azure`, `OpenAI`, `System.Net.Http`); every module the iPad's pages (`src/ipad/App/Web/*.js`) load, followed through their imports, exists and lies in a folder `src/ipad/App/project.yml` bundles (CI does not build the iPad app, so such a break would show only on the device); each folder of the iPad app uses only the folders it may (below); each namespace of music-core uses only the layers below its own (below) |
| `config` | `tools/quality/quality.mjs` | the generated configuration matches `settings.json` |

The iPad app is one Swift module, so the compiler does not keep its folders (`src/ipad/App/MotiffLab/<Folder>`,
blueprint v0.7, 5.2) apart: `dependencies` indexes the types each folder declares and reports a file that names a type
of a folder it may not use. The table is `IPAD_FOLDER_USES` in `tools/quality/dependencies.mjs`, its test keeps this copy
equal, and a folder always uses its own types:

| Folder | May use |
| --- | --- |
| `Platform/` | nothing |
| `Workspace/` | every folder |
| `Selection/` | `Platform` |
| `Playback/` | `Platform`, `Selection` |
| `Roll/` | `Platform`, `Selection` |
| `Ink/` | `Platform` |
| `Marks/` | `Platform`, `Selection` |
| `Sources/` | `Platform`, `Selection` |
| `Pdf/` | `Platform` |
| `Library/` | `Platform` |
| `Import/` | `Platform` |
| `Insights/` | `Platform`, `Selection` |
| `Reference/` | `Platform` |
| `Assistant/` | `Platform`, `Selection` |
| `Account/` | `Platform` |

A finding `src/ipad/App/MotiffLab/Marks/Check.swift:12 | dependencies ipad-folder | Workspace: ScoreController` says
that the file names `ScoreController`, a type of `Workspace/`, first on line 12. Move the type down to `Platform`, read it through a protocol
the folder may use, or let `Workspace` do it. The edges the app had when the check came are baseline entries, one per
file and type. A new one that the task cannot avoid: `node tools/quality/quality.mjs --only dependencies --update-baseline
--why "<reason> (#issue)"`; a gone one: the same without `--why`. A conflict in `baseline.json`: keep both sides'
entries, then run the command without `--why`, which keeps their reasons and drops what no longer matches. What the
scan cannot see is written above `swiftCode` in `dependencies.mjs`; above all, it indexes only types, so a free
function, a global constant or a member an extension adds to a type used from another folder is missed.

music-core is one C# project, so the compiler lets its namespaces (`MotiffLab.MusicCore.<Namespace>`, a sub-namespace
such as `Import.Scan` counted as its top one) use each other in a loop. `dependencies` keeps them in layers: a namespace
uses only the namespaces of the layers above it in this table (the layers below it in the code), never one of its own
layer, so none uses another, directly or round a loop (W1.1 to W1.3, 2026-11-18; `docs/code-map/core.md`). The table is
`CORE_LAYERS` in `tools/quality/dependencies.mjs`, and its test keeps this copy equal. `(root)` is `MotiffLab.MusicCore`
itself (`Words`):

| Layer | Namespaces |
| --- | --- |
| 1 | `(root)`, `Time`, `Settings`, `Accounts` |
| 2 | `Events` |
| 3 | `Motifs`, `Export` |
| 4 | `Pieces` |
| 5 | `Import`, `Playback` |
| 6 | `Analysis`, `Performance` |
| 7 | `Notation`, `Variations`, `Invitations` |
| 8 | `Experiments`, `Assembly` |
| 9 | `Analytics`, `Projects` |
| 10 | `Assistant` |
| 11 | `Facade` |

A file uses a namespace by a `using` line (a dead one too) or by a name written qualified, in code or in a `cref`
(`Performance.SavedPerformance`, `<see cref="Projects.Project"/>`): in C# an unqualified name reaches another namespace
only through a `using` line or the namespaces around the file's own. A doc comment that names a type of a higher
namespace says it in plain text (`<c>Project.PartOf</c>`), since a `cref` would need the namespace. A finding
`src/backend/MotiffLab.MusicCore/Pieces/Bookmark.cs:1 | dependencies core-namespace | Pieces: Projects` says that the
file, in `Pieces` (layer 4), uses `Projects` (layer 9) first on line 1: move the type it needs down to a layer `Pieces`
may use, or the code up. A new namespace, or a namespace moved to another layer, is a change of this table, which
Andrei merges like the rest of this file. What the scan cannot see is written above `csharpCode` in `dependencies.mjs`.

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
- A baselined violation that got worse (a longer function, a bigger file) fails in blocking mode (since 2026-11-16,
  Andrei: `piano-roll.ts` had grown from 2,149 to 4,077 lines unnoticed). Split or simplify the code; never raise the
  entry's `value` to pass. The values were reset to the measured ones on 2026-11-16.
- A suppression (the `suppressions` check) is fixed by removing it; when it truly cannot go, it is a baseline entry
  with a reason like any other.
- In CI the report lists the entries the pull request adds to the baseline, with their reasons.

## Soft mode and the switch

`"mode"` in [`settings.json`](settings.json) is the one switch:

- `"soft"` (from the merge of #332 until 2026-11-11): the report goes to the job summary and annotations, and the checks
  never fail `build-test`, whatever they find, even when a check cannot run.
- `"blocking"` (since 2026-11-11, the current mode): `build-test` fails on a new violation, a baselined violation that got worse, an entry to remove, an entry without a reason, a configuration
  that differs from `settings.json`, or a check that could not run. Old code in the baseline does not fail as long as it
  does not get worse; new code is held to the thresholds.

Changing the mode, or raising or lowering a threshold, is a pull request Andrei merges, followed by `--write-config` and `--update-baseline`.
