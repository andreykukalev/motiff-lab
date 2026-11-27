# Code map: iPad `Workspace/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Workspace`. Split in v1.6 i-9: the screen and its
workspace in [`workspace-screen.md`](workspace-screen.md), `ScoreController` and its parts in
[`workspace-controller.md`](workspace-controller.md), drafts' views in [`workspace-drafts.md`](workspace-drafts.md);
here the folder's files and the rules that hold across them.

## Files

Screen ([screen](workspace-screen.md)): `PanelState`, `PieceScreen`, `RecordingScreen`, `RecordingWave`, `ScoreScreen`,
`ScoreScreenBars`, `ScoreScreenPanels`, `ScoreScreenReactions`, `ScoreScreenRoll`, `ScoreScreenSources`, `ScoreBar`,
`Pill`, `PillItems`, `SelectionMenu`, `FocusBar`, `MorePanel`, `ScoreWebView`, `TopBar`, `ScoreWorkspace`,
`ScoreWorkspaceDrafts`, `ScoreWorkspaceFollowing`, `ScoreWorkspaceLife`, `ScoreWorkspaceReactions`,
`ScoreWorkspaceSources`. Controller ([controller](workspace-controller.md)): `ScoreController`, `ScoreColouring`,
`ScoreFocus`, `ScoreInput`, `ScoreMarks`, `ScorePageLayout`, `ScorePhraseEdits`, `ScorePhraseMarks`, `ScorePlayheads`,
`ScorePractice`, `ScoreScreenViews`, `ScoreSelecting`, `ScoreUnreadPages`, `ScoreVersions`. Drafts
([drafts](workspace-drafts.md)): `DraftAnchorCard`, `DraftAssistant`, `DraftBridgeCard`, `DraftMap`, `DraftPlaceCard`,
`DraftSeamCard`, `DraftStructureControls`, `DraftVisitPieces`, `ScoreDraftPlaces`, `ScoreDraftViews` (`.swift`).

## The contract others may rely on

`PanelState`: a panel changes only through `asked` and `following`, both pure; the views read `panels[.x]` and send
intents through `ScoreWorkspace.change` (sheets' and popovers' `isPresented` through `PanelState.binding`).

`ScoreWorkspace`: `ScoreScreen` holds it as a `@StateObject`: made once, as the screen first appears, gone with it.
`ScoreScreenContent` observes each controller itself (`@ObservedObject`), never through the workspace. The callbacks
hold the workspace weakly (`editsKept` only the piece, so the file is written as the screen goes). A method takes what
only the view has (appearance, geometry, `switchView`, `reopen`, texts, the cover for a view) as a parameter.

`ScoreBar`, `Pill` (v1.6 i-9): handed what they show and the actions they call; what an action does is the screen's
(`ScoreScreenBars`) or the workspace's, never theirs, so a new layout of the screen can move them. The pill names no
controller; since v1.6 i-10 neither does the bar: the screen makes Practice and the layers list for it, and the modes get
the mode as a binding and the Pen.

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
2. `checking`: `placeFromMoreOpen` (`.placeFromMore`); `panels[.check]` (`score.checkModel.drawerOpen`, then `.check`);
   `panels[.versions]` (`.versions`, with Expand as it is now); `otherPlaceOpen(focused:)` (`.otherPlace`).
3. `sourcing`: `panels[.versions]` (`insights.leave()`). 4. `following`: `rollPlace` (`roll.place`, then
   `showRollPosition` while it is shown); `playRange` (the player first, then `syncPlayheads`). 5. `body`:
   `assistant.panelOpen` (`.assistantOpened`); `panels.sheetOpen` (`assistant.close()`).

An order in actions: `expandFromDrawer` asks `.expandFromDrawer` before `score.expandRoll()` (the other, «Открыть в
черновике» leaving Expand and opening Sources in one update, left with the merge in v1.1). A reaction
may act twice in a frame at most (SwiftUI drops a third), which is why the rules are not one reaction. A rule or
reaction moved, merged or reordered changes what a player sees for a frame or more: change `panel-states.txt` first.

## The state it owns

`ScoreController`: drawing the marks and Check's flags on the page; `mode` (never nil since v0.6.5);
the models. `ScoreWorkspace`: the controllers and `panels` (PanelState), the one copy of which panels are open and
whether Sources is there (`panels.sourcesOn`); `resumeId`, `handingOver`.
`ScoreScreenContent`: what only the view shows (the drawer's height, Piece DNA, a view shown over the piece).

## Baseline entries that protect it

UX baseline: `VIEW-01` to `VIEW-06` (tests: `PanelBaselineTests`, `WorkspaceScenarioTests`). Quality ratchet:
none: `ScoreScreen.swift`'s two entries went in v1.6 i-9 (split by concern, `layers` in parts), `ScoreController.swift`'s
in v1.6 i-10 (its parts by concern in their files, the versions to `SourcesModel`).
