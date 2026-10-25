# UI conventions

Small, pragmatic rules for the web UI (`src/MotiffLab.Web`) so that UI fixes stay consistent while music-core is the priority. The UI only displays what music-core returns: it never changes analysis or variation code, or the shapes of their results, and it never computes musical facts itself.

## Layout

- The playback lab follows the iPad mockups (PR #120): a header, the canvas on the left, the inspector on the right from about 64rem (below that it stacks under the canvas), and the transport fixed along the bottom.
- The header holds the piece and what changes the whole view: Notes (the filter), Hear (Original, Variation, Both, and with a variation The piece with the variation, which plays the whole piece with the variation in place, and The original, then the variation; see Compare) and Score (Hidden, As written, Generated from the notes, With the variation), each a labelled select.
- The canvas is the piano roll with its keyboard, the score below it (ink on paper, scrolling within its panel), and a closed "Compare with a second file, mute and solo tracks" section.
- The inspector stays in view as the canvas scrolls. On top it says what is selected, with Loop and Clear, and while a variation exists the compare switch (see Compare); under that five tabs: **Practise** (play, loop and slow down the phrase, open first), **Facts** (the facts panel), **Explore** (the Explore card), **Selection** (Keep preset and chips, which notes a step changes, the operations, the steps, What changed, hear and export) and **Edit** (editing variation notes and correcting imported notes). Put new selection tools in the tab they belong to, not under the canvas.
- The transport: a round lime play button, Stop, the position slider and text, the state, tempo, metronome and count-in.
- Tokens are on `:root` in `wwwroot/css/app.css`: charcoal ground, `--ml-panel` and `--ml-surface` for raised panels, lime for what is on, `--ml-inferred` (blue) and `--ml-uncertain` (amber) for certainty, and a monospace stack for positions and counts. Buttons are rounded (0.6rem) with a `--ml-border` outline; on or primary ones are lime.
- There are no bar number boxes: selection is made on the roll, with the mouse or, with the roll focused, the keyboard.

## Phrases

- The piece is shown as phrases (#18, `Phrases/PhraseStrip.razor`): a strip above the roll with one chip per phrase from `PhraseMap`, its number, its bars and how its start was found (slur, rest, repeat sign, comes back, yours, or start). Certainty shows by line as well as colour: solid for marks in the file and the player's own boundaries, dashed blue for inferred ones. The chip's accessible name and hover text say the whole reason and the confidence.
- Picking a chip selects the phrase as the working context; Practise, Explore and the facts read it. A phrase longer than a selection holds (8 bars) is selected up to 8 bars, and the selection text says so. The selection text names the phrase ("Selected phrase 2, bars 4–8").
- **Make the selection a phrase** turns any other selection into a phrase (moving or splitting boundaries; an edge less than a beat from a boundary keeps that boundary), **Join with the next phrase** takes away the boundary after the selected phrase, and **Back to the suggested phrases** forgets the player's boundaries. Edits are saved with the project; the UI never computes a boundary itself.
- Where the file gives little evidence, the strip says so and points to selecting bars; no boundary is invented.
- The roll marks each phrase start with a flag on the bar numbers and a faint line, and the score with a labelled line through the system (`setPhrases` in the roll and score scripts); both dashed and blue where inferred.

## Selection on the piano roll

- **Drag anywhere** on the roll (the bar numbers or the notes area) to select a range. music-core snaps it to beats (`MotifSelector.Drag`); the roll only reports where the drag started and ended.
- **Drag an edge** of the selection (grips on the bar numbers, an ew-resize cursor anywhere on the edge line) to move just that edge; the other edge stays. A reshape in place is made again on the new range.
- **Click a bar number** to select that bar. **Shift-click a bar number** to extend the selection to that bar, in whole bars.
- **By keyboard**, with the roll focused: **]** and **[** select the bar after or before the selection (the first bar when nothing is selected), and with **Shift** they extend the selection by that bar, as a click or Shift-click on its number does (WCAG 2.1.1). Space loops the selection.
- **Click a note** to hear it and select it; Shift, Ctrl or Cmd adds to the note selection. A press on a note that moves becomes a range drag.
- **Clear** with Escape, a click on an empty place in the roll, or a click on the page outside the roll. Clicks on controls (buttons, inputs, links, labels) never clear. Panels that show or act on the selection carry `data-keeps-selection`, so reading or using them does not clear it: today the inspector and the score. Mark any new panel of that kind the same way.
- **Space** plays and pauses from anywhere on the page: with notes selected it loops them and plays (later presses pause and resume the loop), with nothing selected it plays the piece. It acts on buttons and sliders too (Enter still presses a button), but not in text fields, lists, checkboxes or radios, where Space types or ticks.
- The 8-bar limit and its message come from music-core (`MotifSelection.MaxBars`).

## The selection in the score, and the range that plays

- The score shows the same selection as the roll: notes that start outside the range that plays are drawn faint, as on the roll, and the first note in the range is scrolled into view. Both views take the range from the page (`ShownRange`), so they never disagree with each other or with what loops.
- The range that plays is the selection's range while the original is heard. While the variation is heard it runs from the selection's start to the end of the variation's last note, rounded up to the beat: a variation that runs longer (half-time) extends it, one that runs shorter (double-time) shortens it. Heard together with the original (Both), it never ends before the selection. It never runs past the end of the piece.
- The roll marks the range that plays with a white line over the bar numbers; where it differs from the selection it adds a dashed end line, and a light band where it runs past the selection. The selection itself (lime band and edges) stays as the player made it.
- Applying, changing or removing a variation never changes the selection. While the selection loops, the loop follows what is heard and the variation's length; changing Hear while it loops keeps looping.

## Practise

- The Practise tab (`Practise/PractisePanel.razor`, #50) is the "I'm just practising" way of working: **Play from the start** (of the phrase; Pause while playing), **Loop the phrase**, **Previous phrase** and **Next phrase**, the tempo with − and + in steps of 5% and presets 50%, 75% and 100%, and what to hear while the phrase loops. Controls are large enough to reach with the iPad on the music stand. Nothing asks the player to do a task and nothing scores them.
- Hear offers **Right hand** and **Left hand** when the phrase has notes in both (as the file marks them or as inferred, and the panel says when some are inferred), and **Melody alone** and **Accompaniment alone** when it has a melody over an accompaniment. Choosing one loops the phrase; the selection text says what loops.
- Practise only drives the page's playback state, which every tab shares: switching to Facts or Explore and back keeps the phrase, the tempo, the loop and the hand. Moving to another phrase keeps them too, unless the new phrase has no notes for that hand or layer.

## Hearing notes by hand

- Clicking a note, a key on the keyboard under the roll, or a pitch name in the roll's left gutter plays that key at once, also while the piece plays; dragging across keys plays each in turn. These notes go to the engine's `live` source: transport, source and track changes never cut them, and they never touch the piece or its playback.

## Colour and the Notes filter

- Colour by Track, Voice or Hand. A grouping with only one group in the piece (one track; no notated voices; no hands) is disabled with its reason next to the control, never left as a control that does nothing. The roll starts with the first grouping that tells notes apart: tracks, else hands, else voices.
- The Notes filter offers what music-core can pick out: hands, tracks, and notated voices of a track (`SelectionFilter`). It limits what plays (the original source), what is drawn at full strength and lights up on the roll, and what a selection keeps. Changing it keeps the selection's range and makes a reshape in place again.
- Melody, bass and harmony are listed but disabled until music-core can find them (voice and chord analysis, S1-A3). Add them as filters only when music-core provides them.

## Explore

- The Explore card (`Explore/ExploreCard.razor`, decision record 0010) shows only what `ExperimentCatalogue` offers for the selection: with nothing tried, each experiment's title and question; picking one tries its first alternative and plays it at once. Never build an experiment in the UI.
- An experiment on the card, as in the mockup: the title, what changes, the question, a chip with the passage ("Selected phrase · bar 1", or "The motif's return · bar 5"), its alternatives as one-click buttons, what it keeps, What changed, **Original** and **Variation** (the compare switch's, see Compare), the listening focus, then **Keep this variation**, **Try another** (the next alternative, then the next experiment) and **Leave it**. Other ideas for the phrase are listed under the card.
- Trying an experiment sets the Keep chips to its preset and replaces the variation. It is saved with the project only once kept. **Leave it** puts back the selection, Keep choices and steps the player had; selecting other bars before keeping drops the experiment and puts back the Keep choices and steps. Changing Keep or adding a step builds on the experiment instead.
- An experiment on the motif's return moves the selection there and says so; **Hear the motif, then the answer** loops from the motif to the end of the return with the piece around them (the Hear choice The piece with the variation). The roll marks that range as the range that plays.
- With a melody over an accompaniment, **Everything**, **Melody alone** and **Accompaniment alone** choose what plays while the passage loops. The layers come from music-core's texture reading (`OperationScopes`), not from the hands, and the selection text says which one loops.
- **Surprise me** asks music-core for an experiment not tried yet since the piece was opened; **I have a question** lists the questions, each disabled with its reason when nothing in the selection answers it.

## Compare

Return to the original and compare (#43, step 6 of `docs/08-central-product-model.md`), so the player notices whether their understanding changed.

- **The compare switch** (`Compare/CompareSwitch.razor`) sits in the inspector head on every tab while a variation exists: **Original | Variation** (aria-pressed) and **One after the other**. Original hears the original phrase and shows the original score (as written when the file has one and no note was corrected, else generated); the variation and its steps stay. Variation hears it and shows With the variation. A running loop keeps looping, at the same place, with the new choice; otherwise the passage loops from its start. The Explore card's Original and Variation do the same.
- **One after the other** loops the original phrase, then the variation (over the range it takes), then again; the Hear select offers it too as The original, then the variation, and any other Hear choice, Original, Variation or the Looping button ends it. The engine switches on the exact frame (an alternating loop, decision record 0007) and reports the pass; the selection text says "Now the original" or "Now the variation" (shown, not announced at every pass), the sounding button is outlined (aria-current), the roll draws the one sounding at full strength with its range, and the score shows its version. The score is engraved again at each pass, so on a long piece it can follow the sound a moment late; the sound never waits for it.
- **Compare** (`Compare/ComparePanel.razor`) is under the Explore card and next to What changed in the Selection tab: What changed counts the diff (`Variation.Changes`, `Added`, `Removed`: pitch, start and length of how many of the original's notes) and names the properties the checks found changed; What was kept lists the Keep checks (`Variation.Checks`) as kept, no longer kept and not checked, with the property names, then the properties not asked for that stayed the same (`Variation.Properties`); then the rule flags (`Variation.Flags`). Only what the diff and the checks say; nothing is computed again in the UI.

## Variations

- Each operation's parameters are one-click buttons that apply and play at once (S1-E3); Keep properties are toggle chips that turn lime when kept and dashed when the step broke them.
- The Selection tab lists the operations of the catalogue this version has (V1, V13, V2, V4, V9, V10), each a folding group named by its id; V1 is open at first. An open group says why the Keep choices rule it out, and each button says why it cannot run, from music-core. **Change** limits the next step to the melody, the accompaniment or the bass for the operations that can be limited (V1, V13); the others change every selected note and say so.
- The roll draws the variation as white outlines where its notes land, with a line from each note it moved; the motif's own notes go faint behind it. The score can show the piece with the variation in place ("With the variation"), generated from the notes and so marked inferred; making the first variation switches the score to it.
- A variation is a chain of steps (`VariationChain`): a parameter button proposes a step on top of the accepted ones; **Accept step**, **Reject step**, **Undo** and **Redo** sit in one row with the breadcrumbs ("Original › Half-time › trying Later by 1 eighth"; a limited step reads "Detached (the melody)"). Selection and Keep changes replay the chain; if a step no longer runs, the variation is cleared. Rule flags (`Variation.Flags`) are listed under the explanation, never hidden.
- Draw variations from `Variation.Changes` (before and after per note), never by recomputing the operation in the UI.
- **Export motif MIDI** and **Export variation MIDI** hand over the file `MidiExporter` writes (S1-X1), named by `MidiExporter.FileName` after the project, the bars and the steps. The download goes through the storage script; nothing leaves the device.

## Correcting imported notes

- **Correct notes** (S1-I7) is a collapsible table of the selected notes under the variation controls: each row nudges pitch by a semitone, onset and length by a 16th, and velocity by 10, or removes the note. Edited rows are marked "Edited by you" as well as by the lime edge.
- Every correction goes through `NoteEditLog`; the UI never changes a note itself. **Undo correction** drops the last edit and **Revert** drops every edit of one note, so the imported value comes back exactly. Removed notes are listed with a Revert button each.
- The project saves the log, and opening a project applies it. After the first correction the score switches from the file as written to the score generated from the notes, since the file does not know the corrections.

## Editing variation notes

- **Edit notes by hand** (S1-E7) is a collapsible table inside the variation controls, with the same nudges as Correct notes plus **Add after** (a note of the same pitch and length straight after) and **Remove**. It lists the notes as heard; with no steps yet it lists the selected notes, and the first edit starts a hand-made variation.
- Every edit goes through `VariationChain.Edit` as one step: a reshape being tried is accepted first, Undo and Redo take edits back like any other step, and the explanation shows "This edit" before "From the original". Keep chips are checked again after each edit and are never a reason to block it (S1-E9). A reason why an edit cannot apply is shown under the table.

## Facts

- Facts are shown by `Facts/FactsPanel.razor`, one collapsible section per `FactLens`, in the enum's order. A new fact, or a new lens added to `FactLens`, appears without UI changes. Pass every analysis's facts to the panel in one list.
- Each section header gives the lens, the number of facts and how many are detected, inferred or uncertain, so a closed section still shows how sure it is. Closed sections stay closed as the selection changes.
- Every fact keeps its certainty badge and its basis. The badge differs by line as well as colour: solid grey for detected, dashed blue for inferred, dotted amber for uncertain. The basis is the hover text and also opens under the fact with its Basis button, so it can be read on touch. Never drop either.

## General

- Group related controls into one row (`.row`) or panel; prefer a collapsible section with a summary over more lines of text.
- Collapsible sections use a `<button aria-expanded>` header with the state held in the component, not `<details>`, so Blazor re-renders keep the state.
- Colours come from the brand tokens in `wwwroot/css/app.css` (`docs/02-brand-identity.md`); anything inferred is marked as such, by more than colour.
