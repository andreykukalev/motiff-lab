# 0034 · Drafts: the player's own version assembled from fragments of any source, and project file version 17

Status: accepted, 2026-11-19. Andrei asked for a way to build his own practice score from a recognised PDF and his recording, which differ by about 15 % (thread «Спроектировать сборку партитуры из нот и записи», 00:21 UTC). He decided that recognised notes are never changed and that all merging happens in a separate draft (01:03–01:04 UTC), asked for the mechanics of a draft holding fragments of different sources and pieces (13:56 UTC), and accepted the nine recommendations of the page «Механика черновика» (20:55 UTC: «Принято, давай реализовывать»). Builds on [0022](0022-score-and-performance.md) (alignment, comparison), [0024](0024-assembling-a-version.md) (the journal, bars at a seam, matching in words), [0028](0028-ai-bar-edits.md) (the assistant acts through music-core tools) and [0030](0030-originals-notes-piece.md) (originals, Notes, provenance); amends them as the last section says.

Sources (project folder): the spec `product/specs/drafts.md` (Andrei's decisions, the scenarios and the iPad screens), the prior-art report `product/research/draft-mechanics-2026-11-19/prior-art.md`, and the pages «Механика черновика» (https://claude.ai/artifact/8ZGe9HE5i3Bpo9LPR5jhJv) and «Своя версия из нот и записи» (https://claude.ai/artifact/JwxnqJ88xGwZXppWt41wCh, earlier editions). The file format itself is in [`project-file.md`](../../project-file.md), "Drafts".

## Context

The iPad merges a version into the working notes bar by bar (0022, section 5: the version page with its strip, three ways into the merge draft, Mine · Version · Result, the Compare versions window, Apply). The assembly of 0024 lived only in the archived web. Andrei found both hard to follow, and three of his needs are outside them:

- **The notes he recognised must stay as recognised.** Taking bars into the working notes changes them, so the Notes no longer match their original (0030's data integrity).
- **Bars never match.** A recording adds, drops and stretches bars; a fragment from another piece has its own meter, key and tempo. Merging by bar number works only for a print and a MIDI rendering of it.
- **A creative space.** He wants to throw fragments of different sources, and even different pieces, into one place, compare them, take what he likes, and get help from the assistant there.

The prior-art report found the same shape in mature tools: a tray beside the result (Studio One Scratch Pad, Sibelius Ideas), alternatives of one place with one chosen (Final Cut Auditions, Logic alternatives), comping that needs a shared grid (Logic, Ableton), anchors the player confirms (Logic Smart Tempo), never a destructive conform (the long complaint about Cubase's Follow Chord Track), hands reliable and voices not (Dorico), and AI that offers two or three candidates beside the material.

## Decision

### 1. A draft, apart from the sources

1. **A draft is new data of the piece**, beside its Notes and its work: a name, places in order, a shelf and a journal. It never changes an original or a Notes; deleting a draft deletes only the draft.
2. **Recognised notes stay as made.** Correcting a recognition error (Check, repair candidates, a misread note fixed by hand) stays the undoable layer of corrections on the Notes (0030, `edits`). Everything musical (merging, variants, reworks, bridges) happens only in drafts.
3. **A draft lives in the Sources of the piece it was made in** (a group «Черновики») and in the view switch, and may take fragments from any piece of the library.
4. **A draft covers what the player wants**: the whole piece, a sentence or one phrase.

### 2. Places and variants

```
Draft                     own bar numbers from 1; starts in the key and tempo of its first place
├─ Places[]               in order; each one stretch of music
│  ├─ Variants[]          the same music from different sources; one is the base
│  └─ the result          the base plus the steps of the journal that name the place
├─ Shelf[]                variants not yet put in a place
└─ Journal                steps, oldest first, undone one at a time
```

- **A variant** is a copy of a fragment with its provenance: the piece and Notes it came from (their id; the provenance of the Notes copied, so the original's `sha256` of 0030 travels with it), the span (bars, and a recording's milliseconds), and the part taken (all, the right hand, the left hand, the melody). It keeps its own notes, bars, tempo and key, so it stays valid when its source, its original or its piece is removed. A fragment of another piece is always such a copy.
- **The base** of a place gives it its bars, meter, key and tempo. The other variants are aligned with the base (0022, section 2). The player can confirm anchors («такт 9 = 1:12»); a confirmed anchor is locked, and the alignment is worked out again around it on every open.
- **Bar numbers are the draft's own**, from 1, through the places in order. A place's label says where it came from («Ноты, такты 9–16»); a pencil mark in a draft belongs to its place.
- **Each place keeps its own meter, key and tempo.** Nothing is fitted to the place before or after it (section 5).

### 3. A dropped fragment never merges by itself

A fragment is added from the selection menu («Черновик»), in any view of notes (Notes read from a PDF, a recording's notes, another piece's Notes): all, the right hand, the left hand, or the melody where the voices are clean. It goes where the player says:

- **As a variant of a place**, offered first when the alignment finds at least 50 % of the fragment's notes in that place's base (as «частично» in 0024, section 4), with the percentage;
- **as a new place** at the end;
- **on the shelf.**

From the piece open, a fragment also goes into a draft of another piece of the library (version 1.2, the plan's answer 5, Andrei's choice of 2026-11-20): the «В черновик» sheet lists the other pieces' drafts grouped by piece, each offering «Новым местом в конце» or «На полку». music-core reads that piece's project file the app sends and answers it changed for the app to save (`project.drafts`, `drafts.add` with `file`, [0020](0020-music-core-facade.md)); the piece open, its file and its undo do not change, and the variant is a copy that names the piece it came from.

There is no hidden automatic merge. Within a place the player decides consciously, by приём (section 4), with one tap «Взять всё уверенное», or through the assistant (section 6).

### 4. Приёмы: differences grouped by kind, not by bar

music-core compares each variant with its place's base and groups the differences that have one kind and one shape in different bars into a приём: a fill, another bass, a simplification, an ornament, an octave, a rhythm, inserted bars. A приём is sure when at least 85 % of its notes are found in place (as «совпадает» in 0024). The player hears it (as in the notes; as on the recording, from the recording's own sound when it is on the device) and takes it everywhere («Взять везде · N»), here only, or leaves it. «Вставить такты» (bars the recording adds) is never taken by default and never squeezed into the bars of the base: taken, it inserts bars of its own.

What a step takes is written as notes: the base notes it removes and the variant notes it adds, in each bar it covers. The rebuild reads only those; the приём's kind and bars are its label. So a step means the same thing after the comparison's rules change. A hand or a selection taken from a variant is the same step with the notes of that hand or span.

The result of a place is its base with the steps applied in order (the journal of 0024, section 2: deterministic, one step at a time undone, a step whose notes are gone not applied and named). Colours on the score: plum for what is taken from a variant that is not the base, the warning brown for what the base has and the variant does not sound, grey for a likely artefact of the recording, cobalt for the selection. Provenance is shown by labels, not new colours.

### 5. Seams between places

A seam is the boundary of two places. music-core describes it (the difference of key, meter, tempo and register) and offers:

- **transpose a place** (by the interval to the neighbour's key),
- **the neighbour's tempo**,
- **a bridge**: two or three candidates built by music-core from rules of harmony, put as variants of a small new place between the two.

Each is a step or a place the player chooses; nothing is changed by itself. «Оставить» keeps the seam as it is and is remembered. Changing a place's meter is not offered yet (section 8). Version 1.1 builds the description of key, meter and tempo, the transposition, the tempo and «Оставить»; the register and the bridge candidates come in version 1.2 (the lead's decision, 2026-11-20, as the plan's answer 1 allows). Version 1.2 builds them as Andrei approved (the plan's answers 2 and 3, 2026-11-20): the register differs when the mean MIDI pitch of all the notes of one place is an octave or more from the other's, and the place after the seam is offered an octave towards the one before (a transposition by 12 that keeps its spelling); a bridge is offered only where the keys differ, two or three candidates of one bar in the meter, tempo and key of the place after the seam (V7 of its key; a chord both keys have, then V7; the bass by step from the last bass before the seam to the dominant), voiced in the neighbours' register, their notes marked inferred; «Оставить» in the bridge place removes it.

### 6. The assistant in a draft

The assistant (an experiment, Pro) works only in drafts, through music-core tools (0028, section 1), and every change it proposes is a dashed plum proposal the player hears before and after and accepts or refuses. It starts with three roles: **the place assembler** («Собери место ① как на записи без импровизации»: it reads the comparison and takes приёмы through the same steps as the player), **explanations** of differences and provenance citing facts, and **seams** (choosing among the bridge candidates). Accepted steps carry their origin (`assistant`) and are undone like the player's.

What it is sent: the notes of the draft's places (bar, beat, length, pitch, hand), their meters, keys and facts with ids; never a file, a name, a title or sound; in Pro only, after a separate one-time consent that lists exactly that. This widens 0028, section 5 (the selection) to the places of a draft, so the rule in `CLAUDE.md` changes with it (a change to the agents' rules, merged by Andrei). Version 1.2 builds it in music-core as Andrei approved (the plan's answers 1 and 7, 2026-11-20): a question is about one place, and what is sent is the notes of the place's base and of the variant built from (at most 32 bars each), the list of its приёмы and the description of the place's seams, never a recording's sound or the player's data; the roles are `placeAssembler` (`place.techniques`, `place.take`, which proposes the player's own takes), `explainer` (reading only) and `seamChooser` (`place.bridges`, `place.bridge`), and «Принять» makes the same call again and keeps what it proposes ([0020](0020-music-core-facade.md), `drafts.context`, `drafts.call`, `drafts.proposal`, `drafts.accept`). The API's relay serves the three roles, each with its own instructions (`src/backend/MotiffLab.Api/Assistant/Prompts`: `place-assembler-v1`, `explainer-v1`, `seam-chooser-v1`), and tells the model that the question is about the draft's place. On the iPad (i-6) a place's card on the draft's map offers «С ассистентом · Pro» on a place with a variant: the draft's own consent (its own key, the list above), one question at a time in the place assembler's role through the assistant's one client, each proposal a dashed plum card with «▶ До» (the place now), «▶ После» (`drafts.proposal`), «Отказаться» and «Принять» (`drafts.accept`, the draft's step and its toast's Undo).

### 7. Version 17 of the project file

Drafts are a new list in the workspace, `workspace.drafts`, and the file goes to version 17 (Andrei's decision 3, accepted with the concept on 2026-11-19); `project-file.md`, "Drafts", has the layout. A version 16 file reads with no drafts and with nothing else changed. A build before drafts refuses a version 17 file ("saved by a newer version"), as builds before 0.6 did with version 16; the API checks only the envelope (0026 S2) and needs no release. A draft with a value this build does not know is kept whole as an unknown entity: no draft is played, analysed or synced as the piece's notes, so keeping it aside changes nothing that sounds.

### 8. What goes and what comes later

- **The bar-by-bar merge leaves the iPad**: the version page with its strip and three starts, the merge draft and Mine · Version · Result, the Compare versions window, Apply into the working notes, and «Сделать рабочими» by assembly. A file with taken bars (`performance.taken`, `merge`) reads as it does now.
- **Later**, as new music-core operations: carrying a приём into another place, and changing a place's meter.

### 9. Steps and release

1. This record and `project-file.md` (docs only).
2. music-core: the draft model, reading and writing version 17, creating a draft from a selection, adding a variant (to a place, as a new place, to the shelf), the «та же музыка» suggestion, rebuilding a place, the facade.
3. music-core: приёмы, taking and leaving one, «Взять всё уверенное», taking a hand or a selection from a variant, anchors.
4. music-core: seams (description, transposing a place, tempo, bridge candidates).
5. iPad: «Черновик» in the selection menu, the «В черновик» sheet, the Sources group, the view tab, the draft view with place labels, playback.
6. iPad: приём numbers, the приём card, the pill, «Скрыть номера», hands.
7. iPad: the seam card, the draft map, the shelf; the bar-by-bar merge removed.
8. The assistant in drafts (tools, consent) and the privacy rule in `CLAUDE.md`.

## Decided without Andrei (decision log, 2026-11-19)

- «Та же музыка» (a variant of a place offered first): the alignment finds at least 50 % of the fragment's notes in the base, as «частично» in 0024.
- A sure приём: at least 85 % of its notes in place, as «совпадает» in 0024.
- Two or three bridge candidates, never more.
- The melody is the top voice of the right hand, only where the voices of the selection are cleanly split (no crossing, no split); otherwise the choice is not offered.
- A step records the notes it removes and adds, not only the приём it came from (section 4).
- The structure of a draft (its places, their order, their variants and base, the shelf) is its state, not journal steps; removing a variant that steps take from lists those steps and removes them with it.
- In the sync a draft merges by id, as Notes do (0026 S8): the same draft changed on two devices keeps the copy saved last, the other stays in the previous copy kept beside.

## Alternatives

- **Modify the recognised notes** (the «Моя версия» of the first page, the working notes changed in place). Rejected by Andrei on 2026-11-19: it breaks the link between Notes and their original.
- **One timeline for all fragments** (comping as in a DAW). Rejected: fragments of different pieces share no grid, and a recording's bars are not the print's.
- **Automatic merge, whole or incremental.** Rejected: the player could not tell what was taken; one tap «Взять всё уверенное» covers the quick path.
- **Small staves of every variant above the score** (option B of the first page). Rejected as the default: Andrei removed extra staves on 2026-11-07 (testing item 33).

## What this record changes in earlier records

| Record | Amended |
| --- | --- |
| [0022](0022-score-and-performance.md) | Section 5 leaves the iPad: merging happens in drafts. Alignment and comparison (sections 2 and 3) are what a draft lines up and compares its variants with. Taken bars in older files read as before. |
| [0024](0024-assembling-a-version.md) | The journal, bars at a seam and matching in words carry over to a place of a draft; the result is a draft, not the piece's notes. |
| [0028](0028-ai-bar-edits.md) | Section 5: in a draft the assistant may be sent the notes of the draft's places, with a consent of its own. |
| [0030](0030-originals-notes-piece.md) | Section 1: the piece also holds drafts, beside its Notes and work. A variant copies its source's provenance, so `sha256` follows it into a draft and into another piece. |
