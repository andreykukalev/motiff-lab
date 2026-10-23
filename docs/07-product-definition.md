# Motiff Lab: Product definition

Status: draft for the pilot, 2026-10-23. Owner: Andrei.

Motiff Lab helps musicians find a musical possibility they would not have found as easily on their own, make it their own, and learn to find such possibilities again. This doc records that direction and the first pilot that tests whether it works.

Everything below is in one of three states:

| State | Meaning | Changes when |
| --- | --- | --- |
| **Decided** | Established product direction; designs build on it | Only by an explicit product decision |
| **Constrained** | Fixed by what Stage 1 can do today or by its scope | The engine or the scope changes |
| **Hypothesis** | A design choice we expect to revise after watching people use it | The pilot or a later study says so |

It sits beside the Stage 1 specification ([01-mvp-stage-1.md](01-mvp-stage-1.md)) and does not replace it. The spec changes only after the pilot, so it records what we learned rather than what we guessed.

## Decided: what Motiff Lab is for

**Core use case.** Turn musical curiosity into personal creation. A musician starts with an idea they find interesting, experiments with how it can change, discovers a relationship they had not noticed, and uses it to make a decision of their own.

**Value proposition.** A musical possibility you found yourself, and could find again.

**Product principle.** Don't just tell musicians what is interesting about a piece. Help them notice it, investigate it, and discover what they can do with it. Motiff Lab expands the musician's creative agency; it does not replace it.

**Guiding principle for the experience.** Connect what musicians can notice to something meaningful they can investigate and do. The product neither just describes the music nor just exposes tools; every screen makes the path from curiosity to action visible.

**Success.** The musician moves from "I wonder what would happen if..." to "I discovered something, and now I know what I want to do with it." Over time they need the app less to get there. This keeps the creative framing tied to the existing principle that success is measured by what people can do without the app.

**One core job, three supporting jobs.** Supporting jobs serve the core one; they are not separate products.

| Job | The musician wants to | Its role |
| --- | --- | --- |
| **Explore and make it mine** (core) | Experiment with an idea and develop something of their own | The outcome: a creative decision they own |
| Understand why it works | See how rhythm, contour, intervals or repetition shape what they hear | Insight that leads to better decisions, not analysis for its own sake |
| Find a direction when stuck | Get moving again with a motif they cannot develop | Removes creative friction |
| Learn to explore alone | Recognise relationships and predict effects on new material | Makes the value compound outside the app |

**What we will not do.** Make generated alternatives the centre of the experience, force a fixed learning sequence before free experiment, show every analysis at once, or present an interpretation of musical meaning as a fact.

## Decided: facts, interpretations, actions

Everything the product offers sits in one of three layers. The interface makes them feel like one experience and always shows which layer a line belongs to.

