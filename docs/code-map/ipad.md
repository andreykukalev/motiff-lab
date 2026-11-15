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

## The assistant

- `AskField.swift`: the chat's interaction model `AskFlow` (open → focus → keyboard → type → hide → go on), `AskField`
  (UITextField, Scribble declined so the Pencil opens the keyboard), `AskInputRow` (field + Send), `KeyboardRoom`.
- `Ask.swift`: `AskConversation`, `AskContext`, `AskPopover` (Ask without the model), `AskStreamFoot`.
- `Account.swift`: MSAL sign-in, `apiRequest(path)` (the API with the player's token), plan and Pro features.

## Rules not to break

- The score's screen never resizes for the keyboard: a chat keeps itself clear of it (`AskFlow`, `KeyboardRoom`).
- Nothing in `ipad/App` changes analysis or variation data; drafts come from music-core and only the player accepts them.
- Every interface string in English and Russian; colours, sizes and timings are tokens.
