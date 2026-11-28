# Code map: the score's layers (`src/shared/Scripts/score`)

**Status (2026-11-27):** the web app is archived (ADR 0032); these modules live in `src/shared` because the iPad bundles
them ([0033](../architecture/adr/0033-repository-layout.md)). The pure selection rules and the hold timing are in
`Scripts/selection` ([`selection.md`](selection.md), v1.6 i-15).

The iPad's page (`score/ipad-page`, [`score-page.md`](score-page.md)) builds the layers with its host options and posts
what the player did to Swift. Node tests: `tests/shared/*.test.mjs` (after `tsc`). What protects the page as a whole:
the page scenarios `tests/contracts/page-scenarios/score-*.json` (SEL-01..04, PLAY-05, VIEW-03, …), replayed on
`score.html` in Chromium by CI (`node tests/pages/run.mjs`) and in the app's WKWebView (`PageScenarioTests.swift`); a
change to the engraving options (`score-page.ts`) fails the run's fixture check until the fixtures are remade.

## Geometry

- `page-reader.ts` (W3.13): one engraved page read from Verovio's SVG text in page units (`readPage`: a `PageReader`
  walk, a reader per `g.system`, `g.measure`, `g.staff`, `g.note`), `staffFrames`, the page types and `Rect`. Parser:
  `svg-parse.ts`.
- `score-geometry.ts`: `ScoreGeometry` places the pages read on screen and puts their bars in time (bars, systems,
  notes, `runs`, `band`, `bracket`, `barNear`, `edgeNear`, `playhead`, `tickAt`, `ring`, `barFrames`), `placeInBar`,
  `tickInBar`, `bookmarkLevels`, `BRACKET_LIFT`, `BAND_PADDING`. Tests: `score-geometry`, `playhead-*`, `scrub-notes`.

## Selection

- `selection-layer.ts`: `SelectionLayer` draws the band (`handRuns`), the dot handles and places the menu (`menuPlace`,
  `MenuDragger`); `showPlaced` shows the place rule's gesture. Builds `SelectionHandles` (`selection-handles.ts`, a
  handle's drag and arrow keys). The press of v0.6.1 (`selection-press.ts`), which acted only before Swift's first
  `input`, went in v1.6 r-3 (the lead's decision 33): every press on the notes is the place layer's.
- `selection-place-layer.ts` (v0.6.2 S2): `PlaceLayer` takes the presses while `placesOn()` (`page-selection.ts`:
  `input.places` and Select), shows the gesture through `SelectionLayer.showPlaced`, posts `chosen`, `picked`,
  `cleared`, `zoomSelection`; autoscroll in `view()`; prevents the stylus' touches while on; `handleAt` (only an edge's
  handle beats a playhead's, 0.9: `onEdgeHandle`); a span let go is read as the selection until `settle()`. Its edges and
  overlays (`.sel-edge`, ghost, addition, targets, `.sel-trace`) are drawn by `selection-place-draw.ts` (W3.14).
- `selection-boxes.ts` (`bandBoxes`, `setBox`, `ordered`, `handStaves`/`handRuns`, `onMusic`), `selection-beats.ts`
  (`ScoreBeats`), `selection-menu.ts`, `menu-drag.ts` (the menu keeps below the top the page gives, `--menu-top`: on the
  iPad its strips, v1.7 e-1; else the web's bar, `MENU_TOP`).
- `colour-bars.ts` (v0.10 d-5): the pen's colour bars. `page-colouring.ts` builds a second `PlaceLayer` for them (no
  selection, `edges: false`), so they colour exactly as Select selects; it posts `coloured` {start, end} and never
  changes the selection. The eraser takes the colour from the grid's cells under it (`rubbedColour`, `mergedSpans`).
  Highlights draw cut where they start and end inside a bar (`highlightRuns`, `ScoreBeats.cutEdges`).

## Phrases

- `phrase-marks.ts` (v1.2 i-2): where the phrases' marks stand (`placeMarks`, `liftOver`, `MarkSpot.tap`,
  `commaTarget`), the starts drawn (`markStarts`, `commaOf`) and which phrase plays (`phraseAt`). Drawn by
  `ipad-page/page-phrases.ts`.
- `phrase-drag.ts` (v1.2 i-3a): a mark's press (`MarkPress`: tap, hold → menu, hold + move → drag), `nearestOnset`,
  `neighbourSpans`, `barBeatCaption`. Drawn by `ipad-page/page-phrase-drag.ts`.

## Not reached by the iPad's pages (W0.6, measured 2026-11-16; written down, not cut)

`score-page.ts` is loaded by no page (the A4 layout the node tests and `Engraver.swift` share).

- Members: `PhraseCard`, `createPhraseCard` (the page uses `cardLayout`); `ScoreRules`, `ScorePan` (it uses `modeOf`,
  `beatUnder`); `readMarks`, `writeMarks`, `anchorBar`, `MIN_POINT_DISTANCE`, `TAP_DISTANCE`; `ScoreGeometry.barToPage`,
  `pageOf`; `MenuPin.moving`; `dispose` of `SelectionLayer`, `PlaceLayer`, `ScorePress`, `ScorePlayheadMarks`; `num`;
  `PlayheadPress.owns`; `next` and its steps in `playback-model.ts`; `applyTheme`,
  `applyStoredTheme`, `themeChoice`; `applyLanguage`, `clampSplit`.
- Branches: the dot handles draw in Select with `places()` false, which is in focus: confirm on the device before
  cutting.

The Swift side of the selection: [`ipad/selection.md`](ipad/selection.md).
