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
- `ScoreController.swift` (574 lines since v1.6 i-10b): the root's own state (the piece, the pages in view, the playhead
  away, the problem and notice lines, `rendered`, the stored values the parts below read), the web view and its
  `MessageRelay`, `show` and the render order (`render`), what the page reports (`handle`: the selection's events and
  the marks tapped), the notes engraved again (`setPieceTempo`, `notesDidChange`, `engraveScore`) and drawing Check's
  flags (`drawChecks`). The models: [`selection.md`](selection.md), [`playback.md`](playback.md), [`sources.md`](sources.md)
  (`SourcesModel`, through `SourcesScore`, v1.6 i-10), [`ink.md`](ink.md) (`PenModel`, through `PenScore`),
  [`marks.md`](marks.md) (`MarksModel`, through `MarksScore`, v1.6 i-10b; `CheckModel`, through `CheckScore`, v1.6 i-8);
  `FocusModel` through `FocusScore` (v1.6 i-10b).
- v1.6 i-10, the root's parts by concern (each touches only the page and the models it names): `ScoreSelecting.swift`
  (selecting by a span, beats or bars, clearing, the band drawn, the beat lines sent, `showSpan`, `askLabel`,
  `bookmarkTitle`, the selection's Undo and Redo, `selectPassage`); `ScoreMarks.swift` (the root's side of `MarksScore`
  since v1.6 i-10b: Check's drawer, slate, the versions drawn, and `changeMarks`, `applyLayers` for `PenScore` and
  `CheckScore`); `ScoreFocus.swift` (the root's side of `FocusScore` since v1.6 i-10b: the version shown, the page held
  still, `enterFocus` for a bookmark and the resume point); `ScoreInput.swift` (strokes and the
  eraser in the page's pixels, `applyInput`, `penOn`, `penTurned`, the mode sent, the handles on screen, the live style);
  `ScorePageLayout.swift` (room for drawers and the menu, the outline, a page or a tick in view, Insights' rings);
  `ScorePlayheads.swift` (where the player is, the playback position drawn); `ScoreVersions.swift` (the root's side of
  `SourcesScore`: what follows the versions drawn, Check after a version added, a version's bar heard, a marker's way
  to Sources). `BundleSchemeHandler` is Platform's.
- `ScoreScreenViews.swift`: `ScoreController`'s `whenRendered`, `viewPlace`, `showPlace` (where the page is scrolled,
  as a place in the notes), `arrive` and `tickInView` (resume, #327).
- `ScoreUnreadPages.swift` (v1.5 i-7): a scanned PDF's pages no engine read, their marks among the Check flags.
