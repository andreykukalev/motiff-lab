# Routing: intent → role

How the coordinator decides whom to call when Andrei writes what he wants, and the coordinator's own rules. The role
rules are in `roles/`, the common ones in `roles/common.md`.

## Andrei's rule (2026-11-08 20:39)

- **Code by a Task Record** = the full AOM cycle: Developer, then a separate Reviewer and QA (`roles/common.md`, "How a
  task runs"), or the light path for a small code task (Developer plus one Reviewer+QA thread, same section).
- **Thinking and architecture** = a council (`roles/council.md`, light by default).
- **Small work** (audits, folder work, filing Task Records, questions) = a plain thread without roles.

## Coordinator (the Orchestrator in soft mode)

- Helps Andrei word a Task Record, files the Issue, follows progress; does not write code and does not settle disputes.
- Picks the role for Andrei's intent from the table below; if Andrei named the role, calls that one.
- Hands architecture and product forks to the council (`roles/council.md`) instead of deciding them.
- Starts the roles as separate threads: Developer, then Reviewer (a new thread, with no retelling of the Developer's
  reasoning, only the numbers of the Issue and the PR), then QA, or one Reviewer+QA thread on the light path. Does not
  merge itself and does not ask threads to merge.
- After fix commits, sends the same Reviewer (and QA) thread the PR number and the new SHA for a delta re-check; starts
  a new review thread only when the delta touches files the review did not cover, or the Reviewer asks for one.
- One task in progress at a time. The next one starts after the current one is merged or cancelled.
- Does not reassign a reviewer to get a different answer and does not withdraw findings; writes a dispute into the
  Issue for Andrei.

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
| An architecture or product fork; "convene a council", "quickly compare the options" | Council moderator (`roles/council.md`, skill `council`): light council by default; full only when Andrei picks it on the brief card, for a decision expensive to undo | A new thread; the seats are subagents in it | A question from Andrei; a brief he confirmed | Decision record (Claude Doc), questions for Andrei |
| A Developer stopped on a contract (facade, project file, API, Cosmos) or an architecture question | Moderator, light council | A new thread; the delivery task waits | The stop is recorded in the Issue | Decision record, then Andrei's decision |
| "How many…", "how often did it break…", facts before a decision | Researcher (`.claude/agents/researcher.md`) | A subagent in the thread that needs the facts | Questions and a SHA | A fact list with sources |
| "Do …", no Task Record yet | Coordinator (intake) | Project chat | — | An Issue in the Task Record form, for approval |
| An Issue with `aom:criteria-approved` | Developer (`roles/developer.md`) | A new thread | Andrei's approval label | A PR on green CI |
| A PR is open and CI is green; "reviewer for PR #…" | Reviewer (`roles/reviewer.md`); on the light path also QA | A new thread, not the Developer's | The Issue and PR numbers, no summary | An `aom:review` comment (and `aom:qa` on the light path) |
| After review; "QA for PR #…" (full cycle) | QA (`roles/qa.md`) | A new thread, not the Developer's | The Reviewer's report | An `aom:qa` comment, steps for Andrei |
| Comments on a PR, "fix it per the review" | Developer as Feedback Resolver | The Developer thread of that task | The Reviewer's report | New commits, then a delta re-check by the same Reviewer (and QA) thread on the new SHA |
| A new role, a rules change, hooks, CI, `CLAUDE.md`, `tests/acceptance` | Environment task (E1–E3 per the Task Record form) | A new thread, then an independent Reviewer | A Task Record with `aom:criteria-approved`, like any task | A PR to the agents' environment, applied by Andrei |
| A redesign of a screen or mechanic; "show me variants" | UX designer (`roles/ux-designer.md`), variants before any PR | A new thread, no PR | Andrei's request or a draft UI Task Record | Variants for approval, then the chosen one in the Task Record's criteria |
| "What is worth keeping", an audit before a rebuild or a release | Product auditor (`roles/product-auditor.md`) | A new thread | Andrei's request with the scope; the decision frame, if any | A feature registry with verdicts and draft Task Records |
| Acceptance scenarios for journeys or rules | Acceptance designer (`roles/acceptance-designer.md`) | A new thread, never the Developer of the task they check | Andrei's request with the journeys and the level | A scenario list for approval, then an environment task for `tests/acceptance` |
| Merge a PR, release, deploy, change GitHub or Azure settings | Andrei only | — | — | The coordinator gives the steps and does none of it |
| A question that the documents answer | The coordinator answers itself | Project chat | — | An answer with a link |
| Small work: an audit other than the product audit above, folder work, filing a Task Record, a question that needs digging | A plain thread without roles (Andrei's rule above) | A new thread | Andrei's request | The result in the thread |

## Model and effort per role

Each thread keeps one model and effort for its whole run; this table says which (task #304). Ids: Opus 5.5
`claude-opus-5-5`, Sonnet 5.5 `claude-sonnet-5-5`.

| Role | Runs as | Model | Effort |
| --- | --- | --- | --- |
| Developer (`roles/developer.md`) | thread | Opus 5.5 | high |
| Reviewer (`roles/reviewer.md`) | thread | Opus 5.5 | high |
| QA (`roles/qa.md`) | thread | Sonnet 5.5 | medium |
| QA on a High or Guardrail task (`roles/qa.md`) | thread | Opus 5.5 | high |
| Council moderator (`roles/council.md`) | thread | Opus 5.5 | high |
| Council seats and researcher (`roles/council.md`) | subagents of the moderator | as in the table "Starting the seats" of `.claude/skills/council/SKILL.md` | the moderator's (no file sets effort per agent) |
| Product auditor (`roles/product-auditor.md`) | thread | Opus 5.5 | high |
| Acceptance designer (`roles/acceptance-designer.md`) | thread | Opus 5.5 | high |
| UX designer (`roles/ux-designer.md`) | thread | Opus 5.5 | high |
| Plain thread without a role (audits, filing Task Records, questions, folder work) | thread | Opus 5.5 | medium |
| Coordinator | project chat | Opus 5.5 | low |

The moderator, product auditor, acceptance designer and UX designer keep the thread default; #304 set no other value
for them.

How it takes effect:

- **claude.ai cloud thread.** A thread starts on the project's default (`claude-opus-5-5[1m]`, effort high) and changes
  its own model or effort only on Andrei's own words; a coordinator's note does not count. Those words are one line in
  the project instructions ("Model and effort per role: follow the table in governance/routing.md; at the first turn
  switch this thread to its row"). So at its first turn a thread finds its row, compares it with `get_session`
  (`session_context.model` and the effort), and switches (`switch_model`, `set_effort_level`) only where they differ.
  The coordinator names the role in the thread's first message, so the thread knows its row.
- **Claude Code on Andrei's computer.** The same table, set with `/model` and `/effort` when the session starts.

## Fable 5.1: a rare escalation tier

Fable 5.1 (`claude-fable-5-1`; in the Agent tool `model: "fable"`) has no row in the table above: no thread runs on it
and no `.claude/agents/*.md` names it (their `model:` lines stay `opus`, `sonnet` or `haiku`,
`governance/tests/agents.test.mjs`). It runs only as a one-off, read-only subagent started with
`subagent_type: "Explore"` (no Edit or Write) and `model: "fable"`, for these three uses and no others:

1. **Final review opinion** on a PR of a High or Guardrail task, from the diff and the Issue only
   (`roles/reviewer.md`).
2. **A disputed blocking finding**: still disputed by the Developer and the Reviewer after one round. Fable gives an
   independent opinion attached to the dispute in the Issue, and Andrei decides; the finding is not withdrawn and the
   Reviewer is not replaced (the coordinator's rules above).
3. **The blind seat of a council** on a decision that is expensive to undo (a data contract, the project file version,
   an ADR), instead of Opus (`roles/council.md`, the skill's table "Starting the seats").

Who asks and when:

- The coordinator asks Andrei for each use (a decision card or his message); his yes covers that one use. No thread
  starts Fable on its own: a Reviewer or moderator who thinks a use is due tells the coordinator. After the yes, the
  coordinator tells the thread named in the use (the Reviewer for 1 and 2, the moderator for 3), which starts the
  subagent.
- Never while the weekly limit is "warning" (`rate_limit_info.status` = `allowed_warning`); at most two uses per week.
- Each use is recorded (model, tokens, time) in the Issue comment that carries the opinion (1, 2) or in the council's
  `usage.md` (3). Fable's price against Opus is not known: the first use is measured before the second.

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
