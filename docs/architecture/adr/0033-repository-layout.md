# 0033 · Repository layout: one source tree, one test tree, the web in an archive

Status: accepted, 2026-11-16 (Andrei's tasks «релокейт папки iPad в папку SRC», 18:33 UTC, and «Repository Structure
Reorganization and Standardization», 18:57 UTC, with his choices of `tests/ipad` for the Swift tests and `src/shared` for
what the iPad shares with the web, 19:00 UTC). Amends [0032](0032-ipad-first-web-archived.md) (the web is archived in
`archive/web`, no longer in place) and [0019](0019-native-ipad-app.md) section 1.4 (the iPad's score scripts are
`src/shared`'s). Changes no code, behaviour, saved data or contract: only where files live. The layout and the table of
moves: [`docs/repository-structure.md`](../../repository-structure.md).

## Context

- Active code sat in four places at the top level: `ipad/`, `src/` (music-core, the API and the web), `services/omr`,
  and the tests in `tests/` without a split by kind, with the iPad's tests inside the app's folder.
- After 0032 the web was archived but still in the solution and in `build-test`, and the five TypeScript modules, words,
  piano and example piece the iPad bundles lived inside the frozen web project, so "frozen" and "maintained" shared one
  folder.

## Decision

1. **All active production code is under `src/`**: `src/backend` (music-core, the API, the scan server `omr`),
   `src/ipad` (the app, its NativeAOT core, Verovio) and `src/shared` (the TypeScript modules the iPad's score page runs,
   the interface words, the piano, Verovio's npm build and the example piece, compiled by
   `src/shared/MotiffLab.Shared.csproj`).
2. **All tests are under `tests/`, by kind and platform**: `tests/backend/{Unit,Integration,Acceptance}`, `tests/ipad`
   (XCTest and the UI walks, built from `src/ipad/App/project.yml`), `tests/shared` (node tests) and `tests/contracts`
   (fixtures both sides of a contract read). No empty folder is made for a kind that has no tests yet (`E2E`: the only
   end-to-end tests are the archived web's Playwright specs).
3. **Each platform keeps its native conventions inside its folder.** .NET: one project per folder with its `.csproj`,
   the solution stays `MotiffLab.slnx`. Swift: XcodeGen's `project.yml` with relative paths. Python: the scan server keeps
   its `tests/` and `requirements*.txt` beside its code, the folder its Docker build and `python -m pytest` run in.
4. **Archived code is in `archive/`** and is not built, tested, linted or deployed: the web is `archive/web/{src,tests,
   tools,workflows}`; `archive/Directory.Build.props` is empty so the root MSBuild rules do not reach it; CI treats a
   change there like documentation; the quality check and jscpd skip it; its risk class is low. Nothing in `archive/` is
   changed; bringing a part back moves it out again (`docs/web-archive.md`, "Reactivation").
5. **Decision records are in `docs/architecture/adr`.**
6. Kept as they are: the solution format `.slnx`, `tools/` for repository tools, no `Directory.Packages.props` (the
   projects pin their own versions), `infra/` and `design/kit`.

## Consequences

- The top level says what is live (`src`, `tests`) and what is not (`archive`); a frozen file and a maintained one no
  longer share a folder.
- `build-test` builds less: no Blazor project and no web publish, deploy or browser steps.
- The archived web does not compile by itself: its scripts import the shared modules by relative paths that moved, and
  its project no longer has the shared words, piano and example piece. Reactivation makes it build against `src/shared`
  (`docs/web-archive.md`, step 3); until then its run-time behaviour is the restore point's (0032).
- The web's mirrors of the design kit (`wwwroot/css/tokens.css`, fonts, `img/icons.svg`) are no longer checked against
  the kit; `node tools/icons/build.mjs` still writes the sprite.
- `src/ipad/build-version.sh` builds a tag made before this layout with the old paths, so old versions still build.
- Paths in issues, pull requests and the project's own notes written before 2026-11-16 name the old layout; the table
  in `docs/repository-structure.md` maps them.

## Alternatives considered

- **`tests/backend/E2E` and `tests/web` as empty folders now.** Rejected: an empty folder says nothing and git keeps
  none; they appear with their first test.
- **The scan server's tests in `tests/backend`.** Rejected: they import `app` as a package from the scan server's own
  folder (`tests/` beside `app/`, Python's usual layout), so moving them changes how they are run, not only where.
- **Keep the web in the solution, archived in place (0032 as first written).** Rejected by Andrei's task: the archive is
  to be outside the active build.
- **Rename `tools/` to `scripts/`, move to `.sln`.** Not needed: both work and every guide names them.
