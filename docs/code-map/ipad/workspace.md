# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`DraftAnchorCard`, `DraftAssistant`, `DraftBridgeCard`, `DraftMap`, `DraftPlaceCard`, `DraftSeamCard`, `PanelState`, `PieceScreen`, `RecordingScreen`, `RecordingWave`, `ScoreColouring`,
`ScoreController`, `ScoreDraftPlaces`, `ScoreDraftViews`, `ScorePhraseEdits`, `ScorePhraseMarks`, `ScorePractice`, `ScoreScreen`, `ScoreScreenViews`, `ScoreWorkspace`, `ScoreWorkspaceDrafts`, `ScoreWorkspaceFollowing`, `ScoreWorkspaceLife`, `ScoreWorkspaceReactions`, `ScoreWorkspaceSources`,
`TopBar` (`.swift`).

- `PanelState.swift`: `PanelState`, a pure value: which panel is open (roll, Insights, Contents, Check, Sources,
  Settings, Reference, More), what More opened, what Expand and Sources keep. `asked(_:)` is what an action writes (an
  `Intent`), `following(_:)` one rule (a `Rule`). Tested by `PanelStateTests` against `Golden/panel-states.txt`.

- `PieceScreen.swift`: the screen the library opens, by what the piece is: `PdfScreen`, `RecordingScreen`, else
  `ScoreScreen`; `reopen` opens the piece's own project file again in its place, `openFile` another (v1.2, collage).
- `RecordingScreen.swift` (v1.1, finding 29): a piece that is only its recording: the score's bar (Back, Contents with a
  line that there are none yet, the title, Sources), the waveform across the screen (or why not: the sound is not here,
  or here and unreadable), `RecordingViewCapsule` under it;
  nothing docked. «Распознать · Pro» in Sources makes its working notes and the piece opens again as notes.
- `RecordingWave.swift`: the levels of every 100 ms (`RecordingSound.levels(of:)`) as columns, the play cursor, a tap
  seeking there (`moment`, `share`); one adjustable VoiceOver element, ±5 s. Tests: `RecordingWaveTests`.
- `ScoreWorkspace.swift`: `ScoreWorkspace`, a main-actor class (blueprint 5.3 step 2): owns the screen's controllers
  (`player`, `score`, `roll`, `insights`, `assistant`, `contents`, `originals`, `sources`) and `panels`. `wire()` hands
  the controllers their callbacks, each a named method here (`blankTapped`, `rollPicked`, …); `change`, `apply` and
  `backToMore` write `panels`; `assistantPressed` is the pill's assistant (its last conversation, or closed; an Ask
  opens it empty). Tests: `ScoreWorkspaceTests`, `DraftsTests`; sessions:
  `WorkspaceScenarioTests` (`../ipad.md`).
- `ScoreWorkspaceReactions.swift`: the methods of the view's `.onChange` reactions on the panels and Expand, in the
  view's order (`contentsChanged`, `focusChanged`, …), with `syncPlayheads`, `showRollPosition`, `rollPlace`.
- `ScoreWorkspaceFollowing.swift`: the methods of `following`'s reactions (`selectionChanged`, …), and
  `playRangeChanged`, `applyModel`.
- `ScoreWorkspaceLife.swift`: the screen's life: `appeared` (`wire`, the loads, the resume point, the view kept),
  `disappeared`, `scenePhaseChanged`, `save`, `keepNotesView`, the hand-over (`showPdf`, `handOverForLanguage`, and v1.2
  `openElsewhere`: another piece's file in its place through `ScoreScreen.openFile`, the library's `openFile`, for the
  collage toast's «Открыть»; the draft's view handed by `SourcesHandover.show`).
