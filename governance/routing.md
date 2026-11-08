# Маршруты: намерение → роль

Как координатор решает, кого звать, когда Андрей пишет о своём намерении. Правила ролей — в `roles/`, общие — в
`roles/common.md`.

## Два способа

- **Андрей называет роль сам**: «созови совет по …», «ревьюер для PR #…», «QA по PR #…», «исследователь: сколько …».
  Координатор зовёт названную роль. Отступает от этого только тогда, когда названная роль нарушила бы независимость
  (например, ревью тем же тредом, который писал код): тогда он говорит, почему, и зовёт роль в новом треде.
- **Андрей роль не называет**: координатор выбирает по таблице ниже и одной строкой пишет, какую роль выбрал и
  почему. Если строк подходит несколько или ни одной — спрашивает, а не угадывает.

Имена ролей, которые понимает координатор: совет (модератор), исследователь, разработчик (Developer), ревьюер
(Reviewer), QA, задача среды, product auditor, acceptance designer, UX designer. Роли, которые ещё не заведены, Андрей тоже может назвать: координатор ответит, что
роли нет, и предложит ближайшую из таблицы.

## Таблица

| Намерение Андрея | Роль | Где работает | Что нужно до старта | Выход |
| --- | --- | --- | --- | --- |
| Архитектурная или продуктовая развилка, которую дорого отменить; «созови совет» | Модератор совета, полный совет (`roles/council.md`, skill `council`) | Новый тред; места — субагенты в нём | Вопрос от Андрея; бриф, который он подтвердил | Запись решения (Claude Doc), вопросы Андрею |
| Развилка поменьше, «быстро сравни варианты» | Модератор, лёгкий совет | Новый тред | То же | То же, два раунда |
| Developer остановился на контракте (facade, файл проекта, API, Cosmos) или архитектурном вопросе | Модератор, лёгкий совет | Новый тред; задача доставки ждёт | Остановка записана в Issue | Запись решения, затем решение Андрея |
| «Сколько…», «как часто ломалось…», факты до решения | Исследователь (`.claude/agents/researcher.md`) | Субагент в треде, которому нужны факты | Вопросы и SHA | Лист фактов с источниками |
| «Сделай …», Task Record ещё нет | Координатор (Intake) | Чат проекта | — | Issue по форме Task Record на утверждение |
| Есть Issue с `aom:criteria-approved` | Developer (`roles/developer.md`) | Новый тред | Метка утверждения от Андрея | PR на зелёном CI |
| PR открыт, CI зелёный; «ревьюер для PR #…» | Reviewer (`roles/reviewer.md`) | Новый тред, не тред Developer | Номер Issue и PR, без пересказа | Комментарий `aom:review` |
| После review; «QA по PR #…» | QA (`roles/qa.md`) | Новый тред, не тред Developer | Отчёт Reviewer | Комментарий `aom:qa`, шаги для Андрея |
| Замечания в PR, «поправь по review» | Developer как Feedback Resolver | Тред Developer этой задачи | Отчёт Reviewer | Новые коммиты, затем повторные review и QA |
| Новая роль, правка правил, hooks, CI, `CLAUDE.md`, `tests/acceptance` | Задача среды (E3) | Новый тред, затем независимый Reviewer | Task Record с `aom:criteria-approved`, как у любой задачи | PR в среду агентов, применяет Андрей |
| A redesign of a screen or mechanic; "show me variants" | UX designer (`roles/ux-designer.md`), variants before any PR | A new thread, no PR | Andrei's request or a draft UI Task Record | Variants for approval, then the chosen one in the Task Record's criteria |
| "What is worth keeping", an audit before a rebuild or a release | Product auditor (`roles/product-auditor.md`) | A new thread | Andrei's request with the scope; the decision frame, if any | A feature registry with verdicts and draft Task Records |
| Acceptance scenarios for journeys or rules | Acceptance designer (`roles/acceptance-designer.md`) | A new thread, never the Developer of the task they check | Andrei's request with the journeys and the level | A scenario list for approval, then an environment task for `tests/acceptance` |
| Слить PR, выпустить, задеплоить, поменять настройки GitHub или Azure | Только Андрей | — | — | Координатор даёт шаги, сам не делает |
| Вопрос, на который можно ответить по документам | Координатор отвечает сам | Чат проекта | — | Ответ со ссылкой |

## Ещё не заведены

Waiting for a decision: the product profiles "web" and "iPad" from the multi-agent design doc
(https://claude.ai/artifact/UtpGa79GKqHQeE9wga1eM3). Until they exist, such intents go to the nearest row of the table,
or to Andrei as a question.

Внешнее место совета другого семейства моделей — на паузе (`roles/council.md`).

## Где роли загружаются сами

- **Claude Code на компьютере Андрея** читает `.claude/agents/` и `.claude/skills/` репозитория: роль можно позвать
  по имени (`@agent-researcher` [не проверено, сверить на Mac], `/council <тема>`). Папка совета и вопросы
  Андрею там другие: skill `council`, «Где идёт совет».
- **Облачные треды claude.ai** на 2026-11-08 их не читали (и не применяли `.claude/settings.json`): тред читает файл
  роли сам и передаёт его субагенту промптом, как описано в skill `council`, раздел «Запуск мест».
