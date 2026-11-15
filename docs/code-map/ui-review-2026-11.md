# UI review of the iPad app, November 2026 (v0.6.4, spec sections 11 and 13)

Scope: the iPad app's screens around PDF | Notes (`LibraryView`, `PdfScreen`, `ScoreScreen` and their controllers), their
lifecycle, state and resources. Method: the code paths read end to end; music-core's calls of one switch timed in a loop
on a project of 12 MB (a 6-page scanned PDF and Für Elise, `scratch/2026-11-15-v064-freeze/switchbench`); and a UI test
that switches PDF ↔ Notes again and again in the iPad simulator (`MotiffLabScreens/SwitchUITests.swift`) with a
Debug-only probe in the app (`ScreensHook.swift`: the screen ready, the controllers alive, the score's renders,
music-core's longest call, the app's memory). Numbers: see "Evidence".

## Actual defects (wrong behaviour now), fixed in v0.6.4

1. **The old Play flashed on every switch to the notes.** `ScoreController.mode` was nil until `ScoreScreen.wire()` had
   awaited the bookmarks and the sound (`contents.load`, `player.load`); only then `applyExperiments()` set Navigate. Until
   then `onePosition` was false and the screen drew the model without the modes: the bar's Play (`barPlay`), the Pen
   button in place of the modes, no mode options, the pill's old Play in place of `PositionTransport`, and taps and one
   finger read by the rules without modes. Fix: the mode comes from Settings when the controller is made
   (`ScoreController(mode:)` in `ScoreScreen.init`), before the first frame. `applyExperiments()` still follows Settings.
2. **The view switch changed under the bar as each screen opened** (experiment Sources on). The PDF screen showed
   PDF | Notes until it had read the sources, then the switch of all views with Sources; the score showed no switch at all
   (`SourcesViewSwitch` draws nothing for empty layers) and then the switch. Fix: the screen handing over gives its layers
   to the next screen of the same file (`SourcesHandover.give/layers/drop`), which draws them from its first frame and
   drops them once it has read its own.
3. **Every switch read every project of the library on the main thread.** `LibraryView.show()` ran `reload()`: for each
   project, `Data(contentsOf:)`, the whole file JSON-encoded into a request on the main thread, then `project.summary`
   in music-core (98 ms median on a 12 MB project on a PC; slower on an iPad). The switch waited for all of them, the
   button was ignored meanwhile (`opening`), and the screen left on show had its piece closed already (below), so taps on
   it did nothing. The time grows with the library: this is the freeze. Fix: a switch (any open with a landing) reads only
   the file it opens; the library reads them all when it shows again (`onChange(of: piece == nil)`, as before).
4. **The screen giving way had its piece closed while it was still on screen.** `ClosedPieces.close` ran right after the
   new piece opened, before the summaries were read (item 3). Fix: closed only just before the new screen takes its place.
5. **Each screen visited with Sources on stayed in memory.** `sources.reopen = { await reopenHere() }` captured the
   screen (a struct holding its `@StateObject`s), so `SourcesController` held the screen's controllers: two web views
   (score and roll, each a WebKit process), the player and its audio engine; on the PDF screen the `PdfController` with the
   PDF. Fix: `onDisappear` clears `sources.reopen` on both screens.
6. **The score was rendered again and again as it opened.** `ScoreController.show()` is called on every update of the
   web view and started a new `render()` until the first had set `shown`, so the pages (all the SVG) were sent to the page
   and laid out several times, each render also asking music-core for marks, phrases, check, versions. Fix: one render at a
   time (`scheduleRender`), another only when the piece or the appearance changed meanwhile.

## Structural risks (not causing a wrong behaviour now; proposed later)

