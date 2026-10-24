# UI conventions

Small, pragmatic rules for the web UI (`src/MotiffLab.Web`) so that UI fixes stay consistent while music-core is the priority. The UI only displays what music-core returns: it never changes analysis or variation code, or the shapes of their results, and it never computes musical facts itself.

## Selection on the piano roll

- **Drag anywhere** on the roll (the bar numbers or the notes area) to select a range. music-core snaps it to beats (`MotifSelector.Drag`); the roll only reports where the drag started and ended.
- **Click a bar number** to select that bar. **Shift-click a bar number** to extend the selection to that bar, in whole bars.
- **Click a note** to select it; Shift, Ctrl or Cmd adds to the note selection. A press on a note that moves becomes a range drag.
- **Clear** with Escape, a click on an empty place in the roll, or a click on the page outside the roll. Clicks on controls (buttons, inputs, links, labels) never clear. Panels that show or act on the selection carry `data-keeps-selection`, so reading or using them does not clear it: today the selection panel (facts, Keep, variation) and the score. Mark any new panel of that kind the same way.
- **Space** plays and pauses from anywhere on the page: with notes selected it loops them and plays (later presses pause and resume the loop), with nothing selected it plays the piece. It acts on buttons and sliders too (Enter still presses a button), but not in text fields, lists, checkboxes or radios, where Space types or ticks.
- The 8-bar limit and its message come from music-core (`MotifSelection.MaxBars`).

## Facts

- Facts are shown by `Facts/FactsPanel.razor`, one collapsible section per `FactLens`, in the enum's order. A new fact, or a new lens added to `FactLens`, appears without UI changes. Pass every analysis's facts to the panel in one list.
- Each section header gives the lens, the number of facts and how many are detected, inferred or uncertain, so a closed section still shows how sure it is. Closed sections stay closed as the selection changes.
- Every fact keeps its certainty label (detected, inferred, uncertain) and its basis as the hover text. Never drop either.

## General

- Group related controls into one row (`.row`) or panel; prefer a collapsible section with a summary over more lines of text.
- Collapsible sections use a `<button aria-expanded>` header with the state held in the component, not `<details>`, so Blazor re-renders keep the state.
- Colours come from the brand tokens in `wwwroot/css/app.css` (`docs/02-brand-identity.md`); anything inferred is marked as such, by more than colour.
