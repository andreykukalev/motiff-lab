# Code map: iPad `Workspace/`, drafts' views

Part of [`workspace.md`](workspace.md). The drafts' models are in [`sources.md`](sources.md).

- Drafts (v1.1): `ScoreDraftPlaces.swift`, a view's marks (`placeMarkers`, `pickMarker`), hearing, `playDraft`,
  `loopPlace`; `ScoreDraftViews.swift`, `DraftTechniqueCard`, `DraftCardAtMark` (every card at a mark, a version marker's
  too: under it, else above it, `top`, by `ScoreController.markHeight`, clear of the bars and the pill, `covered`, from
  `ScoreScreen.barsBottom` and `ToastPlace.bottom`; v1.4 a-1, `closeCardAtMark` on `markAway`), `DraftAssemblyPill`, `DraftStepToast`,
  `ShownDraftScreen` (holds `draftMap` and, v1.4, the visit's `AssistantController`, handed to each `ScoreWorkspace`;
  r-1: `DraftVisitPieces.swift` closes each number it showed, one opened after it went at once); `DraftSeamCard.swift`; `DraftMap.swift`, the map in the sheet's place;
  v1.4: `DraftPlaceCard.swift`, `DraftPlaceContent` (a place's variants, base, «Сделать основой», «Перенести…»,
  «Взять всё уверенное»), one card on the map and as the ▾ menu a tap on a place's label opens (`pickLabel`, F3; none
  while the numbers are hidden), and `DraftMenuLabel` (a menu's label on the draft's cards and map);
  v1.2: `DraftBridgeCard.swift` (a bridge place's candidates, at its label and on the map), `DraftAnchorCard.swift`
  (an anchor's «Открепить», and «Сопоставить…»'s bars in the приём card), `DraftAssistant.swift` (step 8: the map place
  card's «С ассистентом · Pro», `DraftAssistantSection`, `offered`, `named`; v1.4: its roles, a conversation, a bridge
  place's chooser; `DraftProposalCard`, dashed plum, «▶ До» `hearDraft`, «▶ После» `hearProposal`, «Принять» by
  `SourcesController.acceptProposal` while `proposedAgain`);
  `ScoreWorkspaceDrafts.swift`, `draftOwner`, `draftSheet`, `newDraft` (no selection: a hint).
  v1.4 i-3b: `DraftStructureControls.swift`, `DraftPlaceOrder` (the map's «Раньше», «Позже», past a bridge: `target`),
  `DraftRecordingRow` («Найти на записи», searched on a tap) and `DraftLengthRow` (the place's bars, read when opened,
  `PageStepper` of `Pdf/`, held to step on, within `DraftLengthChoice.firstBars`/`lastBars`, v1.5) in `DraftPlaceContent`, `DraftCarryMenu` («Перенести приём…») in `DraftTechniqueCard`; each
  hands its step to its card's `act`/`take`; every draft card opens the view again through `SourcesController.reopening`,
  which holds every step (`stepping`) until it has.
