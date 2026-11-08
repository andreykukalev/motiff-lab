# 0025 · Two specialised workspaces of one product, with no parity

Status: accepted, 2026-11-08. Supersedes the parity of the web app and the iPad app in [0018](0018-redesign.md) (section 14, the iPad's copy of Piece DNA in section 11, the Pro-only online saving of sections 10 and 12) and in [0019](0019-native-ipad-app.md) (the context and section 5).

Source: Andrei's decisions of 2026-11-08 (UTC), given as comments on the product review document *Ревью продукта: две студии* (https://claude.ai/artifact/4S7ynFjr4UqSHXGP6mck9T) and in the review thread: 17:39, 17:42, 17:45, 17:47, 17:48, 17:49, 17:50, 17:51, his review of the specification at 17:53, and the product working context at 18:12. His concept *One product, two workspaces* (11:51) is the background; the decisions below are what he took from it.

This record holds **product decisions and the working hypothesis** only. How the product is built (the shared project model, sync, the hand-over of the working context, independent releases, the code architecture) gets its own architecture record; the feature audit and the delivery tasks stay outside it. Every statement is marked:

- **Decided**: Andrei decided it; the time of his comment is given.
- **Working hypothesis**: the current direction, to be tested and changed as the product is explored; not a boundary.
- **Open**: not decided; a question for Andrei.

## Context

0018 and 0019 said that the redesign is built in both apps, every screen web first, and that nothing is meant to stay web only. The product review of 2026-11-08 walked the web app and read the iPad app's code against Andrei's concept of two workspaces: the iPad carried most of the web's studio features (sources and merge, Piece DNA, note editing, Check, scan, transcription, the assistant), while what the player needs at the instrument (hands separately, the resume point, the ideas kept on the web) was missing or broken. Andrei then accepted two specialised workspaces instead of parity (17:48) and set the frame in which that split is to be read (18:12).

## Decision

### 1. The product frame

**Decided** (18:12). Motiff Lab is at the stage of forming its product concept; the final product model is not defined. The starting point is an environment in which a musician works with musical material presented as scores: stores and organises them, views and plays them, explores the material, gets more information, analyses pieces, experiments, makes variants and uses the material at the instrument. The aim is to make working with the score and the material more meaningful, free and productive, supporting the player's musical curiosity.

The five rules:

1. Do not treat the current concept as final.
2. Do not turn single functions into mandatory product sections without a reason.
3. Do not add restrictions only to preserve the original architecture.
4. Do not aim for functional parity of the platforms.
5. Do not propose features only because they can be built.

The six questions. Every later product decision about the two workspaces is weighed by them:

1. Which task of the player does it solve?
2. How does it relate to the score and the musical material?
3. What value does it add?
4. In which context will the player use it?
5. How well does it fit what exists?
6. What do we know, and what do we only assume?

The task now is not to prove a concept made in advance but to explore what exists, find real scenarios and the connections between capabilities, and form the product model step by step. Agents' proposals help decisions; they do not close the space of product options early.

### 2. Two workspaces, no parity

**Decided** (17:48, 17:53, 18:12). The parity rule of 0018 and 0019 is dropped. The web app and the iPad app are two specialised workspaces of one product. They work on shared musical projects and keep the player's work continuous. Neither is a lighter copy of the other: the Practice Studio is a product experience of its own, not a reduced Music Studio.

### 3. What each workspace is mostly for

**Working hypothesis** (18:12). Written as "mostly", not as a boundary:

- The web **Music Studio** is mostly the library, working with scores, editing, analysis, exploring and transforming the material.
- The iPad **Practice Studio** is mostly reading, playback and working with the material while playing and practising.

A function may move to the other workspace, or appear in it, when the six questions support it. This record states no list of what either app may never do.

### 4. One function, different purposes

**Decided** (17:45, 17:53) as a principle; the rows are current examples, not boundaries, and the list is not complete. A feature in either workspace is judged by the six questions, by whether its form serves the purpose it has there, and not by whether the other app has it.

| Function | Purpose in the Music Studio (web) | Purpose in the Practice Studio (iPad) |
| --- | --- | --- |
| Piano roll | editing notes | visualising a hard passage |
| Playback | merging versions | listening |
| Piece DNA | deep analysis of the whole piece | reflection and harmonic memory while playing |
| Compare | assembling a version | listening and choosing |
| Insights | exploring the material | making sense of it while practising |

### 5. Piece DNA in both workspaces

**Decided** (17:39, 17:42, 17:53). Piece DNA is in both workspaces, with two purposes. It gives information on the composition and dramaturgy of the piece (key, chords, harmonic functions, the circle as a visualisation, form) for reflection and for building harmonic memory.

- On the web it explores the whole musical architecture of the piece.
- On the iPad it is quick reference to the musical content while playing: the current key, the chord sequence, harmonic functions, inversions, the circle, the form, the returns of the main motifs. The web's whole analysis interface is not carried over.

A **chord summary** (inversions, open and close voicings, and so on) is wanted (17:42) and **not yet built**: today Piece DNA has the key, a chord per bar, harmonic functions, the circle and the form, and shows inversions only as slash chords.

### 6. The library

**Decided** (17:47, 17:53). The library is to be redone. Today it is cards on the home screen; its purpose is storing and managing a large number of scores and the life of each musical project: the piece, its working versions, prepared variations, notes, practice state and the history of returns to it. forScore is a reference for managing many scores, not a template to copy. Design variants approved by Andrei come before any PR.

Where the library lives is not fixed by this record beyond the working hypothesis of section 3.

### 7. Assembling and comparing versions

**Decided** (17:49, 17:50). Full version assembly ([0024](0024-assembling-a-version.md)) is in the Music Studio. The Practice Studio gets a simplified compare: listen to another version and choose. PR #282 (full assembly on the iPad) was closed unmerged on this ground. Removing the old merge (facade contract 7) from the iPad and building the simplified compare are a separate product task.

### 8. Sync is free, with a quota

**Decided** (17:49). Sync between the workspaces is free, with a quota. This replaces the Pro-only online saving of 0018 sections 10 and 12 and its rule that the free tier has no cloud account: a free player may sign in to sync. The rest of the Pro rule stands: the assistant, scanning and transcription are Pro. Nothing leaves the device until the player signs in and turns sync on ([0014](0014-hosted-scan-server.md), item 13). Until the sync task lands, the API still checks Pro for saving online as built.

**Open**: the quota itself; this record sets no number.

**Not decided here**: how conflicts are resolved. Andrei's v1 direction (17:53) is input to the sync work, not a rule of this record: independent changes are combined, conflicting changes are kept as separate versions, and the player chooses.

### 9. Continuity: what the product requires

**Decided** (17:53, 18:12) as product requirements; how they are met is architecture (section 13).

1. **One musical project model** whose entities are the same in both apps: Project, Score, Musical Object, Phrase, Version, Variation, Annotation, Practice State, Resume Point. **Phrase identity** above all: a variation made for a phrase on the web belongs to the same phrase on the iPad.
2. **The working context moves between the workspaces, not just the file.** On the iPad the player finds the same phrase, the original, the kept variation and the notes, and can start practising at once; back on the web they see what was noted at the instrument.
3. **Independent releases.** The web app and the iPad app can be released independently; the project stays compatible, and each app shows the data it understands.

### 10. Practice state

**Decided** (17:51, 17:53). Practice state is not in v1 of the Practice Studio. When it comes, it records the state of the work, not a grade: In progress / Practised / Ready to perform, not a binary "learned"; the tempo reached is kept apart from the state.

### 11. The iPad v1 scope

**Decided** (17:51): the iPad v1 feature list is Andrei's choice, row by row, in the table *Функции iPad: выбор для первой версии* of the review document (Yes / View only / No / Later). The rows below are copied as set when this record was written (2026-11-08, document read at 21:45 UTC); this record adds no row of its own. They are the **v1 scope, not a permanent boundary**. For v1 full note editing stays on the web, and the iPad shows the piano roll (row 19).

| # | Function | v1 |
| --- | --- | --- |
| 1 | Library and resume point | Yes |
| 18 | Piece DNA and the circle of fifths | Yes |
| 19 | Piano roll to show a passage | View only |
| 21 | Another version: listen and choose | Yes |
| 22 | Compare and merge versions (facade contract 7) | No |
| 24 | Sign-in and sync | Yes |
| 26 | Goals and practice state | Later |

**Open**: every row Andrei has not set yet: 2 import of MIDI, MusicXML and PDF; 3 import and export of the project file; 4 a PDF as it is, marks on the PDF; 5 scan recognition (Pro); 6 transcription of a recording (Pro); 7 Check of uncertain notes; 8 the A4 score following playback; 9 phrases in three levels and selection; 10 phrase focus, previous and next phrase; 11 loop, tempo, metronome, count-in; 12 hands separately; 13 Contents (pages, bookmarks, suggestions); 14 pencil, highlighter, notes, bookmarks; 15 Insights on the selection; 16 Try / Keep, making a variation on the iPad; 17 playing a variation kept on the web; 20 editing notes in the roll (correcting a single note at the instrument; full editing stays on the web, above); 23 Ask about the piece (AI, Pro); 25 PDF export and share.

### 12. What stays as it is

**Decided** (unchanged by this record):

- The design kit (`design/kit`: tokens, `ds.css`, icons, the look of the score and its marks) is the one visual language of both workspaces ([0018](0018-redesign.md), sections 13 and 14).
- Both languages, Russian and English, for the interface and for all generated content.
- The musical rules of `CLAUDE.md`: canonical time, provenance and confidence, pure variation operations, AI only interprets engine results.

### 13. Open questions

**Open**, each a question for Andrei, not decided here:

1. The iPad v1 rows not yet set (section 11).
2. The sync quota (section 8).
3. The architecture decisions A1–A5 of the review's feature registry. The product requires of them what sections 7–9 say; each is decided in an architecture record, not here:
   - **A1 The shared project model and phrase identity.** Required: the entities of section 9.1 are the same objects in both apps, and a phrase keeps its identity. To be decided in ADR 0026, project continuity (Task Record #292), from the project-continuity council of 2026-11-08.
   - **A2 Sync**: project identity, the transfer of changes within the quota, conflicts. Required: free sync with a quota (section 8), nothing the player did on either device lost. To be decided in 0026.
   - **A3 The hand-over of the working context.** Required: section 9.2. To be decided in 0026.
   - **A4 Independent web and iPad releases over a compatible data contract.** Required: section 9.3. To be decided in 0026, with the project file ([docs/project-file.md](../project-file.md)) and the facade ([0020](0020-music-core-facade.md)).
   - **A5 One source for the form of a piece.** Required: Contents and Piece DNA give one answer about the form of a piece (today Contents shows "A B C D E" where Piece DNA shows "A A′ B A″"). To be decided in its own task, before either screen changes.
4. The code architecture behind the two workspaces: one C# music-core under both apps (0019, 0020) or two codebases. This record does not change it; it is to be decided with A1–A4 in 0026.

## Consequences

- A feature is not built in the iPad app because the web app has it, nor the other way round. Each feature, in each workspace, is proposed with the purpose it has there and weighed by the six questions; where a function serves both, its two forms may differ.
- The redesign's visual system, the shared strings and icons, music-core and the project file stay common to both apps. What differs is which functions each app carries and in which form.
- The follow-up product work (sync, hands separately on the iPad, the ideas kept on the web on the iPad, the resume point, the library, the Piece DNA chord summary, the simplified compare on the iPad and the rest of the review's draft tasks) runs through its own Task Records, each citing the section of this record it depends on. None of them settles an architecture decision of section 13 silently.
- Documents that still assume the same screens in both apps or Pro-only sync (outside 0018, 0019 and `CLAUDE.md`) are listed in the pull request of this record and corrected in follow-up work.

## What this record changes in earlier records

| Record | Stands | Replaced by this record |
| --- | --- | --- |
| [0018](0018-redesign.md) Redesign | the page, phrases, marks, the Insights stream, playback, resume points, settings, Piece DNA on the web, the visual system, the rollout | the redesign built screen by screen in both apps (section 14); the iPad's copy of the Piece DNA screen (section 11); online saving as Pro and the free tier without a cloud account (sections 10 and 12) |
| [0019](0019-native-ipad-app.md) Native iPad app | SwiftUI, music-core through the facade, Verovio as a framework, sound, PencilKit, projects as files, MSAL, releases from tags, the macOS workflow | "nothing is meant to stay web only: both apps get every screen of the redesign, web first" (section 5 and the context); sign-in only for Pro (section 2.4) |
