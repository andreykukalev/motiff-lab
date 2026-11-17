# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`PanelState`, `ScoreController`, `ScoreScreen`, `ScoreScreenViews`, `TopBar` (`.swift`).

- `PanelState.swift`: `PanelState`, a pure value: which of the roll's drawer, Insights, Contents, Check, Sources,
  Settings, Reference and More is open, the way back to More, the drawer and the Contents kept for after Expand, what
  Sources keeps under it. `after(_:expand:)` applies an `Intent` and then the screen's rules (the old `.onChange`
  reactions, in their order, round by round). Tested as tables in `tests/ipad/MotiffLabTests/PanelStateTests.swift`.

- `ScoreScreen.swift`: `ScoreScreen`: `body` (GeometryReader, ignores the keyboard), `layers` (the web view and
  everything floating over it: selection menu at `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill; the
  score narrowed for the assistant's side panel), `wire()` (the controllers' callbacks), `change` (the one writer of `panels`), `ScoreBar` (top bar, More, Ask
  about the piece), `SelectionMenu` (the menu over the selection; its More holds Highlight and Ask), `FocusBar`
  (Expand's bar).
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).

## The contract others may rely on

`PanelState`: a panel opens, closes or follows Expand only through `after`, which is pure and Equatable; the views read
`panels[.x]` and send intents through `ScoreScreen.change` (sheets' and popovers' `isPresented` through
`PanelState.binding`). The plan (blueprint 5.3, B3): `ScoreWorkspace` takes the rest of the wiring out of the view.

## The state it owns

`ScoreController`: the page's selection, menu place, marks, corrections, merge and focus; `mode` (never nil since
v0.6.5). `ScoreScreen`: `panels` (PanelState), the one copy of which panels are open; reactions with other work
(`roll.show`, `cancelNoteEdit`, `insights.load`, `checkDrawerOpen`, the assistant's panel) observe values of it. The rest:
not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-04` (the piece's screen and its views). Quality ratchet: `ScoreController.swift`
file_length and `class ScoreController` type_body_length; `ScoreScreen.swift` file_length and `layers`
function_body_length; the jscpd pair with `Roll/RollController.swift`.
