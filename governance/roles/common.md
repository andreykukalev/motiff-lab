# Common process rules

Every agent reads this file and its role's file before a task. `CLAUDE.md` holds the product and code rules; this
file, how the work is run (AOM v1.3, model A); the coordinator's rules are in `governance/routing.md`, the guards in
`governance/README.md`. This file, the role files, `.claude/`, `.github/`, `CLAUDE.md` and `tests/acceptance/` change
only in an environment task (E3) that Andrei applies; an agent does not change its own rules or widen its rights.

## Where we are now

- **Soft A1**: agents work in claude.ai threads (or Claude Code on Andrei's computer when he starts it), every GitHub
  action under Andrei's name; roles are separated by process, not identity.
- Only Andrei's subscription pays for models; no API keys or purchased tokens.
- One task at a time; parallel tasks (A2), auto-merge (B1) only in strict mode.

## Source of truth

- A task is a **Task Record** (an Issue from `.github/ISSUE_TEMPLATE/task-record.yml`, label `task`); its evidence is
  the PR with `Closes #N`. Chat, memory and `/mnt/project-files/` are notes: they change no criteria, scope or status.
- A Task Record body is at most about 4 KB: goal, criteria, scope, checks, stops. Quoted sources and background go in
  a linked file (`/mnt/project-files/` or a decision record) that Reviewer and QA open only when a criterion needs it.
- **Andrei approves the criteria** before implementation with the label `aom:criteria-approved` (only he sets or
  removes it); without it the Developer does not start. Approved criteria are not edited: if wrong, the agent stops and
  says so in the Issue; a changed goal is a new task.
- The risk class only goes up: any role raises it with a one-line reason in the Issue; only Andrei lowers it. The
  minimum by path is `governance/risk-paths.yml`.

## How a task runs

1. **Intake**: Andrei or the coordinator files the Issue from the form.
2. **Approval**: Andrei's label.
3. **Developer**, own thread and branch: makes and checks the change; a PR from the template, labels `agent`,
   `role:developer`, `risk:*`.
4. **CI** `build-test` green on the last commit (the ruleset requires it).
5. **Reviewer**, a new thread: only the Task Record and the PR, never the Developer's thread.
6. **Fixes**: the Developer answers with commits. A verdict holds only for the SHA it checked: a new commit has no
   `aom-verdicts` success until the Reviewer's `approve` and QA's `pass` name it (`roles/reviewer.md`, `roles/qa.md`).
   The same Reviewer (and QA) thread re-checks only the delta (fix commits, criteria they affect) on the new SHA,
   started by a coordinator message holding only the PR and SHA. A new review thread only if the delta touches files
   the review did not cover, or the Reviewer asks.
7. **QA** checks every criterion on the last SHA and writes steps for Andrei. The check `aom-verdicts`
   (`.github/workflows/aom-verdicts.yml`) turns green on that SHA only when both verdicts for it are there.
8. **Acceptance and merge** by Andrei **himself** (UI on the dev site or the iPad), only with `build-test` and
   `aom-verdicts` green on the head commit; the ruleset requires both once Andrei has run
   `infra/github/protect.ps1 -RequireVerdicts`.
9. **Metrics**: after the merge Andrei runs `node governance/metrics/collect.mjs task <issue>`.

**Light path**: a Low or Standard task with no contract touched (facade JSON, project file, HTTP API, Cosmos), no High
or Guardrail path and at most about 300 changed lines runs as Developer plus one Reviewer+QA thread; if the PR outgrows
that, the Reviewer says so and QA gets its own thread. High and Guardrail tasks keep the full cycle.

## What an agent must never do

- Merge a PR, turn on auto-merge, approve a deploy, start or restart a workflow by hand.
- Push to `main` or someone else's branch, push with force, delete branches, push tags.
- Change GitHub settings (rulesets, Environments, secrets, variables, labels, visibility) or anything in Azure; only
  Andrei runs `infra/azure/*.ps1` and `infra/github/*.ps1`.
- Set or remove `aom:criteria-approved`, edit approved criteria, lower the risk class.
- Use Andrei's computer (folders, Desktop Commander, Remote Control) or his mail, calendar or drive.
- Get around a guard that fired (another command, tool or script): an incident, soft mode goes one step back until
  Andrei reviews it.

## Branches, commits and PRs

- Work on the thread's branch (`claude/project-thread-*`) or `task/<number>-<short>`; one task, one branch, one PR,
  merged only by Andrei. Nothing outside `allowed_paths` "while we are at it".
- Commits and PRs name the requirements they implement (`S1-…`, `FR-…`); commits keep the `Claude-Session` trailer.
- Batch commits, push rarely (every push starts CI); no "reset" pushes, empty commits or reopened PRs for CI.
- A PR starts with the platform's attribution block, then the template.
- GitHub and the repository's documents are in English; the chat with Andrei is in Russian.

## CI and heavy checks

- CI stays within 5 minutes (`build-test`): whitespace, build, unit, TypeScript and guard tests.
- Browser tests run in CI only when Andrei asks (label `browser-tests` or a manual run); run touched specs locally.
- The iPad macOS build only with the label `ipad-tests` or a tag `ipad-v*`, when Andrei agrees; first fix the
  expectations in `ipad/App/MotiffLabTests` (page counts, i18n keys, `score.js` DOM counters) in the same commit.
- Azure deploys wait for Andrei in the Environment `production`; the dev site updates after green CI on `main`, the
  iPad app (prod) only through releases, with the web.

## Product decisions

Redesigns of the interface or mechanics show variants first; a PR only after Andrei agrees. `design/kit/HANDOFF.md`
section 7 questions and every product fork are Andrei's.

## Stopping

Stop and ask when you need a file outside `allowed_paths`, touch a contract (facade JSON, project file, HTTP API,
Cosmos), need a right from the "never" list, find the criteria unclear, contradictory or not solving the task, find the
class higher than recorded, or a guard fired. A stop is an Issue comment (and a thread line): what you were doing, what
you hit, the options, what you need from Andrei. A stop is a correct result (metric M8).

## Guards

They catch a mistake but cannot prevent it; in a cloud thread `.claude/` does not load, so only the ruleset, the
Environment and these rules hold (`governance/README.md`). Hook refusals and questions go to `.aom/guard-log.jsonl`; the
Developer copies them into the PR section "Guard triggers".
