# 0027 · The product concept of the first prototype: playing at the keys, the piece map, and the directions kept

Status: accepted, 2026-11-11. Andrei answered the seven questions of the product-concept council (09:31–09:33 UTC), each with the council's recommendation, clarified the audience (09:33), added two functions to the first prototype (09:37) and gave his assessment of the council's outcome (09:40), which qualifies several answers; where they differ, the assessment wins. Amends [0024](0024-assembling-a-version.md) (its interface, sections 5 and 6), [0025](0025-two-studios.md) (section 7, the web side) and [02-brand-identity.md](../02-brand-identity.md) (the name). Leaves [0026](0026-project-continuity.md) as it is.

Sources: the council's decision record *Совет по концепции продукта: итог* (https://claude.ai/artifact/3Tpao6QBAsDK1Vw5stLtJP) and the council folder `/mnt/project-files/councils/product-concept/`: `brief.md` (Andrei's inputs, questions 1–17), `facts-baseline.md` (web facts F1–F144, code facts F201–F205), `facts.md` (facts V1–V15, read on `main` `93d64c6`), `r1/`, `r2/` and `README.md`, section "Andrei's answers". Andrei's assessment *Моя оценка выводов совета* (2026-11-11 09:40 UTC, verbatim in `andrei-assessment.md` in the council folder; summarised in the decision record's section «Оценка Андрея и что она уточняет», which overrides the card answers) is quoted by its section numbers (A1.1 … A6). A fact number points into the council files; "item N" is a feature of the inventory *Что умеет Motiff Lab сейчас* (`inputs/feature-inventory.md`).

Every statement is marked as in 0025:

- **Decided**: Andrei decided it; the time is given (UTC).
- **Agreed by the council**: all three seats agreed after round 2 and Andrei did not change it; the current direction, open to the six questions of 0025 section 1.
- **Open**: not decided; a question for Andrei or for design.

## Context

0025 set two workspaces and the six questions, but left the product's centre open; Andrei's priorities of 2026-11-09 put the assistant first and recognition second. The council (light format: a web seat, an iPad seat and the blind seat, two rounds, 2026-11-11 00:50–01:25 UTC) was asked which whole capabilities the first version is, not which features. The facts that shaped the answer:

- Learning a piece is best supported by structure: landmarks of form explained most of one concert pianist's memory after 27 months (F102), and practice start and stop points moved to structural bars over time (F105). Those landmarks were set by the musician, not by a program (F133).
- The better pianists differed by the share of correct run-throughs, not by the number of repeats (F106, F132).
- The evidence on demand is thin: one firm number (440,000 ABRSM assessments a year, all instruments, F21, F52), no measured willingness to pay.
- Today both apps select only whole bars, while the core can select beats and 2–32 single notes (V1). The iPad has the loop, tempo, metronome and roll, but no hands-separately control, no resume to the place, tempo and loop, and no excerpt picker for a recording (V9).
- A second source is already matched to the score note by note; only the summary of differences is per bar (V3). Doubt about recognised notes does not reach chords (V5). The server transcribes with Transkun V2; on a synthetic Für Elise onsets are almost exact (0.999), durations weaker (0.64–0.73); live playing is not measured (V2).
- The Contents and Piece DNA show different forms of the same piece, and phrase lengths jump from 1 to 21 bars (inventory items 16, 19, 21).

**How far the evidence goes** (Decided, A5). The research does not prove that an automatic form map improves learning, nor that landmarks proposed by a program work as the musician's own do; no research on the piano roll does not mean it is useless, only that its effect is to be checked; accuracy on a synthetic recording says nothing about a real performance. Product conclusions are drawn from checking a concrete player's task on real material, not from proving a function useful in the abstract.

## Decision

### 1. The centre of the first prototype, not the definition of the product

**Decided** (09:31; qualified 09:40, A2.1). The central scenario of the **first prototype** is **playing at the keys** on a score the musician trusts: phrases and landmarks → loop, tempo, hands separately, metronome (items 11–14) → the piano roll as another view of a hard passage (item 15) → resume with the context kept (item 2); plus **the piece map** (section 3).

This does not define the whole product. Ориентир / Landmark is not an app that accompanies piano practice: it is an environment in which a musician works with musical material, understands it, explores its connections, changes it and forms a musical picture of their own (A6). Many of its tasks do not need the instrument. The web Music Studio stays a full environment for independent work with the material, not a place that prepares data for the iPad (A2.1; Andrei 2026-11-10: the web is the most complete version).

