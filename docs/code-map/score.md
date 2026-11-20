# Code map: the score's selection (`src/shared/Scripts/score`)

**Status (2026-11-16):** the web app is archived (`docs/architecture/adr/0032-ipad-first-web-archived.md`); these modules live in `src/shared` because the iPad bundles them ([0033](../architecture/adr/0033-repository-layout.md)), and iPad work owns them. The web's own callers are frozen in `archive/web`.

The web and the iPad run the same TS modules; the iPad's page (`score/ipad-page`, [`score-page.md`](score-page.md)) builds
the layers with its host options and posts what the player did to Swift. Node tests: `tests/shared/selection-*.test.mjs` (after `tsc`).
What protects the page as a whole: the page scenarios `tests/contracts/page-scenarios/score-*.json` (SEL-01, SEL-02, SEL-03,
SEL-04, PLAY-05, VIEW-03), replayed on `score.html` in Chromium by CI (`node tests/pages/run.mjs`) and in the app's
WKWebView (`PageScenarioTests.swift`); a change to what the page posts or draws there fails them, and a change to the
engraving options (`score-page.ts`) fails the run's fixture check until the fixtures are remade.

## Modules

- `selection-gesture.ts`: v0.6.1's hold rule (`pressGesture`, `heldGesture`, `movedGesture`), `snapEdge`,
  `editedRange`, `extendedRange`, `HOLD_MS`.
- `selection-places.ts` (v0.6.2, the iPad only): the rule of the three places for the score and the roll: `placeAt` →
  `edge-start | edge-end | inside | outside | margin` (`EDGE_REACH` 20), `placePress`, `placeMoved`, `placeEnded`,
  `placeChange`, `drawnRange`, `movedEdge`, `edgeHandles`, `onEdgeHandle`, `strokeHand`, `headAt`, `scrollStep`. Pure.
