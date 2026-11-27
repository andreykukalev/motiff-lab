# Code map: iPad `Workspace/`, the score's controller

Part of [`workspace.md`](workspace.md). `ScoreController` is the composition root over the models (ADR 0031); its
parts by concern are extensions in their own files.

- `ScorePhraseMarks.swift` (v1.2 i-2, the phrases spec): `ScoreController`'s part of the phrases' marks: `phraseMarks`
  (what the page's `phraseMarks` draws, with the labels VoiceOver reads; none on a view only shown,
  `OpenPiece.shownOnly`: a draft's view, other Notes), `pickPhrase` and `pickSection` (a tap on a number
  or a letter: the phrase's or the section's selectable span as a new selection, a replace with «Вернуть»), `pickMotif`
  (a motif's comma on the card in focus, v1.2 i-3c: its span the same way, which Expand loops).
- `ScorePhraseEdits.swift` (v1.2 i-3a): a mark held (`phraseHeld` → `phraseMenu`, `PhraseMarkMenu` in `DraftCardAtMark`)
  or dragged (`phraseMoved` → `phrases.edit` move), «Убрать границу», «Вернуть найденную», the toast (`PhraseEditToast`,
  the score's `UndoToast`) with Undo/Redo through `SelectionModel`; `setBarBeats` (the drag's caption). v1.2 i-3b:
  «Выделить фразу», «Начать фразу здесь» (`startPhrase`, the menu's More), the reset (`resetPhrases`, the ⓘ list),
  «Вся фраза» (`selectWholePhrase`), `PhraseOfferCard` in the menu («Выделить» = `takePhraseOffer`, a selection step).
- `ScorePractice.swift` (v1.2 i-4): `playSelection`, the selection just made played looped (Цикл on) or once, which
  `loopPlace` and the assistant's practice action use; `practiseSuggestion` (the chip of `practice.suggest`: bars, tempo, hand).
- `ScoreColouring.swift` (v0.10 d-5, finding 31): `ScoreController`'s part of the pen's colour bars (`PenScore`'s
  `colouringChanged`, `colourBars`, `uncolourBars`, `recolour`): the change `Marks/HighlightPaint` works out, sent to
  music-core as `highlight.remove` (ids found by value) and `highlight.add`, drawn at its last call and answered to the
  pen for its history; `recolour` sends a step's change again for the Pen's Undo and Redo (v1.4 i-5).
- `ScoreController.swift`: the page (the menu's drag, marks, corrections, versions, Expand on the page); the
  models' state: [`selection.md`](selection.md), [`playback.md`](playback.md), [`sources.md`](sources.md),
  [`ink.md`](ink.md) (`PenModel`, through `PenScore`), [`marks.md`](marks.md) (`MarksModel`; `CheckModel`, through
  `CheckScore`, v1.6 i-8: the root keeps drawing the flags, `drawChecks`, and `showProblem`).
- `ScoreScreenViews.swift`: `ScoreController`'s `whenRendered`, `viewPlace`, `showPlace` (where the page is scrolled,
  as a place in the notes), and `PdfController.keepView`.
- `ScoreUnreadPages.swift` (v1.5 i-7): a scanned PDF's pages no engine read, their marks among the Check flags.
