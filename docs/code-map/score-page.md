# Code map: the iPad's score page (`src/shared/Scripts/score/ipad-page`)

**Status (2026-11-20, v1.2 i-1):** the page that was `src/ipad/App/Web/score.js` (1,808 lines of plain JavaScript) is
TypeScript modules here, typed by `Scripts/score/page-contract.ts` (decision record 0031, step 11; audit W2.13), in the
type, lint, duplicate and size checks like the rest of `src/shared/Scripts`. Behaviour as before. The shared layers it
builds: [`score.md`](score.md).

## How the app loads it

`ScoreController` loads `Web/score.html` (`BundleSchemeHandler`), which loads `Web/score.js`, now only the entry: one
`import "../score/ipad-page/page.js"`. `dotnet build src/shared` compiles the modules to `wwwroot/js/score/ipad-page/`,
which `project.yml` bundles with the rest of `wwwroot/js/score` as `score/`. The `dependencies` check follows the imports
from `Web/*.js` through these modules into the bundled folders.

## The contract

`page-contract.ts`: `PageCommands` (the 41 functions of `window.motiff`, Swift's `PageCommand`, with what each takes and
answers), `PageEvents` (the 23 message kinds, Swift's `PageEvent.Kind`, with what each carries; `performanceBar` with
`MarkerTap`), `PageMessage`, and the `Window` globals. The names are `tests/contracts/score-page.json`:
`tests/shared/score-page-contract.test.mjs` checks the interfaces, `page.ts`'s `window.motiff` and every `post(…)` of
the modules against it, and that `Web/score.js` holds no page code; `ScorePageContractTests` checks Swift.

## Modules (one part of the page each)

- `page.ts`: the entry. Calls each module's `install…`/`listen…` in the order the old page made its listeners and
  elements (the order a press reaches the layers), sets `window.motiff` (`satisfies PageCommands`), posts `"ready"`.
- `page-state.ts`: what several modules read (`state`: the pages' SVG, timeline, geometry and frames, pen, mode, input
  rules, focus, the playback model, beats, highlights' bands), each field written by one module; `post` (the only way to
  Swift), `pointerRules`, `documentPoint`, `READING_LINE_PX`. DOM-free at load (node test `score-page-state.test.mjs`).
- `page-layout.ts`: `show`, `layout` (places every mark again), `inView`, `inset`, `frames`; the scroll and resize listeners.
- `page-ink.ts`: the pencil (`anchor`, `erase`, `eraseAlong`, `rub`, `rubEnd`, `draw`), owns the marks and the rubbing.
- `page-bookmarks.ts`: `bookmarks`, `bookmarksTop` (the menu clears them).
- `page-marks.ts`: margin notes and highlights (`notes`), Check flags (`checks`), `layers`, `tint`, highlight bands.
- `page-versions.ts`: another version's or a draft's labels, numbers and seams (`performance`), tints, taken bars.
- `page-selection.ts`: the menu's stand-in, `SelectionLayer` and `PlaceLayer` (`selection`, `places`), `select`,
  the menu commands, `beats`, `clearLeft`, `selectionControls`, the grid and tuplets; reports `menu`, `handles`.
- `page-colouring.ts`: the pen's colour bars (`colouring`, a second `PlaceLayer`), `gridCell` for the eraser.
- `page-playheads.ts`: the playback model's playheads (`playheads`, `heads`, `grabs`, `press`), the line of before
  the modes, `practice`, `returnToPlayhead`, `heads`/`playheadAway` reports.
- `page-sounding.ts`: `playing` (notes sounding, following), `writing`, the hand-scroll listener.
- `page-card.ts`: focus (`focus`, `card`): the card, its ossias, its playhead and taps; `cardLayout`, `leave`.
- `page-taps.ts`: `mode`, `input`, `pen`; the click on the page (marks, bars, the beat under the modes).
- `page-rings.ts`: `highlight` (an Insights fact's rings). `page-navigation.ts`: `outline`, `showPage`, `showTick`,
  `firstTickOn`, `reveal`, `placeAt`, `lineAt`.

Modules import each other in a loop (layout ↔ marks ↔ selection, playheads ↔ card); none uses an import while it
loads, and the objects with listeners are `let`s made by their `install…`.

## Tests

- The page scenarios `tests/contracts/page-scenarios/score-*.json` (`node tests/pages/run.mjs`, Chromium;
  `PageScenarioTests.swift`, WKWebView); `PencilTests`, `EraserTests` load `Web/score.html` on macOS.
- `score-page-contract.test.mjs`, `score-page-state.test.mjs`; the shared layers' own node tests.

## Rules not to break

- A module creates nothing on the page when it loads: elements, listeners and layers come from its `install…` or
  `listen…`, called by `page.ts` in order. A new one goes into that order where it must run.
- Only `post` speaks to Swift. A new command or message: `page-contract.ts`, `score-page.json`, Swift's `PageCommand`
  or `PageEvent` in one pull request.
- A state field has one writer (its module); others read it or call the owner.
- Nothing here is shared with the roll page (`Web/roll.js`, still JavaScript: `roll.md`).
