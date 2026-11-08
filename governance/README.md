# governance

Среда, в которой агенты работают над Motiff Lab: правила процесса, роли, таблица рисков, метрики и тесты ограждений.
Основа — Agent Operating Model v1.3 и Agent Environment Architecture (мягкий режим). Всё здесь — класс Guardrail:
меняется только задачей среды, применяет Андрей.

| Путь | Что это |
| --- | --- |
| `roles/common.md` | Общие правила процесса для всех ролей; читается до начала любой задачи |
| `roles/developer.md`, `roles/reviewer.md`, `roles/qa.md` | Роли мягкого режима: что делает, что видит, что нельзя, какой выход |
| `roles/council.md` | Advisory track: council moderator, seats, researcher; the order of rounds is the skill `.claude/skills/council` |
| `routing.md` | Routing: which of Andrei's intents goes to which role, and how to name a role explicitly |
| `risk-paths.yml` | Минимальный класс риска по путям |
| `metrics/` | Метрики M1–M11 и ограждений по каждой задаче: определения и скрипт `collect.mjs` |
| `tests/` | Тесты hooks из `.claude/hooks` и скрипта метрик: `node --test "governance/tests/*.test.mjs"` |

Рядом, вне этой папки: `.claude/settings.json` и `.claude/hooks/` (deny и hooks), `.claude/agents/` (council seats and
researcher, read only), `.claude/skills/council/` (the council procedure), `.github/ISSUE_TEMPLATE/task-record.yml`
(Task Record), `.github/pull_request_template.md`, `infra/github/protect.ps1` (ruleset на `main` и Environment
`production`, запускает Андрей).

Строгий режим добавит сюда профили ролей для ранеров (`roles/*.profile.yml`), роли контура среды (`env-roles/`),
поведенческие сценарии (`evals/`), тесты отказа (`failure-tests/`) и скрипт `tools/aom`.
