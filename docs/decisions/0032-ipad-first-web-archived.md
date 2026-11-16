# 0032 · Transition to iPad-first development and Web platform archival

Status: accepted, 2026-11-16 (Andrei's task «Archive Web Application and Transition to iPad-First Development», 17:59
and 18:03 UTC, thread «Перейти на iPad-first, заархивировать веб»); in force when its pull request merges. Puts the web
half of [0025](0025-two-studios.md) on hold, amends [0026](0026-project-continuity.md) section 3,
[0019](0019-native-ipad-app.md) section 3 item 1, [0018](0018-redesign.md) and [0027](0027-product-concept-v1.md) (see
"What this record changes"). The inventory of what was archived and how to restore it: [`docs/web-archive.md`](../web-archive.md).

## Context

- The product is still evolving; its core experience (selection, playback, practice at the instrument, the assistant,
  the sources model) is being established through Andrei's test cycles on the iPad.
- Two actively developed clients cost more than they return at this stage. Measured on `develop` 0d2bc52
  ([REFACTORING_AUDIT.md](../REFACTORING_AUDIT.md) sections 1–2): the web app is 38.6k lines of C# and Razor, 12.9k of CSS
  and 23.9k of TypeScript, with its own application layer (`PlaybackLab`) beside the facade's `OpenPiece`, its own
  page controller beside the iPad's, and 58 Playwright spec files that have been red since v0.6.1. Versions 0.6.1 to
  0.6.5 were iPad-only, and the list of iPad changes waiting to be ported to the web
  (`/mnt/project-files/product/web-backlog.md`) grew by eleven entries in two days.
- The iPad experience is the primary product direction (governance "Platform focus" since 2026-11-15, PR #592).
- The iPad app is not independent of the web project's folder: it bundles five TypeScript modules the web project
  compiles (`Scripts/score`, `roll`, `layout`, `storage`, `playback` → `wwwroot/js/…`), the interface words
  (`wwwroot/i18n`), the piano samples (`wwwroot/audio/piano`) and the example piece (`Playback/FurElise.musicxml`), and
  it signs in with the web app's External ID registration (`ipad/App/MotiffLab/Account.json`). Moving or deleting the
  web project would break the iPad build or force a refactoring of shared code, which this task excludes.

## Decision

1. **iPad is the primary and only actively developed client platform.** The product frame of 0025 (two specialised
   workspaces of one product) stays as the long-term architecture; its web Music Studio is on hold.
2. **Web development is suspended and the existing implementation is archived in place, frozen.**
   - Restore point: the tag `v0.6.5` (`develop` d8050f4) and `main` at 9b98d6b, the last version in which the web was
     developed and deployed to the main address. Both already exist; nothing has to be reconstructed.
   - The code stays where it is (`src/MotiffLab.Web`, `tests/e2e`), so the iPad's bundled paths do not move and no
     shared code is restructured. `docs/web-archive.md` splits the folder into its **live shared part** (the five
     modules, words, piano, example piece, the TypeScript build and the mirrors of the design kit, owned by iPad work
     from now on) and its **frozen
     part** (everything else: pages, components, C# features and services, web-only scripts, styles, service worker,
     hosting configuration, Playwright specs).
   - Frozen means: no features, fixes or ports. It keeps compiling in `build-test`, which keeps it recoverable and costs
     nothing extra; a change elsewhere that breaks its compilation gets the smallest mechanical fix in the same pull
     request, never behaviour work. Its run-time behaviour on `develop` is not maintained (the shared modules move on
     with the iPad): the working web is the restore point.
3. **Web remains a potential future platform.** The API keeps serving the web origin, the sign-in registration keeps the
   web's redirects, the Static Web App and its environments stay in Azure, and the project file stays the contract both
   clients read (`docs/project-file.md`).
4. **Web development may resume** after the product experience and core capabilities have been validated and
   stabilised on the iPad. Resuming is a separate initiative and Andrei's decision (`governance/README.md`, decision 1);
   the path is in `docs/web-archive.md`, "Reactivation".

### Workflows

- `build-test` (`ci.yml`) still builds the whole solution, the frozen web included, and runs the node tests in
  `tests/web` (they cover the live shared modules too). It no longer publishes or deploys the web app: publishing,
  provisioning `infra/main.bicep` and deploying to Static Web Apps run only when the repository variable `WEB_ACTIVE`
  is `true` (it is not set, so both the main address and the web test site stay at their last deploy).
- `Browser tests` (`browser-tests.yml`) run only by hand: no daily run, no run on version tags, no label.
- The `iPad` workflow and `ipad/build-version.sh` are unchanged: they run `dotnet build src/MotiffLab.Web` only to
  compile the live shared TypeScript; they need no web publish, deploy, site or browser test.
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
  place, the porting list and the reactivation path.
- Future web development is evaluated as a separate initiative.
- Costs accepted: the main web address keeps v0.6.5 with its known defects and the browser suite stays red until
  reactivation; the frozen web keeps compiling in `build-test`, so a public music-core change may need a mechanical edit
  there; Android-tablet users (Andrei's 2026-11-10 reason, in the project chat, for keeping the web the most complete version) have no maintained
  client meanwhile.
- The live shared modules still sit in the web project's folder. Giving them their own home (REFACTORING_AUDIT Wave 3)
  belongs to the separate refactoring task; after it, the frozen web project can leave the solution build.

## Alternatives considered

- **Delete the web code and keep only the tag.** Rejected: the iPad needs the shared modules, words and piano from that
  folder, and the task forbids deleting the web application.
- **Move the web project to an `archive/` folder.** Rejected: it changes every path the iPad bundles and the TypeScript
  build, and separating the shared modules first is a refactoring of shared architecture, outside this task.
- **An archive branch.** Adds nothing over the existing tag and `main`; agents cannot push branches other than their own
  or tags from a cloud thread. Andrei may add a tag `web-archive` on 9b98d6b if he wants a named one.
- **Remove the web project from the solution now.** Rejected for now: the TypeScript build the iPad needs lives in it.

## What this record changes in earlier records

- 0025: the frame stays; the web Music Studio (sections 2–3, and Andrei's 2026-11-10 word in the project chat that the web is the most complete version) is
  on hold until reactivation.
- 0026 section 3: suspended while the web is frozen (the iPad releases alone); applies again on reactivation.
- 0019 section 3, item 1: the web is no longer the development site; no web is deployed from `main` or `develop` (the API and scan server deploy as before).
- 0018: the redesign continues on the iPad only; its web screens stay as built at v0.6.5.
- 0027: "the web is a full environment, not a practice app" (Consequences) is on hold with the web.
- 0014: the Static Web App part is frozen; the API and scan server are unchanged.
