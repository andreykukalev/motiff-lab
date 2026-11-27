# Code map: iPad `Insights/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Insights`.

## Files

`Circle`, `DnaBookmarks`, `DnaCards`, `DnaHarmony`, `DnaLanes`, `DnaModel`, `DnaPlayhead`, `DnaPlaying`, `DnaScreen`,
`FactRow`, `InsightsController`, `InsightsModel`, `InsightsStream`, `KeyCircleCard` (`.swift`).

- Piece DNA, one view over seven files (split from `Dna.swift` in v0.10 d-1, #723; behaviour unchanged):
  - `DnaModel.swift`: `DnaMap`, the typed `dna` answer (form, ideas and their marks, keys, chords, closes, the
    circles, density, new material, register, dynamics, `barStarts`, `bar(_:)`, `fraction(of:)` and `tick(at:)` along the
    bar axis, `tick(at:)` on the roll's sixteenth grid, `dragGrid`). Also read by `Marks/Bookmarks.swift` (the form's sections for the Contents) and
    `Workspace/ScoreScreenReactions.swift` (`openDna`), which decodes it and shows the screen.
  - `DnaScreen.swift`: `DnaScreen`, what it is given (the map, the player, `showInScore`, `addBookmarks`, `ask`), its
    view state, `body` and the header (v0.10 d-2: Back, the title, the counts, Play or Stop, Add Structure bookmarks,
    Ask about the piece; what the last one did). The other files but `DnaPlayhead` are extensions of it.
  - `DnaLanes.swift`: the lanes on the bar axis (`lanes`, `row`, `x`), the playhead over them; `MarkShape`, `Arc`.
  - `DnaPlayhead.swift`: `DnaPlayhead`, the player's position always shown, its handle on top drawn as the roll's
    marker and dragged as the roll's black playhead (`PlayheadGesture` through `Player.handle`, T14–T16); VoiceOver
    puts it down a bar on (`place`).
  - `DnaHarmony.swift`: the harmony rows (`chordRow`, `degreeRow`, `functionRow`, `cadenceRow`), their switches
    (`DeviceSettings`), and the card at the corner (`pop`): a section's circle of fifths, or a tapped close.
  - `DnaCards.swift`: a card per idea (`hearButton`: Hear, In the score, In Insights).
  - `DnaPlaying.swift`: `tap`, `hear`, `hearOnce`, `playFromPlayhead` (the header's Play, from where the
    player is), `playheadTaken` (a passage the map plays gives way), `stop`, `leave`, over `Player.setLoop` and
    `toggle`; `DnaPlaying`, which button reads Stop (`DnaPlayingTests`).
- `DnaBookmarks.swift`: what Piece DNA's «Добавить закладки «Структура»» did, in words (how many, or that every section
  has one, and where; nothing when the call failed).
- The Insights stream, cut from `Insights.swift` in v1.6 i-12 (W2.12; behaviour unchanged apart from E-insights-1 to 3):
  - `InsightsModel.swift`: `Fact` (with `Place`, `Try`) and `Understanding`, the typed `understand` answer (Main,
    the lenses in the stream's order, a lens's facts under Details); `KeptIdea` (`ideas`, v1.6 i-18) and its `Shown`.
    `Fact.Place` is also read by `Circle.swift`
    (`KeyCircle`, `CircleDrawing`), `Roll/RollController.highlight`, `Workspace/ScoreController` and
    `ScorePageLayout` (the rings); `Understanding` by `FacadeTests` and `PencilTests`.
  - `InsightsController.swift`: the stream's state and rules (below), its music-core calls (`understand`, `circle`,
    `try` then `playback` of `inPlace`, `idea.keep`, `variation.clear`, `ideas`), and the callbacks `ScoreWorkspace.wire()`
    hands in (`placeChosen`, `variationReady`, `variationLeft`, `scaleReady`, `scaleStop`; `ideaKept`: phrases read again).
  - `InsightsStream.swift`: the stream («Границы фраз изменились» with «Привязать заново» while anchors match no phrase,
    the ideas on the selection's phrases, the chips, a step's refusal, Main, Details by lens); `FactRow.swift`: a
    fact's row with `TryRow` and `TrialRow`; `KeyCircleCard.swift`: the circle in a row.
  - The sheet around it is the screen's: `Workspace/InsightsSheet.swift` (its head, `HearingSwitch`, the assistant's
    foot), beside `Workspace/HearingSwitch.swift`, which Expand's bar shares. `FlowLayout` is in Platform.

## The contract others may rely on

Not recorded yet.

## The state it owns

`InsightsController`: what music-core understood and for which selection, the lens filtered to (`filter`), the lenses
opened under Details (`fold`), Evidence, the fact or place chosen, the open Try rows, the trial and the way being made,
the circle and its row, the ideas kept (`ideas`: read by `load`, `loadIdeas` when the sheet sees the anchors change, and
each Keep), and whether the scale sounds (`hearingAloneChanged`, the player's, which the workspace follows).
Each is written only by its methods; the views read the rules (`mainShown`, `detailsShown`, `opens`,
`trialFilteredOut`, `shows`, `opensCircle`, `ideasShown`: an idea on the phrase of its anchor, else by its bars, E-insights-6). Rules not to break: a variation belongs to the passage it was tried on
(`load` and `leave` count a `generation`; a way still being made when it moves on is left, sheet open or not,
E-insights-2); one Try (`starting`) and one Keep (`keeping`, E-insights-3) at a time; a step's refusal shows over the
facts until the next step (E-insights-1). `DnaScreen`: its own view state only (`@State`, written
by its extensions); the playback state is the `Player`'s, the harmony rows' switches `DeviceSettings`'. The rest: not
recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet; the protected harness reads `workspace.insights.chosen` and calls
`factChosen(InsightsController.Choice?)` (`tests/ipad/Baseline/WorkspaceSession.swift`), so both names stay. Quality
ratchet: `InsightsController.swift`'s two Playback edges (the piece with the variation in place for the player); none for
size since i-12 nor Piece DNA's since d-1. Tests: `InsightsControllerTests` (the stream's rules, E-insights-1 to 3), `PhraseAnchorsTests` (i-18),
`FacadeTests.testInsightsListMusicCoresFactsByLens`,
`FacadeTests.testPieceDnaReadsTheFormKeyHarmonyDensityAndTheIdeasFound` (`DnaMap`), `DnaPlayingTests`,
`DnaBookmarksTests`, `DnaPlayheadTests` (`fraction`, `tick(at:)`).
