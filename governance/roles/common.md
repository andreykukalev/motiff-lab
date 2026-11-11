# Rules for every agent

Read `governance/README.md` (the process) first, then this file, then your role's file and `CLAUDE.md`.

## Working

- Do the work yourself. Split it or start another agent only when it truly needs it (team mode, a second Developer on
  files that do not overlap); never a thread or sub-agent for a routine step.
- Act; do not ask Andrei to confirm intermediate steps. Ask only about the six decisions in `governance/README.md`, or
  when a request can be read in ways that lead to different products. Every question names the task in a few words,
  links the exact place, restates the question with your recommendation and says how to answer.
- Decisions you take yourself go to the decision log (`/mnt/project-files/product/decisions.md`), one line: date, what,
  why, how to undo.
- Claims about the product cite a record (decision, idea, Andrei's message); claims about the code are checked in the
  code, never taken from memory or chat.
- Only Andrei's subscription pays for models: no API keys, no purchased tokens.
- GitHub and the repository's documents are in English; the chat with Andrei is in Russian, in plain words.

## Branches, commits and pull requests

- One change, one branch (`claude/*`), one pull request to `develop` (to `main` only for a release or a hotfix).
- Before every push of code: the checks that apply (`CLAUDE.md`, "Commands") and `node tools/quality/quality.mjs`.
  New code meets the thresholds; the quality checks block new violations. Never loosen
  `governance/quality/settings.json` or a tool's configuration to pass.
- Batch commits, push rarely: every push starts CI. No empty commits, no pushes only to restart CI.
- Commits keep the `Claude-Session` trailer. A pull request starts with the platform's attribution block, then the
  template.

## CI and heavy checks

- `build-test` runs on every pull request and stays quick (a few minutes): whitespace, build, unit, TypeScript, guard
  tests, code quality.
- The iPad macOS tests and the browser tests run on every version tag (`v*`), by hand, or by label (`ipad-tests`,
  `browser-tests`) when a change needs them before a version. For `ipad/App`, fix the expectations in
  `ipad/App/MotiffLabTests` in the same commit.

## Never

- Push to or merge into `main`; force push; delete branches other than your own merged one.
- Change GitHub settings (rulesets, Environments, secrets, variables) or anything in Azure; only Andrei runs
  `infra/azure/*.ps1` and `infra/github/*.ps1`.
- Merge a pull request that changes the agents' rules (decision 6) or the saved data format (decision 3).
- Use Andrei's computer, mail, calendar or drive unless he asks in this thread.
- Get around a guard that refused something: stop and say what was needed.
