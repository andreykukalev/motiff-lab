# Contributing to Motiff Lab

Most changes here are made by agents under the rules in [`CLAUDE.md`](CLAUDE.md) and [`governance/`](governance/README.md);
the same rules apply to anyone. Read them first: the product rules and «Writing code» in `CLAUDE.md`, how versions,
branches and reviews work in `governance/README.md`.

## Where things go

The layout and the reasons for it: [`docs/repository-structure.md`](docs/repository-structure.md) (decision record
[0033](docs/architecture/adr/0033-repository-layout.md)). In short:

- active code in `src/`: `src/backend` (music-core, API, scan server), `src/ipad` (the iPad app), `src/shared` (the
  score modules, words and piano the iPad bundles);
- tests in `tests/`: `tests/backend/{Unit,Integration,Acceptance}`, `tests/ipad`, `tests/shared`, `tests/contracts`
  (the scan server keeps its pytest tests in `src/backend/omr/tests`);
- `archive/` is frozen: the web app (0032) is not built, tested or changed;
- decision records in `docs/architecture/adr`, listed in [`docs/README.md`](docs/README.md).

## Before a pull request

Requires the .NET 10 SDK, Node 22 or later, Docker for SwiftLint, and a Mac with Xcode and XcodeGen for the iPad app.

```bash
dotnet build MotiffLab.slnx
dotnet test MotiffLab.slnx
node --test "tests/shared/*.test.mjs"           # after the build: it compiles src/shared
node --test "governance/tests/*.test.mjs"
node tools/quality/quality.mjs                  # the ratchet: no new violation
dotnet format whitespace --folder --verify-no-changes
(cd src/backend/omr && python -m pytest -q)     # when the scan server changed
```

The iPad app is built and tested on a Mac (`src/ipad/README.md`), or by the `iPad` workflow with the label `ipad-tests`.

## Pull requests

- Open them against `develop`, filled in from [`.github/pull_request_template.md`](.github/pull_request_template.md);
  GitHub text in English.
- A pull request that changes `CLAUDE.md`, `governance/`, `.claude/`, `.github/` or `tests/backend/Acceptance/` is
  merged by Andrei; one that changes the acceptance scenarios also needs his label `protected-scenarios-ok`.
- `main` is Andrei's: it moves only by a release.
