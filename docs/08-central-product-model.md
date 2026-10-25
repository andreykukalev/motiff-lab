# Motiff Lab: Central Product Model

Status: decided, 2026-10-25. Owner: Andrei. It sets the frame for the next chapter of Stage 1 and builds on the Explore and Understand direction (decision record 0010). Where `07-product-definition.md` describes a track page and learning modes, this model takes precedence.

> Motiff Lab is a musical environment for working with a piece. It helps you not only learn the notes but also understand, explore and master the musical ideas within them.

You sit down at the piano, open a piece, select a phrase and work on it. Motiff Lab doesn't replace this process; it enriches it. This is not a set of useful features but a natural workflow in which each feature has its purpose.

## 1. A piece and a work session

Not a catalogue of features. Not an AI chat. Not a MIDI editor.

The central object is the loaded piece, and the main context for everything the player does is a work session around it.

- **The piece.** The original score stays the primary source.
- **Musical phrases.** The app suggests meaningful divisions, which the player can refine.
- **Working on a selected phrase:**
  - Practice: notes, rhythm, coordination.
  - Understanding: structure, harmony, motifs.
  - Technique: fingering, movement.
  - Experiments: variations and alternatives.
- **Putting the piece together.** Connecting phrases, the transitions between them, and performing the piece as a whole.

This is not a fixed linear programme. A player can move from harmonic analysis to ordinary playing, then return to fingering or try a variation. The structure helps them find their way but doesn't prescribe one correct path.

## 2. Why the phrase is the unit of work

A phrase sits at a useful level. Working on a single note can lose the musical meaning; working on the whole piece at once, too much happens at the same time.

A phrase is small enough to repeat, compare and explore, and large enough for musical relationships to appear in it: melodic direction, harmonic movement, tension and resolution, rhythmic patterns.

Phrase boundaries are not always unambiguous facts. Sometimes several segmentations make sense. So the app **suggests** boundaries and never presents them as absolute truth.

A phrase is therefore not just a selected range of bars but the working context around which every activity is organised.

## 3. Repetition that stays engaging without becoming a game

The environment stays unobtrusive. Suppose the player has selected a phrase from Für Elise. They can start by playing it as usual. Then open the analysis and find a recurring motif, a harmonic feature or characteristic notes. Then try a different fingering, if there is a justified alternative, or explore a rhythmic variation. Then return to the original and notice whether their understanding of the phrase has changed.

The app doesn't keep assigning tasks or evaluating the player. It makes interesting directions available when the player wants them.

Three ways of working:

| Way of working | What it offers |
| --- | --- |
| **I'm just practising** | Score, phrase selection, playback and easy navigation |
| **I want to understand** | Musical facts, explanations, and the places they refer to highlighted in the score |
| **I want to experiment** | Variations, comparison, and alternatives to explore |

These don't have to be three separate pages. The player switches between them while staying on the same phrase.

## 4. What music-core is for

| Core capability | Value for the player | Where it stands (2026-10-25) |
| --- | --- | --- |
| Import and score model | The piece opens as a working musical environment, not just a document | Built: MIDI, MusicXML and .mxl import, note events with provenance, score view |
| Phrase segmentation | The player can focus on a manageable passage | Built: boundaries from slurs, rests, repeat signs and passages that come back, the player's own edits, and the phrase strip in the app (#18) |
| Fact analysis | They explore the actual properties of this piece | Built: rhythm, melody, structure and character facts, fact digest with stable IDs |
| Transformations | They hear alternatives and test their own ideas | Built: V1, V2, V4, V9, V10, V13, hand edits, step scope, experiment catalogue and Explore card |
| Diff and comparison | They understand what changed between the original and a variation | Built: diff, Keep checks, "What changed", and returning to the original to compare (#43) |
| Recording observations | They keep their own conclusions and come back to them | Built: Keep this variation keeps the phrase, the experiment and alternative, the variation and a note in the player's words; the phrase lists what was kept, to open again, rename, export as MIDI and delete; saved with the project on the device (#78) |
| Export and restoring the original | They continue outside the app without losing the original | MIDI export and per-note revert built |

Facts, transformations and explanations work together. If the app detects a recurring motif, that observation both helps the player understand the phrase and suggests an experiment with another occurrence of the motif.

AI can become a useful guide connecting these capabilities, but the core workflow never depends on it being available.

## 5. Putting the piece together is a stage, not another mode

Working on single phrases and performing a whole piece have different needs. A phrase practised on its own may still not connect to its neighbour: tempo, continuity of movement, dynamic direction or the sense of overall form can get lost.

After local work, the app helps the player return to the wider context:

- Connect neighbouring phrases.
- Find places where the transitions are unstable.
- Listen to or play the whole piece.
- Notice musical ideas that recur across sections.
- Return to a specific phrase without losing the overall picture.

No performance evaluation is built before the core workflow is validated. To begin with, easy phrase navigation and switching between a local and a whole-piece view are enough.

## 6. The first complete workflow

Take the interface that already works and build one complete workflow around it:

| Step | What the player does | Issue |
| --- | --- | --- |
| 1 | Open a score file | Built |
| 2 | Select a suggested musical phrase | Built (phrase strip, #18) |
| 3 | Play it in the normal mode | Built (Practise, #50) |
| 4 | Open one specific musical analysis | Built (Understand, #25) |
| 5 | Try one meaningful experiment | Built (Explore card, PR #125) |
| 6 | Return to the original and compare | Built (Compare, #43) |
| 7 | Move to the neighbouring phrase or play a larger section | Built (#126) |

Keeping what was found (built, #78) supports steps 5 and 6: a kept variation opens again on its phrase to hear and compare. The pilot (#77) runs this workflow at the piano.

Every type of analysis, fingering, technique guidance and help with putting the piece together doesn't need to exist yet. First, check whether this organisation actually makes practice deeper and more interesting.

The phrase is the central unit of work, and the loaded piece is the persistent context for the whole session. Every feature is reached from this context rather than existing as a separate tool. Then Motiff Lab stops looking like an editor with a collection of musical operations and becomes an unobtrusive musical environment in which a person practises a piece, understands how it is built, and gains the ability to explore it on their own.
