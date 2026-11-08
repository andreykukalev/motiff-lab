# governance

The environment the agents work on Motiff Lab in: process rules, roles, the risk table, metrics and the guards' tests.
It is based on the Agent Operating Model v1.3 and the Agent Environment Architecture (soft mode). Everything here is
class Guardrail: it changes only in an environment task, which Andrei applies.

| Path | What it is |
| --- | --- |
| `roles/common.md` | Common process rules for every role; read before any task starts |
| `roles/developer.md`, `roles/reviewer.md`, `roles/qa.md` | The roles of soft mode: what each does, what it sees, what it must not do, what it delivers |
| `roles/council.md` | Advisory track: council moderator, seats, researcher; the order of rounds is the skill `.claude/skills/council` |
| `routing.md` | Routing: which of Andrei's intents goes to which role, and how to name a role explicitly |
| `risk-paths.yml` | The minimum risk class by path |
| `metrics/` | Metrics M1–M11 and the guards' figures for each task: definitions and the script `collect.mjs` |
| `tests/` | Tests of the hooks in `.claude/hooks` and of the metrics script: `node --test "governance/tests/*.test.mjs"` |

Next to it, outside this folder: `.claude/settings.json` and `.claude/hooks/` (deny rules and hooks), `.claude/agents/`
(council seats and researcher, read only), `.claude/skills/council/` (the council procedure),
`.github/ISSUE_TEMPLATE/task-record.yml` (Task Record), `.github/pull_request_template.md`, `infra/github/protect.ps1`
(the ruleset on `main` and the Environment `production`, run by Andrei).

Strict mode will add here role profiles for the runners (`roles/*.profile.yml`), the roles of the environment track
(`env-roles/`), behaviour scenarios (`evals/`), failure tests (`failure-tests/`) and the script `tools/aom`.
