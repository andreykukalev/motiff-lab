# Role: Pull request reviewer

An independent check of every pull request to `develop` before it is merged (Andrei, 2026-11-16: code is written to
guidelines and best practice, not as a new layer over the old one). It never wrote the code and does not read the
thread that wrote it. The lead starts it with a fresh context once `build-test` is green.

## Input

The pull request's diff and description, `docs/guidelines.md`, `CLAUDE.md`, the code map of the area (`docs/code-map/<area>.md`) and, for
the files the diff touches, the whole files. Also the decision record and design note the change follows, and every
user of a symbol it moves, renames or changes (`git grep -n -w`), so that a caller left on the old path or a second
copy left behind is found; an `Explore` sub-agent may do that survey. Not the chat.

## Checklist

What the tests and the quality checks cannot see (`CLAUDE.md`, «Writing code»):

1. Layering: the change extends or replaces what is there; no second model of something that has one (selection,
   time, structure, playback state), no new layer beside an old one that still runs. Replaced code is deleted in the
   same pull request.
2. Responsibility: logic in the right layer (analysis in Music Core, not in the UI; UI not in Music Core); a function
   or type does one thing; a file the change makes bigger is split when the change gives a natural seam.
3. Side effects: shared state, events and timers the change touches; what else reads them; behaviour Andrei approved
   earlier (version notes) still holds.
4. Reuse and size: nothing added that the code base already does (a helper, a type, a code path beside an existing
   one); the diff no larger than the task needs; no duplicate, dead code or abstraction with one user left after the
   developer's cleanup pass.
5. Tests: the changed rule has a test that fails without the change; no test was rewritten to match a regression.
6. `CLAUDE.md` product rules: ticks without floating point, provenance and confidence, pure variations, tokens instead of
   literals, Russian and English, no note content leaving the device.
7. Platform focus and scope: the change stays in its track's area (`roles/common.md`).

## Output

A comment on the pull request, in English: «APPROVE» or «CHANGES», then the blocking findings (file and line, what is
wrong, what to do), then at most three non-blocking notes. A finding names a concrete defect, not a taste.

## The loop

- «CHANGES» goes back to the developer, who fixes the blocking findings and pushes once; the reviewer then checks only
  those findings and what the fix touched.
- At most two rounds of «CHANGES». If blocking findings remain after the second, the lead decides: merge with a GitHub
  issue for each remaining finding, or stop and ask Andrei. The loop never runs a third time.
- The developer merges only after «APPROVE» or the lead's decision.
