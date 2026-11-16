# 0032 · Transition to iPad-first development and Web platform archival

Status: accepted, 2026-11-16 (Andrei's task «Archive Web Application and Transition to iPad-First Development», 17:59
and 18:03 UTC, thread «Перейти на iPad-first, заархивировать веб»); in force when its pull request merges. Puts the web
half of [0025](0025-two-studios.md) on hold, amends [0026](0026-project-continuity.md) section 3,
[0019](0019-native-ipad-app.md) section 3 item 1, [0018](0018-redesign.md) and [0027](0027-product-concept-v1.md) (see
"What this record changes"). Amended the same day by [0033](0033-repository-layout.md) (Andrei's 18:57 UTC task
«Repository Structure Reorganization»): the web moved from "archived in place" to `archive/web`, the parts the iPad
bundles to `src/shared`. The inventory of what was archived and how to restore it: [`docs/web-archive.md`](../../web-archive.md).

## Context

- The product is still evolving; its core experience (selection, playback, practice at the instrument, the assistant,
  the sources model) is being established through Andrei's test cycles on the iPad.
- Two actively developed clients cost more than they return at this stage. Measured on `develop` 0d2bc52
  ([REFACTORING_AUDIT.md](../../REFACTORING_AUDIT.md) sections 1–2): the web app is 38.6k lines of C# and Razor, 12.9k of CSS
  and 23.9k of TypeScript, with its own application layer (`PlaybackLab`) beside the facade's `OpenPiece`, its own
  page controller beside the iPad's, and 58 Playwright spec files that have been red since v0.6.1. Versions 0.6.1 to
  0.6.5 were iPad-only, and the list of iPad changes waiting to be ported to the web
  (`/mnt/project-files/product/web-backlog.md`) grew by eleven entries in two days.
