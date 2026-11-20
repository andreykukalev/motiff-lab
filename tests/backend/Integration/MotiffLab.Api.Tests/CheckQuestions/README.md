# The assistant's check questions

Status: current, 2026-11-20 (version 1.2, the assistant's option A). New; replaces nothing.

The set of questions the assistant's prompts and model are measured with ([0028](../../../../../docs/architecture/adr/0028-ai-bar-edits.md),
Consequences; [0003](../../../../../docs/architecture/adr/0003-ai-integration.md) section 8): `check-questions.json`, about
the example piece, Für Elise (`src/shared/Playback/FurElise.musicxml`). Each question has an id, its language (`en`,
`ru`), whether it is `general` (how to learn, practise or play) or `specific` (a section, bars named in words, or the
bars selected: `bars`), and `near`: id prefixes of the facts the engine's template for it must cite (empty: no
expectation). Each is asked as the iPad asks it: the proposer, the context music-core makes for the piece or the
selected bars, the model's tool calls run by music-core, turn after turn through the relay until the answer
(`CheckQuestions.cs`).

Add a question to the list when a real question went wrong; keep both languages.

## In CI: with a fake model

`CheckQuestionsTests` runs every question with a fake model that answers with advice citing nothing: each must make a
turn within the limits, be retried once, then get the template with the reason `noCitation` and facts near the
question. No network, nothing costs anything:

```bash
dotnet test tests/backend/Integration/MotiffLab.Api.Tests --filter "FullyQualifiedName~CheckQuestions"
```

## By hand: with the real model

It costs money (about 24 questions, each one to ten model calls of 3–5 thousand tokens), so it never runs in CI and
only with Andrei's go-ahead (decision 4). Run it before a change of a prompt (`src/backend/MotiffLab.Api/Assistant/Prompts`)
or of the model, with the settings of the model under test as environment variables (the same as the API's,
`infra/README.md`, «The assistant's model»):

```bash
export MOTIFF_CHECK_QUESTIONS=real
export Assistant__Provider=anthropic        # or foundry, azure-openai
export Assistant__Key=...                   # the provider's key; never commit it
export Assistant__Model=                    # empty for the default; the deployment for foundry or azure-openai
export Assistant__Endpoint=                 # the resource for foundry or azure-openai
export MOTIFF_CHECK_QUESTIONS_REPORT=$PWD/check-questions-report.md
dotnet test tests/backend/Integration/MotiffLab.Api.Tests --filter "FullyQualifiedName~CheckQuestionsRealModelTests" --logger "console;verbosity=detailed"
```

`CheckQuestionsRealModelTests` writes a Markdown table: each question's outcomes in the API's log (`toolCalls`, `answer`,
`retried:<reason>`, `fallback:<reason>`, `timeout`), the reason of a fallback, the tool calls, the answer's length, the
ids it cites and the answer itself. It fails when more than 5 % of the answers fall back (0003 section 8); read the
answers too: the check proves only that they cite listed facts, not that what they say is right.
