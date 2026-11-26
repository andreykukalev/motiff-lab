# UI edge cases decided by pattern

**Status (2026-11-26):** current. New; replaces nothing.

The register of the iPad app's edge cases that no spec, approved mockup or `docs/ux-baseline.md` entry defines and a
developer decided by a common pattern (`governance/roles/developer.md`, step 6; Andrei, 2026-11-26): a tap that misses
or lands beside a target, a tap outside an open menu, a cancelled or interrupted gesture, a repeated tap, an empty or
extreme selection. Agents keep it; specs and mockups are never edited to record such a decision.

- **What it is not.** Not a spec and not approved behaviour: a spec, an approved mockup or a baseline entry always wins
  over an entry here, and an entry that contradicts one is removed. Andrei sees new entries in the version note
  («Подпилено без вас») and can overturn one; one he wants kept for good becomes a `docs/ux-baseline.md` entry through
  the baseline's own process, and its line here says so.
- **Who reads it.** A developer, before deciding a case of its own: the same case already decided here is followed,
  so the app answers it the same way everywhere.
- **An entry.** One line per case, under its area: the id (`E-<area>-<n>`), the case, the decision, the source of the
  pattern (another place in the app, Apple's Human Interface Guidelines, the usual behaviour of score or document apps),
  the pull request. The code that implements it carries a one-line comment naming the id.

## Score and selection

- `E-score-1` · Bars taken from another version or restored (performance.take, .takeAll, .restore) or a version with
  bars taken from it removed, while a passage is selected · the selection keeps its passage over the new notes and a
  variation in progress, made from the old notes, goes; when the notes stay the same (bars restored that were never
  taken, a double tap) nothing changes · the same as after a correction (`SelectionSession.FollowNotes`, «Править ноты»)
  · v1.5 w17-4

## Roll

## Menus, sheets and popovers

## Pen and marks

## Library, import and scan

## Drafts and the assistant