- `selection-rect.ts` (v0.6.3): the drawn rectangle `traceBox` and what it covers, `rectPassage`. Pure.
- `selection-grid.ts` (v0.6.4): the one grid of the score and the roll, `gridLines` (every sixteenth from each bar's start,
  a tuplet one block: `tupletBlocks` from the page's `g.tuplet`); PlaceLayer's `grid` option. Pure.
- `selection-place-layer.ts` (v0.6.2 S2): `PlaceLayer` takes the presses while `placesOn()` (`page-selection.ts`: `input.places`
  and Select), shows the gesture through `SelectionLayer.showPlaced`, draws `.sel-edge` (full height, circles),
  ghost, addition, targets, `.sel-trace`; posts `chosen`, `picked`, `cleared`, `zoomSelection`; autoscroll in `view()`;
  prevents the stylus' touches while on; `handleAt` (only an edge's handle beats a playhead's, 0.9: `onEdgeHandle`); a span let go is read as the selection
  until `settle()` (the page's `select`). Handles: score.css `.sel-edge` (ds `.handle` at the edge's middle). `decorate()` from `placeMenu`.
- `colour-bars.ts` (v0.10 d-5, finding 31): the pen's colour bars. `page-colouring.ts` builds a second `PlaceLayer` for them
  (`colouring`: no selection, `edges: false`, on while `input.colour` names the colour, the pointer's `input.<pointer>.colour`),
  so they colour exactly as Select selects: the rectangle on the grid, a bar tapped, a note head's beats (`noteUnit`); it
  posts `coloured` {start, end} and never changes the selection; the preview is `.hl.hl-preview`. The eraser takes the
  colour from the grid's cells under it on a highlight's band (`rubbedColour`, `mergedSpans`), handed back by `rubEnd` as
  `uncoloured`. Highlights draw cut where they start and end inside a bar (`highlightRuns`, `ScoreBeats.cutEdges`). Pure.
- `selection-boxes.ts`: `bandBoxes`, `setBox`, `ordered`, `handStaves`/`handRuns` (a hand's staff), `onMusic` (margin).
- `phrase-marks.ts` (v1.2 i-2, the iPad only): where the phrases' marks stand (`placeMarks`: right above the notes, raised
  only over what is printed under them, a close number moved right, targets split halfway; `liftOver`; `MarkSpot.tap`: the
  letter and number take the tap, or in focus a motif's comma, `commaTarget`, v1.2 i-3c), the starts drawn (`markStarts`,
  `commaOf`: phrases, sections, ghosts, motifs) and which phrase plays (`phraseAt`). Pure: `tests/shared/phrase-marks.test.mjs`.
  Drawn by `ipad-page/page-phrases.ts`.
- `phrase-drag.ts` (v1.2 i-3a): a mark's press (`MarkPress`: tap, hold → menu, hold + move → drag), `nearestOnset`,
  `neighbourSpans`, `barBeatCaption`. Pure: `tests/shared/phrase-drag.test.mjs`. Drawn by `ipad-page/page-phrase-drag.ts`.
- `selection-layer.ts` (near 600 lines: add modules): band, dot handles, v0.6.1 gesture, menu place.
  Also `selection-handles.ts`, `selection-beats.ts`, `selection-menu.ts`, `menu-drag.ts`.

## Not reached by the iPad's pages (W0.6, measured 2026-11-16; written down, not cut)

From the score page (then `score.js`) and `roll.js` with the TypeScript language service; the full list is in the v0.7 shared track's note.
`score-page.ts` is loaded by no page (the A4 layout the node tests and `Engraver.swift` share); `svg-parse.ts` is the
parser half of the archived `svg-to-pdf.ts` (`docs/web-archive.md`).

- Members: `PhraseCard`, `createPhraseCard` (the page uses `cardLayout`); `ScoreRules`, `ScorePan` (it uses `modeOf`,
  `beatUnder`); `readMarks`, `writeMarks`, `anchorBar`, `MIN_POINT_DISTANCE`, `TAP_DISTANCE`; `ScoreGeometry.barToPage`,
  `pageOf`; `MenuPin.moving`; `dispose` of `SelectionLayer`, `PlaceLayer`, `ScorePress`, `ScorePlayheadMarks`; `num`;
  `PlayheadPress.owns`; `next` and its steps in `playback-model.ts` (the page reads `viewOf`; `PlaybackPosition.swift`
  runs the rule, both on `playback-model.json`); `PracticeView`, `attach` (the pages read only the two constants);
  `applyTheme`, `applyStoredTheme`, `themeChoice`; `applyLanguage`, `clampSplit`.
- Branches: `SelectionLayer`'s own press (v0.6.1's hold rule: `onPointerDown`, `take`, `startDrag`, `cleared`, `hint`)
  runs only when `places()` is false and `dragStart` is not "none": in Select `places()` is true, in the other tools
  and in focus `dragStart` is "none", so only before Swift's first `input`. Its dot handles draw in Select with
  `places()` false, which is in focus: confirm on the device before cutting.

## Swift side (src/ipad/App/MotiffLab)

- Page messages: `receivedPlace` (`chosen` with `places` → `chosenByPlace`, `picked`, `zoomSelection`); a refusal
  with the switch on puts the band back and says `selection.tooLong` / `selection.noNotes` (`softlyRefused`).
- `ScoreController.select(request:...)` (music-core `select`); `PlaceSelection.swift`: `selectNotes`, `selectSpan`,
  `selectRollSpan`, `RollController.sharePlaces` (span and tuplets between the views).
- The one selection: `ScoreSelection` (`Phrases.swift`) with `hand` (music-core's filter) and `picked` (notes picked);
  `SelectionSpan` (`SelectionHistory.swift`) keeps both and `request` selects it again; `record(..., replaces:)`.
- `InputRules.page` tells the page who selects; `places` is `PlaceSelection.on` (always since v0.6.3). The Pencil's «switch to previous»:
  `StylusRules.selectTurn`, `toolBeforeSelect`. Kept on the device: `SelectionSpanKept` (`ViewStates`, optional fields).

## Rules not to break

- The iPad always passes `places` (PlaceSelection.on, v0.6.3); the web passes none and keeps v0.6.1's ways.
- v0.6.4: no bars/beats choice; Swift selects the span exactly as drawn (`SelectionSpan.request`, no snap); taps unchanged.
- A finger's long press on a bar selects the bar; moved on, it draws the stylus's frame from the press (`placeMoved`: `bar`
  → `draw`, Andrei 2026-11-17), never from the bar's start.
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.
