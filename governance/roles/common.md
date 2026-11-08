# Common process rules

Every agent that works on Motiff Lab reads this file before it starts a task, together with the file of its own role in
this folder. The rules of the product and the code are in `CLAUDE.md`; this file holds only how the work is run. It is
based on the Agent Operating Model v1.3 (model A, Controlled Autonomy) and on the Agent Environment Architecture,
section "Current choice: the foundation and soft mode".

This file, the role files, `.claude/`, `.github/`, `CLAUDE.md` and `tests/acceptance/` are the agents' environment. They
change only in an environment task of class E3, which Andrei applies. An agent does not change the rules it works by and
does not widen its own rights.

## Where we are now

- The mode is **soft A1**: agents work in threads of the claude.ai project (and, when Andrei starts it, in Claude Code on
  his computer), and every action on GitHub is taken under Andrei's name. Roles are separated by process, not by
  identity.
- Until the foundation is applied, **A0** holds: research and documents only, with no writes to the repository, GitHub,
  Azure or CI. What is applied can be seen on `main`: whether this file, `.claude/settings.json` and the ruleset on
  `main` exist.
- Models are paid for only by Andrei's subscription. API keys and purchased tokens are not proposed.
- One task at a time. A second task in parallel (A2) and auto-merge (B1) are possible only in strict mode.

## Source of truth

- A task is a **Task Record**: an Issue made with the form `.github/ISSUE_TEMPLATE/task-record.yml` and labelled `task`.
  The evidence is in the PR that refers to it (`Closes #N`). The chat, the project memory and `/mnt/project-files/` are
  working notes: they change neither the criteria, nor the scope, nor the status of a task.
- **Andrei approves the acceptance criteria before implementation**: he sets the label `aom:criteria-approved` himself.
  An agent neither sets nor removes this label. Without it, the Developer does not start.
- Once approved, the criteria are not edited. If they are wrong or incomplete, the agent stops and says so in the Issue;
  the change is Andrei's decision. If the goal itself changes, it is a new task.
- The risk class only goes up: any role can raise it with a one-line reason in the Issue; only Andrei lowers it. The
  minimum is set by the AOM path table (section "Risk classification"): `.github/`, `infra/`, `governance/`, `.claude/`,
  `CLAUDE.md` are Guardrail; the facade, the project file, `ipad/Core`, `src/MotiffLab.Api`, `services/omr`, `Scripts`,
  `i18n`, `PlaybackLab*`, `src/MotiffLab.MusicCore` (while the API deploys on its changes) are High; the rest of `src`,
  `ipad/App`, `design/kit`, `tests`, `docs/decisions` are Standard; `docs`, `README.md` are Low; anything not listed is
  Standard.

## How a task runs

1. **Intake.** Andrei or the coordinator files an Issue with the form: goal, criteria, class, components,
   `allowed_paths`, platforms, stop conditions.
2. **Approval.** Andrei checks the criteria and sets `aom:criteria-approved`.
3. **Developer**, in a separate thread. Makes the change on its own branch, checks it, opens a PR with the template
   (`.github/pull_request_template.md`) and the labels `agent`, `role:developer` and the class `risk:*`.
4. **CI** `build-test` is green on the PR's last commit. The ruleset on `main` does not let a PR merge without it.
5. **Reviewer**, a new thread with a fresh context. Sees only the Task Record and the PR, not the Developer's thread.
   Writes its report in the PR (`governance/roles/reviewer.md`).
6. **Fixes.** The Developer (as Feedback Resolver) answers the findings with new commits; every new commit voids the
   earlier verdict: review and QA hold only for the SHA they checked.
7. **QA** checks every approved criterion on the last SHA and prepares the steps for Andrei to check
   (`governance/roles/qa.md`). For class Low, Reviewer and QA can be one thread.
8. **Acceptance and merge.** Andrei checks the result (for UI, on the dev site or on the iPad) and merges the PR
   **himself**.
9. **Metrics.** After the merge Andrei runs `node governance/metrics/collect.mjs task <issue>`
   (`governance/metrics/README.md`).

## What an agent must never do

- Merge a PR, turn on auto-merge, approve a deploy, start or restart a workflow by hand.
- Push to `main`, to someone else's branch, with force, delete branches, push tags.
- Change GitHub settings (rulesets, Environments, secrets, variables, repository labels, visibility) or anything in
  Azure. The scripts `infra/azure/*.ps1` and `infra/github/*.ps1` are run only by Andrei.
- Set or remove `aom:criteria-approved`, edit approved criteria, lower the risk class.
- Use Andrei's computer (folders, Desktop Commander, Remote Control) or his mail, calendar or drive.
- Get around a guard once it has fired (with another command, another tool, or through a script).

## Branches, commits and PRs

- Work happens on the branch the thread was given (`claude/project-thread-*`) or on `task/<number>-<short>`. Only a PR
  that Andrei merges reaches `main`.
