# How Motiff Lab is developed

Agreed with Andrei on 2026-11-11. It replaces the Agent Operating Model (Task Records, per-PR Reviewer and QA,
councils). The product is steered by **working versions**: agents prepare a version, Andrei installs it on his iPad and
opens the web test site, tries it at the instrument, and his feedback becomes the next version. Documents serve the
versions, not the other way round.

Every agent reads this file, `roles/common.md` and its role's file before it starts. `CLAUDE.md` holds the product and
code rules.

## Andrei decides only these six things

1. The product concept: what the product is, for whom, the central scenarios, the roles of the web and the iPad.
2. Removing or noticeably cutting a feature that exists.
3. A change of the saved data format that affects projects already saved (project file, facade contract, HTTP API).
4. Anything that costs money or adds an external service.
5. A release to `main` (his «в стабильную» in a version thread, or «ок» for a hotfix).
6. The agents' own rules: `CLAUDE.md`, `governance/`, `.claude/`, `.github/`, `tests/acceptance/`. A pull request that
   changes them is merged by Andrei.

Everything else agents decide themselves, openly: each such decision is one line in the decision log (what, why, how
to undo it), and each version note lists them under «Решено без вас».

## Branches

- `main`: stable versions only. Moves by a release (Andrei's decision 5) or a hotfix. Agents never push or merge to it.
- `develop`: the line of development. Always builds, always green. Agents open pull requests to it and merge them
  themselves when `build-test` (with the code-quality checks) is green. Every push deploys the web test site.
- Task branches: short, one per change, `claude/*`. Parallel agents work on branches whose files do not overlap.
- `hotfix/<version>`: from the last stable tag, for an urgent fix of a stable version.

## Versions

- A **test version** is a snapshot of `develop`, tagged `v0.<n>` (later `v<major>.<minor>`) by the `Version` workflow
  when a pull request changes the file `VERSION` (agents cannot push tags from a cloud thread). It is cut when `develop`
  has at least one change Andrei can see and everything is green, at most once every 2–3 days. What has merged goes
  in; the rest waits for the next one. Nothing waits for a feature.
- If the two previous versions have no feedback yet, no new version is cut; work goes on.
- A **stable version** is the test version Andrei calls «в стабильную»: one independent review of everything since the
  last stable version (`roles/release-reviewer.md`), then `develop` is merged to `main` by a pull request Andrei merges,
  and the tag stays.
- A **hotfix**: branch from the stable tag, fix, tests and the quality checks, Andrei installs it and writes «ок», then
  a pull request to `main` that sets `VERSION` to `<x>.<y>.1` (the workflow tags it) and the same fix merged to `develop` at once. No release review.
- **Bugs**: in a test version, fixed in `develop` and shipped with the next version; one that blocks testing (a crash, a
  piece that will not open) gets a same-day version `v0.<n>.1`; in a stable version, a hotfix.
- **Rollback** of a broken version: Andrei installs the previous tag (iPad) or the release is reverted on `main`.
- Where to see a version: the iPad app is built by Andrei on his Mac from the tag with one command
  (`ipad/build-version.sh`, no paid developer account, 7-day signing); the web test site is the Static Web App's
  environment `develop`; the main web address changes only from `main`.

## Experiments

Every new idea goes into the product first as an **experiment**: in its natural place in the existing flows, with a
minimal UI, behind a switch in Settings, section «Экспериментальное» (off by default). Big features merge in parts behind
the same switch, so `develop` always builds. Only an experiment Andrei tried and called «оставить» becomes a feature:
finished UX, tests, switch removed, concept and decision log updated. «Убрать» removes its code from `develop` and work
continues from the present state; the ideas bank keeps "tried, rejected, because …". No exceptions: no new idea goes
straight into the product.

An idea that needs a shared change (saved data format, Music Core structure model) cannot hide behind a switch: it goes
to Andrei (decision 3) and the architect first, and its first version is built without the format change where possible.

UX: mockups first only for a big new screen; everything else is tried in the app behind the switch, sometimes two
variants side by side.

## Feedback

- On a version: a reply in that version's thread. The product lead opens one thread per version with its note (what is
  new, what to check, what is uncertain, «Решено без вас», models and limits used) and closes it when the next one comes.