- `ScoreWorkspaceSources.swift`: `loadOriginals`, `loadSources`, `pickView` (a draft by `showDraft`, v1.1), `toPdf`.
- Drafts (v1.1): `ScoreDraftPlaces.swift`, a view's marks (`placeMarkers`, `pickMarker`), hearing, `playDraft`,
  `loopPlace`; `ScoreDraftViews.swift`, `DraftTechniqueCard`, `DraftCardAtMark`, `DraftAssemblyPill`, `DraftStepToast`,
  `ShownDraftScreen` (holds `draftMap`); `DraftSeamCard.swift`; `DraftMap.swift`, the map in the sheet's place;
  v1.4: `DraftPlaceCard.swift`, `DraftPlaceContent` (a place's variants, base, «Сделать основой», «Перенести…»,
  «Взять всё уверенное»), one card on the map and as the ▾ menu a tap on a place's label opens (`pickLabel`, F3; none
  while the numbers are hidden), and `DraftMenuLabel` (a menu's label on the draft's cards and map);
  v1.2: `DraftBridgeCard.swift` (a bridge place's candidates, at its label and on the map), `DraftAnchorCard.swift`
  (an anchor's «Открепить», and «Сопоставить…»'s bars in the приём card), `DraftAssistant.swift` (step 8: the map place
  card's «С ассистентом · Pro», `DraftAssistantSection`; `DraftProposalCard`, dashed plum, «▶ До» `hearDraft`, «▶ После»
  `hearProposal`, «Принять» by `SourcesController.acceptProposal`);
  `ScoreWorkspaceDrafts.swift`, `draftOwner`, `draftSheet`, `newDraft` (no selection: a hint).
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
- `ScoreScreen.swift`: `ScoreScreen` holds the workspace; `ScoreScreenContent` shows it: `body`, `layers` (the web
  view and all over it), the modifiers that call the workspace (`wired`, `watching`, `checking`, `sourcing`,
  `following`); `ScoreBar`, `Pill` (the transport, then the roll and the assistant), `SelectionMenu` (with the pill,
  `iconItem`), `FocusBar`.
- `ScoreScreenViews.swift`: `viewPlace`, `showPlace`.
- `ScoreColouring.swift` (v0.10 d-5, finding 31): `ScoreController`'s part of the pen's colour bars (`PenScore`'s
  `colouringChanged`, `colourBars`, `uncolourBars`, `recolour`): the change `Marks/HighlightPaint` works out, sent to
  music-core as `highlight.remove` (ids found by value) and `highlight.add`, drawn at its last call and answered to the
  pen for its history; `recolour` sends a step's change again for the Pen's Undo and Redo (v1.4 i-5).
- `ScoreController.swift`: the page (the menu's drag, marks, corrections, versions, Expand on the page); the
  models' state: [`selection.md`](selection.md), [`playback.md`](playback.md), [`sources.md`](sources.md),
  [`ink.md`](ink.md) (`PenModel`, through `PenScore`), [`marks.md`](marks.md) (`MarksModel`).

## The contract others may rely on

`PanelState`: a panel changes only through `asked` and `following`, both pure; the views read `panels[.x]` and send
intents through `ScoreWorkspace.change` (sheets' and popovers' `isPresented` through `PanelState.binding`).

`ScoreWorkspace`: `ScoreScreen` holds it as a `@StateObject`: made once, as the screen first appears, gone with it.
`ScoreScreenContent` observes each controller itself (`@ObservedObject`), never through the workspace. The callbacks
hold the workspace weakly (`editsKept` only the piece, so the file is written as the screen goes). A method takes what
only the view has (appearance, geometry, `switchView`, `reopen`, texts, the cover for a view) as a parameter.

### The order the view must keep

A change settles render by render: each rule runs in the `.onChange` reaction that observes its value, and the side
effects beside it (the roll, Insights, the page's inset, the assistant) fire in the render and at the place they always
did. The modifiers stay in the view. Those on the panels, in the order `ScoreScreenContent` declares
them, and the order inside each:

1. `watching`: `panels[.contents]` (`.contents`, then `contents.cancel()`); `focusModel.focused` (`focusChanged`:
   `.expand(true)` first going in, `.expand(false)` last coming out, after the player's hold);
   `focused && asRoll` (`focusRollChanged`: `.rollOnCard`, then the roll); `panels[.roll]`
   (`rollDrawerChanged`: `.roll`, then `roll.show` or `cancelNoteEdit`); `panels[.insights]` (`.insights`, then load or
   `clearChoice`).
2. `checking`: `placeFromMoreOpen` (`.placeFromMore`); `panels[.check]` (`score.checkDrawerOpen`, then `.check`);
   `panels[.versions]` (`.versions`, with Expand as it is now); `otherPlaceOpen(focused:)` (`.otherPlace`).
3. `sourcing`: `panels[.versions]` (`insights.leave()`). 4. `following`: `rollPlace` (`roll.place`); `playRange`
   (the player first, then `syncPlayheads`). 5. `body`: `assistant.panelOpen` (`.assistantOpened`); `panels.sheetOpen`
   (`assistant.close()`).

An order in actions: `expandFromDrawer` asks `.expandFromDrawer` before `score.expandRoll()` (the other, «Открыть в
черновике» leaving Expand and opening Sources in one update, left with the merge in v1.1). A reaction
may act twice in a frame at most (SwiftUI drops a third), which is why the rules are not one reaction. A rule or
reaction moved, merged or reordered changes what a player sees for a frame or more: change `panel-states.txt` first.

## The state it owns

`ScoreController`: drawing the marks on the page, corrections; `mode` (never nil since v0.6.5);
the models. `ScoreWorkspace`: the controllers and `panels` (PanelState), the one copy of which panels are open and
whether Sources is there (`panels.sourcesOn`); `resumeId`, `handingOver`.
`ScoreScreenContent`: what only the view shows (the drawer's height, Piece DNA, a view shown over the piece).

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-06` (tests: `PanelBaselineTests`, `WorkspaceScenarioTests`). Quality ratchet:
`ScoreController.swift` file_length and `class ScoreController` type_body_length; `ScoreScreen.swift` file_length and
`layers` function_body_length.
