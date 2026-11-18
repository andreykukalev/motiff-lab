# Code map: iPad `Assistant/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Assistant`.

## Files

`AskField`, `AssistantChat`, `AssistantContextual`, `AssistantController`, `AssistantDrafts`, `AssistantPanel`,
`AssistantRelay`, `AssistantSide`, `AssistantStates`, `AssistantStore`, `AssistantTurns`, `ChatFollow`, `Dictation` (`.swift`).

- `AskField.swift`: the chat's interaction model `AskFlow` (open → focus → keyboard → type → hide → go on), `AskField`
  (UITextView: wraps, grows from one line to `AskField.lines` then scrolls, top-leading text and placeholder; Scribble
  declined so the Pencil opens the keyboard; focused only by a press; return is Send), `AskInputRow` (the kit's field
  `DSFieldLook` with the microphone inside, Send under it at the trailing edge), `KeyboardRoom`. The keyboard rule itself: `Platform/TypingFocus.swift`.
- `Dictation.swift` (always on since v0.6.5): `DictationFlow`, `Dictation` (SFSpeech on the device only).
- `AssistantStates.swift`: `AssistantScope` (core AskScope), `AssistantPhase` (no streaming), `AssistantRecovery`.
- `AssistantController.swift`: panel state, `scope` (`choose`; the chips' `pickShowingBars` also scrolls the score to
  the selected bars by `ScoreController.reveal`), `phase`, `standing` (`check`: `/assistant/status`), the piece's
  conversations, `ask` (status → `assistant.context` → turns), `forget` (Try again), `add` («Добавить в контекст»).
- `AssistantTurns.swift`: one question's turns, tool calls run by `assistant.call`. `AssistantChat.swift`: `JSONValue`,
  the turn format v1, cited facts. `AssistantRelay.swift`: `/assistant/status`, `/assistant/turn`.
- `AssistantStore.swift`: conversations by piece in Application Support (never the project file).
- `AssistantPanel.swift`, `AssistantSide.swift`: the side panel, the consent, the list, `AssistantPendingView`,
  `AssistantProblemLine`. `AssistantContextual.swift`: the small chat at the selection (`SelectionMenu` turns into it;
  «Продолжить в ассистенте», `continueHere`, starts a conversation in the panel with its question and answer),
  `KeyboardLift`.
- `ChatFollow.swift`: `ChatFollow`, the one follow rule of both chats (a height change is growth, an end moved at the
  same height the player; scrolled up, nothing pulls back; a new answer from its start or its end, the caller's
  argument: the panel its end, the chat at the selection its start), and `FollowingScroll`. The chat at the selection's
  card drags by its head only (`menuDragHandle`, Selection's `MenuDrag`), so its answer scrolls by finger and Pencil.
- `AssistantDrafts.swift` (the proposer, the only role since v0.6.5): the drafts a question's tools made, Accept by
  `corrections.acceptDraft` (`ScoreController.acceptAssistantDraft`).

## The contract others may rely on

Every Ask opens the real assistant, no switch (v0.6.3): selection menu → `AssistantHereCard`, others → the panel.

## The state it owns

`AssistantController`: panel state, `scope`, `phase`, `standing`, the piece's conversations; `AssistantStore`: the
conversations kept by piece.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: none.
