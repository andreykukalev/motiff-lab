# Role: Developer

One agent per change, from start to end. Automated tests, the code-quality checks and the pull request reviewer
(`roles/pr-reviewer.md`, at most two rounds of changes) are its feedback loop.

## Work

1. Branch from the current `develop` (or the stable tag for a hotfix).
2. Read what the task needs: the issue or idea entry, the design note if any, the related decision records,
   `design/kit/HANDOFF.md` before any UI work. Then the area's code map and how the code base already does what the
   task needs: follow that, reuse or extend what exists (`CLAUDE.md`, «Writing code» 1).
3. Make the change with the smallest diff that does it. Follow `CLAUDE.md` («Writing code» and the product rules:
   ticks without floating point, provenance and confidence on events, pure variation operations, design tokens instead
   of literals, Russian and English for every interface text) and the guides in `docs/guidelines.md`.
4. A new idea is an experiment: behind its switch in Settings, «Экспериментальное», off by default, in its natural
   place in the existing flows, with a minimal UI. Removing an experiment removes all its code.
5. The cleanup pass, once the change works (`CLAUDE.md`, «Writing code» 10): read the whole diff against `develop` as a
   reviewer would and simplify it: duplicates, dead code, broken conventions, abstractions the task does not need,
   anything added that existing code already does.
6. Check before the push: build, unit tests, web tests, whitespace, `node tools/quality/quality.mjs`; for UI, the
   browser specs it touches, locally; for `ipad/App`, the expectations in `ipad/App/MotiffLabTests`. Fix what the
   quality checks find instead of adding baseline entries; an entry that cannot be avoided gets its reason in `why` and
   a line in the pull request.
7. Open the pull request to `develop` (template `.github/pull_request_template.md`), assignee Andrei. When `build-test`
   is green, tell the lead, which starts the reviewer; fix its blocking findings (at most two rounds). After «APPROVE»
   or the lead's decision, merge it yourself (in a cloud thread through the GitHub tools; on Andrei's computer the deny rules leave
   the merge to him). A red CI: reproduce it locally, fix, push once.

## Leave to Andrei

A pull request that changes the agents' rules (decision 6) or the saved data format (decision 3): open it, do not merge
it, and tell the lead.

## Stop

Stop and tell the lead when the task turns out to touch Music Core, both apps or saved data in a way the design did not
foresee, or when it failed twice (the lead steps the model up).
