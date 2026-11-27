# Code map: iPad `Insights/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Insights`.

## Files

`Circle`, `DnaBookmarks`, `DnaCards`, `DnaHarmony`, `DnaLanes`, `DnaModel`, `DnaPlayhead`, `DnaPlaying`, `DnaScreen`,
`FactRow`, `InsightsController`, `InsightsModel`, `InsightsStream` (`.swift`).

- Piece DNA, one view over seven files (split from `Dna.swift` in v0.10 d-1, #723; behaviour unchanged):
  - `DnaModel.swift`: `DnaMap`, the typed `dna` answer (form, ideas and their marks, keys, chords, closes, the
    circles, density, new material, register, dynamics, `barStarts`, `bar(_:)`, `fraction(of:)` and `tick(at:)` along the
    bar axis, `tick(at:)` on the roll's sixteenth grid, `dragGrid`). Also read by `Marks/Bookmarks.swift` (the form's sections for the Contents) and
    `Workspace/ScoreScreenReactions.swift` (`openDna`), which decodes it and shows the screen.
  - `DnaScreen.swift`: `DnaScreen`, what it is given (the map, the player, `showInScore`, `addBookmarks`, `ask`), its
    view state (what is heard, the open circle, the explained close, the More lanes and the last action's words),
    `body` and the header (v0.10 d-2, findings 14, 15: Back, the title, the counts, then by their icons Play or Stop
    from the playhead, Add Structure bookmarks, Ask about the piece; what the last one did under the summary). The
    other files but `DnaPlayhead` are extensions of it.
  - `DnaLanes.swift`: the lanes on the bar axis (`lanes`, `row`, `x`), the playhead over them; `MarkShape`, `Arc`.
  - `DnaPlayhead.swift`: `DnaPlayhead`, the player's position always shown, its handle on top drawn as the roll's
    marker and dragged as the roll's black playhead (`PlayheadGesture` through `Player.handle`, T14–T16: taken where it
    is, moved by the finger's travel, playback stands while held, plays on from where it is let go, let go too when the
    drag is cancelled); VoiceOver puts it down a bar on (`place`).
  - `DnaHarmony.swift`: the harmony rows (`chordRow`, `degreeRow`, `functionRow`, `cadenceRow`), their switches
    (kept in `DeviceSettings`), and the card at the corner (`pop`): the circle of fifths of a section or key stretch,
    or what a tapped close is.
  - `DnaCards.swift`: a card per idea (`hearButton`: Hear, In the score, In Insights).
  - `DnaPlaying.swift`: `tap`, `hear`, `hearOnce`, `playFromPlayhead` (the header's Play, from where the
    player is), `playheadTaken` (a passage the map plays gives way), `stop`, `leave`, over `Player.setLoop` and
    `toggle`; `DnaPlaying`, which button reads Stop (`DnaPlayingTests`).
- `DnaBookmarks.swift`: what Piece DNA's «Добавить закладки «Структура»» did, in words (how many, or that every section
  has one, and where: the contents, Bookmarks › Structure; nothing when the call failed).
- The Insights stream, cut from `Insights.swift` in v1.6 i-12 (W2.12; behaviour unchanged):
  - `InsightsModel.swift`: `Fact` (with `Place`, `Try`) and `Understanding`, the typed `understand` answer (Main,
    the lenses in the stream's order, a lens's facts under Details). Also read by `Roll/RollController.highlight`
    (`Fact.Place`) and the tests (`FacadeTests`, `PencilTests`).
  - `InsightsController.swift`: the stream's state and rules (below), its six music-core calls (`understand`,
    `circle`, `try` then `playback` of `inPlace`, `idea.keep`, `variation.clear`), and the callbacks to the player that
    `ScoreWorkspace.wire()` hands in (`placeChosen`, `variationReady`, `variationLeft`, `scaleReady`, `scaleStop`).
  - `InsightsStream.swift`: the stream (the chips, a step's refusal, Main, Details by lens); `FactRow.swift`: a
    fact's row with `TryRow` and `TrialRow`. The circle in a row is `Circle.swift`'s `KeyCircleCard`.
  - The sheet around it is the screen's: `Workspace/InsightsSheet.swift` (its head, `HearingSwitch`, the assistant's
    foot), beside `Workspace/HearingSwitch.swift`, which Expand's bar shares. `FlowLayout` is in Platform.

## The contract others may rely on

Not recorded yet.

## The state it owns

`InsightsController`: what music-core understood and for which selection, the lens filtered to (`filter`), the lenses
opened under Details (`fold`), Evidence, the fact or place chosen, the open Try rows, the trial and the way being made,
the circle and its row, and whether the scale sounds (`hearingAloneChanged`, the player's, which the workspace follows).
Each is written only by its methods; the views read the rules (`mainShown`, `detailsShown`, `opens`,
`trialFilteredOut`, `shows`, `opensCircle`). Rules not to break: a variation belongs to the passage it was tried on
(`load` leaves it; a way still being made when the selection moves on is left, E-insights-2); one Try and one Keep at a
time (`starting`, E-insights-3); a step's refusal shows over the facts until the next step (E-insights-1). `DnaScreen`: its own view state only (`@State`, written
by its extensions); the playback state is the `Player`'s, the harmony rows' switches `DeviceSettings`'. The rest: not
recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet; the protected harness reads `workspace.insights.chosen` and calls
`factChosen(InsightsController.Choice?)` (`tests/ipad/Baseline/WorkspaceSession.swift`), so both names stay. Quality
ratchet: `InsightsController.swift`'s two Playback edges (the piece with the variation in place for the player); none for
size since i-12 nor Piece DNA's since d-1. Tests: `InsightsControllerTests` (the stream's rules, E-insights-1 to 3),
`FacadeTests.testInsightsListMusicCoresFactsByLens`,
`FacadeTests.testPieceDnaReadsTheFormKeyHarmonyDensityAndTheIdeasFound` (`DnaMap`), `DnaPlayingTests`,
`DnaBookmarksTests`, `DnaPlayheadTests` (`fraction`, `tick(at:)`).
