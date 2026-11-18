# Code map: iPad `Playback/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Playback`.

## Files

`Expand`, `Metronome`, `Piano`, `PlaybackEngine`, `PlaybackPosition`, `Player`, `PlayheadDrag`, `PositionTransport`,
`Practice`, `PracticeView`, `Tempo` (`.swift`).

- `PositionTransport.swift`: the pill's one Play; `PlaybackScope`: the one model everywhere (the modes,
  `ScoreController.mode` never nil since v0.6.5), Expand over its passage (`expandPassage`); the page's selection and
  Цикл only out of it (`holdsSelection`). The card's black playhead: `score.js` `drawCardHeads`/`cardGrabs` (v0.6.4 spec 8).
- `PlaybackPosition.swift`: `PlaybackModel`, the Swift port of `Scripts/playback/playback-model.ts`; both run
  `tests/ipad/MotiffLabTests/Golden/playback-model.json`.
- `PracticeView.swift`: the two visibilities (`selectionControlsShown`, `playbackControlsShown`, #582 item 10), two
  switches under the bar's button, nothing over them, kept on the device (`PracticeView.saved`/`keep`, v0.6.4 spec 9).

## The contract others may rely on

`PlaybackModel` follows the shared case file above, but for the iPad's own cases (`Golden/playback-model-ipad.json`): a
selection changed while Цикл plays, and `place`, the score's tap on a beat and the position put back from the PDF, which
leaves Цикл on and, outside the selection, makes a lead-in that Play plays from (answers 14 and 16, PLAY-06). The roll's
taps stay `seek` (T12, Цикл off outside the selection). The rest: not recorded yet.

## The state it owns

The practice visibilities (`PracticeView`); the selection, Цикл and the lead-in (`Player`, written only from the model's
decisions, the lead-in also ended in `poll` once the position reaches the selection). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `PLAY-01` to `PLAY-06`. Quality ratchet: `PlaybackEngine.swift` file_length and `handle`
cyclomatic_complexity.
