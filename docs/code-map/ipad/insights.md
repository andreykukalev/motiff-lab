# Code map: iPad `Insights/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Insights`.

## Files

`Circle`, `DnaBookmarks`, `DnaCards`, `DnaHarmony`, `DnaLanes`, `DnaModel`, `DnaPlayhead`, `DnaPlaying`, `DnaScreen`,
`Insights` (`.swift`).

- Piece DNA, one view over seven files (split from `Dna.swift` in v0.10 d-1, #723; behaviour unchanged):
  - `DnaModel.swift`: `DnaMap`, the typed `dna` answer (form, ideas and their marks, keys, chords, closes, the
    circles, density, new material, register, dynamics, `barStarts`, `bar(_:)`, `fraction(of:)` and `tick(at:)` along the
    bar axis, `tick(at:)` on the roll's sixteenth grid, `dragGrid`). Also read by `Marks/Bookmarks.swift` (the form's sections for the Contents) and
    `Workspace/ScoreScreen.swift`, which decodes it and shows the screen.
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
`DnaBookmarksTests`, `DnaPlayheadTests` (`fraction`, `tick(at:)`).
