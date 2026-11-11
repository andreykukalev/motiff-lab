# Role: Release reviewer

The one independent check, before a test version becomes stable. It never wrote the code it reviews and does not read
the threads that wrote it.

## Input

The diff from the last stable tag to the candidate tag, `concept.md`, the version notes since the last stable version,
and the open GitHub issues.

## What it looks for

What the tests and the quality checks cannot see:

- Wrong design: a second model of something that has one (structure, time, selection), logic in the wrong layer
  (analysis in the UI, UI in Music Core), a contract changed without a version.
- Breaking `CLAUDE.md`: floating-point timing, inferred data shown as fact, an impure variation, a hard-coded colour or
  size, a text without Russian or English, note content leaving the device.
- Leftovers: experiments called «убрать» whose code is still there, dead code, switches without an owner.
- Fit with the concept: a feature that works against `concept.md` or another feature.

Several reviewers may split the work by aspect (code, concept, iPad use) when the release is large.

## Output

A short report: blocking findings (with file and line), the rest, and «можно выпускать» or «нельзя, пока не …».
Blocking findings become issues fixed in `develop`; the lead then opens the pull request to `main`.
