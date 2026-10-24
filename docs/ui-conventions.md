# UI conventions

Small, pragmatic rules for the web UI (`src/MotiffLab.Web`) so that UI fixes stay consistent while music-core is the priority. The UI only displays what music-core returns: it never changes analysis or variation code, or the shapes of their results, and it never computes musical facts itself.

## Layout

- The playback lab is a wide workspace: the piano roll on the left and the score on the right, splitting the width 50/50 from about 70rem; below that they stack. Hiding the score gives the roll the full width.
- A toolbar above the workspace holds what changes the whole view: the Notes filter, what is heard (Hear), and the score switch and choice. Selection, reshape controls and facts sit below the workspace, side by side on wide screens.
- There are no bar number boxes: selection is made with the mouse on the roll.

## Selection on the piano roll

- **Drag anywhere** on the roll (the bar numbers or the notes area) to select a range. music-core snaps it to beats (`MotifSelector.Drag`); the roll only reports where the drag started and ended.
- **Drag an edge** of the selection (grips on the bar numbers, an ew-resize cursor anywhere on the edge line) to move just that edge; the other edge stays. A reshape in place is made again on the new range.
- **Click a bar number** to select that bar. **Shift-click a bar number** to extend the selection to that bar, in whole bars.
- **Click a note** to hear it and select it; Shift, Ctrl or Cmd adds to the note selection. A press on a note that moves becomes a range drag.
- **Clear** with Escape, a click on an empty place in the roll, or a click on the page outside the roll. Clicks on controls (buttons, inputs, links, labels) never clear. Panels that show or act on the selection carry `data-keeps-selection`, so reading or using them does not clear it: today the selection panel (facts, Keep, variation) and the score. Mark any new panel of that kind the same way.
- **Space** plays and pauses from anywhere on the page: with notes selected it loops them and plays (later presses pause and resume the loop), with nothing selected it plays the piece. It acts on buttons and sliders too (Enter still presses a button), but not in text fields, lists, checkboxes or radios, where Space types or ticks.
- The 8-bar limit and its message come from music-core (`MotifSelection.MaxBars`).

## The selection in the score, and the range that plays

- The score shows the same selection as the roll: notes that start outside the range that plays are drawn faint, as on the roll, and the first note in the range is scrolled into view. Both views take the range from the page (`ShownRange`), so they never disagree with each other or with what loops.
- The range that plays is the selection's range while the original is heard. While the variation is heard it runs from the selection's start to the end of the variation's last note, rounded up to the beat: a variation that runs longer (half-time) extends it, one that runs shorter (double-time) shortens it. Heard together with the original (Both), it never ends before the selection. It never runs past the end of the piece.
- The roll marks the range that plays with a white line over the bar numbers; where it differs from the selection it adds a dashed end line, and a light band where it runs past the selection. The selection itself (lime band and edges) stays as the player made it.
- Applying, changing or removing a variation never changes the selection. While the selection loops, the loop follows what is heard and the variation's length; changing Hear while it loops keeps looping.

## Hearing notes by hand

- Clicking a note, a key on the keyboard under the roll, or a pitch name in the roll's left gutter plays that key at once, also while the piece plays; dragging across keys plays each in turn. These notes go to the engine's `live` source: transport, source and track changes never cut them, and they never touch the piece or its playback.

## Colour and the Notes filter

- Colour by Track, Voice or Hand. A grouping with only one group in the piece (one track; no notated voices; no hands) is disabled with its reason next to the control, never left as a control that does nothing. The roll starts with the first grouping that tells notes apart: tracks, else hands, else voices.
- The Notes filter offers what music-core can pick out: hands, tracks, and notated voices of a track (`SelectionFilter`). It limits what plays (the original source), what is drawn at full strength and lights up on the roll, and what a selection keeps. Changing it keeps the selection's range and makes a reshape in place again.
- Melody, bass and harmony are listed but disabled until music-core can find them (voice and chord analysis, S1-A3). Add them as filters only when music-core provides them.

## Variations

- Each operation's parameters are one-click buttons that apply and play at once (S1-E3); Keep properties are toggle chips that turn lime when kept and dashed when the step broke them.
- The roll draws the variation as white outlines where its notes land, with a line from each note it moved; the motif's own notes go faint behind it. The score can show the piece with the variation in place ("With the variation"), generated from the notes and so marked inferred; making the first variation switches the score to it.
- A variation is a chain of steps (`VariationChain`): a parameter button proposes a step on top of the accepted ones; **Accept step**, **Reject step**, **Undo** and **Redo** sit in one row with the breadcrumbs ("Original › Half-time › trying Later by 1 eighth"). Selection and Keep changes replay the chain; if a step no longer runs, the variation is cleared. Rule flags (`Variation.Flags`) are listed under the explanation, never hidden.
- Draw variations from `Variation.Changes` (before and after per note), never by recomputing the operation in the UI.
- **Export motif MIDI** and **Export variation MIDI** hand over the file `MidiExporter` writes (S1-X1), named by `MidiExporter.FileName` after the project, the bars and the steps. The download goes through the storage script; nothing leaves the device.

## Correcting imported notes

- **Correct notes** (S1-I7) is a collapsible table of the selected notes under the variation controls: each row nudges pitch by a semitone, onset and length by a 16th, and velocity by 10, or removes the note. Edited rows are marked "Edited by you" as well as by the lime edge.
- Every correction goes through `NoteEditLog`; the UI never changes a note itself. **Undo correction** drops the last edit and **Revert** drops every edit of one note, so the imported value comes back exactly. Removed notes are listed with a Revert button each.
- The project saves the log, and opening a project applies it. After the first correction the score switches from the file as written to the score generated from the notes, since the file does not know the corrections.

## Facts

- Facts are shown by `Facts/FactsPanel.razor`, one collapsible section per `FactLens`, in the enum's order. A new fact, or a new lens added to `FactLens`, appears without UI changes. Pass every analysis's facts to the panel in one list.
- Each section header gives the lens, the number of facts and how many are detected, inferred or uncertain, so a closed section still shows how sure it is. Closed sections stay closed as the selection changes.
- Every fact keeps its certainty label (detected, inferred, uncertain) and its basis as the hover text. Never drop either.

## General

- Group related controls into one row (`.row`) or panel; prefer a collapsible section with a summary over more lines of text.
- Collapsible sections use a `<button aria-expanded>` header with the state held in the component, not `<details>`, so Blazor re-renders keep the state.
- Colours come from the brand tokens in `wwwroot/css/app.css` (`docs/02-brand-identity.md`); anything inferred is marked as such, by more than colour.
