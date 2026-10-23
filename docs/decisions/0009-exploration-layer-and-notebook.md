# 0009 · Exploration layer and notebook

Status: proposed, 2026-10-23. To be accepted or changed after the Für Elise pilot ([07-product-definition.md](../07-product-definition.md)).

## Context

The product definition puts a track page in front of the engine: things worth noticing, experiments, musical DNA and "make it your own". Each line on it is a musical fact, an interpretation or question that cites facts, or a product action (an operation from the catalogue with its parameters and Keep preset). The player's predictions, observations, next moves and free process notes are kept in one notebook.

Stage 1 already has most of the pieces:

- music-core computes facts (S1-A1 to S1-A5) and, for AI, builds a `FactDigest` with fact IDs.
- The variation catalogue (V1–V12) and the Keep table say which operations exist and what they keep.
- IndexedDB holds the project with autosave and an export file (S1-P1, #16).
- The data model has a `LearningRecord`: prompts answered and choices made, but no free text.

What is missing is the step that turns facts into invitations, one place that says whether an action is available, and somewhere to keep the player's own words. The track page must also work with AI off, so it cannot depend on 0003.

## Options considered

| Option | Verdict |
| --- | --- |
| Generate the track page with AI from the digest | Rejected for Stage 1. It breaks "every feature works with AI off" and makes invitations hard to test. AI may later reword invitations, citing the same fact IDs (S1-AI1, S1-AI3) |
| Hand-write pages per piece | Fine for the pilot and the starter library, but it does not cover the player's own files |
| **A deterministic observation catalogue in music-core** | **Chosen.** Rules map facts to invitations, as the variation catalogue maps parameters to notes. Testable with xUnit, the same for every player, no network |
| A backend store for notebooks | Rejected. Stage 1 keeps everything the player imports or writes on the device |

## Decision

1. **Stable fact IDs.** Facts from S1-A1 to S1-A5 get IDs that stay the same for the same notes, analysis settings and engine version, so a notebook entry or an interpretation can cite them later (#24). The digest for AI uses the same IDs.
2. **Observation catalogue, pure, in music-core.** A function from the facts of a piece or motif to a list of invitations. Each invitation names the fact IDs it rests on, a question, and a product action: a listening action (loop, tempo, compare two selections) or a catalogue operation with parameters and a Keep preset. Same facts give the same invitations. Rules that find nothing confident produce nothing; the page then says so and offers the actions that always work.
3. **Availability from one place.** Each action carries a status: available now, planned for Stage 1 (a Must, not built yet), needs a Should item (for example V2, V3 and V12 need chord detection S1-A3), or later stage. The track page and the experiment screen read the same status; nothing is hidden or faked.
4. **Experiment record and process note.** Two new objects in the Stage 1 data model, stored in IndexedDB with the project and included in the project export:
   - `ExperimentRecord`: motif, operation, parameters, Keep preset, diff reference, optional prediction (fixed choice), optional observation, optional next move, times. It extends what `LearningRecord` holds and replaces separate records for Guided and Discovery (#50, #51).
   - `ProcessNote`: project, optional motif, variation, experiment or bar range, text, created and edited times.
   No field is required. Note text and observations are not in analytics, and go into the study export (#52) only if the participant chooses.
5. **No new infrastructure.** No backend database, accounts, sync, agent framework or vector store. The track page is a Blazor screen over music-core results.

## Consequences

- #24 gains a requirement for stable IDs and a text snapshot test of the digest, so ID churn is caught in CI.
- #18 becomes the track page (or a reworked piece overview), built on the observation catalogue. A new issue covers the empty and low-confidence state.
- The Stage 1 data model gains `ExperimentRecord` and `ProcessNote`; the spec changes after the pilot, not before.
- The observation catalogue is new engine work. If the pilot shows that hand-written pages only work with richer facts than Stage 1 plans (hypothesis H6), that is a scope finding to decide separately.
- Safari may evict IndexedDB data for sites not used for a while; the project export remains the player's backup. Whether to request persistent storage is checked when #16 lands.