- An idea or a bug outside a version: the project chat. «Идея:» or «Баг:» helps but is not required.
- Each message becomes a record linking Andrei's original words: a bug is a GitHub issue with the version (English); an
  idea goes to the ideas bank. Andrei gets one line back: «записал: 2 бага, 1 идея», with links.
- Agents work from these records, never from long chat history.

## The product lead's memory

Private, in Russian, in the project files (`/mnt/project-files/product/`), not in this public repository:

| File | What it is | Rule |
| --- | --- | --- |
| `concept.md` | The product concept, 2–3 pages | Rewritten, not appended; Andrei approves changes (decision 1) |
| `decisions.md` | The decision log, one line each: date, what, why, who, how to undo | Append only; read the tail and search |
| `ideas.md` | The ideas bank: each idea with its status (new, experiment, kept, rejected and why) | One entry per idea |
| `state.md` | One page: current version, what is in work, open questions to Andrei | Rewritten at every version |

Bugs and development tasks are GitHub issues. A claim about the product cites a record; a claim about the code is
checked in the code. At each stable version the lead tidies `concept.md` and `ideas.md`.

## Agents and models

Sonnet is not used. `model` and `effort` are set at the start of each thread by the project instructions.

| Role | File | When | Model |
| --- | --- | --- | --- |
| Product lead | `roles/product-lead.md` | Always: one per version thread | Opus high |
| Recording chat messages | `roles/product-lead.md` | Each message in the project chat | Opus medium |
| Ideas partner | `roles/ideas-partner.md` | When Andrei brings an idea or asks | Opus high |
| Architect | `roles/architect.md` | A change touches Music Core, both apps or saved data | Opus high |
| Developer | `roles/developer.md` | One per change | Opus high (features, Music Core, iPad, data); Opus medium (small UI, texts, a bug with a clear cause) |
| Version mechanics | `roles/product-lead.md` | Tag, note, thread | Opus medium |
| Release reviewer | `roles/release-reviewer.md` | Before a release to `main` | Opus high |

Fable 5.2 is used only for: rewriting the product concept (the one author who converges it), team mode on a decision
that is costly to reverse, and a task that failed twice on Opus high (CI or the quality checks red again, or going in
circles). The lead picks the class when it starts a task and steps up (medium → high → Fable) when a task proves
harder.

One Developer by default; a second in parallel only when the tasks share no files and the limits allow. No threads or
sub-agents for routine steps.

## Team mode

Several agents in parallel pay off where the work is thinking that needs variety, and harm where one coherent result or
one code base is needed. The rule: **they diverge in parallel, one converges, Andrei decides.**

- Diverge: two or more agents each carry their own variant to the end without seeing each other, from different
  starting conditions (for example "the smallest change to what exists" and "as if from scratch"), ideally on
  different models (Opus and Fable). Then each attacks the other's variant.
- Converge: one author. A hybrid is redesigned by one author as a whole, never assembled from pieces by a committee.
- Critique: several critics from different places (a pianist at the instrument, a teacher, an engineer counting the
  cost) are cheap and useful.
- Choose: Andrei, or the lead when the choice changes nothing on the list of six.

The lead may start team mode by itself for an architecture fork that is costly to reverse, and tells Andrei. For the
product concept, only on Andrei's word («собери команду»). Team mode costs about 2–4 times a single agent.

## Guards

Soft mode: all agents act under Andrei's GitHub identity, so the guards catch mistakes but cannot make them impossible.

| Guard | Where | What it does |
| --- | --- | --- |
| Rulesets `main` and `develop` | GitHub (`infra/github/protect.ps1`, run by Andrei) | Pull requests only, `build-test` must pass, no force push, no deletion |
| Environment `production` | GitHub | Deploys of the API and the scan server from `main` wait for Andrei; `develop`'s go to `rg-motifflab-dev` (test data only) by themselves |
| Deny rules and hooks | `.claude/settings.json`, `.claude/hooks/` | On Andrei's computer only (a claude.ai cloud thread does not load them): refuse merges, workflow runs, settings, Azure, force push, pushes to other branches |
| `env-scope` | CI | Marks a pull request that changes the agents' rules (decision 6) |
| Protected scenarios | CI, label `protected-scenarios-ok` | A change to `tests/acceptance/` needs Andrei's label |

`risk-paths.yml`, `metrics/` and `tests/` are kept from the previous process: the guards' tests and the acceptance
check still use them.
