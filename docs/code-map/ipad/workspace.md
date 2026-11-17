# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`PanelState`, `ScoreController`, `ScoreScreen`, `ScoreScreenViews`, `TopBar` (`.swift`).

- `PanelState.swift`: `PanelState`, a pure value: which of the roll's drawer, Insights, Contents, Check, Sources,
  Settings, Reference and More is open, the way back to More, the drawer and the Contents kept for after Expand, what
  Sources keeps under it. `asked(_:)` is what an action writes (an `Intent`), `following(_:)` one rule of the screen (a
  `Rule`: what the change of one value does to the other panels). Tested by
  `tests/ipad/MotiffLabTests/PanelStateTests.swift` against `Golden/panel-states.txt`, the table of start states and
  every action offered in each, render by render (78 states, 914 lines: every kind of change the reachable states have).

- `ScoreScreen.swift`: `ScoreScreen`: `body` (GeometryReader, ignores the keyboard), `layers` (the web view and
  everything floating over it: selection menu at `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill; the
  score narrowed for the assistant's side panel), `wire()` (the controllers' callbacks), `change` and `apply` (the writers of `panels`: an intent, a rule), `ScoreBar` (top bar, More, Ask
  about the piece), `SelectionMenu` (the menu over the selection; its More holds Highlight and Ask), `FocusBar`
  (Expand's bar).
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).

## The contract others may rely on

`PanelState`: a panel changes only through `asked` and `following`, both pure; the views read `panels[.x]` and send
intents through `ScoreScreen.change` (sheets' and popovers' `isPresented` through `PanelState.binding`). The plan
(blueprint 5.3, B3): `ScoreWorkspace` takes the rest of the wiring out of the view.

### The order the view must keep

A change settles render by render: each rule runs in the `.onChange` reaction that observes its value, and the side
effects beside it (the roll, Insights, the page's inset, the assistant) fire in the render and at the place they always
did. The reactions, in the order `ScoreScreen` declares them, and the order inside each:

1. `watching`: `panels[.contents]` (`.contents`, then `contents.cancel()`); `score.focused` (`expandChanged`:
   `.expand(true)` first going in, `.expand(false)` last coming out, after the player's hold);
   `score.focused && score.focusRoll` (`.rollOnCard`, then the roll); `panels[.roll]` (`.roll`, then `roll.show` or
   `cancelNoteEdit`); `panels[.insights]` (`.insights`, then load or `clearChoice`).
2. `checking`: `placeFromMoreOpen` (`.placeFromMore`); `panels[.check]` (`score.checkDrawerOpen`, then `.check`);
   `panels[.versions]` (`.versions`, with Expand as it is now); `otherPlaceOpen(focused:)` (`.otherPlace`).
3. `sourcing`: `panels[.versions]` (`insights.leave()`). 4. `body`: `assistant.panelOpen` (`.assistantOpened`);
   `panels.sheetOpen` (`assistant.close()`).

Two orders in actions: `expandFromDrawer` asks `.expandFromDrawer` before `score.expandRoll()`;
`ScoreController.openDraftFromFocus` leaves Expand before `openDraft(at:)` asks for Sources, both in one update, so
Sources opening over a drawer or Contents kept for after Expand rests on SwiftUI running the `focused` reaction before
the `versions` one (the declared order; in the other, the drawer comes back and Sources does not open). A reaction may
act twice in a frame at most (SwiftUI drops a third), which is why the rules are not one reaction. A rule or reaction
moved, merged or reordered changes what a player sees for a frame or more: change `panel-states.txt` first.

## The state it owns

`ScoreController`: the page's selection, menu place, marks, corrections, merge and focus; `mode` (never nil since
v0.6.5). `ScoreScreen`: `panels` (PanelState), the one copy of which panels are open, written by `change`, `apply` and the
controllers' callbacks in `wire()` (`ScoreScreen.set`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-04` (the piece's screen and its views). Quality ratchet: `ScoreController.swift`
file_length and `class ScoreController` type_body_length; `ScoreScreen.swift` file_length and `layers`
function_body_length; the jscpd pair with `Roll/RollController.swift`.
