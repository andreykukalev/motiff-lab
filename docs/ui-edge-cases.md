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

## Roll

## Menus, sheets and popovers

- `E-sheets-1` · A recording with note lengths to check (#352) and no flag open: none at all, or every one resolved · More
  offers Check (as for flags); the drawer's heading is «Длительности под вопросом», not «Все такты проверены» (false over
  marks still shown), and the lengths' own heading is not repeated under it; with no flag at all the line on flagged bars
  is left out too (only where the notes were heard). A mark goes when its note is fixed (any correction makes it the
  player's), or with «Верно» on its bar's flags; a bar with no flag has no «Верно», its entry only «Показать» (lead,
  2026-11-26). With no mark left, «Все такты проверены». · Pattern: a list's heading names what it holds; a text never
  states what did not happen (robustness rule); Check is offered wherever it holds something. · v1.5 c-15b.

## Pen and marks

## Library, import and scan

## Drafts and the assistant
