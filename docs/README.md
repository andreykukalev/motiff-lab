# Motiff Lab documentation

Product concept and specification for Motiff Lab, exported from the shared specification doc on 2026-10-22. Read the Overview first; Stage 1 is what gets built first.

| File | What it covers |
| --- | --- |
| [00-overview.md](00-overview.md) | Vision, principles, users, journey and modules, development stages, scope, business model, roadmap, risks |
| [01-mvp-stage-1.md](01-mvp-stage-1.md) | **First milestone:** notation-based motif exploration (MIDI, MusicXML): core journey, requirements S1-…, rhythm restoration, variation engine and Keep table, learning modes, AI assistance, screens, acceptance criteria, architecture, validation, milestones, open decisions |
| [02-brand-identity.md](02-brand-identity.md) | Name, positioning, tagline, voice, messaging, palette with contrast checks, typography, verification checklist |
| [03-prd.md](03-prd.md) | Stages 2–3: user stories, functional requirements FR-…, hint system, feedback rules, variation operations, acceptance criteria, edge cases, NFRs, analytics events |
| [04-mvp-ux.md](04-mvp-ux.md) | Stages 2–3: screen map, layout rules, flows 1–6 (S1–S12), system states |
| [05-domain-architecture.md](05-domain-architecture.md) | Shared domain model: audio assets, musical events and layers, motifs, corrections, transformations, alignment, playback, analysis pipeline, system architecture, storage and API |
| [06-validation-plan.md](06-validation-plan.md) | Hypotheses, three studies, accuracy benchmark, product metrics, decision rules, consent |
| [images/](images/) | Diagrams and wireframes used in the documents |
| [source/](source/) | Original concept, vision, MVP and brand notes the specification was built from |
| [decisions/](decisions/) | Architecture decision records: 0001 sets the .NET and Azure stack (it supersedes the Rust, Flutter and FastAPI choices in 05); 0002 defines canonical musical time; 0003 adds AI assistance through Azure AI Foundry; 0004 fixes the note event shape and the meter map |

## How the documents relate

- Development happens in four stages. Stage 1 is specified in `01-mvp-stage-1.md`.
- `03-prd.md`, `04-mvp-ux.md` and `06-validation-plan.md` specify Stages 2–3 (guided reconstruction and audio). Their "phase 1–4" labels are that release plan's own phases, which start after the Stage 1 exit gate.
- `05-domain-architecture.md` is shared by all stages. Stage 1 uses it with notation input and runs fully in the browser.
- "Motif Lab" in older documents is the earlier working title for Motiff Lab.

## Conventions

- Requirement IDs: `S1-…` for Stage 1 (`S1-R…` rhythm restoration, `S1-AI…` AI assistance, `AC-S1-…` Stage 1 acceptance criteria), `FR-…` and `NFR-…` for Stages 2–3, `AC-…` acceptance criteria, `US-…` user stories, `V1–V12` variation operations, `H…` hints and hypotheses.
- When code implements a requirement, reference its ID in the commit message or PR.
