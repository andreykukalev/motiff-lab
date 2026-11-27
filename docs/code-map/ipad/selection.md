# Code map: iPad `Selection/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Selection`.

## Files

`Phrases`, `PlaceSelection`, `SelectionHistory`, `SelectionModel`, `SelectionTools` (`.swift`).

- `Phrases.swift`: `PiecePhrases` with its `PhraseUnit`s (each start's bar, beat, «Почему здесь» and whether it is the
  player's own) and `FormSection`s (the form's letters, `Platform/FormSection.swift`), from `PhrasesAnswer` (the text of `phrases` and `phrases.edit`
  by `init?(answer:)`); `unit(selected:)`, the one rule of a selection that is a phrase exactly (its selectable span),
  `unit(enclosing:)` («Вся фраза»), `change(selected:)` (the edit's `changed` for that phrase), `own`; and
  `ScoreSelection`, built from `SelectionAnswer.Selection` only. Since v1.6 i-18 (#318) also the phrase anchors
  (`anchors`, `unmatched`, `unit(anchor:)`: the phrase an anchor is now), an edit's `step` and a reattach's `reattached`.
- `PlaceSelection.swift` (v0.6.2; Select's only way since v0.6.3): the one selection (span, hand, notes picked) and
  `ScoreController.selectNotes`. The page's rule is `Scripts/selection/selection-places.ts` (`../selection.md`).
- `SelectionHistory.swift`: the selection's Undo and Redo: one step a finished gesture, up to 50 for the piece.
  Also the phrases' edits' (`SelectionHistory<SelectionModel.PhraseStep>`, v1.2 i-3a: the edits and since i-18 the step's
  number); `dropLast` takes a refused step away.
- `SelectionModel.swift` (S1, S3, ADR 0031 section 3): the selection's state and its own rules (the history's steps,
  the toast's and the hold hint's time), selecting and reading the phrases through music-core, and editing them
  (`editPhrases`, `stepPhrases`: c-2's restore/expected with c-14d's "step", `phraseSteps`, `phraseToast`;
  `reattachPhrases`: `phrases.reattach`, its count in the toast, E-insights-4; the selection untouched;
  `phraseOffer`, the card after an edit changed the phrase selected exactly, gone with the next selection). Tests:
  `MotiffLabTests/SelectionModelTests`.

## The page's messages and music-core

- `receivedPlace` (`chosen` with `places` → `chosenByPlace`, `picked`, `zoomSelection`); a refusal puts the band back
  and says `selection.tooLong` / `selection.noNotes` (`softlyRefused`). `ScoreController.select(request:...)` (music-core
  `select`); `selectSpan`, `selectRollSpan`, `RollController.sharePlaces` (span and tuplets between the views).
- `InputRules.page` tells the page who selects; the Pencil's «switch to previous»: `StylusRules.selectTurn`,
  `toolBeforeSelect`. Kept on the device: `SelectionSpanKept` (`ViewStates`, optional fields).
- music-core's `select` with `"picked": true` (the iPad only) takes one note or more; a pick a motif of single notes
  cannot hold (1, or over 32) is saved as no selection is (`Motif.Holds`), so no project file changes shape.

## The contract others may rely on

`menuDragHandle()` (`SelectionTools.swift`, with `MenuDrag`): a card in the selection menu that marks a handle is
dragged by it alone, the rest keeping its own gestures (the assistant's chat at the selection marks its head, so its
answer scrolls); a menu with no handle drags whole.

`SelectionModel`: `record` makes a step of the history (a clear or a replace shows the toast), `undo`, `redo` and
`revert` give the step whose selection the caller selects again, with the toast put away. `select` (music-core's
refusal back, nil once selected) and `loadPhrases` ask music-core and keep its answer. `ScoreController` (the composition root) calls them, then runs what follows (the
page, the play range, Expand), and clears the selection (`answered(nil)`) after leaving Expand. The views that show the selection or the phrases observe
the model themselves (S7b, S7c). The model's
state is set only through its methods. What stays in the root, and the step that removes it (S7): [ADR 0031](../../architecture/adr/0031-ipad-score-modules.md), the
notes of 2026-11-18.

## The state it owns

`SelectionModel`: the selection, its history (`steps`, kept for the piece in `SelectionHistories`), the toast, the
hold hint, the menu's place and the piece's phrases (`phrases`). Whether their marks show is `MarksModel`'s.

## Baseline entries that protect it

UX baseline: `SEL-01` to `SEL-04`. Quality ratchet: none.
