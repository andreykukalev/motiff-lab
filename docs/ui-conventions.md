# UI conventions

Small, pragmatic rules for the web UI (`src/MotiffLab.Web`) so that UI fixes stay consistent while music-core is the priority. The UI only displays what music-core returns: it never changes analysis or variation code, or the shapes of their results, and it never computes musical facts itself.

## Layout

- The playback lab is a wide workspace: the piano roll on the left and the score on the right, side by side from about 70rem; below that they stack. Hiding the score gives the roll the full width.
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

## Hearing notes by hand

- Clicking a note, a key on the keyboard under the roll, or a pitch name in the roll's left gutter plays that key at once, also while the piece plays; dragging across keys plays each in turn. These notes go to the engine's `live` source: transport, source and track changes never cut them, and they never touch the piece or its playback.

## Colour and the Notes filter

- Colour by Track, Voice or Hand. A grouping with only one group in the piece (one track; no notated voices; no hands) is disabled with its reason next to the control, never left as a control that does nothing. The roll starts with the first grouping that tells notes apart: tracks, else hands, else voices.
- The Notes filter offers what music-core can pick out: hands, tracks, and notated voices of a track (`SelectionFilter`). It limits what plays (the original source), what is drawn at full strength and lights up on the roll, and what a selection keeps. Changing it keeps the selection's range and makes a reshape in place again.
- Melody, bass and harmony are listed but disabled until music-core can find them (voice and chord analysis, S1-A3). Add them as filters only when music-core provides them.

## Variations

- Each operation's parameters are one-click buttons that apply and play at once (S1-E3); Keep properties are toggle chips that turn lime when kept and dashed when the step broke them.
- The roll draws the variation as white outlines where its notes land, with a line from each note it moved; the motif's own notes go faint behind it. The score can show the piece with the variation in place ("With the variation"), generated from the notes and so marked inferred; making the first variation switches the score to it.
- Draw variations from `Variation.Changes` (before and after per note), never by recomputing the operation in the UI.

## Facts

- Facts are shown by `Facts/FactsPanel.razor`, one collapsible section per `FactLens`, in the enum's order. A new fact, or a new lens added to `FactLens`, appears without UI changes. Pass every analysis's facts to the panel in one list.
- Each section header gives the lens, the number of facts and how many are detected, inferred or uncertain, so a closed section still shows how sure it is. Closed sections stay closed as the selection changes.
- Every fact keeps its certainty label (detected, inferred, uncertain) and its basis as the hover text. Never drop either.

## General

- Group related controls into one row (`.row`) or panel; prefer a collapsible section with a summary over more lines of text.
- Collapsible sections use a `<button aria-expanded>` header with the state held in the component, not `<details>`, so Blazor re-renders keep the state.
- Colours come from the brand tokens in `wwwroot/css/app.css` (`docs/02-brand-identity.md`); anything inferred is marked as such, by more than colour.