### 2. The recording is an entry door built at full quality

**Decided** (09:31; qualified 09:40, A2.2). Recording → notes → check of the recognised notes → piano roll → playback → practice on the iPad (items 33, 33a, 34, 15) is one of the entry doors, beside a PDF, a scan, MusicXML and MIDI, not the only main path. It is a need Andrei has tested himself and it joins recognition, notes, the check, the roll, playback and practice, so it is built at the same quality as the centre and is a strong demonstration scenario.

### 3. The audience

**Decided** (09:33). The product is for the mass musician: anyone who wants to work with the material. It is not a teacher's tool, and no teacher is needed. Teachers must be able to use it, but they are one entry point, the most obvious one, not a priority. Demand tests, design and marketing target self-practising musicians first.

### 4. No imposed learning order

**Decided** (A1.2, agreeing with the council). The app does not turn learning into an algorithm: it makes the useful operations available and helps the musician choose the next one. A musician may play the whole piece first, start with the hard places, learn by phrases, work on the parts separately, return to passages already learnt, or explore the harmony or the structure at once. The map is offered on the first visit; the daily path is the phrase loop (the research and pedagogy on order disagree, F114, F120).

### 5. The piece map: one source of structure, several levels

**Decided** (A1.1, A1.4; the council agreed on one source).

- Structure is computed once, in one consistent source; the Contents, Piece DNA, the phrase brackets, Insights and the assistant read it. First the contradiction between the Contents and DNA and the jumping phrase lengths go.
- The map is **not** a ladder of phrases alone. Phrases, sections of the form, harmonic landmarks and recurring motifs are related but different levels of structure: one shared view may join them, but they are not treated as one thing.
- Each workspace presents it for its task: on the iPad, where am I in the piece and what comes next; on the web, why the piece is built this way.
- Landmarks can start the work on a phrase (a loop from a landmark).
- **Agreed by the council.** The player may move a section boundary, only the boundary, with no recomputation of the whole analysis; the correction is stored on the phrase anchor and shown as "confirmed by you", apart from what the engine inferred (F133).

### 6. The assistant: a layer in the interface, a main value of the product

**Decided** (09:32; qualified 09:40, A1.3, A2.6). In the interface the assistant is a contextual layer, not a section or a screen of its own: it is called from the selection or from a musical fact, and knows the selection, the engine's facts and the player's notes. That is its position, not its weight: it stays one of the main values of the product.

It is not to become a chat that only explains what the engine computed, nor a few buttons with template text. The idea is wider: the musician asks about the material in natural language, shares observations, tests hypotheses and deepens understanding ("why does this harmony feel this way?", "where else in the piece is a similar sequence?", "how do these two passages differ?", "what if this sequence moved to another key?"). Its capabilities are still to be defined and tested; the role can grow with the product. The hypothesis that AI can substantially widen independent musical exploration is to be tested, not assumed away.

Until those capabilities are defined, the code rules of CLAUDE.md on AI stand (the AI interprets engine results, cites fact ids, every feature works with AI off). **Open**: which of them the defined capabilities will change, decided by Andrei in an environment task. The player's history comes later and only with consent; a spoken question on the iPad waits until a microphone beside a sounding piano is tried.

### 7. Selection

**Agreed by the council.** One explicit selection mode: a selection holds until it is closed, and a tap on empty space does not clear it. It serves the loop, the roll, analysis and the assistant. In the first prototype boundaries snap to beats; selecting single notes comes later (on the iPad by touching notes in the roll). Boundaries at a note's onset need a change in the core.

### 8. The player's own recording beside the notes

**Decided** (09:32). Not built in the first prototype ("my playing against the notes"). It is asked in the demand-test conversations.

### 9. The player's own edition from two sources

**Decided** (09:32; qualified 09:40, A2.5). The old merge on the iPad (item 38, facade contract 7) and the legacy second-file panel on the web (item 39) are removed. The assembly screen on the web (item 37) is replaced by **per-place hear-and-choose**: where the official score and a performance differ, the player hears each and chooses.

