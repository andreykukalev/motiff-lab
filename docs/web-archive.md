# The web app archive: what is frozen, what stays live, how to bring it back

Status: current, 2026-11-16 (the web-only shared modules and interface words archived, v0.7 step A6). Decision: [0032](architecture/adr/0032-ipad-first-web-archived.md). One living document: update it
when a shared module moves or the web is reactivated.

The web app (Blazor WebAssembly PWA, the Music Studio of [0025](architecture/adr/0025-two-studios.md)) is archived in
`archive/web` ([0033](architecture/adr/0033-repository-layout.md), [repository-structure.md](repository-structure.md)). Its
code stays in the repository; it is not built, developed, tested or deployed, and it does not compile by itself any more:
the parts the iPad bundles moved to `src/shared` (see "Reactivation").

## Restore point

| What | Where |
| --- | --- |
| Last developed and deployed web | tag `v0.6.5` (`develop` d8050f4); `main` at 9b98d6b (release 0.6, PR #633), deployed to the main address |
| Main address (frozen at that deploy) | https://delightful-ground-046924d0f.4.azurestaticapps.net |
| Web test site (frozen at the last `develop` deploy) | https://delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net |
| Known state at archival | Browser suite red since v0.6.1 |
| Deferred web work | GitHub issues labelled `web-deferred` (at archival: #66, #319, #323, #325, #326, #337, #350, #401, #418, #419, #433, #561), kept open with their text |
| What the iPad changed since that the web lacks | `/mnt/project-files/product/web-backlog.md` (project files) |

## What moved where

The iPad app bundles part of the former web project (`src/ipad/App/project.yml`); that part is now `src/shared` and is
maintained by iPad work. The rest is in `archive/web`, frozen.

| Live, in `src/shared` (maintained) | Why |
| --- | --- |
| `Scripts/score`, `Scripts/roll`, `Scripts/layout`, `Scripts/storage`, `Scripts/playback` → `wwwroot/js/…` | The iPad's score page and roll load them (`src/ipad/App/Web`), all but `score/score-page.ts`, the A4 layout the node tests engrave with (`Engraver.swift` keeps the same values) |
| `MotiffLab.Shared.csproj`, `tsconfig.json`, `package.json`, `eslint.config.mjs` | `dotnet build src/shared` compiles that TypeScript for the iPad (`ipad.yml`, `src/ipad/build-version.sh`) |
| `wwwroot/i18n` | The interface words the iPad names, or may name (`tests/shared/i18n.test.mjs` checks that each is there in both languages) |
| `wwwroot/audio/piano` | The piano samples the iPad plays |
| `wwwroot/lib/verovio` | The npm build of Verovio the score scripts were written against (`tools/verovio`) |
| `Playback/FurElise.musicxml` | The iPad's example piece |
| `tests/shared` (node tests) | Cover the shared modules; in `build-test` |

| Frozen, in `archive/web` | What it is |
| --- | --- |
| `src/MotiffLab.Web`: `Pages`, `Layout`, `App.razor`, `_Imports.razor`, `Program.cs` and the feature folders (`Account`, `Analytics`, `Compare`, `Design`, `Dna`, `Explore`, `Import`, `Insights`, `Library`, `Localization`, `Observations`, `Pdf`, `Playback` except the example piece, `Projects`, `Reference`, `Roll`, `Score`, `Settings`, `Sources`, `Understand`, `Versions`, `Workspace`) | The Blazor app: pages, components, `PlaybackLab`, services (`ProjectStore`, `CloudSync`, `ScanService`, …) |
| `src/MotiffLab.Web/Scripts`: `account`, `import`, `metronome`, `pdf`, `phrases`, `reference`, and the web-only `score/score-view.ts`, `storage/originals.ts`, `storage/project-store.ts`, `storage/recording-store.ts`, `storage/test-library.ts`, `playback/playback.ts`, `layout/test-site.ts` | Web-only browser layer; they import the shared modules by relative paths that no longer resolve |
| `src/MotiffLab.Web/Scripts`, moved from `src/shared/Scripts` in v0.7 (step A6, `docs/REFACTORING_AUDIT.md` W0.6, Andrei's decision 1): `layout/bar-fade.ts`, `drawer-resize.ts`, `fragment.ts`, `hold-repeat.ts`, `long-press.ts`, `mode-keys.ts`; `playback/playback-owner.ts`, `playback-processor.ts`; `score/pdf-document.ts`, `pencil-history.ts`, `pencil-layer.ts`, `svg-to-pdf.ts` (its PDF half: the SVG parser stayed as `src/shared/Scripts/score/svg-parse.ts`); `storage/conversation-store.ts`, `heard-parts.ts`, `idb.ts`, `import-store.ts`, `pencil-store.ts` | Loaded by no iPad page and imported by no module a page loads: the web's AudioWorklet engine (`PlaybackEngine.swift` is the one that runs), its pen layer and pen store, its playback owner (`Player.swift` holds the model), its PDF export, its chrome (drawer, long press, hold to repeat, keyboard modes, bar fade, links) and its IndexedDB stores. Same folders as in `src/shared/Scripts`, so their relative imports of the live modules resolve again once they move back |
| `src/MotiffLab.Web/wwwroot`: `css/app.css`, the mirrors `css/tokens.css`, `css/fonts`, `img/icons.svg`, `index.html`, `manifest.webmanifest`, `service-worker*.js`, `staticwebapp.config.json`, `appsettings.json`, the rest of `img`, the app icons, `lib/pdfjs` | The site's shell, styles, offline worker and hosting configuration. The mirrors of `design/kit` are no longer checked against the kit; `node tools/icons/build.mjs` still writes `icons.svg` |
| `tests/e2e` | 58 Playwright spec files on the published app |
| `tests/web` | Node tests of web-only code, and the web halves of `design`, `i18n`, `stylesheets` and `pencil-marks`; from v0.7 (A6) also the tests of the modules above: `conversation-store`, `drawer-resize`, `hold-repeat`, `long-press`, `pencil-read-only`, `playback-owner`, `playback-processor`, `playback-scrub`, `scrub-rule` whole, and the web halves of four live tests as files of their own: `mode-keys` (from `score-modes`, `playheads`, `selection-gesture`), `pdf-export` (from `score-pdf`), `scrub-notes-heard` (from `scrub-notes`). They still import `../../src/shared/wwwroot/js/…`: they run unchanged once back in `tests/shared` |
| `src/MotiffLab.Web/wwwroot/i18n/en.json`, `ru.json`, moved from `src/shared/wwwroot/i18n` in v0.7 (step A6, W0.6/F6, Andrei's decision 5): 1401 English and 1432 Russian keys | The words only the web said: no iPad code, page script, music-core or API text names them, literally or by a family it builds. Same names as before; the live files kept 1111 English and 1134 Russian keys |
| `tools/pdfjs`, `tools/perf` | Web-only tools |
| `workflows/browser-tests.yml` | The browser tests' workflow (GitHub runs only `.github/workflows`) |
| `infra/main.bicep`, `main.bicepparam`, `infra/azure/develop.ps1`, `test-site-signin.ps1` (still in `infra/`) | The Static Web App and the test site's setup |

`archive/Directory.Build.props` is empty on purpose: it keeps the repository's MSBuild rules (warnings as errors, code
style, metrics) away from the archived project.

Rules while frozen (0032, 0033): no changes in `archive/`, not even to keep it compiling (the one exception: the icon
sprite `node tools/icons/build.mjs` writes with the iPad's icons). A change to a shared module that
alters what the web would show gets one line in `web-backlog.md`. Never import archived code into the iPad app or
`src/shared`.

## Workflows

| Workflow | Active | Archived |
| --- | --- | --- |
| `CI` (`build-test`) | Build of the solution (backend, `src/shared`, iPad core), unit and node tests, guards, quality; a change only in `archive/` builds nothing | The web's publish, provisioning and deploy steps: in `ci.yml` at commit 6088b98 |
| `Browser tests` | — | `archive/web/workflows/browser-tests.yml`, not run; its triggers at commit 44360d2 |
| `iPad`, `API`, `Scan server`, `Version` | Unchanged except paths | — |

Developer commands that belong to the archive (they work again after step 3 of "Reactivation"): `dotnet run --project
archive/web/src/MotiffLab.Web`, `dotnet publish archive/web/src/MotiffLab.Web` and the Playwright run
(`archive/web/tests/e2e/README.md`).

## Infrastructure

The full table of web-only and shared components is in 0032, "Infrastructure". In short: only the Static Web App
(`swa-motifflab`, Free plan, with its environment `develop`) is web-only; it is kept as it is and no longer updated.
Sign-in, the API, the databases, the scan servers and the CI identities are shared and unchanged; the API and scan server
still allow the web's origin (`WEB_HOST`).

## Reactivation

Andrei decides it (product concept, `governance/README.md` decision 1), as a separate initiative. Then:

1. Decide where the web restarts: `develop` as it is then (the frozen code plus the shared modules as the iPad left
   them), or the restore point. `develop` is the default: it already has the current music-core and project file.
2. A decision record that ends 0032's hold, and the platform focus in `governance/README.md`, `roles/common.md` and
   `CLAUDE.md` set back to both platforms.
3. Move the web project back out of the archive (`archive/web/src/MotiffLab.Web` → `src/web/MotiffLab.Web`, its tests to
   `tests/web`), add it to `MotiffLab.slnx`, delete `archive/Directory.Build.props` if nothing else is archived, and
   make it build with `src/shared`: its `tsconfig.json` and `MotiffLab.Web.csproj` compile `src/shared/Scripts` with
   its own scripts into its `wwwroot/js`, and its publish copies `src/shared/wwwroot/{i18n,audio,lib/verovio}` and
   `src/shared/Playback/FurElise.musicxml` (or one shared folder is laid out so both read it in place). Copy the
   design kit's tokens, fonts and icons into its `wwwroot` again, and move the web halves of the node tests back into
   `tests/shared` (`design`, `i18n`, `stylesheets`, `pencil-marks`). The 17 modules archived in v0.7 (A6, table above) go
   back to `src/shared/Scripts/<folder>` and their 12 test files to `tests/shared`, where both run unchanged; web code that
   took `parseSvg`, `parseTransform`, `viewBoxOf` or `num` from `svg-to-pdf.js` takes them from `svg-parse.js`. The
   iPad's case file `tests/ipad/MotiffLabTests/Golden/scrub.json` has had no node reader since then: `scrub-rule` runs
   the web's engine on it again. The words archived then (`archive/web/src/MotiffLab.Web/wwwroot/i18n/{en,ru}.json`) are
   merged back into `src/shared/wwwroot/i18n/{en,ru}.json` before the web's `i18n` test runs (no key is in both). The
   reactivation task's `allowed_paths` name
   `archive/web/**`, which opens it to the agents (`.claude/hooks/archive-guard.mjs`); once nothing is left in
   `archive/`, remove it from `.ignore`.
4. Bring the web to the current project file version and facade before any writer of a newer version ships to it
   (0026 section 3 applies again).
5. Restore the web's publish, provisioning and deploy steps in `ci.yml` from commit 6088b98 (and its paths), move
   `archive/web/workflows/browser-tests.yml` back to `.github/workflows` with its triggers from commit 44360d2, and
   Andrei sets `WEB_ACTIVE` to `true`.
6. Run the browser suite by hand, fix what is red, then plan the porting versions from `web-backlog.md` and the issues
   labelled `web-deferred`.
7. Mark this document "history" and remove the frozen notes from the earlier records (0018, 0019, 0025, 0026, 0027).
