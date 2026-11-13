# 0028 · Reworking bars in words: the assistant edits notes through music-core tools

Status: accepted, 2026-11-13, as the direction for the future assistant, with nothing built or changed yet (Andrei, 18:18 and 18:21 UTC, thread «Заменить вариации правкой тактов через ИИ»). The parts marked **Open** are still to be decided. Supersedes the variation catalogue as a feature the player sees ([0009](0009-exploration-layer-and-notebook.md), [0010](0010-explore-experiments.md), the Try chips and Adjust of [0018](0018-redesign.md) section 7 and `design/kit/docs/01-ux-concept.md`) and the request shape of [0003](0003-ai-integration.md) (one request with a fixed schema, no tool use). Answers the open question of [0027](0027-product-concept-v1.md) section 6 on which AI rules the assistant's capabilities change.

Notes behind it (project folder): `product/architecture/ai-bar-edits-2026-11-13.md` (feasibility, read on `develop` `1d8ca4d`) and `product/architecture/ai-variations-2026-11-13.md` (architecture draft).

## Context

Andrei asked whether the assistant could change a given bar from the player's words: to fix a recognition artifact, or to rework the bar. Then he decided (18:18): the engine's rhythm and melody variations are no longer a feature of their own; an AI layer replaces them. The player works mainly with scores, selects a bar or a passage and, in text, tells the assistant how to rework it: for a concert, to feel out an improvisation, or to fix what a scan misread. An MCP server with tools for manipulating notes was suggested.

What the code already has (`develop` `1d8ca4d`):

- The player's corrections as a log over the imported notes, never changing them: each edit keeps the values it replaced, is undone one by one, and marks the note user-edited (`MusicCore/Events/NoteEditLog.cs`); the layer can be switched off (`EditsApplied.cs`).
- A note editor on the roll with a draft: change, add, remove, undo, reset, cancel, save (`Web/Roll/NoteEditor.cs`).
- Check flags per bar with a reason: too long or short for the meter, staves differ, out of range, grace notes, a poor page, an octave lost against a recording (`MusicCore/Projects/CheckFlag.cs`).
- Pure variation operations with parameter ranges, Keep checks and explanations written from the diff; a hand edit is a step of a chain (`MusicCore/Variations/*`).
- The assistant's entry points (Ask from the selection, the focus bar, Piece DNA), which send nothing yet (`Web/Pages/PlaybackLab.Assistant.cs`).

A language model writes notes unreliably (bar lengths, ticks, ranges) and its notes cannot be checked; it changes notes reliably through tools that code runs and checks.

## Decision

### 1. The player reworks a bar in words; the assistant acts only through tools

**Decided** (18:18). The player selects bars (or a passage) and asks in text, later by voice on the iPad: «в левой руке октава ниже», «лишняя нота на третьей доле», «сделай левую руку вальсовым аккомпанементом», «упрости ритм». The assistant answers by calling tools of music-core; it never writes notes, ticks or analysis itself. Each call is checked by the engine (ranges, bar length, the selection); a call that fails is returned to the model with the reason and nothing is applied.

### 2. The variation operations become the assistant's tools

**Decided** (18:21, the card «Что сделать с операциями вариаций в движке?», option «Инструменты ИИ»). The variation catalogue, the Try chips, Adjust and chains leave the interface. The operations stay in music-core, pure as before, and are offered to the assistant as tools with their parameter ranges, beside low-level note tools: change, add or remove a note; move, stretch, split or join notes; transpose the selection; move a hand by an octave; set a rhythm. The full list and their JSON schemas are written once in the music-core facade (0020) and serve both apps.

### 3. Every change is a draft the player hears and decides

