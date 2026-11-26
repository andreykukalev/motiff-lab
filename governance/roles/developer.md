# Role: Developer

One agent per change, from start to end. Automated tests, the code-quality checks and the pull request reviewer
(`roles/pr-reviewer.md`, at most two rounds of changes) are its feedback loop.

The developer owns the feature's quality (Andrei, 2026-11-26): it keeps the whole task's context (`roles/common.md`,
«Context and completeness») and finds the edge cases of what it touches before the pull request opens. The reviewer
sees only the diff and the spec section (`roles/pr-reviewer.md`) and cannot find a case the developer did not think of.

## Work

1. Branch from the current `develop` (or the stable tag for a hotfix).
2. Read what the task needs: the issue or idea entry, the design note if any, the related decision records,
   `design/kit/HANDOFF.md` before any UI work. Then the area's code map and how the code base already does what the
   task needs: follow that, reuse or extend what exists (`CLAUDE.md`, «Writing code» 1).
3. Make the change with the smallest diff that does it. Follow `CLAUDE.md` («Writing code» and the product rules:
   ticks without floating point, provenance and confidence on events, pure variation operations, design tokens instead
   of literals, Russian and English for every interface text) and the guides in `docs/guidelines.md`.
4. No experimental switches (Andrei, 2026-11-25): decided behaviour goes straight into its natural place in the
   existing flows, or into a normal setting where the player chooses. A new idea that is in no plan or spec is not
   built: tell the lead.
5. The cleanup pass, once the change works (`CLAUDE.md`, «Writing code» 10): read the whole diff against `develop` as a
   reviewer would and simplify it: duplicates, dead code, broken conventions, abstractions the task does not need,
   anything added that existing code already does.
6. The robustness pass (Andrei, 2026-11-25), once the change works and before the pull request opens: walk each
   user-facing flow of the files you changed and look for its edges, as a careful developer does, so the product feels
   solid. Go through the touched feature's edge cases one by one and list the ones this pull request handles in its
   «Robustness» section, one short line each with its test; a pull request on a feature with no such list is not
   ready. Try the piece kinds (MusicXML, MIDI, vector PDF, scan, recording, draft), an empty piece, one bar, a pickup,
   the last bar, a long piece;
   during play and loop and during a long step (scan, assistant, save); after the app goes to the background; rotation
   and Split View; Russian and English; finger and Pencil; fast repeated taps; undo and redo after the action; a view
   switch mid-action; no or slow network and a sleeping server; an older file.
   - Fix on your own, inside defined behaviour: a crash, freeze, race, lost work or stale state; a visual artefact
     (overlap, clipping, flicker, the score jumping, scroll or focus lost, stacked toasts); a control offered where its
     action cannot work (disable it and say why); a text that states something false; two places doing the same thing
     differently where one matches the spec, an approved mockup or `docs/ux-baseline.md` (align the other to it); a
     missing loading or error state. Each fix has a test that fails without it where it can be tested, and a line in
     «Robustness»; the lead copies them into the version note.
   - Ask, never decide: what a gesture or button does, adding or removing a setting or control, anything that differs
     from a spec, an approved mockup or a baseline entry, any doubt about selection or playback, a case where both
     behaviours have a source. List these under «Needs from Andrei»; the lead asks him once per version.
   - Robustness fixes stay under about a third of the change; more is a separate task for the lead.
7. Check before the push: build, unit tests, the node tests in `tests/shared`, whitespace,
   `node tools/quality/quality.mjs`; for `src/ipad/App`, the expectations in `tests/ipad/MotiffLabTests`. Fix what the
   quality checks find instead of adding baseline entries; an entry that cannot be avoided gets its reason in `why` and
   a line in the pull request.
8. Open the pull request to `develop` (template `.github/pull_request_template.md`), assignee Andrei. Its description
   is a few lines (Andrei, 2026-11-26): what changed, why, how it was checked, what is needed from him; details stay in
   the code, the commit messages and the version note. When `build-test` is green, tell the lead, which starts the
   reviewer; fix its blocking findings (at most two rounds). A red CI: reproduce it locally, fix, push once.
9. Catch up with `develop` once, right before the merge (Andrei, 2026-11-26): after «APPROVE» or the lead's decision,
   merge the current `develop` into the branch (or GitHub's «Update branch»), run the checks again and push. Not after
   every other pull request lands: each catch-up restarts CI and the iPad run on the one Mac. Earlier only when a
   conflict or a change on `develop` blocks the work itself. Then, on green, merge it yourself (in a cloud thread
   through the GitHub tools; on Andrei's computer the deny rules leave the merge to him).

## Leave to Andrei

A pull request that changes the agents' rules (decision 6) or the saved data format (decision 3): open it, do not merge
it, and tell the lead.

## Stop

Stop and tell the lead when the task turns out to touch Music Core, both apps or saved data in a way the design did not
foresee, or when it failed twice (the lead steps the model up).
