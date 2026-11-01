# 0013. The shared interaction model of the workspace

**Status:** proposed, for Andrei's review · **Date:** 2026-10-27 · **Scope:** `MotiffLab.MusicCore` (musical objects, claims, sessions, guidance, annotations), `src/MotiffLab.Web` (one workspace state behind Practise, Understand and Explore, and the Guidance Layer over it)

> 2026-11-01: the redesign ([0018](0018-redesign.md)) keeps this model and changes two things: Practise, Understand and Explore are the verbs of one Insights stream, not modes, and the Tag mark of section 10 becomes bookmarks with categories. Sections 3, 6 and 9 landed with PR #145 (SentenceMap, FactRanking, the Interpretation claim kind).

## Context

0012 gave the workspace its structure: projects, sessions and ideas; the score first; Focus mode. Andrei's review of the mockups that followed asked for one more thing before more screens are built: a shared interaction model, so that the selected musical object, playback, the active variation, the facts and the ideas are one model that the three ways of working read, not three implementations of selection, playback and state. The review also asked to settle the relationship between a session and the object it works on, to make the selected level unambiguous, to tell kinds of musical claim apart rather than relying on a confidence number, to make a recommended experiment honest, and to define the Russian terms once. A second brief asked for guided musical exploration: a contextual practice guide that suggests the next useful question or action for the passage in front of the player, across the whole app rather than as another mode.

