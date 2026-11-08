# governance

The environment the agents work on Motiff Lab in: process rules, roles, the risk table, metrics and the guards' tests.
It is based on the Agent Operating Model v1.3 and the Agent Environment Architecture (soft mode). Everything here is
class Guardrail: it changes only in an environment task, which Andrei applies.

| Path | What it is |
| --- | --- |
| `roles/common.md` | Common process rules for every role (task cycle, light path, stops); read before any task starts |
| `roles/developer.md`, `roles/reviewer.md`, `roles/qa.md` | The roles of soft mode: what each does, what it sees, what it must not do, what it delivers |
| `roles/council.md` | Advisory track: council moderator, seats, researcher; the order of rounds is the skill `.claude/skills/council` |
| `roles/product-auditor.md`, `roles/acceptance-designer.md`, `roles/ux-designer.md` | Advisory roles: a feature registry with verdicts, acceptance scenarios from the music, design variants before any PR |
| `routing.md` | Routing: Andrei's routing rule, the coordinator's rules, which intent goes to which role, how to name a role |
| `risk-paths.yml` | The minimum risk class by path |
| `metrics/` | Metrics M1–M11 and the guards' figures for each task: definitions and the script `collect.mjs` |
| `tests/` | Tests of the hooks in `.claude/hooks` and of the metrics script: `node --test "governance/tests/*.test.mjs"` |

Next to it, outside this folder: `.claude/settings.json` and `.claude/hooks/` (deny rules and hooks), `.claude/agents/`
(council seats and researcher, read only), `.claude/skills/council/` (the council procedure),
`.github/ISSUE_TEMPLATE/task-record.yml` (Task Record), `.github/pull_request_template.md`, `infra/github/protect.ps1`
(the ruleset on `main` and the Environment `production`, run by Andrei).

## Guards

The rules about guards (never get around one, the guard log, the PR section "Guard triggers") are in
`roles/common.md`. In soft mode all agents share one identity, Andrei's, so the guards catch a mistake but do not make
it impossible.

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
threads only the ruleset, the Environment and the rules in `roles/` hold. In Claude Code on a computer the deny rules
and hooks should apply, but that is not checked yet [unverified] (`routing.md`, "Where roles load by themselves", has
what was seen there).

Strict mode will add here role profiles for the runners (`roles/*.profile.yml`), the roles of the environment track
(`env-roles/`), behaviour scenarios (`evals/`), failure tests (`failure-tests/`) and the script `tools/aom`.
