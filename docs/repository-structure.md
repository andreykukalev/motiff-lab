# Repository structure

Status: current, 2026-11-16. Decision: [0033](architecture/adr/0033-repository-layout.md). One living document: update it in the
pull request that adds, moves or removes a top-level folder.

One source tree, one test tree, clear platform boundaries, and each platform's own conventions inside its folder.

```text
motiff-lab/
├── src/                          all active production code
│   ├── backend/
│   │   ├── MotiffLab.MusicCore/  C# music-core: event model, parsing, analysis, variations (pure, no I/O)
│   │   ├── MotiffLab.Api/        ASP.NET Core API (projects in Cosmos DB, the assistant relay)
│   │   └── omr/                  the scan server (Python: homr, Basic Pitch), with its pytest tests inside
│   ├── ipad/                     the native iPad app (SwiftUI, XcodeGen); src/ipad/README.md
│   │   ├── App/                  the app: project.yml, MotiffLab/, Web/ (score page), Words/, Config/
│   │   ├── Core/                 music-core as a NativeAOT framework and the facade's C functions
│   │   ├── Verovio/              Verovio built from source
│   │   └── build-version.sh      builds a version from its tag and installs it on the iPad (Andrei's Mac)
│   └── shared/                   what the iPad bundles from the former web project; src/shared/README.md
│       ├── Scripts/              TypeScript: score, roll, layout, storage, playback (compiled to wwwroot/js)
│       ├── wwwroot/              i18n (interface words), audio/piano, lib/verovio
│       ├── Playback/             FurElise.musicxml, the example piece
│       └── MotiffLab.Shared.csproj, tsconfig.json, package.json, eslint.config.mjs
├── tests/
│   ├── backend/
│   │   ├── Unit/                 MotiffLab.MusicCore.Tests
│   │   ├── Integration/          MotiffLab.Api.Tests
│   │   └── Acceptance/           MotiffLab.Acceptance.Tests (protected scenarios: Andrei merges)
│   ├── ipad/                     MotiffLabTests (unit) and MotiffLabScreens (UI), built by src/ipad/App/project.yml
│   ├── shared/                   node tests of src/shared (after dotnet build)
│   └── contracts/                fixtures both sides of a contract read (the score page's)
├── archive/                      code that is kept but not built, tested or deployed
│   ├── Directory.Build.props     empty: stops the root MSBuild rules reaching archived projects
│   └── web/                      the archived web app (0032); docs/web-archive.md
│       ├── src/MotiffLab.Web/    the Blazor WebAssembly app, its web-only scripts, styles and hosting files
│       ├── tests/                e2e (Playwright) and web (node tests of web-only code)
│       ├── tools/                pdfjs, perf (web-only tools)
│       └── workflows/            browser-tests.yml (GitHub runs only .github/workflows)
├── design/kit/                   the redesign kit (tokens, components, fonts, icons, screens)
├── docs/                         specifications; architecture/adr/ holds the decision records
├── governance/                   how the work is run (agents' rules: Andrei merges)
├── infra/                        Azure (Bicep, scripts)
├── tools/                        repository tools: quality, icons, piano, fur-elise, verovio, pencil-grain
├── MotiffLab.slnx                the .NET solution: backend, shared, iPad core and their tests
└── Directory.Build.props, global.json, .editorconfig, …
```

## Where things go

| Adding | Goes in |
| --- | --- |
| Analysis, notes, rules, a facade function | `src/backend/MotiffLab.MusicCore`, its tests in `tests/backend/Unit` |
| An API endpoint | `src/backend/MotiffLab.Api`, its tests in `tests/backend/Integration` |
| The scan server's code | `src/backend/omr`, its tests in `src/backend/omr/tests` (Python's own layout, run with `python -m pytest` there) |
| An iPad screen or controller | `src/ipad/App/MotiffLab`, its tests in `tests/ipad/MotiffLabTests` (UI walks in `tests/ipad/MotiffLabScreens`) |
| Score, roll, layout, storage or playback behaviour the score page runs | `src/shared/Scripts`, its tests in `tests/shared` |
| An interface text | `src/shared/wwwroot/i18n/{en,ru}.json`, or `src/ipad/App/Words` for what only the iPad says |
| A repository tool | `tools/` |
| Anything in `archive/` | Nothing: archived code is frozen. Bringing it back moves it out of `archive/` again |

Each platform keeps its native layout inside its folder: .NET projects one per folder with their `.csproj`, XcodeGen's
`project.yml` with relative paths in `src/ipad/App`, Python's `requirements*.txt` and `tests/` beside its code.

## Archive

`archive/` holds code that is kept for a possible return, not maintained: today the web app (0032). It is not built,
tested, linted or deployed, and agents do not read, search or change it, so it never enters their context
(`governance/roles/common.md`, "Platform focus"). Search tools skip it (`.ignore`: ripgrep, the agents' Grep, VS Code
search; `git grep` and `find` still see it), and `.claude/hooks/archive-guard.mjs` asks before a read, search or change
there, except in a task whose `allowed_paths` name `archive/`. Bringing a part back moves it out of `archive/`.