**Decided** (following the non-negotiables). A proposal is shown over the selected bars as the ossia of 0021, in plum (what the app composed), with Listen before / after and Accept / Another / Cancel. One request changes one property at a time (pitch, or rhythm, or register, or texture); a larger request is split into steps the player accepts one by one. Accepted changes are written to the corrections log as ordinary edits marked «по подсказке ассистента» and are undone like any other. The explanation of what changed is written from the engine's diff, not from the model's words; the model's text is labelled as interpretation. A Check flag is closed only by the player's «Выглядит верно».

### 4. Where the tools run

**Decided** (architect, within the direction). The tools run on the device: music-core in WebAssembly on the web and as the NativeAOT framework on the iPad. The Motiff Lab API (0014) relays the conversation to the model with the tool list and keeps the key; it never runs the tools and never stores the notes. The model is called through one replaceable interface (0003, `IAssistantModel`), now with multi-step tool use, capped per request (proposed: 8 tool calls) and per player (0003 section 6).

**MCP.** For the app itself plain tool use through the API is simpler and cheaper. The tool schemas are written so that an MCP server can wrap the same facade later, if external agents (Claude Desktop, Claude Code) are to open Motiff Lab projects. **Open**: whether and when to build that server.

### 5. What is sent

**Decided in direction, Open in detail.** For a request the app sends the player's words and a compact description of the selection: the notes of the selected bars (bar, beat, length, pitch, hand), the meter, the key and the engine's facts with their ids. Never the file, its name or the rest of the piece. Only with the player's consent, in Pro (cloud and AI are Pro, 0018 section 12). **Open**: how much context beyond the selection (neighbouring bars, the phrase) a request may carry.

### 6. Recognition artifacts

**Decided** (architect, within the direction). For a bar with an open Check flag the engine first builds a short list of repair candidates from the flag's reason (a bar too long: shorten the last note, drop a doubled note, read a triplet; a lost octave: move the hand; staves differ: fill with a rest). The assistant picks and orders them from the player's words, or calls the note tools when none fits. Without AI the same candidates are offered as buttons in Check, so fixing artifacts keeps working with AI off. Showing the model a picture of the bar is not used: models read dense notation poorly and it would send the player's file to the cloud; re-reading only that region on the scan server is the deterministic alternative (**Open**, research).

### 7. With AI off

Reworking a bar in words needs the model and is unavailable with AI off or offline; the manual note editor and the Check repair buttons stay. Every other feature still works with AI off.

## Consequences

- This record plans the product's future assistant; nothing changes today. The AI and privacy rules of `CLAUDE.md` (Conventions) stay as they are until the assistant is built; they are changed then, with the first code of this record (decision 6, merged by Andrei; Andrei 18:25: «это просто на будущее, когда будет ассистент»).
- music-core: a tool registry in the facade (names, JSON schemas, ranges) over the existing operations and new low-level note tools; repair candidates per Check reason; addressing notes by bar, beat, hand and voice. Still no AI or Azure dependency: music-core only describes and runs tools.
- API: a conversation endpoint with tool use, the key as a secret, limits and a budget; the service and its cost are Andrei's decision 4.
- UI, both apps: the variation catalogue, Try chips and Adjust go; the ossia stays as the place of a proposal; Ask on a selection gains the draft, Listen before / after and Accept.
- The project file does not change in the first step: accepted changes are corrections in the existing log. With the sources model (format v16) the corrections belong to the Notes they edit.
- Prompts and an evaluation set of requests on real bars, as in 0003 section 8, before the feature reaches a version.
- The product concept text (`docs/01-mvp-stage-1.md`, `docs/08-central-product-model.md`, `design/kit/docs/01-ux-concept.md`) is rewritten to this record separately, by the concept author.

## Order

1. This record (now). The rule change in `CLAUDE.md` comes with step 2, when the assistant is built.
2. music-core: the tool registry, the note tools, the operations as tools; Check repair candidates (useful at once, with AI off).
3. API: the conversation relay with tool use and limits (after decision 4).
4. UI: the draft over the bars, before / after, Accept, on the web and the iPad; the catalogue removed.
5. The concept rewrite.
