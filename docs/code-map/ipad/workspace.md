# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`.

## Files

`PanelState`, `ScoreController`, `ScoreScreen`, `ScoreScreenViews`, `ScoreWorkspace`, `ScoreWorkspaceFollowing`,
`ScoreWorkspaceLife`, `ScoreWorkspaceReactions`, `ScoreWorkspaceSources`, `TopBar` (`.swift`).

- `PanelState.swift`: `PanelState`, a pure value: which of the roll's drawer, Insights, Contents, Check, Sources,
  Settings, Reference and More is open, the places More opened, the drawer and the Contents kept for after Expand, what
  Sources keeps under it. `asked(_:)` is what an action writes (an `Intent`), `following(_:)` one rule of the screen (a
  `Rule`: what the change of one value does to the other panels). Tested by
  `tests/ipad/MotiffLabTests/PanelStateTests.swift` against `Golden/panel-states.txt`, the table of start states and
  every action offered in each, render by render.

- `ScoreWorkspace.swift`: `ScoreWorkspace`, a main-actor class (blueprint 5.3 step 2): owns the screen's controllers
  (`player`, `score`, `roll`, `insights`, `assistant`, `contents`, `originals`, `sources`) and `panels`. `wire()` hands
  the controllers their callbacks, each a named method here (`blankTapped`, `rollPicked`, …); `change`, `apply` and
  `backToMore` write `panels`. Tests: `ScoreWorkspaceTests`; sessions: `WorkspaceScenarioTests` (`../ipad.md`).
- `ScoreWorkspaceReactions.swift`: the methods of the view's `.onChange` reactions on the panels and Expand, in the
  view's order (`contentsChanged`, `focusChanged`, …), with `syncPlayheads`, `showRollPosition`, `rollPlace`.
- `ScoreWorkspaceFollowing.swift`: the methods of `following`'s reactions (`selectionChanged`, …), and
  `playRangeChanged`, `applyModel`.
- `ScoreWorkspaceLife.swift`: the screen's life: `appeared` (`wire`, the loads, the resume point, the view kept),
  `disappeared`, `scenePhaseChanged`, `save`, `keepNotesView`, the hand-over (`showPdf`, `handOverForLanguage`).
- `ScoreWorkspaceSources.swift`: `loadOriginals`, `loadSources`, `pickView`, `toPdf`.
- `ScoreScreen.swift`: `ScoreScreen` holds the workspace; `ScoreScreenContent` shows it: `body`, `layers` (the web
  view and all over it), the modifiers that call the workspace (`wired`, `watching`, `checking`, `sourcing`,
  `following`); `ScoreBar`, `SelectionMenu`, `FocusBar`.
- `ScoreScreenViews.swift`: where the page is scrolled to (`viewPlace`, `showPlace`).
- `ScoreController.swift`: the page (the menu's drag, marks, corrections, merge, Expand on the page); the
  models' state: [`selection.md`](selection.md), [`playback.md`](playback.md), [`sources.md`](sources.md).

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

1. `watching`: `panels[.contents]` (`.contents`, then `contents.cancel()`); `score.focused` (`focusChanged`:
   `.expand(true)` first going in, `.expand(false)` last coming out, after the player's hold);
   `score.focused && score.focusRoll` (`focusRollChanged`: `.rollOnCard`, then the roll); `panels[.roll]`
   (`rollDrawerChanged`: `.roll`, then `roll.show` or `cancelNoteEdit`); `panels[.insights]` (`.insights`, then load or
   `clearChoice`).
2. `checking`: `placeFromMoreOpen` (`.placeFromMore`); `panels[.check]` (`score.checkDrawerOpen`, then `.check`);
   `panels[.versions]` (`.versions`, with Expand as it is now); `otherPlaceOpen(focused:)` (`.otherPlace`).
3. `sourcing`: `panels[.versions]` (`insights.leave()`). 4. `following`: `rollPlace` (`roll.place`); `score.playRange`
   (the player first, then `syncPlayheads`). 5. `body`: `assistant.panelOpen` (`.assistantOpened`); `panels.sheetOpen`
   (`assistant.close()`).

Two orders in actions: `expandFromDrawer` asks `.expandFromDrawer` before `score.expandRoll()`;
`ScoreController.openDraftFromFocus` leaves Expand before `openDraft(at:)` asks for Sources, both in one update, so
Sources opening over a drawer or Contents kept for after Expand rests on SwiftUI running the `focused` reaction before
the `versions` one (the declared order; in the other, the drawer comes back and Sources does not open). A reaction
may act twice in a frame at most (SwiftUI drops a third), which is why the rules are not one reaction. A rule or
reaction moved, merged or reordered changes what a player sees for a frame or more: change `panel-states.txt` first.

## The state it owns

`ScoreController`: the page's marks, corrections, `focused`, `sources`; `mode` (never nil since v0.6.5);
the models. `ScoreWorkspace`: the controllers and `panels` (PanelState), the one copy of which panels are open and
whether Sources is there (`panels.sourcesOn`); `resumeId`, `handingOver`.
`ScoreScreenContent`: what only the view shows (the drawer's height, Piece DNA, a view shown over the piece).

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-06` (tests: `PanelBaselineTests`, `WorkspaceScenarioTests`). Quality ratchet:
`ScoreController.swift` file_length and `class ScoreController` type_body_length; `ScoreScreen.swift` file_length and
`layers` function_body_length.
