# Rules for every agent

Read `governance/README.md` (the process) first, then this file, then your role's file and `CLAUDE.md`.

## Working

- Do the work yourself. Split it or start another agent only when it truly needs it (team mode, a second Developer on
  files that do not overlap); never a thread or sub-agent for a routine step.
- Act; do not ask Andrei to confirm intermediate steps. Ask only about the six decisions in `governance/README.md`, or
  when a request can be read in ways that lead to different products. Every question names the task in a few words,
  links the exact place, restates the question with your recommendation and says how to answer.
- Decisions you take yourself go to the decision log (`/mnt/project-files/product/decisions.md`), one line: date, what,
  why, how to undo.
- Claims about the product cite a record (decision, idea, Andrei's message); claims about the code are checked in the
  code, never taken from memory or chat.
- Only Andrei's subscription pays for models: no API keys, no purchased tokens.
- GitHub and the repository's documents are in English; the chat with Andrei is in Russian, in plain words.

## Context and tokens

Every step resends the whole context, so what you read once is paid again on every later step; the context is
compacted automatically at about 200K tokens.

- Keep a track note (the path your brief names, else `/mnt/project-files/product/notes/<branch>.md`, under 2 KB):
  goal, decisions with reasons, root causes found, the files and line ranges that matter, done and next, open
  questions. Write it after you have studied the code, after each pull request and before any long step, so that a
  compaction loses nothing you need. After a compaction read the note first, then only the ranges you need.
- Read code and documents in ranges (`grep -n`, then `sed -n a,b`), not whole files and never several files in one
  `cat`; print only the tail of build, test and CI output.
- A lead keeps the same kind of note in the version's state file.

## Platform focus

- Every track in a version plan has an area: iPad, web, core or shared. Without Andrei's explicit word it is the focus
  in `governance/README.md`; «shared» only when he asks for both platforms at once.
- An iPad track reads and changes `ipad/`, `src/MotiffLab.MusicCore` and the score and roll modules the iPad page runs
  (`Scripts/score`, `Scripts/roll`, checked by node tests); it does not read or change the web pages and components or
  run the web Playwright suites. A web track mirrors this.
- A change with a counterpart on the other platform is not ported: append one line to
  `/mnt/project-files/product/web-backlog.md` (or `ipad-backlog.md`): what changed, the pull request, the shared code
  touched. A porting version is planned from that list.

## Code maps and tracks

- Each code area has a map, `docs/code-map/<area>.md` (ipad, core, score, web), under 3 KB: which files do what, the
  key functions with line ranges, the rules not to break. Read your area's map before the code. A pull request that
  adds, moves or splits structure updates the map in the same pull request; the first agent in an area with no map
  writes it from what it studied, in its first pull request.
- A lead splits a version into tracks by code area, not by spec item: items that touch the same files go to one agent
  in sequence (up to about three pull requests, the track note between them); parallel agents only on files that do
  not overlap.

## Branches, commits and pull requests

- One change, one branch (`claude/*`), one pull request to `develop` (to `main` only for a release or a hotfix).
- Before every push of code: the checks that apply (`CLAUDE.md`, "Commands") and `node tools/quality/quality.mjs`.
  New code meets the thresholds; the quality checks block new violations. Never loosen
  `governance/quality/settings.json` or a tool's configuration to pass.
- Batch commits, push rarely: every push starts CI. No empty commits, no pushes only to restart CI.
- Commits keep the `Claude-Session` trailer. A pull request starts with the platform's attribution block, then the
  template.

## CI and heavy checks

- `build-test` runs on every pull request and stays quick (a few minutes): whitespace, build, unit, TypeScript, guard
  tests, code quality.
- The iPad macOS tests and the browser tests run on every version tag (`v*`), by hand, or by label (`ipad-tests`,
  `browser-tests`) when a change needs them before a version. For `ipad/App`, fix the expectations in
  `ipad/App/MotiffLabTests` in the same commit.

## Never

- Push to or merge into `main`; force push; delete branches other than your own merged one.
- Change GitHub settings (rulesets, Environments, secrets, variables) or anything in Azure; only Andrei runs
  `infra/azure/*.ps1` and `infra/github/*.ps1`.
- Merge a pull request that changes the agents' rules (decision 6) or the saved data format (decision 3).
- Use Andrei's computer, mail, calendar or drive unless he asks in this thread.
- Get around a guard that refused something: stop and say what was needed.
