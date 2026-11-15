# UI/UX Baseline

The product behaviour Andrei has explicitly approved. It is a contract for every agent: a feature is not done if it
breaks a rule here, and preserving these rules is as much the work as building the new thing (Andrei, 2026-11-15,
issue #582 item 18).

The document describes behaviour, not looks: interaction patterns, the player's workflows, how parts of the screen
relate, constraints that must not change, product decisions already validated. Colours, sizes and spacing belong to
`design/kit` and `docs/ui-conventions.md`, not here.

## Who may add or change a rule

Only Andrei. A rule enters, changes or leaves this document only when he asks for that specific behaviour in his own
words. An agent never promotes an observation, a temporary implementation, a mockup or its own assumption into a rule;
it may propose candidates to him (in the version note or thread), and they stay candidates until he says yes.

This file is under `governance/`, so a pull request that changes it is merged by Andrei (governance/README.md,
decision 6). Each rule links his words.

## How an agent uses it

1. Before changing any UI or interaction, read the rules of the screens and flows you touch.
2. Keep every rule's tests green. When a baseline test fails, fix the code, not the test.
3. If a new request contradicts a rule, do not choose silently: name the rule and the conflict in the pull request and
   in your report, and ask Andrei (through the lead) which one stands. Until he answers, the rule stands.
4. When Andrei approves a new rule: write it here in the format below, write its tests in the same pull request, and
   ask him to merge it.

## Rule format

Each rule is a level-3 heading `### UXB-<n>: <short name>` followed by:

- **Rule:** the behaviour, in one to three sentences the player would recognise.
- **Approved:** the date and a link to Andrei's words (thread message, issue or version note).
- **Applies to:** web, iPad or both.
- **Tests:** one or more test references, each `` `path` — `test name` ``. The test name contains the tag `[UXB-<n>]`.

Numbers are never reused; a removed rule stays as a heading with **Removed:** the date and Andrei's words.

## Tests

Every rule has at least one test that checks the behaviour itself, ideally also one that runs it through a whole
workflow with the neighbouring parts of the screen. Baseline tests live in files of their own, so that a change to them
is visible and protected like the acceptance scenarios (`tests/acceptance/README.md`): a pull request that changes them
needs Andrei's label `protected-scenarios-ok`.

| Kind | Where | Runs |
| --- | --- | --- |
| Pure rules of the web and the iPad score (TypeScript) | `tests/web/baseline-*.test.mjs` | Every pull request (`build-test`) |
| Browser workflows on the published web app | `tests/e2e/baseline-*.spec.mjs` | Every version tag, label `browser-tests` |
| iPad behaviour | `ipad/App/MotiffLabTests/Baseline*.swift` | Every version tag, label `ipad-tests` |
| Music Core behaviour behind a rule | `tests/acceptance/` | Every pull request |

`governance/tests/ux-baseline.test.mjs` (every pull request) checks that each rule has the fields above, that each
referenced test file exists and contains its test with the rule's tag, and that no test carries the tag of a rule that
does not exist.

## Rules

None yet. Rules are added here only on Andrei's word.
