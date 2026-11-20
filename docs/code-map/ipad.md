# Code map: the iPad app (`src/ipad/App`), index

SwiftUI over the one C# music-core (NativeAOT framework, `src/ipad/Core`). The app's Swift is in feature folders under
`src/ipad/App/MotiffLab` (blueprint v0.7, section 5.2; step B1). `project.yml` compiles every file below that folder, so a
new file needs no project edit. `Account.json` and the asset catalogues (`AppIcon.xcassets`, `Icons.xcassets`, written by
`tools/icons/build.mjs`) stay at its top. A folder's own interface texts are `<Folder>/Words/<Folder>.{en,ru}.json`;
what the pages or several folders say is in `src/shared/wwwroot/i18n` (`UIText`, `platform.md`). The score and the roll
are the shared TS modules in a WKWebView (`Web/score.html` over `src/shared/Scripts/score/ipad-page`; `src/shared/Scripts/score`,
`roll`: `score.md`, `roll.md`).
Tests: `tests/ipad/MotiffLabTests` (XCTest, run by the `iPad` workflow on macOS, not CI), with the UX baseline's cases
in `tests/ipad/Baseline` (the same test target; changed only with Andrei's label). There too, the workspace's scenarios
(blueprint 5.3 step 3): `WorkspaceSession` takes a `ScoreWorkspace` over the real music-core and controllers through a
session as the view would (each render runs the reactions of the values that changed, in the view's order) and writes
what the controllers tell the two pages, the player's model and the panels; `WorkspaceScenarioTests` compares that
with `fixtures/workspace-*.txt` (a transcript that differs is printed whole in the test's log). The pages are recording
ones, put in `ScoreController.scorePage` and `RollController.page`, which are settable for that.

Each folder has one map with four headings: its files; the contract others may rely on; the state it owns; the baseline
entries that protect it (the UX baseline, `docs/ux-baseline.md`, by its area ids, and the quality ratchet,
`tools/quality/baseline.json`).

| Folder | Map | Holds |
| --- | --- | --- |
| `Platform/` | [platform](ipad/platform.md) | music-core's port, the score page's port, design tokens, words, project files, the app |
| `Workspace/` | [workspace](ipad/workspace.md) | the score's screen and its controller |
| `Selection/` | [selection](ipad/selection.md) | the one selection, its tools and history, phrases |
| `Playback/` | [playback](ipad/playback.md) | the player, the engine, the pill, metronome, tempo, practice, Expand |
| `Roll/` | [roll](ipad/roll.md) | the piano roll in the drawer |
| `Ink/` | [ink](ipad/ink.md) | the stylus, the pen, input rules, the modes |
| `Marks/` | [marks](ipad/marks.md) | marks, bookmarks, Check, «Мои правки» |
| `Sources/` | [sources](ipad/sources.md) | sources, versions, originals |
| `Pdf/` | [pdf](ipad/pdf.md) | the PDF view, its notes, its parts |
| `Library/` | [library](ipad/library.md) | the library screen and its shelf |
| `Import/` | [import](ipad/import.md) | Add piece, folders, scans, photos, recordings, recognition |
| `Insights/` | [insights](ipad/insights.md) | Insights, the piece's map (DNA), the circle of fifths |
| `Reference/` | [reference](ipad/reference.md) | the reference sheet |
| `Assistant/` | [assistant](ipad/assistant.md) | the assistant, its field and dictation |
| `Account/` | [account](ipad/account.md) | sign-in, environments, online copies, Settings, About |

Which folder may use which (blueprint 5.2): `Platform` uses nothing above it; a feature uses `Platform` and, where the
blueprint's table says so, reads Selection; only `Workspace` knows several features. The `dependencies` check enforces
it (`IPAD_FOLDER_USES` in `tools/quality/dependencies.mjs`, `governance/quality/README.md`). The edges the app had when
the check came are `ipad-folder` entries of `tools/quality/baseline.json`, one per file and type named, which the
folders' maps do not repeat: `grep '"ipad-folder","file":"src/ipad/App/MotiffLab/<Folder>/' tools/quality/baseline.json`.

## The UI walk

- `tests/ipad/MotiffLabScreens` (the `Screens` scheme): `ScreensUITests` runs the journeys, each naming the UX baseline
  ids it protects; `Walk.swift` drives the app and reads the probe; the journeys are in `ScoreJourneys.swift` and
  `SwitchJourneys.swift`. The probe: `ScreensProbe.line()` (`Platform/ScreensHook.swift`, Debug only, under `-ml-screens`).

## Rules not to break

- The score's screen never resizes for the keyboard: a chat keeps itself clear of it (`AskFlow`, `KeyboardRoom`); the
  pill rises only above a hidden keyboard's bar (`KeyboardLift(barOnly:)`). The tap that hides the keyboard never acts on
  the music (`TypingShield`); no field takes the focus by itself. Dictation never leaves the device.
- Nothing in `src/ipad/App` changes analysis or variation data; drafts come from music-core and only the player accepts them.
- Every interface string in English and Russian; colours, sizes and timings are tokens.