The result is not a comparison screen or a passing choice between sources: it is **new musical material, the player's own edition**, which becomes their working score to study and perform. Hear-and-choose is right only if it lets the player build that edition step by step and keep it. Two sources at most (Andrei, 2026-11-09); the direction (the score cleans a recording, or a recording adds to the score) is a property of the source. Matching stays note by note (V3); matching by phrases and harmony on top of it is the next step, not bar structure. On the iPad: listen and choose (0025 section 7 stands).

**Open** (for design): whether a choice made on the iPad writes into the same edition or only picks the version to practise (the filed iPad task #329 keeps it in the resume point); whether the carry sheet of 0024 section 5 survives inside hear-and-choose.

### 10. Transposition: two scenarios

**Decided** (09:33; qualified 09:40, A2.4). Two different tasks are kept in the concept:

- **Learning**: transpose a piece or a passage to study harmonic relations and train the ear. A temporary view and playback in another key is enough; the notes do not change and the Roman numerals stay. This comes first.
- **Practical**: move an accompaniment to a key that suits a singer and get a new score. This needs a derived edition of the notes, printable; it comes later.

Transposition is not designed as a temporary view only: the first step must leave room for the derived edition (section 9's edition is a natural place for it). The key is chosen on the circle of fifths, for the piece or the selection.

### 11. Fixing notes at the instrument

**Decided** (09:32). The iPad keeps fixing flagged notes in the recognised notes (item 34; it exists today, V11). Full note editing stays on the web.

### 12. Honest recognition

**Agreed by the council.** Uncertain durations are marked; doubt about recognised notes reaches the chords and the facts built on them (today it does not, V5). Before any demo, three real recordings of live playing are recognised and the doubtful places per minute counted.

### 13. The library and handwritten notes in the first prototype

**Decided** (09:37; A3.1, A3.2). Two functions Andrei uses actively today are part of the first prototype, at good quality:

- **The library.** With about 200 pieces it becomes the main workspace: find a piece fast, return to it, organise the collection and link pieces together. Its quality is judged at that scale, not with five demo scores; today every piece is one big card, which does not scale. 0025 section 6 stands.
- **Handwritten notes.** The habit of writing with a stylus directly on the score stays the natural way of working and must not give way to a more complex system of structured notes (today: pencil and marker tied to the bar, item 27). Keep the habit first; open further uses step by step later (a "hard" mark tied to a phrase, a typed note read by the assistant). A finger works too; no stylus is required (CLAUDE.md).

**Open** (for the UX designer): how the library is organised (separate the library and the session, drop the session, or combine them; today a session is a piece, its resume point, selection and open view, council answer 6); where handwritten notes sit beside typed margin notes (item 28).

### 14. Groups of pieces as a context for exploration

**Decided** (A1.5, A4 first point). A group of pieces (an album, a concert) is more than an ordered list in the library: it can become the context for exploring shared keys, recurring harmonic sequences, similar motifs, compositional and dramaturgical links and musical development across pieces. For now the data model and the library leave room for it; no separate album-exploration interface is built now.

### 15. "Where else does this sound": a key exploration hypothesis

**Decided** (A2.3; the council had it as the first addition). Finding where a harmonic sequence appears again is not a minor search feature but one of the key exploration hypotheses of the product: find similar sequences elsewhere in the piece → hear them one after another → compare their context → see how they are used in different parts → perhaps find similar solutions in other pieces or the album. It is not required in the first prototype, and it is kept as a core direction, not an add-on. The chord list with confidence exists; the search does not.

### 16. Musical reference tied to the material

**Decided** (A4 second point). The circle of fifths, keys, chords, harmonic functions and transposition are worth most when they are tied to the open score, the selected passage and the current question, not as separate reference tools.

### 17. Moving between forms of the material

**Decided** (A4 third point). A player works with a PDF, recognised notes, a recording, the piano roll or their own edition, and moves between them as the task needs; these are not stages in a fixed order. No separate interface sections are fixed for them now; the product and technical model must not block those moves.

### 18. The name

**Decided** (09:33). The product is renamed **Ориентир** in Russian and **Landmark** in English. The council's icon idea: a flag on a staff line. The repository, URLs, bundle ids and Azure resources are not part of the decision.

### 19. What the first prototype is, and the directions kept

**Decided** (A6). The base of the first prototype: playing at the keys; the score and the piece map; phrases, loops, tempo, hands; the piano roll; resuming with the context kept; the score library; handwritten notes.

Directions that stay part of the product and must not be lost, even if built later: recognising audio and getting notes; matching a score with a performance; the player's own edition; exploring harmonic connections; transposition; groups of pieces and albums; musical reference; the contextual AI assistant. These are not two priority lists for development: the second list is part of the product's intent.

Second plan, kept as ideas: variations (plan row 4.12), practice state, fingering (4.17). No new interface languages before the demand test; then Ukrainian, Lithuanian as a pilot, Belarusian (council).

### 20. Next: end-to-end scenarios on real material, and the demand test

**Decided** (A6, "Что делать дальше"). No new council or architecture decisions now, and not all the directions are designed now. The next step is to turn the decisions taken into a concrete prototype and check end-to-end scenarios on real material:

1. Load an ordinary bought score, make a handwritten mark, continue on the iPad.
2. Learn a passage: select a phrase, work on the parts, join them, move to the next.
3. Get a score from a real recording and use it for study.
4. Return to a large library and check how it works with dozens and hundreds of pieces. This one checks whether the parts of the product add up to one working environment.

**Agreed by the council.** Demand is not proven, and not disproven. Before large development: three live recordings checked for accuracy, 5–10 conversations with self-practising musicians (teachers one group among them), a short video, then early access paid in advance. These are Andrei's steps, not Task Records.

## Consequences

- Task Records filed with this record (2026-11-11): #346 excerpt picker on the iPad, #347 one source of structure, #348 the player moves a section boundary, #349 explicit selection by beats, #350 the player's own edition by hear-and-choose, #351 transposition as a view (first step), #352 doubt reaches chords, #353 the assistant's entry and context (first step), #354 the library for 200 pieces, #355 handwritten notes, #356 the rename. Those with a UI wait for a UX designer variant approved by Andrei.
- The library (plan row 4.1) and handwritten notes join the first prototype (section 13); the library goes to the UX designer first.
- iPad prerequisites before anything is shown: hands separately (#328), resume with place, tempo and loop (#327), an excerpt picker for a recording (#346).
- The single source of structure (plan row 4.2) moves to the front of phase 4, with the boundary correction after the phrase anchor (#318).
- Plan row 4.7 (sources and assembly on the web) becomes per-place hear-and-choose into the player's own edition; row 4.13 (versions by musical meaning) is answered in part: matching by notes exists, matching by phrases and harmony is its next step.
- Plan row 4.14 (council on the assistant): its position is decided (a layer), its capabilities are not; defining and testing them is still to come, before or with the hosting task.
- The phrase anchor of #318 is the place for the boundary correction and later for state and tempo; whether #318 reserves them is Andrei's choice before approving it.
- #342 (PR #344) removed the AI fact digest as dead code; the assistant will need a per-piece summary of facts with stable ids again.
- The first-screen task of the web (plan row 4.8) is weighed by section 1: the web is a full environment, not a practice app, and the at-the-keys loop works there too, for example on an Android tablet.
- The four scenarios of section 20 become the acceptance walk of the first prototype. The tasks each one stands on: (1) bought score, handwritten mark, continue on the iPad: #355, #354, sync #323 and #324 (or a file moved by hand until then); (2) learn a passage, parts, join, next: #349, #328, #327, #347; (3) a score from a real recording: #346, #352, Andrei's three live recordings; (4) a large library: #354.

## Alternatives considered

- The recording → score → practice path as the only main path: not chosen, because a recognised rubato recording would show the weakest score first (V13); kept as an entry door built at full quality (section 2).
- The assistant as a main screen (Andrei's priority 1 of 2026-11-09): not chosen as a position; kept as a main value (section 6).
- Freezing the assembly as it is until the demand test (the iPad seat): rejected; three overlapping mechanisms confuse the player.
- Only a temporary transposed view: rejected as the whole design; it is the first step (section 10).
- On the iPad only accept or reject a flag (the blind seat): rejected; fixing exists and serves the at-the-keys check.

## Open

- The organisation of the library and the session; the place of handwritten notes (section 13).
- The assistant's capabilities and which code rules they change (section 6).
- The iPad side of hear-and-choose; the carry sheet (section 9).
- The demand-test results, which may change sections 1, 6 and 8.
- Not checked: whether a spoken question works beside a sounding piano; whether forScore marks can be exported for moving 200 pieces; finger editing of the roll on an Android tablet and the iPad.
