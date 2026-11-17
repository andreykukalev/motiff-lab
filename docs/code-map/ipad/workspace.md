# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`PanelState`, `ScoreController`, `ScoreScreen`, `ScoreScreenViews`, `ScoreWorkspace`, `ScoreWorkspaceReactions`, `TopBar`
(`.swift`).

- `PanelState.swift`: `PanelState`, a pure value: which of the roll's drawer, Insights, Contents, Check, Sources,
  Settings, Reference and More is open, the places More opened, the drawer and the Contents kept for after Expand, what
  Sources keeps under it. `asked(_:)` is what an action writes (an `Intent`), `following(_:)` one rule of the screen (a
  `Rule`: what the change of one value does to the other panels). Tested by
  `tests/ipad/MotiffLabTests/PanelStateTests.swift` against `Golden/panel-states.txt`, the table of start states and
  every action offered in each, render by render (78 states, 914 lines: every kind of change the reachable states have).

- `ScoreWorkspace.swift`: `ScoreWorkspace`, a main-actor class (blueprint 5.3 step 2): owns the screen's controllers
  (`player`, `score`, `roll`, `insights`, `assistant`, `contents`, `originals`, `sources`) and `panels`. `wire()` hands
  the controllers their callbacks, each a named method here (`blankTapped`, `notesChanged`, `variationReady`,
  `rollPicked`, `penChanged`, `spacePressed`, …); `change` and `apply` write `panels`. Tested by
  `ScoreWorkspaceTests.swift`.
- `ScoreWorkspaceReactions.swift`: the methods the view's `.onChange` reactions call, in the view's order
  (`contentsChanged`, `focusChanged`, `rollDrawerChanged`, `insightsChanged`, `checkDrawerChanged`, …), and what they
  share: `syncPlayheads`, `showRollPosition`, `rollPlace`. `following`'s and `scenePhase`'s bodies are still in the view.
- `ScoreScreen.swift`: `ScoreScreen` holds the workspace; `ScoreScreenContent` is what it shows: `body`
  (GeometryReader, ignores the keyboard), `layers` (the web view and everything floating over it: selection menu at
  `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill; the score narrowed for the assistant's side panel),
  the `.onChange` reactions (`watching`, `checking`, `sourcing`, `following`); `ScoreBar` (top bar, More, Ask about the
  piece), `SelectionMenu` (the menu over the selection; its More holds Highlight and Ask), `FocusBar` (Expand's bar).
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).

## The contract others may rely on

`PanelState`: a panel changes only through `asked` and `following`, both pure; the views read `panels[.x]` and send
intents through `ScoreWorkspace.change` (sheets' and popovers' `isPresented` through `PanelState.binding`).

`ScoreWorkspace`: `ScoreScreen` holds it as a `@StateObject`, so it and its controllers are made once, as the screen
first appears, and go with the screen. `ScoreScreenContent` observes each controller itself (`@ObservedObject`), so it
is drawn again exactly when the screen was while it owned them; a controller is never observed through the workspace.
The callbacks hold the workspace weakly (`editsKept` holds only the piece, so the file is written even as the screen
goes). A reaction's method takes what only the view knows (appearance, geometry) as a parameter, read at the call.

### The order the view must keep

A change settles render by render: each rule runs in the `.onChange` reaction that observes its value, and the side
effects beside it (the roll, Insights, the page's inset, the assistant) fire in the render and at the place they always
did. The modifiers stay in the view, one line each. Those on the panels, in the order `ScoreScreenContent` declares
them, and the order inside each:

1. `watching`: `panels[.contents]` (`.contents`, then `contents.cancel()`); `score.focused` (`focusChanged`:
   `.expand(true)` first going in, `.expand(false)` last coming out, after the player's hold);
   `score.focused && score.focusRoll` (`focusRollChanged`: `.rollOnCard`, then the roll); `panels[.roll]`
   (`rollDrawerChanged`: `.roll`, then `roll.show` or `cancelNoteEdit`); `panels[.insights]` (`.insights`, then load or
   `clearChoice`).
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
v0.6.5). `ScoreWorkspace`: the controllers and `panels` (PanelState), the one copy of which panels are open and whether
Sources is there (`panels.sourcesOn`). `ScoreScreenContent`: what only the view shows (the drawer's height, Piece DNA,
a view shown over the piece, the resume point's id). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-06` (the piece's screen, its views, its panels; VIEW-05 and VIEW-06 tested in
`tests/ipad/Baseline/PanelBaselineTests.swift`). Quality ratchet: `ScoreController.swift` file_length and
`class ScoreController` type_body_length; `ScoreScreen.swift` file_length and `layers` function_body_length; the jscpd
pair with `Roll/RollController.swift`.