| Layer | Example | Who produces it | Traceable to |
| --- | --- | --- | --- |
| **1. Musical fact** | F1: "Bars 1–2 and 5–6 are note for note the same." | The engine (music-core), deterministic | Bars, notes or a measured figure; a fact ID in the digest (#24) |
| **2. Interpretation or question** | "The second half answers the first." / "Where do the halves stop being the same?" | AI, a hand-written page, or the player | The fact IDs it rests on; never shown as the meaning of the music |
| **3. Product action** | "Keep the pitch order, change the rhythm." | A capability: playback and loop, a catalogue operation with a Keep preset, compare, hand editing | The operation ID, its parameters and the Keep preset; its result is checked against the Keep table |

A fact that cannot point to bars, notes or a measured figure is not shown. If the engine cannot find something, for example a repeated motif, the product says so and invites the player to select it by hand.

**The Stage 1 creative loop.** One experiment runs through these steps; people can enter at any step and leave at any time.

1. **Choose** a short motif.
2. **Pick an experiment**: one change, with what stays the same named first.
3. **Predict** what will happen (default in guided experiences, optional elsewhere).
4. **Listen** to the original and the variation, A/B and layered.
5. **Notice**: write what changed and what you heard, next to the engine's list of what actually changed.
6. **Decide** what to try or make next. This is the step that matters most.

The notebook keeps steps 3, 5 and 6 for every experiment. It is a record of what the player found and decided, not a history of edits.

Guided and Discovery use the same experiment record (question, keep, change, listening focus, prediction, observation, next move). Guided fills in more of it; Discovery leaves more to the player. Re-applying an earlier experiment to new material stays manual on purpose: the product shows the earlier notebook entry, not a replay button.

## Decided: notes alongside experiments

The notebook records what the player thought and where they wanted to go, not only what they did. It holds two kinds of entry, side by side in one timeline:

| Entry | Captures | Created | Required? |
| --- | --- | --- | --- |
| **Experiment record** | Prediction, the change (operation, Keep preset, diff), what they noticed, next move | By running an experiment | No field is required |
| **Process note** | Anything else: a question for later, an idea, a decision, why something worked, a link to other music | Any time, in one step | Never prompted |

Notes stay on the device in Stage 1, like the rest of the project. No sync, sharing or cross-device copies are assumed.

**Proposed design, to test in the pilot**

- **One notebook, not two.** Both kinds of entry live in one list, newest first, so the player never wonders which notebook something went into.
- **A note always belongs to the piece**, and it picks up its context automatically: the experiment that is open, and the bars being looped or selected. The player can remove the link; they never have to choose one. Linking to a single note event can wait until someone asks for it.
- **No session object.** Stage 1's data model has none; entries carry a time, so a session can be shown as a run of entries close together.
- **The lightest capture:** one *Note* control that is always visible, plus a keyboard shortcut. It opens a single text field, keeps playback running, and saves as you type.
- **No clutter:** the page shows a small count and a marker on the bars a note is linked to; the text lives in the notebook.

**Where it fits the existing model.** Stage 1's data model (spec, *Architecture and data model*) has no note object. The nearest is the LearningRecord, which holds prompts answered and choices made, not free text. A small new object would do it: id, project, optional motif, variation or experiment, optional bar range, text, created and edited times. It persists through IndexedDB with the project (S1-P1, #16) and goes into the project export file. In the study export (#52) note text is included only if the participant chooses, and analytics never carry it. The Stage 2–3 PRD already has a precedent: a passage keeps its "annotations" (FR-B2).

## Decided: the track page connects noticing to doing

The track page is the contextual way into the capabilities the product already has. It is not an analysis report and not a feature catalogue. It answers three questions for the piece in front of the player: what is interesting here, what could I investigate, and what can I do in Motiff Lab to investigate it.

Fact (engine) → question → invitation → action → notebook → next move.

The chain runs from a fact the engine can show, through a question and an invitation, to an action the player takes; the notebook keeps what they found, and their next move opens the next question.

**The four parts of the page and what each leads to**

| Part | Answers | Draws on | Leads to |
| --- | --- | --- | --- |
| Things worth noticing | What should I listen for? | Facts, phrased as open questions with bars | A listening action: loop, slow down, compare two passages |
| Experiments | What happens if I change this? | Facts plus a question | A catalogue operation opened with its Keep preset and parameters |
| Musical DNA | What makes this piece distinctive? | Facts and labelled interpretations, each citing fact IDs | Confirms or contradicts what the player wrote; opened after listening |
| Make it your own | What could I make from this? | The player's notebook | A hand-made variation (S1-E7) or the piano itself |

**Rules for every invitation**

- It leads to an action, and the action is labelled honestly: *available now*, *planned for Stage 1* (a Must, not built yet), *needs a Should item*, or *later stage*. An invitation with no action behind it is cut, or offered only as a listening question, since listening and looping always work.
- An interpretation cites the fact IDs it rests on. An experiment names its operation, parameters and Keep preset. The notebook stores those IDs beside the player's own words.
- It never says what the player should feel. Words such as "answers" or "flowing" are interpretations, labelled as such, and shown after the player has had a chance to hear it for themselves (hypothesis H2).

**Worked example: every invitation on the pilot page, traced.** Fact IDs refer to the Musical DNA on the mock page.

| Invitation | Rests on | Product action | Operation, parameters, Keep preset | Stage 1 status | In the pilot |
| --- | --- | --- | --- | --- | --- |
| Where do the two halves stop being the same? | F1, F5 | Loop bars 1–4, then 5–8 | Bar loop (S1-W4) | Available now | Playback lab |
| Does the movement ever stop before the end? | F4 | Listen slower, following the 16ths | Tempo 25–150% (S1-W4) | Available now | Playback lab |
| How many notes do you need to recognise it? | F2, F3 | Loop the pickup and bar 1 | Bar loop (S1-W4) | Available now | Playback lab |
| E1 · Is the tune in its pitches or its even flow? | F1, F3 | Apply a rhythm change, compare A/B | V1 Rhythm reshape, *dotted*; preset "Keep the pitch order, change the rhythm" | Planned, Must (#35), not built | Hand-made file, tracks 1 and 2 |
| E2 · Does the motif survive moving the other way? | F1, F2 | Mirror the motif, compare A/B | V10 Change direction, *mirror the whole motif*; preset "Keep rhythm, change intervals" | Planned, Must (#39), not built | Hand-made file |
| E3 · How much of the character lives in the left hand? | F4, F6 | Change the accompaniment, compare A/B | V2 Accompaniment pattern, *repeated chord tones, 8ths*; preset "Keep the melody, change the accompaniment" | Needs a Should: chord detection (S1-A3, #46, #47) | Hand-made file |
| Make it your own | The notebook | Edit notes by hand, or play at the piano | Hand-made variation (S1-E7) | Planned, Must (#42); playing is off-app | Paper or piano |

**Capability gaps the page exposes, and whether the pilot can test around them**

- **E3 depends on a Should.** V2 needs chord detection, which is a Should. The pilot can test whether the left-hand question is valuable without it. If E3 turns out to be the strongest experiment, that is evidence for raising S1-A3, decided after the pilot, not before.
- **V10 has no mirror axis in the spec.** The pilot mirrors around the motif's first note. Issue #39 should name the axis as a parameter or a rule.
- **No catalogue option for held block chords.** The first draft of E3 used them; the pilot now uses V2's *repeated chord tones* so that it stays inside the catalogue.
- **Comparing two appearances of a motif.** This needs repeats (S1-A4, Must, #22) and the compare screen (N6, Must, #43), both planned and not built. The pilot piece's repeat is exact, so the pilot asks about where the halves differ instead, which the bar loop supports today.
- **No screen is a track page yet.** The nearest is the piece overview and motif picker (N3, #18). Whether the page becomes a new screen or a reworked N3 is a decision for after the pilot.
- **The creative step leaves the app.** Without MIDI input, the player's own version is played at the piano or edited by hand on the piano roll. This stays an open question below.

## Constrained: what Stage 1 can show today

Today the product can import a MIDI or MusicXML file and play it well; it cannot yet find, change or explain anything. The pilot therefore uses hand-made material wherever the engine is not built.

| Capability | Status on 2026-10-23 | Pilot uses |
| --- | --- | --- |
| Exact musical time, notes, bars and beats | Built (decisions 0002, 0004) | As is |
| MIDI and MusicXML import | Built (0005, 0006) | As is |
| Playback: sampled piano, transport, loop, tempo, solo and mute per track | Built, live in the [playback lab](https://nice-smoke-0169fb403.6.azurestaticapps.net/playback) (0007) | As is |
| A/B against a variation at the same position | Built, but the variation is a fixed test (the same notes an octave higher) | Original and variation as separate tracks in one file, switched with solo and mute |
| Score view | Built (0008, #63) | Not needed |
| Motifs, repeats, rhythm and melody facts (S1-A1, A2, A4, A5) | Not built (#17, #20–#23) | Facts worked out by hand from the pilot file and checked against its notes |
| Variation operations V1, V10, V2 | Not built (#32–#47) | Variations written by hand to behave exactly as those operations will |
| Interpretations from AI (S1-AI3) | Not built (#30, #58) | Hand-written, labelled as interpretation |
| Notebook | Not specified yet; proposed in 0009 | A page in the mock track page, or paper |

**Scope limits that stay fixed for Stage 1.** Input is notation only: no audio, so a page cannot start from a recording. No MIDI keyboard input: the player's own creative move is written down or played on a real piano, not captured. No hosting of copyrighted music: pages are built on the device for the player's own files, plus public-domain starter pieces.

**What a Stage 1 page could honestly claim, once the engine exists.** Repeats, exact and transposed, with bar numbers. Rhythm patterns and their repeats. Intervals and contour. Changes in density, register and loudness over bars. Chords and accompaniment patterns only where chord detection (S1-A3, a Should) succeeds. Texture labels such as "arpeggiated" are not in the Stage 1 spec at all.

## Hypotheses: what the pilot should tell us

The central hypothesis: **observations and experiments grounded in a specific piece help a musician discover something meaningful and decide what to explore or create next**, faster and with more intent than a list of tools would. The design choices below serve that hypothesis and are all open to revision.

| # | Hypothesis | We'd see it holding if | We'd revise it if |
| --- | --- | --- | --- |
| H1 | A track page organised as *Things worth noticing → Experiments → Musical DNA → Make it your own* is a good way in | People start an experiment within a few minutes, without asking what to do | They skip the page, or ask for the tools directly |
| H2 | Questions first, facts after: the DNA opens only after the player has listened and noted something | People write observations the DNA then confirms or contradicts, and find that interesting | People feel tested, or want the facts first to know what to listen for |
| H3 | A fixed-choice prediction (same tune / related / a different tune) makes listening sharper | Observations after a prediction are more specific than without one | Predictions feel like a quiz, or people answer at random |
| H4 | A mismatch between prediction and what was heard is where discoveries happen | The specific observations cluster after wrong predictions | Discoveries are no more likely after a mismatch |
| H5 | Three experiments on one piece are enough to reach a creative decision | Most people name a next move they want to try | People want many more options before deciding anything |
| H6 | Stage 1 can work without automatic page generation, AI, or extra Must capabilities | A hand-written page works as well as we hope a generated one would | People need a capability we have not planned, or the page only works because a human wrote it |
| H7 | Writing in the notebook is a help, not friction | People write a sentence without being pushed | People skip it or write "sounds similar" |
| H8 | Tying each question to a ready action gets people exploring sooner than a list of tools | People move from a question to the action it names without help | People read the questions but don't act, or reach for tools the page didn't link |
| H9 | Free process notes catch thinking the structured record misses | People write notes the prompts didn't ask for, and look back at them before the transfer step | Nobody writes one, or notes just repeat the experiment fields |

H6 is the one that changes the roadmap. If the pilot shows that the page only works with richer facts than Stage 1 plans, that is a finding about scope, not a design failure.

## The pilot: one piece, one page, three experiments

Three pianists, one moderated session each, about 45 minutes, using the existing playback lab, a hand-written track page and a notebook. No engine work is needed to run it.

**Materials.** A hand-written mock track page with its built-in notebook and a Note button for process notes, and five MusicXML files, kept with the project's shared files outside this repository: the original, one file per experiment (track 1 original, track 2 variation), and Ode to Joy. All five import with no warnings through the real Motiff Lab MusicXML importer.

**The piece: the opening of Beethoven's Für Elise (WoO 59), pickup and bars 1–8.** The composition is in the public domain and the MIDI is written by us, so there are no rights questions. Almost every pianist knows it, which makes "is it still recognisable?" a real question rather than a guess. The excerpt has an exact repeat (bars 1–2 and 5–6), two halves that open alike and end differently, and one accompaniment figure that passes from hand to hand. The risk is that it feels like a beginner's piece; the alternative is a Satie Gymnopédie, once its notes are transcribed and checked.

**The three experiments**, each a hand-made variation built to behave as its catalogue operation will:

| # | Question | Keep | Change | Operation it stands for |
| --- | --- | --- | --- | --- |
| E1 | Is the tune in its pitches or in its even flow? | Pitches and their order | Rhythm: each pair of 16ths becomes long-short | V1 Rhythm reshape, dotted |
| E2 | Does the motif survive if it moves the other way? | Rhythm and step sizes | Direction: the motif is mirrored around its first note, E | V10 Change direction, mirror |
| E3 | How much of the character lives in the left hand? | The whole right hand | Left hand: each rising arpeggio becomes its chord, struck on every 8th | V2 Accompaniment pattern, repeated chord tones in 8ths |

**Session outline**

1. Before: one question, "When did you last want to change a piece rather than just play it?" (5 min)
2. Open the track page. Listen to the original; answer *Things worth noticing* in the notebook. (8 min)
3. Two experiments, each predict → listen in the playback lab → notice → next move. Half start with E1, half with E2. (15 min)
4. Open the Musical DNA; react to what it confirms or contradicts. (5 min)
5. Choose E3 or their own next move. Try it at the piano. (7 min)
6. Transfer: a new, unrelated tune (the opening of Ode to Joy). "Make it yours using anything you found today." No instructions, no page. (5 min)

**What we observe**, without scoring the musician:

- **The bridge**: did a question on the page lead them into an action without asking how? Did any invitation dead-end, or leave them wanting a tool the page did not offer?
- **Prediction**: did they form an expectation before listening, when asked and when not?
- **Creative activation**: did they name a direction they wanted to pursue?
- **Discovery**: did they put into words a specific relationship they had not noticed before?
- **Ownership**: did they make a decision of their own, rather than accept the page's suggestion?
- **Notes**: did they write a process note nobody asked for, and did they look back at earlier notes or observations before the transfer step?
- **Transfer** (secondary): on Ode to Joy, did they use an idea from today without being told to?
- **Who it suits**: how answers to the opening question line up with what each person did. Learning-focused pianists, creatively curious pianists, songwriters and improvisers may each use a different part of the page, and that is a finding, not a failure.

Returning to notes over days cannot be seen in a 45-minute session. The week of home use in the Stage 1 study is where that shows.

Three people give qualitative evidence, not proof. The notebook stays with the participant unless they choose to hand it over.

## Open questions and the spec after the pilot

**Open questions**

- [ ] **Audience.** Is the late-beginner to intermediate pianist the right primary user for a creation-first product, or do songwriters and improvisers respond more strongly? The opening question in the session starts to answer this.
- [ ] **In-app creation.** Should Stage 1 record a free take from a MIDI keyboard, with no scoring or hints, so the player's own move becomes a hand-made variation? The pilot shows whether doing it at the piano, off-app, breaks the loop.
- [ ] **The pilot piece.** Is Für Elise familiar in a useful way, or do prior expectations steer predictions? Watch whether people predict from memory ("I know how it goes") rather than from listening.
- [ ] **Nothing to suggest.** What does the page show when the engine finds nothing confident? Proposal: never fill the page with general claims. Say plainly that nothing stood out strongly enough to suggest, and offer the actions that always work: loop and select bars, compare two selections, apply any catalogue operation. The hand-written pilot page cannot test this; it needs a second, thin page or a later study.
- [ ] **Notes.** Should a note also attach to a single note event? Do notes need search or tags once there are many? Both wait until people ask.

**Spec changes we expect to propose after the pilot**, each depending on what the pilot shows:

| Area | Likely change | Spec section or issue |
| --- | --- | --- |
| Learning modes | Guided and Discovery become one experiment record, filled to different degrees | S1-L1, S1-L2, #50, #51 |
| Prediction | A new requirement: fixed-choice prediction before listening, default in Guided | Learning modes, S1-L4, #53 |
| Notebook | Prediction, observation and next move stored beside the diff, on the device | *What Stage 1 records for learning*; LearningRecord in *Architecture and data model*; #52 |
| Process notes | A small note object linked to the project and, optionally, a motif, variation, experiment or bar range | *Architecture and data model* (new object); persistence S1-P1, #16; study export #52 |
| Track page | A new screen, or a reworked piece overview (N3), built from the fact digest | #18, #24 |
| Capability priority | Only if H6 fails: motif suggestions (S1-M6) and creative opportunities (S1-AI3) move from Should to Must | #57, #58 |
| Comparison | Load a variation from a second file, or build it in the app, for A/B at the same position; the playback lab today only offers a fixed test variation | S1-W5; playback engine #14 (decision 0007) |

After the pilot, this doc gains a fourth state beside decided, constrained and hypothesis: **supported by the pilot**. The spec update then separates decisions the pilot supports, principles already established, Stage 1 constraints, and questions still open.