- The iPad experience is the primary product direction (governance "Platform focus" since 2026-11-15, PR #592).
- The iPad app is not independent of the web project's folder: it bundles five TypeScript modules the web project
  compiles (`Scripts/score`, `roll`, `layout`, `storage`, `playback` → `wwwroot/js/…`), the interface words
  (`wwwroot/i18n`), the piano samples (`wwwroot/audio/piano`) and the example piece (`Playback/FurElise.musicxml`), and
  it signs in with the web app's External ID registration (`src/ipad/App/MotiffLab/Account.json`). Moving or deleting the
  web project would break the iPad build or force a refactoring of shared code, which this task excludes.

## Decision

1. **iPad is the primary and only actively developed client platform.** The product frame of 0025 (two specialised
   workspaces of one product) stays as the long-term architecture; its web Music Studio is on hold.
2. **Web development is suspended and the existing implementation is archived, frozen** (in place at first; in
   `archive/web` since 0033).
   - Restore point: the tag `v0.6.5` (`develop` d8050f4) and `main` at 9b98d6b, the last version in which the web was
     developed and deployed to the main address. Both already exist; nothing has to be reconstructed.
   - The project is split (0033, `docs/web-archive.md`): its **live shared part** (the five modules, words, piano,
     Verovio's npm build, example piece and the TypeScript build, owned by iPad work) is `src/shared`; its **frozen
     part** (everything else: pages, components, C# features and services, web-only scripts, styles, the mirrors of
     the design kit, service worker, hosting configuration, Playwright specs) is `archive/web`.
   - Frozen means: no features, fixes or ports, and no changes at all in `archive/`. It is out of the solution and of
     `build-test`, and it no longer compiles by itself (its scripts import the shared modules by paths that moved);
     `docs/web-archive.md`, "Reactivation", says how it builds again. The working web is the restore point.
3. **Web remains a potential future platform.** The API keeps serving the web origin, the sign-in registration keeps the
   web's redirects, the Static Web App and its environments stay in Azure, and the project file stays the contract both
   clients read (`docs/project-file.md`).
4. **Web development may resume** after the product experience and core capabilities have been validated and
   stabilised on the iPad. Resuming is a separate initiative and Andrei's decision (`governance/README.md`, decision 1);
   the path is in `docs/web-archive.md`, "Reactivation".

### Workflows

- `build-test` (`ci.yml`) builds the solution, which no longer lists the web, and runs the node tests in `tests/shared`.
  It does not publish, provision or deploy the web (since 0033 those steps are removed; they are in `ci.yml` at commit
  6088b98), so the main address and the web test site stay at their last deploy.
- `Browser tests` is kept as `archive/web/workflows/browser-tests.yml` and does not run.
- The `iPad` workflow and `src/ipad/build-version.sh` run `dotnet build src/shared` to compile the shared TypeScript;
  they need no web publish, deploy, site or browser test.
- A test version is the iPad app built from its tag; there is no web test site to check. A release to `main` releases
  the iPad app and the shared backend; it deploys no web.

### Infrastructure

| Component | Used by | What happens |
| --- | --- | --- |
| Static Web App `swa-motifflab` (Free plan) and its environment `develop` | Web only | Kept, no longer updated: the main address serves v0.6.5, the test site the last `develop` deploy. Free plan, no cost. Turning it off or deleting it is Andrei's decision (not needed) |
| `infra/main.bicep`, `main.bicepparam` | Web only | Kept; provisioned only when `WEB_ACTIVE` is `true` |
| Repository variables `DEPLOY_DEVELOP`, `WEB_HOST` | Web (deploy); `WEB_HOST` also feeds the API's and scan server's allowed origin | Kept unchanged |
| `infra/azure/develop.ps1`, `test-site-signin.ps1` | Web only | Kept for reactivation; not part of the active workflow |
| API, its Cosmos DB databases, scan server, Container Apps environments (main and `rg-motifflab-dev`), ghcr images, Azure OpenAI for the assistant | iPad, web, backend | Unchanged, operational |
| External ID tenant `motifflab`, registrations *Motiff Lab* (client) and the API | iPad and web | Unchanged: the iPad signs in with the web app's registration |
| CI identities `id-motifflab-github`, `id-motifflab-github-dev` | Backend deploys, web deploy | Unchanged |
| The old Static Web App in the Professional subscription | Nothing | Already waiting for Andrei to delete it (unchanged) |

### Saved data and releases

The project file stays one contract. While the web is frozen, the iPad app releases alone, whatever changes; 0026
section 3's rule "a change to the data contract releases the web and the iPad together" is suspended. Older readers
refuse a newer file (`ProjectFile` "saved by a newer version"), so the frozen web site cannot overwrite a project saved
in a later version; it keeps reading the versions it knows. Before the web returns, it gets the reader and writer of the
current version first.

### Agents and backlog

- No web tracks. Platform focus is iPad only (`governance/README.md`, `roles/common.md`).
- `web-backlog.md` stays the porting list for reactivation: one line when an iPad change alters behaviour a live shared
  module gives the web, so the web can be brought up to date without rediscovering it.
- Web-only GitHub issues keep their text and get the label `web-deferred` (blocked by the reactivation decision); none is
  closed or deleted.

## Consequences

- Engineering effort is concentrated on the iPad: no porting, no web test site, no browser suite to keep green.
- Web-specific development, testing and deployment leave the active workflow; the shared backend and domain (music-core,
  API, scan server, storage, sign-in) stay platform-independent and in service.
- The existing web implementation and its documents are preserved for reactivation: the restore point, the code in
  `archive/web`, the porting list and the reactivation path.
- Future web development is evaluated as a separate initiative.
- Costs accepted: the main web address keeps v0.6.5 with its known defects and the browser suite stays red until
  reactivation; the archived web is not compiled, so reactivating it starts with making it build again; Android-tablet users (Andrei's 2026-11-10 reason, in the project chat, for keeping the web the most complete version) have no maintained
  client meanwhile.
- The shared modules have their own home, `src/shared` (0033), and the frozen web project left the solution build.

## Alternatives considered

- **Delete the web code and keep only the tag.** Rejected: the iPad needs the shared modules, words and piano from that
  folder, and the task forbids deleting the web application.
- **Move the web project to an `archive/` folder.** Rejected at first (it changes every path the iPad bundles and the
  TypeScript build); adopted the same day by 0033, which moves the shared modules to `src/shared` first.
- **An archive branch.** Adds nothing over the existing tag and `main`; agents cannot push branches other than their own
  or tags from a cloud thread. Andrei may add a tag `web-archive` on 9b98d6b if he wants a named one.
- **Remove the web project from the solution now.** Rejected at first because the TypeScript build the iPad needs lived
  in it; done by 0033, which gives that build its own project (`src/shared/MotiffLab.Shared.csproj`).

## What this record changes in earlier records

- 0025: the frame stays; the web Music Studio (sections 2–3, and Andrei's 2026-11-10 word in the project chat that the web is the most complete version) is
  on hold until reactivation.
- 0026 section 3: suspended while the web is frozen (the iPad releases alone); applies again on reactivation.
- 0019 section 3, item 1: the web is no longer the development site; no web is deployed from `main` or `develop` (the API and scan server deploy as before).
- 0018: the redesign continues on the iPad only; its web screens stay as built at v0.6.5.
- 0027: "the web is a full environment, not a practice app" (Consequences) is on hold with the web.
- 0014: the Static Web App part is frozen; the API and scan server are unchanged.
