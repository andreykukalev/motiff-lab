# UI edge cases decided by pattern

**Status (2026-11-26, v1.5 d-11):** current. New; replaces nothing.

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

## Pen and marks

## Library, import and scan

## Drafts and the assistant

- **E-drafts-1** · The card «Новый черновик» for a passage selected in notes heard in a recording: that recording is
  not searched for the passage (it is where the passage is), others are; with no other, the card says nothing of a
  recording and offers no «С ассистентом · Pro», never «На записях нет этой музыки.». Pattern: «Найти на записи»
  never offers a place the stretch it is made of («Уже вариант этого места»). v1.5 d-11.
- **E-drafts-2** · On a view shown over the piece (other Notes, a version), which has no switch to show a draft's view,
  the card «Новый черновик» still says where the passage is on the recordings but offers no «С ассистентом · Pro»;
  «Создать» as before. Pattern: a view shown over the piece offers no switch nor Sources (0030). v1.5 d-11.
- **E-drafts-3** · The «В черновик» sheet closed, or another part chosen, while its card searches the recordings: that
  search's result is dropped, and the card searches again for the part chosen; «Создать» does not wait for the search.
  Pattern: «Найти на записи» on a place's card (DRAFT-13), a stale answer never shown. v1.5 d-11.
- **E-drafts-4** · A second tap of «Создать», «Добавить» or «С ассистентом · Pro» while the first is on its way does
  nothing: one draft or fragment, not told as refused. Pattern: one step of a draft at a time (`stepping`, v1.4). v1.5 d-11.
