# Rules for every agent

Read `governance/README.md` (the process) first, then this file, then your role's file and `CLAUDE.md`.

## Working

- Do the work yourself. Split it or start another agent only when it truly needs it (team mode, a second Developer on
  files that do not overlap, an `Explore` survey, below); never a thread or sub-agent for a
  routine step.
- Act; do not ask Andrei to confirm intermediate steps. Ask only about the six decisions in `governance/README.md`, or
  when a request can be read in ways that lead to different products. Every question names the task in a few words,
  links the exact place, restates the question with your recommendation and says how to answer.
- Decisions you take yourself go to the decision log (`/mnt/project-files/product/decisions.md`), one line: date, what,
  why, how to undo.
- Claims about the product cite a record (decision, idea, Andrei's message); claims about the code are checked in the
  code, never taken from memory or chat.
- Only Andrei's subscription pays for models: no API keys, no purchased tokens.
- GitHub and the repository's documents are in English; the chat with Andrei is in Russian, in plain words.

## Documents

- One living document per topic, named clearly by that topic; change it in place (same file, same artifact link),
  never a parallel copy.
- Each document opens with a status line: current, or replaced by <link>; the update date; what it replaces. Follow a
  replaced document to its successor. If two documents conflict, the latest update date wins.
- Changing a decision updates, in the same turn, its source document, the decision log and project memory.
- A research report is a proposal; once built, its rules live in `docs/` and the report is marked "history, current:
  docs/…".
- Once per version the lead checks the current documents against the decision log and marks contradictions.
- Working files that are no source of truth (drafts, mockups, screenshots, histories, discussion notes) go to
  `/mnt/project-files/scratch/<date>-<topic>/`, one folder per task, never elsewhere. It is a cache: never cited as
  current, may be cleared.

## Context and completeness

Understanding the code comes before saving tokens (Andrei, 2026-11-16): a missed dependency costs more than a long
context. The context is compacted automatically at about 400K tokens (the environment's
`CLAUDE_CODE_AUTO_COMPACT_WINDOW`, 500K, at 80 %).

- Read the files you change whole, and every place that uses what they export: find the users by name across `src/`
  and `tests/` (`git grep -n -w <symbol> -- src tests`), including the score page's messages (`src/ipad/App/Web`,
  `src/shared/Scripts`) and the facade's request names, which are strings. Read the decision records and the design
  note the task follows before the code.
- Start `Explore` sub-agents for a survey that spans more than a few files: who calls what, which state is written
  where, which tests cover it. Give each a narrow question and the paths; it returns the map, not the file contents.
  Write what they found into the track note and the area's code map.
- Keep a track note (the path your brief names, else `/mnt/project-files/product/notes/<branch>.md`, under 4 KB):
  goal, decisions with reasons, root causes found, the files and dependencies that matter, done and next, open
  questions. Write it after you have studied the code, after each pull request and before any long step, so that a
  compaction loses nothing you need. After a compaction read the note first, then read again what the next step
  touches.
- Print the tail of build, test and CI output first, and the whole of a failing test's output.
- A lead keeps the same kind of note in the version's state file.

## Platform focus

- The web app is archived (`docs/architecture/adr/0032-ipad-first-web-archived.md`): every track's area is iPad, core or the
  backend (API, scan server). There are no web tracks until Andrei reactivates the web.
- A track reads and changes `src/ipad/`, `src/backend/` and `src/shared/` (the modules, words, piano and example piece
  the iPad bundles; `docs/repository-structure.md`), with their tests in `tests/`. It does not read, change or test
  anything in `archive/` (the frozen web: pages, components, services, web-only scripts, Playwright suites), and
  neither the iPad nor `src/shared` imports archived code. Archived code never enters an agent's context: no Read,
  search, `cat` or `grep` there, no citing it as how something works, and no subagent sent to look at it. What the web
  did is in `docs/web-archive.md` and the tag `v0.6.5`. Search tools skip `archive/` (`.ignore`), and on a computer
  `.claude/hooks/archive-guard.mjs` asks before any use of it; only a task whose `allowed_paths` name `archive/`
  (reactivation, Andrei's decision) opens it.
- A change to a live shared module that alters what the web would show is not ported: append one line to
  `/mnt/project-files/product/web-backlog.md`: what changed, the pull request, the shared code touched. It is the
  porting list for the web's reactivation.

## Code maps and tracks

- Each code area has a map, `docs/code-map/<area>.md` (ipad, core, score, roll; the archived web has
  `docs/web-archive.md` instead), under 6 KB: which files do what, the key functions, what depends on what (the module's
  callers and the modules it calls, the state it owns), the rules not to break. Read your area's map before the code. A pull request that
  adds, moves or splits structure updates the map in the same pull request; the first agent in an area with no map
  writes it from what it studied, in its first pull request (before its first change, so the map
  is the survey).
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
- The iPad macOS tests run on every version tag (`v*`), by hand, or by the label `ipad-tests` when a change needs them
  before a version. The browser tests belong to the archived web and run only by hand. For `src/ipad/App`, fix the expectations in
  `tests/ipad/MotiffLabTests` in the same commit.

## Never

- Push to or merge into `main`; force push; delete branches other than your own merged one.
- Change GitHub settings (rulesets, Environments, secrets, variables) or anything in Azure; only Andrei runs
  `infra/azure/*.ps1` and `infra/github/*.ps1`.
- Merge a pull request that changes the agents' rules (decision 6) or the saved data format (decision 3).
- Use Andrei's computer, mail, calendar or drive unless he asks in this thread.
- Get around a guard that refused something: stop and say what was needed.
