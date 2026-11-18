# UI review of the iPad app, November 2026 (v0.6.4, spec sections 11 and 13)

Scope: the iPad app's screens around PDF | Notes (`LibraryView`, `PdfScreen`, `ScoreScreen` and their controllers), their
lifecycle, state and resources. Method: the code paths read end to end; music-core's calls of one switch timed in a loop
on a project of 12 MB (a 6-page scanned PDF and Für Elise, `scratch/2026-11-15-v064-freeze/switchbench`); and a UI test
that switches PDF ↔ Notes again and again in the iPad simulator (`tests/ipad/MotiffLabScreens/SwitchUITests.swift`) with a
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
| The kept "Notes" view waits for the sound | `ScoreScreen.wire` awaits `player.load` before `returnToNotesView`, which lands the pages | the notes are drawn ~2.2 s after the press in the simulator and shown at ~5 s (Evidence) | restore the view and land, then give the player its place once the sound is in (track P owns the transport) |
| Every screen makes its own web view and loads the score page again | `ScoreController.init` (score), `RollController` (roll) | a WebContent process and the page's scripts per switch: ~1.5 s of the notes' time in the simulator | keep the score's web view between the screens of one piece |
| The kept view waits 10 s for the render, then gives up | `ScoreController.whenRendered` (100 × 0.1 s), `returnToNotesView` | when the web page is slow (once in 96 switches, Evidence) the pages show from the top, blank until drawn, and the kept place is lost | land on the render's own end (`rendered`), not after a time guess |
| The piano's samples decoded again for every screen | `Player.load` → `Piano.load` per `Player` | the files read and decoded at every switch to the notes | decode once per app (owner of `Player`) |
| `docs/code-map/ipad.md` over its 3 KB | — | — | done: one map per feature folder, `docs/code-map/ipad/` (#659) |

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

`SwitchUITests` in the iPad workflow's simulator (iPad, Debug build, landscape, Russian), 20 rounds each way (8 in the
library of 15 more pieces); a scanned-size PDF of 6 pages inside the piece. Runs by hand on evidence branches that skip the
screenshot walk: baseline 43767626872 (v0.6.3 with the probe), fixes 43767801585, 43773520691 (with the phases) and
43778911858 (the app's own clock). In the first table, times are the test's own wait from the tap to the screen ready.

| Test | Build | Switches | Median | Slowest | Memory, first → last | Controllers alive at the end | Score renders | Old Play drawn |
|---|---|---|---|---|---|---|---|---|
| PDF \| Notes | v0.6.3 | 40 | 5.9 s | 12.0 s | 218 MB | 1 of each | 49 for 21 screens | 1356 times |
| PDF \| Notes | fixed | 40 | 5.7 s | 12.4 s | 215 MB | 1 of each | 21 for 21 | 0 |
| Sources on | v0.6.3 | 40 | 6.5 s | 12.6 s | 197 → 773 MB | score 21, roll 21, PDF 20, player 21 | — | 1548 |
| Sources on | fixed | 40 | 4.7 s | 11.7 s | 191 MB | 1 of each | 21 for 21 | 0 |
| Library of 16 | v0.6.3 | 16 | 7.4 s | 15.3 s | 175 → 429 MB | 9 of each | — | 528 |
| Library of 16 | fixed | 16 | 5.8 s | 11.6 s | 242 MB | 1 of each | 9 for 9 | 0 |

In v0.6.3 the time grew with each round in the library and one tap on More went unanswered; with Sources on every
screen visited stayed alive with its two web views, player and PDF (about 30 MB more per round in the app alone, WebKit's
processes not counted). That growth, with the reading of the whole library on the main thread at every switch, is what
freezes the iPad after some switches; neither shows in the fixed runs, whose memory stays flat (peak 361 MB, back to
~185 MB).

Where a switch spends its time now (run 43773520691, medians, ms from the press by the app's clock; plain / Sources on):

| Phase | To the notes | To the PDF |
|---|---|---|
| saved, opened in music-core (with Verovio's layout), summary read, old piece closed | 526 / 505 | 442 / 466 |
| new screen made, its piece shown | 637 / 625 | — |
| score page loaded in the new web view | 2152 / 2045 | — |
| pages drawn | 2650 / 2381 | — |
| marks, phrases, check, versions done | 3656 / 3201 | — |
| sound loaded (`player.load`) | 4526 / 4179 | — |
| pages shown (the kept view restored) | 5230 / 4753 | PDF laid out 718 / 691 |

The test's wait is ~3 s longer than the app's time for the notes (8.5 s median) and ~1 s for the PDF: each read of the
probe is a query of the whole screen, the score's SVG included. The switch test now holds each switch to 10 s by the
app's own clock, as a freeze guard in the simulator; the iPad itself is not measured here.

By the app's own clock (run 43778911858, medians, slowest in brackets): to the PDF 0.56 s (0.9) plain, 0.42 s (0.8)
with Sources, 1.0 s (1.6) in the library; to the notes 4.0 s, 3.3 s (4.4) and 5.5 s (8.3). A tap on More answered after
every one of the 96 switches; memory 180–285 MB throughout, ~190 MB at the end of each test; one controller of each kind
alive. One switch to the notes took 21 s: its new web view's page loaded after 19.4 s (WebKit's process in the
simulator), the kept view gave up waiting at 10 s and showed the pages from the top before they were drawn
(`whenRendered`, structural risks). The app answered taps meanwhile; it is not the freeze, but it is the web view per
screen at its worst. What is left of the notes'
time is in the structural risks above (the sound before the kept view, a web view per screen, the piano per
screen), none a defect that changes what the player sees, and none in this track's files to change safely now.

music-core's calls of one switch, timed on a PC on the same 12 MB project (`switchbench`, medians): open as notes 40 ms,
notation 4 ms, `project.summary` 98 ms, `project.save` 23 ms, open as PDF 26 ms, `document` 17 ms; no call grows with the
rounds and the managed heap stays flat. music-core is not where the switch's time goes.

The switch tests run only when asked (`SWITCH_TEST`, `TEST_RUNNER_SWITCH_TEST=1` for xcodebuild): the three take about
28 minutes in the workflow's simulator, which with the screenshot walk overruns the job's 60 minutes.
