# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`ScoreController`, `ScoreScreen`, `ScoreScreenViews`, `TopBar` (`.swift`).

- `ScoreScreen.swift`: `ScoreScreen`: `body` (GeometryReader, ignores the keyboard), `layers` (the web view and
  everything floating over it: selection menu at `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill; the
  score narrowed for the assistant's side panel), `wire()` (the controllers' callbacks), `ScoreBar` (top bar, More, Ask
  about the piece), `SelectionMenu` (the menu over the selection; its More holds Highlight and Ask), `FocusBar`
  (Expand's bar).
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).

## The contract others may rely on

Not recorded yet. The plan (blueprint 5.3, B2 and B3): `PanelState` and `ScoreWorkspace` take the wiring out of the view.

## The state it owns

`ScoreController`: the page's selection, menu place, marks, corrections, merge and focus; `mode` (never nil since
v0.6.5). `ScoreScreen`: which panels are open. The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-04` (the piece's screen and its views). Quality ratchet: `ScoreController.swift`
file_length and `class ScoreController` type_body_length; `ScoreScreen.swift` file_length and `layers`
function_body_length; the jscpd pair with `Roll/RollController.swift`.
