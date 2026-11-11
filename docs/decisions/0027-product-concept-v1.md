# 0027 · The product concept of the first version: play at the keys and the piece map

Status: accepted, 2026-11-11. Andrei answered the seven questions of the product-concept council (09:31–09:33 UTC), each with the council's recommendation, clarified the audience (09:33 UTC) and added two functions to the first prototype (09:37 UTC). Amends [0024](0024-assembling-a-version.md) (its interface, sections 5 and 6) and [0025](0025-two-studios.md) (section 7, the web side), and [02-brand-identity.md](../02-brand-identity.md) (the name). Leaves [0026](0026-project-continuity.md) as it is.

Sources: the council's decision record *Совет по концепции продукта: итог* (https://claude.ai/artifact/3Tpao6QBAsDK1Vw5stLtJP) and the council folder `/mnt/project-files/councils/product-concept/`: `brief.md` (Andrei's inputs, questions 1–17), `facts-baseline.md` (web facts F1–F144, code facts F201–F205), `facts.md` (facts V1–V15, read on `main` `93d64c6`), `r1/`, `r2/` and `README.md`, section "Andrei's answers". A fact number below points into those files; a "п. N" or "item N" is a feature of the inventory *Что умеет Motiff Lab сейчас* (`inputs/feature-inventory.md`).

Every statement is marked as in 0025:

- **Decided**: Andrei decided it; the time is given (UTC).
- **Agreed by the council**: all three seats agreed after round 2 and Andrei did not change it; it is the current direction, open to the six questions of 0025 section 1.
- **Open**: not decided; a question for Andrei or for design.

## Context

0025 set two workspaces and the six questions, but left the product's centre open; Andrei's priorities of 2026-11-09 put the assistant first and recognition second. The council (light format: a web seat, an iPad seat and the blind seat, two rounds, 2026-11-11 00:50–01:25 UTC) was asked which whole capabilities the first version is, not which features. The facts that shaped the answer:

- Learning a piece is best supported by structure: landmarks of form explained most of one concert pianist's memory after 27 months (F102), and practice start and stop points moved to structural bars over time (F105). Those landmarks were set by the musician, not by a program (F133).
- The better pianists differed by the share of correct run-throughs, not by the number of repeats (F106, F132).
- The evidence on demand is thin: one firm number (440,000 ABRSM assessments a year, all instruments, F21, F52), no measured willingness to pay.
- Today both apps select only whole bars, while the core can select beats and 2–32 single notes (V1). The iPad has the loop, tempo, metronome and roll, but no hands-separately control, no resume to the place, tempo and loop, and no excerpt picker for a recording (V9).
- A second source is already matched to the score note by note; only the summary of differences is per bar (V3). Doubt about recognised notes does not reach chords (V5). The server transcribes with Transkun V2; on a synthetic Für Elise onsets are almost exact (0.999), durations weaker (0.64–0.73); live playing is not measured (V2).
- The Contents and Piece DNA show different forms of the same piece, and phrase lengths jump from 1 to 21 bars (inventory items 16, 19, 21).

## Decision

### 1. The centre of the first version

**Decided** (09:31). The centre is **playing at the keys**: a phrase on the score → loop, tempo, hands separately, metronome (items 11–14) → the piano roll as a magnifier of a hard passage (item 15) → resume from the same place (item 2); plus **the piece map** (section 3). A recording turned into notes and a roll (items 33, 33a, 34) is **one of the entry doors**, beside a PDF, a scan, MusicXML and MIDI, not the showcase.

**Agreed by the council.** The learning order is not imposed: the map opens on the first visit, the daily path is the phrase loop (the research and pedagogy on order disagree, F114, F120).

### 2. The audience

**Decided** (09:33, Andrei in words). The product is for the mass musician: anyone who wants to work with the material. It is not a teacher's tool, and no teacher is needed. Teachers must be able to use it, but they are one entry point, the most obvious one, not a priority. Demand tests, design and marketing target self-practising musicians first; teachers are one group among them.

### 3. The piece map: one form ladder

**Agreed by the council.**

- The form is computed once; the phrases, the Contents, Piece DNA, Insights and the assistant read it. First the contradiction between the Contents and DNA and the jumping phrase lengths go.
- Landmarks (starts of sections, harmonic turns) come from that form; a loop can start from a landmark.
- The player may move a section boundary: only the boundary, with no recomputation of the whole analysis. The correction is stored on the phrase anchor and shown as "confirmed by you", apart from what the engine inferred (the blind seat's point that the research landmarks were the musician's own, F133).

### 4. The assistant is a layer

**Decided** (09:32). The assistant is a layer over the capabilities, not a capability or a main screen of its own. It is called from the selection or from a fact in Insights; it knows the selection, the engine's facts and the player's notes; with AI off the same places show template text (CLAUDE.md rules on AI stand). The player's history comes later and only with consent. A spoken question on the iPad waits until a microphone beside a sounding piano is tried.

The cost the blind seat named is recorded: without the assistant Insights stays a list of mechanical facts, so Insights with musical meaning (plan row 4.4) matters more.

### 5. Selection

**Agreed by the council.** One explicit selection mode: a selection holds until it is closed, and a tap on empty space does not clear it. It serves the loop, the roll, analysis and the assistant. In the first version boundaries snap to beats; selecting single notes comes later (on the iPad by touching notes in the roll). Boundaries at a note's onset need a change in the core.

### 6. The player's own recording beside the notes

**Decided** (09:32). Not built in the first version ("my playing against the notes", draft example 1). It is asked in the demand-test conversations.

