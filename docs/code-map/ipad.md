# Code map: the iPad app (`ipad/App`)

SwiftUI over the one C# music-core (NativeAOT framework, `ipad/Core`), facade calls through `MusicCore.call(piece:, name, json)`.
The score and the roll are the web's TS modules in a WKWebView (`Web/score.js`, `Scripts/score`, `Scripts/roll`). Strings:
the web's `src/MotiffLab.Web/wwwroot/i18n/*.json` plus the iPad's own `Words/*.json` (`UIText`). Tokens: `Design.swift`
reads `design/kit/tokens.css`. Tests: `MotiffLabTests` (XCTest, run by the `iPad` workflow on macOS, not CI).

## The score's screen

- `ScoreScreen.swift`: `ScoreScreen` (l. 14): `body` (GeometryReader, ignores the keyboard), `layers` (l. ~250: the web view and
  everything floating over it: selection menu at `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill), `wire()`
  (l. ~437: the controllers' callbacks), `ScoreBar` (top bar, More, Ask about the piece), `SelectionMenu` (the menu over
  the selection; its More holds Highlight and Ask), `FocusBar` (Expand's bar). `ScoreScreenViews.swift`, `TopBar.swift`.
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).
- `PracticeView.swift`: the two visibilities (`selectionControlsShown`, `playbackControlsShown`, #582 item 10).
- `Insights.swift`: `InsightsController` (facts, Try ways, `trial`), `InsightsSheet` (the stream; Ask's foot).
- `InkInput.swift`, `InputRules.swift`, `PencilPalette.swift`: who writes and who scrolls, Pencil gestures.
- `PlaceSelection.swift` (v0.6.2; `PlaceSelection.on` since v0.6.3, no switch): the one selection (span, hand, notes picked) and
  `ScoreController.selectNotes`; the page's rule is `Scripts/score/selection-places.ts` (map: `docs/code-map/score.md`).
- The roll in the drawer (`RollController`, `RollDrawer`, `RollTools`, `RollPlace`, `Web/roll.js`): `docs/code-map/roll.md`.

## The assistant

- `AskField.swift`: the chat's interaction model `AskFlow` (open → focus → keyboard → type → hide → go on), `AskField`
  (UITextField, Scribble declined so the Pencil opens the keyboard), `AskInputRow` (field + Send), `KeyboardRoom`.
- Every Ask opens the real assistant, no switch (v0.6.3): selection menu → `AssistantHereCard`, others → the panel.
- `AssistantStates.swift`: `AssistantScope` (core AskScope), `AssistantPhase` (no streaming), `AssistantRecovery`.
- `AssistantController.swift`: panel state, `scope`, `phase`, `standing` (`check`: `/assistant/status`), the piece's
  conversations, `ask` (status → `assistant.context` → turns), `forget` (Try again), `add` («Добавить в контекст»). `AssistantTurns.swift`: one question's
  turns, tool calls run by `assistant.call`. `AssistantChat.swift`: `JSONValue`, the turn format v1, cited facts.
  `AssistantRelay.swift`: `/assistant/status`, `/assistant/turn`. `AssistantStore.swift`: conversations by piece in
  Application Support (never the project file). `AssistantPanel.swift`, `AssistantSide.swift`: the side panel (the score
  narrowed in `ScoreScreen.layers`), the consent, the list, `AssistantPendingView`, `AssistantProblemLine`. `AssistantContextual.swift`: the small chat at the selection
  (`SelectionMenu` turns into it), `KeyboardLift`. `AssistantDrafts.swift` (`experimental.assistantActions`): the drafts
  a question's tools made, Accept by `corrections.acceptDraft` (`ScoreController.acceptAssistantDraft`).
- `Account.swift`: MSAL sign-in, `apiRequest(path)` (the API with the player's token), plan and Pro features.

## Rules not to break

- The score's screen never resizes for the keyboard: a chat keeps itself clear of it (`AskFlow`, `KeyboardRoom`).
- Nothing in `ipad/App` changes analysis or variation data; drafts come from music-core and only the player accepts them.
- Every interface string in English and Russian; colours, sizes and timings are tokens.
