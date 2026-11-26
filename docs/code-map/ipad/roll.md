# Code map: iPad `Roll/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Roll`.

## Files

`RollController`, `RollDrawer`, `RollPlace`, `RollTools` (`.swift`): the roll in the drawer, with `Web/roll.js`. The
roll itself and the Swift side are mapped in [`../roll.md`](../roll.md).

- `RollController.swift`: the web view, its holders (`ScoreRollView`, `RollHolder`) and what the roll is told and kept
  for a load again (selection, tuplets, highlight, editor, input, passage, lanes, practice, position); the notes it
  loads come typed from `PieceCore.roll` (`RollAnswer`, B7); every call into the page
  goes through its `RollPage` (`Platform/RollPage.swift`) and every message comes in decoded as `RollEvent`s
  (`received` → the callbacks `picked`, `cleared`, `spanPicked`, `notesPicked`, `playheadMoved`, `noteEdited`, …).
  A load that failed and showed nothing is `failed` (`problem`, cleared by the next load that shows notes): `RollSurface`
  (`RollTools.swift`) says so in the roll's place (v1.4 r-1b, `RollLoadTests`).
  `frame()` (the zoom and view for the "Notes" view) is its, since B7. `page` is settable: a workspace scenario
  (`tests/ipad/Baseline/WorkspaceSession.swift`) puts a recording `RollPage` there.
- `RollTools.swift`: `RollPick` (bars or beats from a `RollRange`, by `byBeats`), the zoom's words on `RollZoom`, the
  drawer's height, `RollPlace`, the mode switch, the zoom controls, the note editor's tools.
- `RollPlace.swift`: `RollPickShape`, `RollPlaceInput` (`place`, `stylus` added to `RollInput.page`), `RollPickSwitch`.
- `RollDrawer.swift`: the drawer's head, ending with «Свернуть» (`close`); the pill's roll button opens the drawer.

## The contract others may rely on

- The page's functions and messages are `tests/contracts/roll-page.json` (B7), through `RollPage` and `RollEvent` only:
  no other file builds `roll.<function>(…)` (`tests/shared/roll-page-contract.test.mjs` checks the Swift sources).
- The callbacks and methods `Workspace` wires (`picked`, `byBeats`, `cleared`, `notesPicked`, `spanPicked`, `show`,
  `showSelection`, `bind`, `position`, `frame`, …) keep their names: track W calls them.
- A call is sent as it always was: `Task { await page.… }` where the controller wrote the state, the load's resends
  awaited one after another.

## The state it owns

The mode chosen, the zoom reported, the colouring, the pick shape (`roll.pickShape`), and the last selection, tuplets,
highlight, editor, input and position sent, which a load again (a new web content process, another naming) sends again.

## Baseline entries that protect it

UX baseline: `ROLL-01`. Quality ratchet: the folder edges of `RollController`, `RollDrawer`, `RollPlace`, `RollTools`.
