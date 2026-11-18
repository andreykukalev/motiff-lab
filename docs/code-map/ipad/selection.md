# Code map: iPad `Selection/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Selection`.

## Files

`Phrases`, `PlaceSelection`, `SelectionHistory`, `SelectionModel`, `SelectionTools` (`.swift`).

- `Phrases.swift`: `PhraseLevelsShown` and `PhraseUnit` from `PhrasesAnswer` (`phrases.level` through
  `PieceCore.phraseLevel`, with its selection; the text of `phrases` and `phrases.edit` by `init?(answer:)`), and
  `ScoreSelection`, built from `SelectionAnswer.Selection` only.
- `PlaceSelection.swift` (v0.6.2; Select's only way since v0.6.3): the one selection (span, hand, notes picked) and
  `ScoreController.selectNotes`. The page's rule is `Scripts/score/selection-places.ts` (`../score.md`).
- `SelectionHistory.swift`: the selection's history, the same rules as music-core's `Motifs/SelectionHistory.cs`.
- `SelectionModel.swift` (S1, ADR 0031 section 3): the selection's state and its own rules (the history's steps, the
  toast's and the hold hint's time). Tests: `MotiffLabTests/SelectionModelTests`.

## The contract others may rely on

`SelectionModel`: `record` makes a step of the history (a clear or a replace shows the toast), `undo`, `redo` and
`revert` give the step whose selection the caller selects again, with the toast put away. `ScoreController` (the
composition root) selects through music-core, hands in `selection` and `phrases` as music-core answers them, and then
runs what follows (the page, the play range, Expand); it sends each change of the model on as its own, so a view
observing the controller sees it, and forwards `selection`, `phrases` and `phrasesHidden` read-only. The model's
state is set only through its methods. What stays in the root, and the steps that remove it (S3, S7): [ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the
note of 2026-11-18.

## The state it owns

`SelectionModel`: the selection, its history (`steps`, kept for the piece in `SelectionHistories`), the toast, the
hold hint, the menu's place, the phrases at the level shown and `phrasesHidden` (kept on the device,
`motiff.phrases.hidden`).

## Baseline entries that protect it

UX baseline: `SEL-01` to `SEL-04`. Quality ratchet: none.