- One task, one branch, one PR. Changes outside `allowed_paths` are not added "while we are at it".
- Commit messages and PR descriptions name the requirements (`S1-…`, `V1–V12`, `FR-…`) when the code implements them.
  The `Claude-Session` trailer in commits is kept: the metrics tell the agent's commits from Andrei's by it.
- Commits are gathered in a batch and pushed rarely: every push starts CI. Do not push a branch to "reset" it, do not
  make empty commits, and do not reopen a PR for the sake of CI.
- A PR begins with the attribution block the platform requires, then the sections of the template.
- Everything on GitHub is in English: Issues, PRs, commits, comments, reviews and the repository's documents. The chat
  with Andrei stays in Russian; the app's own texts stay in Russian and English (`CLAUDE.md`).

## CI and heavy checks

- CI stays within 5 minutes (the timeout of the job `build-test`): whitespace, build, unit and TypeScript tests, the
  guard tests.
- The browser tests (`tests/e2e`) run in CI only when Andrei asks: the label `browser-tests` or a manual CI run. The
  agent runs them locally for the specs it touches.
- The iPad build on macOS runs only with the label `ipad-tests` or the tag `ipad-v*`, and only when Andrei agrees.
  Before the first such run the agent checks the expectations in `ipad/App/MotiffLabTests` (page counts, i18n keys,
  DOM counters in `score.js`) and fixes them in the same commit: Swift does not build in the container.
- Deploys to Azure (`api.yml`, `scan-server.yml`) wait for Andrei's approval in the Environment `production`. The site
  (dev) updates itself after green CI on `main`; the iPad app (prod) only through releases, together with the web.

## Product decisions

- For redesigns of the interface and of mechanics, design variants come first for approval; a PR only after Andrei
  agrees.
- The open questions in `design/kit/HANDOFF.md` (section 7) and every product fork are decided by Andrei, not by the
  agent.

## Stopping

The agent stops and asks, instead of deciding itself, when:

- it needs a file outside `allowed_paths`, or the change touches a contract (facade JSON, project file, HTTP API,
  Cosmos);
- it needs a right or an action from the "must never do" list;
- the criteria are unclear, contradict each other or do not solve the task;
- the actual risk class is higher than the one in the Task Record;
- a guard has fired (deny, hook, a ruleset refusal).

A stop is written as a comment in the Issue (and a line in the thread): what it was doing, what it ran into, what the
options are, what it needs from Andrei. A stop is a correct result, not a failure: drills for it (metric M8) are run on
purpose.

## Guards

In soft mode all agents share one identity, Andrei's, so the guards catch a mistake but do not make it impossible.

| Guard | Where | What it does |
| --- | --- | --- |
| Ruleset `main` | GitHub | PR only, `build-test` required, no force push or deletion; nobody can bypass it |
| Environment `production` | GitHub | Deploys of the API and the scan server wait for Andrei to press the button |
| Deny | `.claude/settings.json` | Refuses merge, auto-merge, starting workflows, writing files through the GitHub API, force push, editing rulesets and Environments, the Azure CLI, Andrei's computer and mail |
| `push-guard` | `.claude/hooks` | Lets through only a push of the agent's current branch to `origin`; refuses `gh` commands that merge, run, release or change settings, and writes to the GitHub API |
| `scope-guard` | `.claude/hooks` | Asks before an edit of the agents' environment outside an environment task, and before an edit outside `allowed_paths` from `.aom/task.json` |
| env-scope | CI | Marks a PR that changes the agents' environment (a warning, not a refusal) |

Check of 2026-11-08: in a claude.ai cloud thread the repository's `.claude/settings.json` did not apply — a denied
command ran, `scope-guard` did not ask, the agents in `.claude/agents` were not found. Until this is fixed, in cloud
threads only the ruleset, the Environment and these rules hold. In Claude Code on Andrei's computer (Windows, Claude
Code 2.1.308, checked 2026-11-08) the deny rules, the hooks and the seven agents of `.claude/agents` do apply: a hook
refused `gh ruleset list`.

Every refusal and question from the hooks is written to `.aom/guard-log.jsonl`. The Developer carries them into the PR
section "Guard triggers". Any attempt to get around a guard is an incident: the whole soft mode goes one step back until
it is reviewed with Andrei (AOM, "Rolling back autonomy").

## Coordinator (the Orchestrator in soft mode)

- Helps Andrei word a Task Record, files the Issue, follows progress, but does not write code and does not settle
  disputes.
- Picks the role for Andrei's intent from the table in `governance/routing.md`; if Andrei named the role, calls that one.
- Hands architecture and product forks to the council (`governance/roles/council.md`) instead of deciding them.
- Starts the roles as separate threads: Developer, then Reviewer (a new thread, with no retelling of the Developer's
  reasoning, only the numbers of the Issue and the PR), then QA. Does not merge itself and does not ask threads to merge.
- One task in progress at a time. The next one starts after the current one is merged or cancelled.
- Does not reassign a reviewer to get a different answer and does not withdraw findings; writes a dispute into the
  Issue for Andrei.
