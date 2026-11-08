# Routing: intent → role

How the coordinator decides whom to call when Andrei writes what he wants. The role rules are in `roles/`, the common
ones in `roles/common.md`.

## Two ways

- **Andrei names the role himself**: "convene a council on …", "reviewer for PR #…", "QA for PR #…", "researcher: how
  many …". The coordinator calls the named role. It departs from this only when the named role would break
  independence (for example, a review by the same thread that wrote the code): then it says why and calls the role in a
  new thread.
- **Andrei names no role**: the coordinator picks one from the table below and says in one line which role it picked
  and why. If several rows fit, or none, it asks rather than guesses.

Role names the coordinator understands: council (moderator), researcher, developer, reviewer, QA, environment task,
product auditor, acceptance designer, UX designer.
Andrei may also name a role that does not exist yet: the coordinator answers that there is no such role and suggests
the nearest one in the table.

## Table

| Andrei's intent | Role | Where it works | Needed before start | Output |
| --- | --- | --- | --- | --- |
| An architecture or product fork that is expensive to undo; "convene a council" | Council moderator, full council (`roles/council.md`, skill `council`) | A new thread; the seats are subagents in it | A question from Andrei; a brief he confirmed | Decision record (Claude Doc), questions for Andrei |
| A smaller fork, "quickly compare the options" | Moderator, light council | A new thread | The same | The same, two rounds |
| A Developer stopped on a contract (facade, project file, API, Cosmos) or an architecture question | Moderator, light council | A new thread; the delivery task waits | The stop is recorded in the Issue | Decision record, then Andrei's decision |
| "How many…", "how often did it break…", facts before a decision | Researcher (`.claude/agents/researcher.md`) | A subagent in the thread that needs the facts | Questions and a SHA | A fact list with sources |
| "Do …", no Task Record yet | Coordinator (intake) | Project chat | — | An Issue in the Task Record form, for approval |
| An Issue with `aom:criteria-approved` | Developer (`roles/developer.md`) | A new thread | Andrei's approval label | A PR on green CI |
| A PR is open and CI is green; "reviewer for PR #…" | Reviewer (`roles/reviewer.md`) | A new thread, not the Developer's | The Issue and PR numbers, no summary | An `aom:review` comment |
| After review; "QA for PR #…" | QA (`roles/qa.md`) | A new thread, not the Developer's | The Reviewer's report | An `aom:qa` comment, steps for Andrei |
| Comments on a PR, "fix it per the review" | Developer as Feedback Resolver | The Developer thread of that task | The Reviewer's report | New commits, then review and QA again |
| A new role, a rules change, hooks, CI, `CLAUDE.md`, `tests/acceptance` | Environment task (E1–E3 per the Task Record form) | A new thread, then an independent Reviewer | A Task Record with `aom:criteria-approved`, like any task | A PR to the agents' environment, applied by Andrei |
| A redesign of a screen or mechanic; "show me variants" | UX designer (`roles/ux-designer.md`), variants before any PR | A new thread, no PR | Andrei's request or a draft UI Task Record | Variants for approval, then the chosen one in the Task Record's criteria |
| "What is worth keeping", an audit before a rebuild or a release | Product auditor (`roles/product-auditor.md`) | A new thread | Andrei's request with the scope; the decision frame, if any | A feature registry with verdicts and draft Task Records |
| Acceptance scenarios for journeys or rules | Acceptance designer (`roles/acceptance-designer.md`) | A new thread, never the Developer of the task they check | Andrei's request with the journeys and the level | A scenario list for approval, then an environment task for `tests/acceptance` |
| Merge a PR, release, deploy, change GitHub or Azure settings | Andrei only | — | — | The coordinator gives the steps and does none of it |
| A question that the documents answer | The coordinator answers itself | Project chat | — | An answer with a link |

## Not created yet

Waiting for a decision: the product profiles "web" and "iPad" from the multi-agent design doc
(https://claude.ai/artifact/UtpGa79GKqHQeE9wga1eM3). Until they exist, such intents go to the nearest row of the table,
or to Andrei as a question.

The external council seat from another model family is paused (`roles/council.md`).

## Where roles load by themselves

- **Claude Code on Andrei's computer** reads the repository's `.claude/agents/` and `.claude/skills/`: a role can be
  called by name (`/council <topic>`; the `@agent-researcher` mention is unverified). Checked on 2026-11-08 on Windows
  (Claude Code 2.1.308, `main` after #284): `/council` started with the brief, all seven agents were listed with their
  tool lists, and a PreToolUse hook refused `gh ruleset list`. So agents, skills and hooks load there; the deny rules
  were not tested on their own, because the hook refused the command first. The council folder and the
  questions to Andrei work differently there: skill `council`, "Where the council runs".
- **claude.ai cloud threads** did not read them on 2026-11-08 (nor apply `.claude/settings.json`): the thread reads the
  role file itself and passes it to a subagent as the prompt, as the skill `council` says in "Starting the seats".
