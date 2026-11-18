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

`PlaybackModel` follows the shared case file above. The rest: not recorded yet.

## The state it owns

The practice visibilities (`PracticeView`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `PLAY-01` to `PLAY-05`. Quality ratchet: `PlaybackEngine.swift` file_length and `handle`
cyclomatic_complexity.