## How the builds find things

- `dotnet build MotiffLab.slnx` builds the backend, `src/shared` (its TypeScript to `src/shared/wwwroot/js`, not
  committed), `src/ipad/Core` and the backend tests. The solution does not list anything in `archive/`.
- `src/ipad/App/project.yml` reads `../../shared/…` (compiled scripts, words, piano, example piece), `../../../design/kit`,
  `../../../tests/ipad` and `../../../tests/contracts`; `src/ipad/Core` references `../../backend/MotiffLab.MusicCore`.
- CI (`ci.yml`) treats a change under `archive/` like documentation: no build, no tests. The quality check
  (`tools/quality`) and jscpd skip `archive/**`; `governance/risk-paths.yml` puts it at low risk.
- The `iPad` workflow runs `dotnet build src/shared` before XcodeGen; `src/ipad/build-version.sh` does the same, and for a
  tag made before this layout it uses the old paths (`ipad/`, `src/MotiffLab.Web`).

## iPad builds

- The `iPad` workflow starts on every pull request; its first job decides whether the pull request touches what the
  app is built from (`src/ipad`, `tests/ipad`, `src/shared`, music-core, `design/kit`, `tests/contracts`, `global.json`,
  `Directory.Build.props`), and only then are the app built, the Swift tests run in the simulator and the walk's
  UI tests compiled (not run), on every push.
  Its job `ipad-gate` reports on every pull request and is a required check on `develop` (`infra/github/protect.ps1`).
  The same tests run on `develop` after a merge that touches those inputs, on every version tag and by hand. The walk
  of the screens' journeys (`tests/ipad/MotiffLabScreens`, asserting, one appearance, a few minutes) runs on tags,
  nightly and with Walk ticked; the pictures for the mockups only with Screenshots ticked (every appearance, about half
  an hour, during which the Mac takes no other run and pull requests queue behind it). Either fails its run when the
  walk fails. A scheduled job starts the walk and the switch test on `develop` each night it changed; a schedule runs
  the default branch's copy of the workflow, so it works once the workflow is released to `main`.
- music-core's framework is cached by the hash of its sources. The `iPad` run on `develop` after a change saves it, so
  pull requests to `develop` take it from the cache instead of three NativeAOT builds.
- It runs on GitHub's macOS, or on Andrei's Mac as a self-hosted runner when the repository variable `IPAD_RUNNER`
  names its label (`motiff-mac`). A pull request from a fork always runs on GitHub's machines; outside collaborators'
  runs need approval (Settings → Actions → General). Removing the variable moves the runs back to GitHub.
  `infra/github/mac-runner.sh` on the Mac sets all of this up, under a hidden account `runner` as a background daemon
  that keeps the Mac awake on the charger with the lid closed (`--remove` undoes it).

## The move of 2026-11-16 (0033)

| Before | After |
| --- | --- |
| `ipad/` | `src/ipad/` |
| `ipad/MotiffLabTests`, `ipad/MotiffLabScreens` | `tests/ipad/MotiffLabTests`, `tests/ipad/MotiffLabScreens` |
| `src/MotiffLab.MusicCore`, `src/MotiffLab.Api` | `src/backend/MotiffLab.MusicCore`, `src/backend/MotiffLab.Api` |
| `services/omr` | `src/backend/omr` |
| `tests/MotiffLab.MusicCore.Tests` | `tests/backend/Unit/MotiffLab.MusicCore.Tests` |
| `tests/MotiffLab.Api.Tests` | `tests/backend/Integration/MotiffLab.Api.Tests` |
| `tests/MotiffLab.Acceptance.Tests` | `tests/backend/Acceptance/MotiffLab.Acceptance.Tests` |
| `docs/decisions` | `docs/architecture/adr` |
| `src/MotiffLab.Web/Scripts/{score,roll,layout,storage,playback}` (what the iPad loads) | `src/shared/Scripts/…` |
| `src/MotiffLab.Web/wwwroot/{i18n,audio,lib/verovio}`, `Playback/FurElise.musicxml` | `src/shared/…` |
| `src/MotiffLab.Web/{package.json,package-lock.json,eslint.config.mjs}` | `src/shared/…` (`tsconfig.json` copied) |
| the rest of `src/MotiffLab.Web` | `archive/web/src/MotiffLab.Web` |
| `tests/e2e` | `archive/web/tests/e2e` |
| `tests/web` | `tests/shared`, except the tests of web-only code: `archive/web/tests/web` |
| `tools/pdfjs`, `tools/perf` | `archive/web/tools/` |
| `.github/workflows/browser-tests.yml` | `archive/web/workflows/browser-tests.yml` |

Files were moved, not rewritten, so git records them as renames and `git log --follow` shows their history. No code
changed except paths, and no test was removed: the node tests `design`, `i18n`, `stylesheets` and `pencil-marks` were
split, their web-only checks moved to `archive/web/tests/web`. The web's publish and deploy steps left `ci.yml`; they
are in `ci.yml` at commit 6088b98.