### 7. Assembling a version

**Decided** (09:32). The old merge on the iPad (item 38, facade contract 7) and the legacy second-file panel on the web (item 39) are removed. The assembly screen on the web (item 37) is replaced by **per-place hear-and-choose**: at a place where the sources differ, the player hears each and chooses, and the choice is written into **one saved edition** of the piece. Two sources at most (Andrei, 2026-11-09); the direction (the score cleans a recording, or a recording adds to the score) is a property of the source. Matching stays note by note (V3); matching by phrases and harmony on top of it is the next step, not bar structure.

On the iPad: listen and choose (0025 section 7 stands).

**Open** (for design): whether a choice made on the iPad writes into the same edition or only picks the version to practise (the filed iPad task #329 keeps the choice in the resume point); whether the carry sheet of 0024 section 5 survives inside hear-and-choose.

### 8. Transposition

**Decided** (09:33). First, view and playback in another key, chosen on the circle of fifths, for the piece or the selection; the piece's notes do not change and the Roman numerals stay as they are. A derived printable version comes later.

### 9. Fixing notes at the instrument

**Decided** (09:32). The iPad keeps fixing flagged notes in the recognised notes (item 34; it exists today, V11). Full note editing stays on the web.

### 10. Honest recognition

**Agreed by the council.** Uncertain durations are marked; doubt about recognised notes reaches the chords and the facts built on them (today it does not, V5). Before any demo, three real recordings of live playing are recognised and the doubtful places per minute counted.

### 11. The name

**Decided** (09:33). The product is renamed **Ориентир** in Russian and **Landmark** in English. The council's icon idea: a flag on a staff line. The repository, URLs, bundle ids and Azure resources are not part of the decision.

### 12. Before building more: the demand test

**Agreed by the council.** Demand is not proven, and not disproven. Before large development: three live recordings checked for accuracy, 5–10 conversations with self-practising musicians (teachers one group among them, section 2), a short video, then early access paid in advance. These are Andrei's steps, not Task Records.

### 13. What is later

**Agreed by the council.**

- First addition after the demand test: **"where else does this harmony sound"**, a search of a chord sequence through the piece (the chord list with confidence exists; the search does not).
- No new interface languages before the demand test; then Ukrainian, Lithuanian as a pilot, Belarusian.
- Second plan, kept as ideas: variations (plan row 4.12), practice state, fingering (4.17).
- Reserve in the data model when the related tasks come: a group of pieces (a concert, an album) as an ordered list, a per-piece summary of facts with stable ids for the assistant, room for state and tempo on the phrase anchor, a fingering field at the note.

### 14. The library and handwritten notes in the first prototype

**Decided** (Andrei, 2026-11-11 09:37). Two functions Andrei uses actively today must be in the first prototype, at good quality:

- **A library for managing the pieces brought in.** It must work for about 200 pieces, not 5: today every piece is one big card on the home screen, which does not scale. 0025 section 6 (the library is to be redone; forScore as a reference, not a template) stands.
- **Handwritten notes**: the player writes a note on the score with a stylus as if by hand, as text written in the margin, not only drawing over the notes (item 27 today: pencil and marker, tied to the bar). A finger works too: no stylus is required (CLAUDE.md). The assistant reads only typed text, not handwriting (council answer 7).

**Open** (for the UX designer): how the library is organised: separate the library and the session, drop the session, or combine them (today a session is a piece, its resume point, selection and open view, council answer 6); and where handwritten notes sit beside the typed margin notes (item 28).

## Consequences

- The library (plan row 4.1) and handwritten notes join the first prototype (section 14); the library goes to the UX designer first.
- iPad prerequisites before anything is shown: hands separately (#328), resume with place, tempo and loop (#327), an excerpt picker for a recording (new task).
- The form ladder (plan row 4.2) moves to the front of phase 4, with the boundary correction after the phrase anchor (#318).
- Plan row 4.7 (sources and assembly on the web) becomes per-place hear-and-choose; row 4.13 (versions by musical meaning) is answered in part: matching by notes exists, matching by phrases and harmony is its next step.
- Plan row 4.14 (council on the assistant as a core layer) is answered: a layer. Its technical layer (history, hosting) stays a later task.
- The phrase anchor of #318 is the place for the boundary correction and later for state and tempo; whether #318 reserves them is Andrei's choice before approving it.
- #342 (PR #344) removed the AI fact digest as dead code; the assistant layer will need a per-piece summary of facts with stable ids again, built when the assistant task comes.
- The first-screen task of the web (plan row 4.8) is weighed by section 1: the web is the most complete version (Andrei, 2026-11-10), and the at-the-keys loop works there too, for example on an Android tablet.

## Alternatives considered

- The recording → score → practice path as the flagship (the web seat in round 1, Andrei's tested capability): rejected for the centre, because a recognised rubato recording would show the weakest score first (V13); kept as one entry door.
- The assistant as the main screen (Andrei's priority 1 of 2026-11-09): rejected for now, with the cost in section 4.
- Freezing the assembly as it is until the demand test (the iPad seat): rejected; three overlapping mechanisms confuse the player.
- A derived printable transposed version at once (the blind seat): later.
- On the iPad only accept or reject a flag (the blind seat): rejected; fixing exists and serves the at-the-keys check.

## Open

- The iPad side of hear-and-choose (section 7).
- The demand-test results, which may change sections 1, 4 and 6.
- Not checked: whether a spoken question works beside a sounding piano; whether forScore marks can be exported for moving 200 pieces; finger editing of the roll on an Android tablet and the iPad.
