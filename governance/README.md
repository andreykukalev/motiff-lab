# governance

The environment in which agents work on Motiff Lab: process rules, roles, the risk table, metrics and the guards' tests.
It is based on the Agent Operating Model v1.3 and the Agent Environment Architecture (soft mode). Everything here is
Guardrail class: it changes only through an environment task, which Andrei applies.

| Path | What it is |
| --- | --- |
| `roles/common.md` | Common process rules for every role; read before any task starts |
| `roles/developer.md`, `roles/reviewer.md`, `roles/qa.md` | The soft-mode delivery roles: what each does, sees, must not do, and hands back |
| `roles/council.md` | Advisory track: council moderator, seats, researcher; the order of rounds is the skill `.claude/skills/council` |
| `roles/product-auditor.md`, `roles/acceptance-designer.md`, `roles/ux-designer.md` | Advisory roles: a feature registry with verdicts, acceptance scenarios from the music, design variants before any PR |
| `routing.md` | Routing: which of Andrei's intents goes to which role, and how to name a role explicitly |
| `risk-paths.yml` | The minimum risk class by path |
| `metrics/` | Metrics M1–M11 and guard metrics per task: definitions and the script `collect.mjs` |
| `tests/` | Tests of the hooks in `.claude/hooks`, the agent definitions and the metrics script: `node --test "governance/tests/*.test.mjs"` |

Next to this folder: `.claude/settings.json` and `.claude/hooks/` (deny rules and hooks), `.claude/agents/` (council
seats and researcher, read only), `.claude/skills/council/` (the council procedure),
`.github/ISSUE_TEMPLATE/task-record.yml` (Task Record), `.github/pull_request_template.md`, `infra/github/protect.ps1`
(the ruleset on `main` and the Environment `production`, run by Andrei).

Strict mode will add role profiles for runners (`roles/*.profile.yml`), environment-track roles (`env-roles/`),
behavioural scenarios (`evals/`), failure tests (`failure-tests/`) and the script `tools/aom`.