This record defines that model. It names what exists in code today and what changes. The code changes come in the batches that follow (#132, #133, #135, #136, #137), each against this record.

## Decision

### 1. Four things, and who owns what

| Thing | Owns | In code today | Change |
| --- | --- | --- | --- |
| **Project** | the source piece, its notes, meter and keys, the player's corrections, the phrases and (later) sentences and sections, the ideas, the sessions | `Project`, `ProjectFile` (version 8) | adds sentences and sections beside phrases; nothing else moves |
| **Session** | the player's goal (intent and task), the **working context** (the selected musical object, the active variation, the tab) and the **experiment history** (the chains tried in it, kept or not) | `Session(Id, Intent, Task, Passage, StartedAt, LastActive)` | `Passage` becomes a `WorkingContext`; the history is the session's list of chain ids; a session keeps working when the player moves from a phrase to its sentence or down to a motif, so moving levels never starts a session or drops its history |
| **Musical object** | a region or structure of the source piece: its kind, its span, which notes count, and how it was found | `MotifSelection` (span, bars, filter, note ids), `Phrase` (number, span, boundary with provenance) | one `MusicalObject` record (below) that every level shares; `MotifSelection` stays the note-level resolution of an object |
| **Idea** (a saved observation) | a reference to a musical object, the variation kept with it (as steps, so it is restored by replaying), the player's interpretation and note, what they intended and what they heard, and the session it was kept in; later also a kept **annotation on the object's notes**, such as a fingering, keyed by note id so it stays attached when the visible range or the view changes | `Observation(Id, Phrase: SavedPassage, ExperimentId, Alternative, Variation, Name, Note, CreatedAt) { SessionId }` | `Phrase` becomes a `MusicalObject` reference; gains `Intended`, `Heard` and `Interpretation` (#135, #136); the parent idea when branched |

Rule: a project outlives its sessions, a session outlives its selections, an idea outlives the session it was kept in. Nothing in a session is lost by ending it except the session itself.

### 2. The musical object

```csharp
public enum ObjectKind { Piece, Section, Sentence, Phrase, Motif, Range }

public sealed record MusicalObject(
    ObjectKind Kind,
    TickSpan Span,                 // where it sounds; both edges on beats, as today
    SelectionFilter Filter,        // which notes count: all, a hand, a track, a voice
    string? StructureId,           // "ph_2", "se_A", "sc_A" when it is a structure the map knows; null for a free range or a motif made by hand
    Provenance Found,              // how its boundaries were found: Imported (marked in the file), Inferred (a rule), UserEdited (the player's)
    double? Confidence)            // for Inferred boundaries only
{
    public MusicalObject? Parent { get; }   // the structure that contains it, resolved by the project's maps
    public IReadOnlyList<MusicalObject> Children { get; }
}
```

- `Range` is a bar or beat selection made on the roll or in the score that is not a known structure; it has no parent other than the piece and no children. Making it a phrase (as today) turns it into a structure.
- A `Motif` is a note-level selection inside a phrase (two to 32 notes, as `MotifSelection` already bounds); its parent is the phrase it lies in.
- The object is resolved to notes by `MotifSelector`, as now; the object itself never carries notes, so it stays small enough to save in a session and an idea.
- The 8-bar limit (S1-M1) applies to ranges and motifs. A phrase, sentence or section longer than that is selectable whole; the operations that need a short passage say so rather than silently cutting the selection.

### 3. One primary selection, with context

1. **Exactly one object is the primary selection.** It is what the selection text names, what loops, what Understand describes, what Explore varies, and what Keep saves. The score, the roll and the panel show it the same way: lime band, lime chip or crumb, bold in the selection text.
2. **Parents and children stay visible as context.** The hierarchy line above the strip (`Minuet › Section A › Sentence A › Phrase 2 › motif`) shows the chain from the piece to the selection; the selection's children are outlined on the strip (the phrases of the selected sentence) and marked in the score. Context is never lime.
3. **Selecting is one gesture at every level**, with the same result everywhere: tap a crumb, a chip, a bar in the score, a bar number on the roll, or ‹ ›. ‹ › step among siblings of the selected level. Selecting a child narrows (the parent becomes context); selecting a parent widens (the former selection becomes context, still outlined); selecting a sibling moves. Nothing else changes: the tab, the tempo, the loop state, what is heard and the active variation carry over, and the loop is re-cut to the new object.
4. **An action applies to the primary selection**, always. A button that acts on something else names it in its label ("Loop just bar 6"); there is no implicit scope.

### 4. Kinds of claim, with evidence

Every statement about the music on the screen carries a **kind**, shown by a word and a line style, not only by a colour or a number:

| Kind | Meaning | Examples | Shown as |
| --- | --- | --- | --- |
| **Detected** | read from the file or measured from the notes | a tie held over the bar line; a rhythm that matches; a contour that falls; a note count | solid, "detected" |
| **Inferred** | an algorithmic hypothesis from the notes, with a confidence | a half close; a phrase boundary at a rest; a key; a hand | dashed, blue, "inferred · 0.8" |
| **Interpretation** | a musical reading that the notes do not decide | "phrase 2 answers phrase 1"; "this makes the phrase feel tense"; what the player writes | outlined, "interpretation", with who made it (music-core's template, AI, or you) |

- `FactCertainty` today has Detected, Inferred and Uncertain. Uncertain is a low-confidence Inferred, so it stays a display threshold, and **Interpretation** is added as a kind that music-core's facts can carry (for its own readings, such as "answer") and that the player's observations carry.
- Every claim carries **evidence**: the fact ids it rests on and the places (`FactPlace`) it points at, so *Show in score* and *Why?* work the same for all kinds. An interpretation with no evidence is allowed only when it is the player's.
- AI text is always an interpretation that cites fact ids (0003); it never changes a claim's kind.

### 5. A recommended experiment is a real one

A "Recommended next" or an intention button offers only what the engine can run now. The contract of **Try it**:

1. It names the operation and the notes before it runs ("Release the tie on F♯, bar 6: Articulation on one note").
2. It creates a variation; the original is never changed (as every operation today).
3. It explains what changed from the diff, and only from the diff.
4. Original and variation are one tap apart (the compare switch), with *One after the other*.
5. Keep or Leave; Keep records what was intended (the recommendation's words), what changed, and, if the player answers, what they heard.

A suggestion the catalogue cannot execute is not shown as a button. It may be shown as a question for the player to answer at the instrument, marked so.

The same contract holds for any generated candidate the player tries and keeps, not only variations of the notes: a fingering candidate (movement-aware fingering, a later batch) names its strategy and the notes it covers, never changes the notes, explains its trade-off from what it computed, is compared one tap from its alternatives, and is kept as an annotation marked Inferred until the player edits it (UserEdited). Only a strategy with a valid candidate for the actual passage is offered.

### 6. The shared workspace state

One state behind the page, which the three tabs read and ask to change:

```
WorkspaceState
  Project, Session
  Selection: MusicalObject (primary), plus its resolved MotifSelection
  Playback: position, tempo, looping (bool), loop range, what is heard (sources), hand or layer
  Variation: the active chain and step, or none; the compare choice (original, variation, alternating)
  Facts: the facts of the selection, of the variation, and the diff between them
  Interpretations: the player's, for this object
  Ideas: those referencing this object or its ancestors
```

- **Practise** reads Selection and Playback and changes Playback only.
- **Understand** reads Selection, Facts and Interpretations; it changes Interpretations (the player writes) and can ask Explore to start an experiment.
- **Explore** reads everything and changes Variation and Ideas.
- **Tools** holds the mechanisms over the same state.
- Switching tabs changes nothing in the state. The score and the roll are two views of Selection, Playback and Variation and never hold their own.

In code this is the `PlaybackLab` page's fields today, spread over partials. The change is to gather them into one `WorkspaceState` class that the tab components receive as a cascading value, with the page as the only writer, so that no tab can reset another's part. No new UI is needed for this step; it is the ground the sentence-level workflow (#133) is built on.

### 7. Russian: terms defined once

Terms live in `wwwroot/i18n/ru.json` under `term.*` and are used from there everywhere, so a term is never retyped in a component (0012). Technical terms keep the English in brackets the first time they appear on a screen.

| English | Русский | Note |
| --- | --- | --- |
| piece | произведение | never "пьеса" in the interface |
| section | раздел | |
| sentence | предложение | "музыкальное предложение" only in help text |
| phrase | фраза | |
| motif | мотив | |
| bar, beat | такт, доля | |
| selection | выделение | the action: "выделить", clear: "снять выделение" |
| loop | цикл | the action: "зациклить" |
| variation, original | вариация, оригинал | |
| detected, inferred, interpretation | обнаружено, предположено, интерпретация | the claim kinds, always these words |
| keep an idea | сохранить идею | |
| Practise, Understand, Explore, Tools | Поиграть, Понять, Исследовать, Инструменты | the tabs |

Actions read as actions ("Попробовать", "Сохранить", "Назад к работе"); operation names show the pair, for example "Синкопирование (Syncopate)".

### 8. The Guidance Layer

Guidance is a layer over the shared state, not a fourth tab. It suggests the next useful question or action for the object the player has selected; the player stays in control and may ignore every suggestion.

**The practice sequence it guides** (flexible; a step can be skipped or repeated):

1. Select a meaningful passage ("Is this a complete musical thought? Where does it end?").
2. Listen and form a hypothesis ("What is the character? Where is the tension?").
3. Learn the material, hands separately ("Which hand carries the line here?").
4. Integrate the hands ("Where do the hands pull against each other?").
5. Reflect and explore ("What did you hear? What would you change?").

**Hints are contextual**, phrased about the actual passage, informed by the facts and their kinds, and never prescribing one interpretation:

> Bars 5–8 · **Suggested next step · Listen.** The left hand changes register at bar 7 (detected). Play these bars once at a slow tempo and decide where the phrase feels like it ends, then try the loop with the left hand only.

**The hint pattern library** (`src/MotiffLab.MusicCore/Guidance/`, data, not prose in components), one entry per pattern in five families: passage selection, listening and analysis, technique and coordination, evaluation and reflection, creative exploration. Each pattern has a purpose, a short instruction, an optional *why* (which cites fact ids, as 0003 requires of all explanations), and a concrete next action, which is one of: a selection to make, a playback setting (loop, tempo, a hand), a question to answer, or a catalogue operation to try (so it obeys the Try it contract of §5 and is never shown when the engine cannot run it).

**The guidance engine** is deterministic and lives in music-core; the first version has no AI. Its inputs are the shared state: the activity (Practise, Understand, Explore), the selected object and its kind, the facts of the selection with their kinds and confidence, the session's goal, the previous hint and the player's response to it, and the operations the catalogue can run on the selection. Same inputs, same hint. Candidate rules for the first version:

| When | Suggest |
| --- | --- |
| the selection is a free range that does not match a phrase | check whether it is a complete phrase (the phrase map's nearest boundary, as an inferred claim) |
| the passage is dense (note count, hand crossing or a register change detected) | listen first at a slower tempo, then isolate one hand or the transition |
| the player has looped one hand for a while | integrate the hands when the single hand feels comfortable |
| the selection contains a repeated motif (detected) | ask what changes between the statements |
| the selection contains a large leap or a change of hand position (detected) | examine the fingering and the movement into the next position; compare two candidates, then try the transition slowly (a leap is a question to ask, never assumed difficult) |
| a variation was just created | compare what was intended with what was heard (§5, step 5) |
| an idea was just kept | develop it further, or return to the original passage |

**A hint has a state**, which describes the interaction, never mastery: *Suggested*, *In progress* (the player acted on it: looped, selected, answered), *Reflected on* (the player answered the reflective question), *Skipped*. The app never claims a passage is learnt; it records what was tried. States live in the session's history beside the experiment chains, so a new session starts without them.

**Placements**, all optional and none interrupting playback:

- the one recommended action in the contextual panel's *Now* section (#132), which is the hint of highest priority for the selection;
- a session guide at the top of the panel: the session's goal and a few steps, each with its state, skippable as a whole;
- a reflective question after an experiment or a practice attempt ("How did that feel: more tense, calmer, almost unchanged, something else?"), shown when playback stops, never while it plays.

**First implementation** is one scenario end to end, on one real piece: select a phrase, listen, identify one question, practise the relevant passage with one hand, integrate the hands, reflect. AI may later rephrase a hint's text (0003: an interpretation that cites the hint's fact ids); it never chooses the hint.

### 9. Understand this phrase

Understand is one capability over the selected object, not a report: it answers three questions about it, **what is happening**, **why it might matter** and **how to hear or recognise it**, and leads to an action. Andrei's specification (2026-10-27) is folded in here; #143 carries the batches.

**Information hierarchy** (the *Now* view of #132): the selected object with its parent as context; one key observation, ranked from music-core's facts by `FactRanking` (deterministic, with its reason: places first, structure before the line, the surer first; an interpretation is never the key observation, since it rests on others); its **why it matters** line, which is an interpretation and says so; **Try this**, one action the engine can run now; then *Details*, where the dimensions open one at a time. Nothing is shown that analysis cannot support; where analysis is missing the view says so rather than filling the gap.

**Dimensions** are the fact lenses, each a question: Melody (what is the line doing), Harmony (how the bass and the closes move the direction; a new lens that holds the key, the closes and bass movement, with its uncertainty always shown), Rhythm (how timing shapes the phrase), Structure (where the phrase comes from and what it contributes), Character (how the choices might be heard; always interpretive). The spec's claim types map onto §4: *observed* is Detected or Inferred with its confidence, *interpreted* is Interpretation, *uncertain* is the Uncertain threshold.

**Local and global.** Every fact carries a scope: about the selected object itself, or about its place in the piece (its sentence, its recurrences, a similar gesture elsewhere). The view separates the two and lets the player move to the related object (a recurrence, the parent sentence) while the analytical question stays: the selection changes, the chosen dimension and the highlighted fact do not.

**Relationships, not labels.** A fact's text may connect evidence to significance in one sentence ("phrase 2 repeats the start of phrase 1 and ends on the tonic instead of the dominant") when both halves are measured; the significance half ("so it feels like an answer") is a separate Interpretation claim that cites the measured one.

**Active understanding.** Each supported fact can lead to a short activity, offered as an invitation, never a test, and run by the Guidance Layer (§8) as hint patterns: *Listen* (loop with one feature in mind, a hand or a layer alone), *Predict* (stop before the phrase end or the next bass note, then reveal), *Recall* (hide the score, play or sing, then show it), *Compare* (two occurrences, one after the other, then say what changed), *Explain* (write what makes it recognisable, kept as an annotation). The score and playback are always one tap away.

**Continuity.** An observation becomes a practice focus (Practise with the same selection, a hint naming what to listen for), an experiment (Explore, only catalogue operations), an annotation (§10) or a kept idea with its source and context. The selection, the tab's question and the active variation carry across, as §6 requires.

### 10. Annotations and practice continuity

An **annotation** is the player's own claim or intention about the music, kept with the project and anchored to the music it is about, so that what is noticed away from the piano is there at the piano. Andrei's specification (2026-10-27) is folded in here; #144 carries the batches.

```csharp
public enum AnnotationKind { Observation, Question, PracticeGoal, CreativeIdea, Interpretation }
public enum AnnotationStatus { Open, Resolved, Deferred, Done }

public sealed record Annotation(
    string Id, AnnotationKind Kind, string Text,
    AnnotationAnchor Anchor,               // note ids; a musical object (kind, span, structure id); a transition between two objects; or the piece
    AnnotationStatus Status, string? Resolution,
    IReadOnlyList<string> FactIds,         // the facts it rests on or asks about
    IReadOnlyList<string> IdeaIds,         // the kept ideas or experiments it led to
    string? CreatedInSession, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
```

- **Ownership** follows §1: the project owns annotations; a session records which it created, reviewed or acted on; ending a session loses none of them. The existing `Observation` (a kept idea) stays what it is, a variation kept with its context; an annotation can point at it. Both are saved in the project file (a version bump), never in a new store.
- **Anchors** are stable: note ids and a musical object (its structure id and span), with bar and beat kept only for display. When the notes change so that an anchor no longer resolves, the annotation is flagged for review, never moved to other music.
- **Kinds** shape the actions, not a workflow: an Observation can become a Question or a Practice goal; a Question has a resolution note and is resolved, deferred or left open by the player, never by the app; a Practice goal can be added to a session and marked done; a Creative idea opens Explore and records intention and outcome (#135); an Interpretation is always marked as the player's and may cite facts, as §4 says.
- **Where they live on screen:** the contextual panel shows the annotations of the selected object and its ancestors (under *Yours* in Understand and in *History*), with one-tap creation from the selection (kind, a line of text, save; no modal); the project page lists all of them with a filter by kind; the hierarchy line shows a count on an object that has them.
- **Preparation to practice:** the project page works without a session (browse, select, read facts, annotate). Starting or resuming a session shows a compact preparation summary (open questions, practice goals, creative ideas, each opening its passage); it never starts playback or changes the selection by itself. Practice is possible without any of it.
- **Guidance** (§8) reads open questions and goals as inputs, so a hint can say "you asked why the harmony changes in bar 6; loop the left hand there"; AI may later suggest questions, but never writes a note or resolves one.

## Consequences

- Batches build on this in order: the shared state (no visible change, a refactor with the existing browser tests as the guard), then the sentence-level workflow end to end on one real piece (#133 with #132's Now panel), then claim kinds in facts and the Try it contract (#132, #137), then recording intended, changed and heard with ideas (#135, #136), then Russian terms and the rest of the strings (#134).
- The project file gains a version for `MusicalObject` references in sessions and ideas; version 8 files open, their passages read as `Range` objects, or as the phrase they match.
- music-core gains `MusicalObject`, the `Interpretation` claim kind and evidence links; nothing in it depends on the UI.
- Every claim on the screen can be traced: tap it, see its kind, its evidence and its places.
- The Guidance Layer comes after the shared state and the sentence-level workflow, as the deterministic engine and the first scenario; its hints read the same state and offer only what §5 allows.
- Understand this phrase (§9) lands in phases: the grounded observation with the sentence workflow (#133, #132), then local and global context and the dimensions, then the active-understanding activities through the Guidance Layer, then continuity into annotations. Annotations (§10) come with a project file version and the preparation summary (#144).
