# Code map: iPad `Insights/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Insights`.

## Files

`Circle`, `DnaBookmarks`, `DnaCards`, `DnaHarmony`, `DnaLanes`, `DnaModel`, `DnaPlaying`, `DnaScreen`, `Insights`
(`.swift`).

- Piece DNA, one view over six files (split from `Dna.swift` in v0.10 d-1, #723; behaviour unchanged):
  - `DnaModel.swift`: `DnaMap`, the typed `dna` answer (form, ideas and their marks, keys, chords, closes, the
    circles, density, new material, register, dynamics, `barStarts`, `bar(_:)`). Also read by `Marks/Bookmarks.swift`
    (the form's sections for the Contents) and `Workspace/ScoreScreen.swift`, which decodes it and shows the screen.
  - `DnaScreen.swift`: `DnaScreen`, what it is given (the map, the player, `showInScore`, `addBookmarks`, `ask`), its
    view state (what is heard, the open circle, the explained close, the More lanes and the last action's words),
    `body` and the floating bar. The other files are extensions of it.
  - `DnaLanes.swift`: the lanes on the bar axis (`lanes`, `row`, `x`) and the playhead across them; `MarkShape`, `Arc`.
  - `DnaHarmony.swift`: the harmony rows (`chordRow`, `degreeRow`, `functionRow`, `cadenceRow`), their switches
    (kept in `DeviceSettings`), and the card at the corner (`pop`): the circle of fifths of a section or key stretch,
    or what a tapped close is.
  - `DnaCards.swift`: a card per idea (`hearButton`) and the actions under them (Play the map, Add Structure bookmarks,
    Ask about the piece).
  - `DnaPlaying.swift`: `tap`, `hear`, `hearOnce`, `playMap`, `stop`, `leave`, over `Player.setLoop`, `play(from:)`
    and `toggle`; `DnaPlaying`, which button reads Stop (`DnaPlayingTests`).
- `DnaBookmarks.swift`: what Piece DNA's «Добавить закладки «Структура»» did, in words (how many, or that every section
  has one, and where: the contents, Bookmarks › Structure; nothing when the call failed).
- `Insights.swift`: `InsightsController` (facts, Try ways, `trial`), `InsightsSheet` (the stream; Ask's foot).

## The contract others may rely on

Not recorded yet.

## The state it owns

`InsightsController`: the facts, the Try ways and the trial. `DnaScreen`: its own view state only (`@State`, written
by its extensions); the playback state is the `Player`'s, the harmony rows' switches `DeviceSettings`'. The rest: not
recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: `Insights.swift` file_length; none for Piece DNA's size since d-1. Tests:
`FacadeTests.testPieceDnaReadsTheFormKeyHarmonyDensityAndTheIdeasFound` (`DnaMap`), `DnaPlayingTests`,
`DnaBookmarksTests`.
