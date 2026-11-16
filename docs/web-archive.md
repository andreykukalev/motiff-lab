# The web app archive: what is frozen, what stays live, how to bring it back

Status: current, 2026-11-16. Decision: [0032](decisions/0032-ipad-first-web-archived.md). One living document: update it
when a live shared module moves or the web is reactivated.

The web app (Blazor WebAssembly PWA, the Music Studio of [0025](decisions/0025-two-studios.md)) is archived in place.
Its code stays in the repository and keeps compiling; it is not developed, tested in browsers or deployed.

## Restore point

| What | Where |
| --- | --- |
| Last developed and deployed web | tag `v0.6.5` (`develop` d8050f4); `main` at 9b98d6b (release 0.6, PR #633), deployed to the main address |
| Main address (frozen at that deploy) | https://delightful-ground-046924d0f.4.azurestaticapps.net |
| Web test site (frozen at the last `develop` deploy) | https://delightful-ground-046924d0f-develop.eastus2.4.azurestaticapps.net |
| Known state at archival | Browser suite red since v0.6.1 |
| Deferred web work | GitHub issues labelled `web-deferred` (at archival: #66, #319, #323, #325, #326, #337, #350, #401, #418, #419, #433, #561), kept open with their text |
| What the iPad changed since that the web lacks | `/mnt/project-files/product/web-backlog.md` (project files) |

## The web project's folder, split

`src/MotiffLab.Web` holds two kinds of files. The iPad app bundles the live part (`ipad/App/project.yml`), so it stays
in place and is maintained by iPad work; the rest is frozen.

| Live, shared with the iPad (maintained) | Why |
| --- | --- |
| `Scripts/score`, `Scripts/roll`, `Scripts/layout`, `Scripts/storage`, `Scripts/playback` → `wwwroot/js/…` | The iPad's score page and roll run them (`ipad/App/Web`) |
| `MotiffLab.Web.csproj`, `tsconfig.json`, `package.json`, `eslint.config.mjs` | `dotnet build src/MotiffLab.Web` compiles that TypeScript for the iPad (`ipad.yml`, `ipad/build-version.sh`) |
| `wwwroot/i18n` | The interface words both apps read |
| `wwwroot/audio/piano` | The piano samples the iPad plays |
| `Playback/FurElise.musicxml` | The iPad's example piece |
| `tests/web` (node tests) | Cover the live modules; they also cover some frozen scripts and stay in `build-test` |

| Frozen (archived) | What it is |
| --- | --- |
| `Pages`, `Layout`, `App.razor`, `_Imports.razor`, `Program.cs` and the feature folders (`Account`, `Analytics`, `Compare`, `Design`, `Dna`, `Explore`, `Import`, `Insights`, `Library`, `Localization`, `Observations`, `Pdf`, `Playback` except the example piece, `Projects`, `Reference`, `Roll`, `Score`, `Settings`, `Sources`, `Understand`, `Versions`, `Workspace`) | The Blazor app: pages, components, `PlaybackLab`, services (`ProjectStore`, `CloudSync`, `ScanService`, …) |
| `Scripts/account`, `import`, `metronome`, `pdf`, `phrases`, `reference` | Web-only browser layer |
| `wwwroot/css` (`app.css`, the copy of `tokens.css`, fonts), `index.html`, `manifest.webmanifest`, `service-worker*.js`, `staticwebapp.config.json`, `appsettings.json`, `img`, icons, `lib/pdfjs`, `lib/verovio` | The site's shell, styles, offline worker and hosting configuration |
| `tests/e2e` | 58 Playwright spec files on the published app |
| `infra/main.bicep`, `main.bicepparam`, `infra/azure/develop.ps1`, `test-site-signin.ps1` | The Static Web App and the test site's setup |

Rules while frozen (0032): no features, fixes or ports in the frozen part; if a change elsewhere breaks its compilation,
the smallest mechanical fix in the same pull request. A change to a live module that alters what the web would show gets
one line in `web-backlog.md`. Never import frozen code into the iPad app.

## Workflows

| Workflow | Active | Archived |
| --- | --- | --- |
| `CI` (`build-test`) | Build of the whole solution, unit and node tests, guards, quality | Publish, provisioning and deploy of the web: only with the repository variable `WEB_ACTIVE` = `true` |
| `Browser tests` | — | By hand only (Actions tab) |
| `iPad`, `API`, `Scan server`, `Version` | Unchanged | — |

Developer commands that belong to the archive: `dotnet run --project src/MotiffLab.Web`, `dotnet publish src/MotiffLab.Web`
and the Playwright run (`tests/e2e/README.md`).

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
3. Bring the web to the current project file version and facade before any writer of a newer version ships to it
   (0026 section 3 applies again).
4. Andrei sets `WEB_ACTIVE` to `true`; the `browser-tests.yml` triggers (schedule, tags, label) come back from the
   commit that removed them.
5. Run the browser suite by hand, fix what is red, then plan the porting versions from `web-backlog.md` and the issues
   labelled `web-deferred`.
6. Mark this document "history" and remove the frozen notes from the earlier records (0018, 0019, 0025, 0026, 0027).
