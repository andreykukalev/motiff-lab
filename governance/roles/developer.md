# Role: Developer

One agent per change, from start to end. Automated tests and the code-quality checks are its feedback loop; there is no
per-change reviewer.

## Work

1. Branch from the current `develop` (or the stable tag for a hotfix).
2. Read what the task needs: the issue or idea entry, the design note if any, the related decision records,
   `design/kit/HANDOFF.md` before any UI work.
3. Make the change. Follow `CLAUDE.md`: ticks without floating point, provenance and confidence on events, pure
   variation operations, design tokens instead of literals, Russian and English for every interface text.
4. A new idea is an experiment: behind its switch in Settings, «Экспериментальное», off by default, in its natural
   place in the existing flows, with a minimal UI. Removing an experiment removes all its code.
5. Check before the push: build, unit tests, web tests, whitespace, `node tools/quality/quality.mjs`; for UI, the
   browser specs it touches, locally; for `ipad/App`, the expectations in `ipad/App/MotiffLabTests`. Fix what the
   quality checks find instead of adding baseline entries; an entry that cannot be avoided gets its reason in `why` and
   a line in the pull request.
6. Open the pull request to `develop` (template `.github/pull_request_template.md`), assignee Andrei. When `build-test`
   is green, merge it yourself (in a cloud thread through the GitHub tools; on Andrei's computer the deny rules leave
   the merge to him). A red CI: reproduce it locally, fix, push once.

## Leave to Andrei

A pull request that changes the agents' rules (decision 6) or the saved data format (decision 3): open it, do not merge
it, and tell the lead.

## Stop

Stop and tell the lead when the task turns out to touch Music Core, both apps or saved data in a way the design did not
foresee, or when it failed twice (the lead steps the model up).
