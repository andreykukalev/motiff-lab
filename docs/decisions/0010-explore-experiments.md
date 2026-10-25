# 0010 · Explore experiments built from catalogue operations

Status: proposed, 2026-10-25. Prototyped for the four experiments Andrei picked; to be confirmed after playing them at the piano.

## Context

After the first experiments with the playback lab, the product direction is two ways of working with one piece over the same score: **Understand** (why does this music work the way it does?) and **Explore** (what else could it be?). The app stays beside a real instrument: the iPad on the music stand, the piano in front of the player.

Single operations on notes (level 1, "halve the durations") rarely carry creative value by themselves. A musical transformation (level 2, "create space between the notes, keeping their onsets") has a clear idea and boundary. An experiment (level 3, "how does articulation change the character of this phrase?") picks a fitting passage and offers alternatives the player hears, plays and compares with the original. Level 3 is the centre of Explore.

The review page of 2026-10-25 traced five candidate experiments to the engine. Andrei picked four to prototype: A "Give the melody more space", B "Same notes, different groove", C "Keep the melody, change the accompaniment" and D "Answer the motif differently".

## Decision

1. **Step scope.** Every step carries a scope: the whole selection, the melody, the accompaniment or the bass, read from the shared texture (`MotifTexture`). A hand is not a voice. V1 and V13 accept any scope; operations that choose their own notes (V4, V9, V10, hand edits) accept only the whole selection.
2. **Two operations the experiments need.** V13 Articulation (short, detached, connected; onsets kept) and V2 Accompaniment pattern, built from each bar's own accompaniment notes so it needs no chord detection.
3. **An experiment catalogue in music-core** (`MotiffLab.MusicCore.Experiments`). An experiment has a stable id, a topic for "I have a question", a title, the change in a sentence, the question, a listening focus, the facts it rests on, a passage, a Keep preset and alternatives. Each alternative is a recipe of one or more catalogue steps. A rule offers the experiment when its facts are present and not uncertain, and keeps only the alternatives that can run on the passage and sound different from each other. It reads the same `ObservationContext` as the observation catalogue, so Understand and Explore rest on the same facts.
4. **Surprise me** picks one offered experiment and one alternative from a seed, preferring experiments not tried yet. It is deterministic and works with AI off.
5. **Starting an alternative gives an ordinary variation chain**, so it plays, compares, saves and exports like any other variation.

## Consequences

- Project files move to version 4 and save each step's scope, plus V2, V4, V9, V10 and V13 steps.
- The Explore card is the first tab of the playback lab's inspector (`Explore/ExploreCard.razor`, docs/ui-conventions.md). It reads `ExperimentCatalogue.Offer` and `Surprise`, lists questions by `ExperimentTopic`, and starts alternatives with `ExperimentCatalogue.Start`. An experiment being tried is saved with the project only when the player keeps it; Leave it puts back the selection, Keep choices and steps the player had. The card can play the melody or the accompaniment alone, read from the texture, and for the motif's return it loops the motif and the new answer with the piece around them. Analytics count which experiment and alternative was tried or kept and how the player got there (`experiment_tried`, `experiment_kept`), never the notes.
- Backlog items that this reframes (learning modes #50, guided sequences #51, the Transform, Compare and Variations screens #41, #43, #44) are not changed until the direction is confirmed.
- AI may later phrase the idea or choose among catalogue experiments, citing fact IDs, but it never adds a transformation (0003).