| Finding | Where | Why it matters | Proposal |
|---|---|---|---|
| Two models in one screen: the modes (v0.5) and the score without them, chosen at run time by `experimental.playheads` (off by default) | `ScoreScreen` (`barPlay`, the Pen button, `Pill`), `onePosition`, `InputRules`, `placeTapped` | every control has two paths; the old one is what flashed | Andrei decides whether the modes become permanent; then delete `barPlay`, the bar's Pen button, the pill's old Play and the `mode == nil` branches |
| `PlaceSelection.on` is a constant `true` | `PlaceSelection`, `InputRules(places:)`, `RollDrawer.pickExperiment`, the 0.6.1 span-only kept view | the `false` branches are dead code | remove with track S (selection), which owns those files |
| `onePosition` false in Expand: the old transport lives on for focus | `PositionTransport.swift:79`, `Pill` | the old Play path stays alive | track P (spec section 8) |
| music-core calls are not ordered: each `MusicCore.run` starts a thread that waits on one `NSLock`, which is not fair | `MusicCore.swift` | two calls started in order may run in either order; one long call (Verovio's whole layout runs inside the lock in `OpenPiece.open`) holds all others | one long-lived core thread with a FIFO queue; Verovio's layout outside the core's lock |
| A piece left by Back is never closed in music-core | `LibraryView` (only `show()` closes the piece it replaces) | every piece opened from the library stays in the facade's memory, its PDF bytes included, until the app quits | close it once its screen's last save has run (needs the ordered queue above) |
| The PDF's JSON parsed on the main thread | `PdfController.load` (`document` answer with the PDF as base64), `facadeObject` | ~10 MB JSON and base64 decode on the main thread at every switch to the PDF | parse in the core's task, hand back `Data` |
| Three guards for one switch | `PdfScreen.handingOver`, `ScoreScreen.handingOver`, `LibraryView.opening` and the `piece?.id == current.id` check | each fixed one double open (#582 item 2); none says the whole order | one switch owner in the library: save, open, close, swap, as one function |
| Sources read twice per screen in two models | `ScoreController.sources` (`SourcesInfo`, call `sources`) and `SourcesController.layers` (`sources.layers`) | two calls and two states for the same file | `SourcesInfo` from `sources.layers` once the experiment is permanent |
| Views shown over the score closed 2 s after they go | `ScoreScreen.shownViewScreen` `onDisappear` | a time guess instead of an order | close after the screen's own last call, with the ordered queue |
| A hover recogniser added to the window per score | `PencilPalette.watch` | moved, not added, as long as one controller lives; leaked controllers left theirs | none now that the screens are freed (item 5 above) |
| `docs/code-map/ipad.md` over its 3 KB | — | — | split the map by area when it next grows |

## Lifecycle and ownership: PDF | Notes now

1. The screen shown saves its piece (`ProjectFiles.save`), keeps its view (`ViewStates`), hands its sources' layers over,
   and asks the library (`switchView`); a second press meanwhile does nothing (`handingOver`, `opening`).
2. `LibraryView.openFile` → `show`: the file is opened again in music-core as the other view (`OpenPiece.open`, Verovio
   for the notes), only that file's summary is read again, the old piece is closed (`ClosedPieces.close`), and the
   navigation item becomes the new piece; `.id(piece.id)` makes the new screen.
3. The old screen's `onDisappear`: it clears `sources.reopen`, finds its piece closed (`ClosedPieces.take`) and writes
   nothing more; its controllers are freed with it.
4. The new screen draws its final model from its first frame (mode from Settings, the handed layers), then reads its
   sources and, for the notes, the bookmarks and the sound, the resume point and the kept view; its pages stay out of
   sight until they stand where they land (#533 6), as before.

## First frames compared with the settled screen

| Element | Before | Now |
|---|---|---|
| Bar's Play, Pen button, pill's old Play, mode options | the model without modes until the sound loaded | the settled model from the first frame |
| View switch (Sources on) | PDF: PDF \| Notes, then the switch of all views; score: no switch, then it | the switch of all views from the first frame |
| Pages | out of sight until landed (intended, #533 6) | unchanged |
| Roll drawer of a kept view | opens once the pages are drawn, while they are still out of sight | unchanged (intended: it opens before the pages show) |
| Selection and its menu of a kept view | come back once the pages are drawn, before they show | unchanged |
| PDF pages | a progress spinner until the PDF is read | unchanged |

## Evidence

To be filled from the iPad workflow runs (baseline on the code of v0.6.3, then with the fixes).
