# Code map: iPad `Playback/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Playback`.

## Files

`Expand`, `FocusModel`, `Metronome`, `Piano`, `PlaybackEngine`, `PlaybackPosition`, `Player`, `PlayheadDrag`, `PositionTransport`,
`Practice`, `PracticeView`, `Tempo` (`.swift`).

- `FocusModel.swift` (S3, ADR 0031 section 3): Expand's state but `focused` (the way back, the card as a roll, its
  options, the version in focus) and the play range, reading `SelectionModel`; `ScoreController` forwards `focusRoll`,
  `playRange` and `focusVersion`. Tests: `MotiffLabTests/FocusModelTests`.
- `PositionTransport.swift`: the pill's one Play; `PlaybackScope`: the one model everywhere (the modes,
  `ScoreController.mode` never nil since v0.6.5), Expand over its passage (`expandPassage`); the page's selection and
  Цикл only out of it (`holdsSelection`). The card's black playhead: `score.js` `drawCardHeads`/`cardGrabs` (v0.6.4 spec 8).
- `PlaybackPosition.swift`: `PlaybackModel`, the Swift port of `Scripts/playback/playback-model.ts`; both run
  `tests/ipad/MotiffLabTests/Golden/playback-model.json`.
- `PracticeView.swift`: the two visibilities (`selectionControlsShown`, `playbackControlsShown`, #582 item 10), two
  switches under the bar's button, nothing over them, kept on the device (`PracticeView.saved`/`keep`, v0.6.4 spec 9).

## The contract others may rely on

`FocusModel`: its state changes only through its methods (`open`, `close`, `showAsRoll`, `takeCardOptions`,
`showVersion`, `updatePlayRange`); the play range is the selection's span, nil without one; `unit` is the unit of the
level shown that is selected, or the one that holds the selection's start.

`PlaybackModel` follows the shared case file above, but for the iPad's own cases (`Golden/playback-model-ipad.json`): a
selection changed while Цикл plays, and `place`, the score's tap on a beat and the position put back from the PDF, which
leaves Цикл on and, outside the selection, makes a lead-in that Play plays from (answers 14 and 16, PLAY-06). The roll's
taps stay `seek` (T12, Цикл off outside the selection). The rest: not recorded yet.

## The state it owns

`FocusModel`: `entry`, `asRoll`, the card's options, `version`, `playRange` and the version's notes to hear; Expand's
`focused` is still `ScoreController`'s ([ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the note of S3).

The practice visibilities (`PracticeView`); the selection, Цикл and the lead-in (`Player`, written only from the model's
decisions, the lead-in also ended in `poll` once the position reaches the selection). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `PLAY-01` to `PLAY-06`. Quality ratchet: `PlaybackEngine.swift` file_length and `handle`
cyclomatic_complexity.
