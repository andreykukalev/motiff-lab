<!-- Exported from the Motiff Lab specification doc (https://claude.ai/artifact/8uR7nDk6Z7ar8vLjbsCMz3) on 2026-10-22. Edit here or in the doc; keep them in sync. -->

# Product Requirements (PRD)

*Stage note: this PRD specifies Stages 2–3 (guided reconstruction and audio). Its "phase 1–4" labels are that release plan's own phases. The first release is the notation-based Stage 1, specified in MVP · Stage 1, which reuses the variation operations and event model defined here.*

## Scope and personas

This PRD covers the MVP: the browser prototype (phase 1) and the mobile app (phase 2) for solo piano. Phase 3–4 items appear only where the MVP must leave room for them. Each requirement has an ID (FR-A1, NFR-3) so the UX spec, domain model and validation plan can refer to it.

| Persona | Profile | What success looks like |
| --- | --- | --- |
| Ana, self-taught pianist (primary) | Plays pop and film pieces from tutorials; knows triads and major scales; has a MIDI keyboard | Works out an 8-bar theme from a film score with only tier-2 hints, then plays it without the screen |
| Marius, returning classical player | Had lessons as a child; reads music slowly; wants to improvise | Understands why a passage works and makes two variations he likes |
| Ieva, piano teacher (phase 3 context only) | Teaches teenagers; wants ear training with music they like | Not in MVP scope; the data model keeps hint history per attempt so teacher views can come later |

**Terms used in this PRD**

- **Piece:** an imported recording and its analysis.
- **Passage:** a selected time range of a piece, usually 2–8 bars; the unit of work.
- **Reference:** the app's estimated notes for a passage, including user corrections.
- **Attempt:** one try by the player to reproduce the passage.
- **Hint:** a piece of the reference revealed on request.
- **Variation:** a new version of the passage made by one or more transformation operations.

## User stories

Twenty-two stories cover the MVP journey; all are written for the primary pianist persona. "Must" stories define the phase 1 prototype unless marked otherwise.

| ID | As a pianist, I want to… | So that… | Priority | Module |
| --- | --- | --- | --- | --- |
| US-01 | import a piano recording from my device | I can learn a piece I love | Must | A |
| US-02 | pick a starter piece when I have no recording | I can try the app straight away | Should (phase 2) | A |
| US-03 | see how sure the app is about each note | I know what to trust | Must | A |
| US-04 | fix a note the app got wrong | hints and feedback become right | Should (phase 2) | A |
| US-05 | see suggested phrases and repeated motifs | I can choose a passage quickly | Must | B |
| US-06 | select a passage and have it snap to bars | my loop starts and ends cleanly | Must | B |
| US-07 | loop and slow down without changing pitch | I can hear fast passages clearly | Must | B |
| US-08 | hear the left or right hand alone | I can focus on one line | Must | B |
| US-09 | come back to a passage where I left it | I don't lose progress | Must | B |
| US-10 | play my attempt on a MIDI keyboard without seeing notes | I train my ear, not my eyes | Must | C |
| US-11 | enter notes on an on-screen keyboard | I can use the app without a MIDI keyboard | Must | C |
| US-12 | ask for the smallest useful hint | I keep doing most of the work | Must | C |
| US-13 | get the most important mistake first | I know what to fix next | Must | C |
| US-14 | reveal the full answer when I choose | I am never stuck | Must | C |
| US-15 | see chords, the bass line and intervals with short explanations | I understand why the passage works | Should (phase 2) | D |
| US-16 | change one property of the motif and hear the result | I learn what each change does | Must | E |
| US-17 | read one sentence on what a variation changed | I can connect the sound to the change | Must | E |
| US-18 | compare original, my attempt and my variation | I can hear the differences | Must | E |
| US-19 | undo, edit and name variations without touching the original | I can experiment freely | Must | E |
| US-20 | export my attempt or variation as MIDI | I can use it in other music software | Should (phase 2) | E |
| US-21 | play the passage or my variation without visual help and be scored | I know I really learned it | Must | C, F |
| US-22 | see which passages I solved and how much help I used | I can see my progress | Should (phase 2) | F |

## Functional requirements

Requirements are grouped by module, A to G. Priority follows the scope table in the Overview; "Must" means phase 1 unless marked.

### A. Audio import and analysis

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-A1 | Accept MP3, WAV, M4A and FLAC files of 5 seconds to 15 minutes and up to 100 MB | US-01 | Must |
| FR-A2 | Reject a file with no detectable piano content, with a plain reason and a tip | US-01 | Must |
| FR-A3 | Score recording quality (reverb, noise, clipping, note density) and warn below the threshold before the player starts | US-03 | Must |
| FR-A4 | Estimate notes: pitch (MIDI 21–108), onset, offset, velocity and a confidence of 0–1 per note | US-03 | Must |
| FR-A5 | Estimate sustain pedal on and off events | — | Must |
| FR-A6 | Estimate tempo curve, beats, downbeats and meter (2/4, 3/4, 4/4, 6/8) | US-06 | Must |
| FR-A7 | Estimate key and key changes, with confidence | — | Must |
| FR-A8 | Derive hand (left or right), voice (melody, inner, bass) and chord labels; keep up to 3 ranked alternatives for each chord | US-03 | Must |
| FR-A9 | Detect phrase boundaries and repeated motifs, exact or transposed | US-05 | Must |
| FR-A10 | Link every event to its time in the source audio (seconds) and its grid position (bar, beat, tick) | — | Must |
| FR-A11 | Mark every event's provenance: detected, inferred, user-confirmed or user-edited | US-03 | Must |
| FR-A12 | Let the player add, delete and edit notes (pitch, timing, duration, hand), move barlines and relabel chords; derived layers update within 200 ms | US-04 | Should (phase 2) |
| FR-A13 | Offer a starter library of at least 20 public-domain pieces with hand-checked analysis | US-02 | Should (phase 2) |

### B. Motif and phrase workspace

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-B1 | Create a passage by dragging on the waveform or picking a suggestion; snap to barlines by default; length 1–16 bars, suggestions 2–8 bars | US-05, US-06 | Must |
| FR-B2 | A passage keeps its source range, reference, attempts, hint log, annotations and variations together | US-09 | Must |
| FR-B3 | Playback: play, pause, loop, optional one-bar count-in, speed 25–100% in 5% steps with pitch preserved, transpose ±6 semitones, metronome | US-07 | Must |
| FR-B4 | Playback sources: original audio, reference (piano sound), left hand, right hand, attempt, variation, and original mixed with reference | US-08 | Must |
| FR-B5 | Play the passage in context, with two bars before and after | — | Should (phase 2) |
| FR-B6 | Views: piano roll, keyboard, chord timeline; each can be hidden, and all are hidden at the start of Discovery | — | Must |
| FR-B7 | Save state on every change and restore view, loop, speed and hint state on return | US-09 | Must |
| FR-B8 | Moving between Listen, Reconstruct, Understand and Transform keeps loop, playback position and selection | — | Must |

### C. Guided reconstruction

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-C1 | Input by MIDI keyboard (USB or Bluetooth; Web MIDI in the prototype), on-screen keyboard with octave shift, or step entry on the piano roll grid | US-10, US-11 | Must |
| FR-C2 | Two attempt modes: play-along (original plays while the player plays) and call-and-response (the loop plays, then the player answers in free time) | US-10 | Must |
| FR-C3 | The player picks an assistance preset per passage: Explorer (no hints shown), Guided (tier 2 open), Study (full reveal) | US-12, US-14 | Must |
| FR-C4 | Hints follow the Hint system section; every reveal logs type, tier, time and region | US-12 | Must |
| FR-C5 | Feedback after each attempt follows the Feedback rules section: one primary correction, the rest behind "More" | US-13 | Must |
| FR-C6 | Full reveal is always available after one confirmation tap and is logged as tier 4 | US-14 | Must |
| FR-C7 | Each attempt gets pitch, rhythm and completeness scores of 0–100 | US-13 | Must |
| FR-C8 | Hands-separate mode: the player picks a hand and the other hand's reference plays as accompaniment | US-08 | Must |
| FR-C9 | Performance check: all views hidden; the attempt passes when all three scores reach the pass threshold | US-21 | Must |

### D. Structure explorer

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-D1 | Melody: pitch sequence, intervals (semitones and names), contour line, repeated notes | US-15 | Should (phase 2) |
| FR-D2 | Harmony: bass line, chord labels with inversion, ranked alternatives where unsure, shared notes between neighbouring chords highlighted | US-15 | Should (phase 2) |
| FR-D3 | Rhythm: beat grid, subdivision, durations, syncopation and anticipations flagged | US-15 | Should (phase 2) |
| FR-D4 | Texture label per bar: block chords, broken chords, arpeggio, repeated notes, ostinato | US-15 | Should (phase 2) |
| FR-D5 | One-sentence explanations from templates filled with analysis facts; marked "interpretation" when chord confidence is below 0.7 or alternatives exist | US-15 | Should (phase 2) |
| FR-D6 | Tapping an explanation highlights the notes it refers to and plays them | US-15 | Should (phase 2) |
| FR-D7 | Voice-leading view, suspensions and pedal tones | — | Could (phase 3) |

### E. Variation engine

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-E1 | Offer the operations in the Variation operations section: 3 in phase 1, all 6 in phase 2 | US-16 | Must |
| FR-E2 | By default each operation changes one dimension; the unchanged dimensions are shown as locked | US-16 | Must |
| FR-E3 | Each operation has 1–3 parameters with sensible defaults; the result plays within 300 ms of a change | US-16 | Must |
| FR-E4 | Each variation gets a one-sentence description built from its actual difference to the source | US-17 | Must |
| FR-E5 | Variations are stored as a chain of operations over a source; the original is read-only; undo and redo up to 50 steps | US-19 | Must |
| FR-E6 | Compare view: A/B toggle, synced playback of original, attempt and variation, piano roll overlay of differences | US-18 | Must |
| FR-E7 | The player can edit single notes in a variation; manual edits become an "edit" step in the chain | US-19 | Must |
| FR-E8 | Save, rename, duplicate and delete variations; up to 20 per passage | US-19 | Must |
| FR-E9 | A variation can be practised and performance-checked like the original | US-21 | Must |
| FR-E10 | Export attempts and variations as MIDI (type 1, one track per hand, with tempo map) | US-20 | Should (phase 2) |

### F. Practice and progress

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-F1 | Log every attempt with mode, hand, hints used, scores and duration | US-22 | Must |
| FR-F2 | Passage state: new, in progress, solved with hints (highest tier used), solved without hints, performed from memory | US-22 | Must |
| FR-F3 | Progress screen: passages by state, practice time, hint use over time | US-22 | Should (phase 2) |
| FR-F4 | Record evidence for nine separate skills (pitch, interval, contour, rhythm, bass line, harmony, voice leading, motif development, variation making); show the profile in phase 3 | — | Should (phase 2 capture) |
| FR-F5 | Transfer tasks: apply a studied concept to an unfamiliar passage | — | Could (phase 3) |
| FR-F6 | Review prompts for passages solved at tier 3 or above, after 2, 7 and 21 days | — | Could (phase 3) |

### G. Accounts and plans

| ID | Requirement | Stories | Priority |
| --- | --- | --- | --- |
| FR-G1 | Prototype works without an account and stores data in the browser | — | Must (phase 1) |
| FR-G2 | Sign in with email, Apple or Google; data syncs between devices | — | Must (phase 2) |
| FR-G3 | Delete the account and all personal data from within the app | — | Must (phase 2) |
| FR-G4 | Enforce plan limits (3 imports a month on Free) and show remaining imports before upload | — | Must (phase 2) |

## Hint system

Hints come in four tiers, from no help to the full answer. Within tiers 2 and 3 the player picks individual hints, or taps "Next best hint" to get the one that targets their biggest current error.

| ID | Tier | Hint | What the player sees | Best for error type |
| --- | --- | --- | --- | --- |
| — | 1. Independent | No reference shown | Loop, speed, hands-separate playback and context only | — |
| H2.1 | 2. Structural | Tonal centre | Key and scale highlighted on the keyboard | Many wrong pitches |
| H2.2 | 2. Structural | Note count | Number of notes per hand in the loop | Missing or extra notes |
| H2.3 | 2. Structural | Contour | A line showing the melody going up or down, no pitches | Wrong direction |
| H2.4 | 2. Structural | Rhythm landmarks | Onset ticks on the beat grid, no pitches | Rhythm displacement |
| H2.5 | 2. Structural | Bass anchors | Lowest note at the start of each bar | Wrong bass movement |
| H3.1 | 3. Partial | Phrase starts | First note of each melody phrase | Lost at the start |
| H3.2 | 3. Partial | Chords | Chord name at each change | Harmonic mismatch |
| H3.3 | 3. Partial | Candidates | For one chosen note, three options including the right one | One stubborn note |
| H3.4 | 3. Partial | Next note | The next note the player got wrong or missed | Stuck at one point |
| H3.5 | 3. Partial | Simple rhythm | Durations rounded to the beat grid | Duration errors |
| H3.6 | 3. Partial | One hand | Every note of one hand on the piano roll | One hand solved, the other not |
| H4 | 4. Full | Full reveal | All notes of both hands, pedal and chords, with the latest attempt overlaid | Study, or giving up |

**Rules**

1. Hints apply to the current loop and hand. Revealed hints stay visible until the player taps "Reset hints".
2. The assistance preset (FR-C3) sets which tier is open at the start; any tier can be opened at any time.
3. "Next best hint" picks the lowest-tier hint that targets the most frequent error type in the last attempt.
4. Hints use only notes with confidence 0.6 or higher. A hint that depends on less certain notes is labelled "uncertain".
5. Help used is counted as weights (tier 2 = 1, tier 3 = 2, tier 4 = 5) for progress and research. The UI shows it as a neutral "help" meter, never as a penalty.
6. All hints are hidden during a performance check.

The tier order and the hint list are first proposals; Study 1 measures which hints players use and which ones lead to a solve.

## Feedback rules

Feedback judges the musical identity of the passage, not every performance detail, and shows one correction at a time. The matching algorithm that produces these errors is in Domain & Architecture (attempts and alignment).

**What counts and what is ignored**

- Essential: pitch, octave of melody and bass notes, order of notes, rhythm measured in beats, chord content.
- Ignored by default: velocity, small tempo changes (rubato), exact durations while the sustain pedal is down, grace notes shorter than 80 ms, rolled chords.
- Notes starting within 50 ms of each other count as one chord.

**Tolerances**

| Mode | Onset tolerance | Duration tolerance |
| --- | --- | --- |
| Play-along | ±60 ms at 100% speed, scaled by 1 ÷ speed (±120 ms at 50%) | ±50% of the reference, not checked under pedal |
| Call-and-response | After tempo alignment: ±1/16 note | ±50% of the reference, not checked under pedal |

**Error types, in the order they are shown**

| Order | Error type | Detected when | Example message |
| --- | --- | --- | --- |
| 1 | Missing notes | A reference note has no match | "You're missing 2 notes in bar 3; listen to the right hand there." |
| 2 | Wrong pitch | Matched in time, different pitch class | "Bar 2, beat 3: close, but that note is a step too low." |
| 3 | Wrong octave | Same pitch class, different octave | "Right note, one octave too high." |
| 4 | Wrong bass movement | Bass line direction or root differs | "The bass goes down here, not up." |
| 5 | Harmonic mismatch | Notes played over a chord do not belong to it or its alternatives | "These notes don't fit the chord in bar 4." |
| 6 | Rhythm displacement | Onset outside tolerance | "You come in early on beat 2." |
| 7 | Extra notes | A played note has no reference match | "There's one note too many at the end." |
| 8 | Wrong duration | Held too short or long, outside pedal | "Hold the last note for two beats." |

The primary correction is the earliest occurrence of the highest error type in this order. All other errors sit behind "More". Messages never mention scores; they point to a bar, a beat and an action.

**Scores and thresholds**

- Pitch = matched notes with the right pitch ÷ reference notes.
- Rhythm = matched notes within onset tolerance ÷ matched notes.
- Completeness = matched notes ÷ reference notes, minus 0.5 × extra notes ÷ reference notes, floored at 0.
- A passage is solved when pitch ≥ 90, rhythm ≥ 75 and completeness ≥ 90. A performance check passes on the same thresholds with all views hidden.

Thresholds are starting values to tune in Study 1.

## Variation operations

The MVP ships six rule-based operations, one per category from the vision, each changing one musical dimension and locking the rest. Rules, not a generative model, keep results predictable, playable and honestly explainable.

| ID | Operation | Changes | Locked | Parameters (default first) | Phase |
| --- | --- | --- | --- | --- | --- |
| V1 | Rhythm reshape | Note timing of the chosen line | Pitches, harmony | Style: syncopate, dotted, half-time, double-time; line: melody or accompaniment | 1 |
| V2 | Accompaniment pattern | How the left hand plays the chords | Melody, harmony | Pattern: broken chord, arpeggio up, arpeggio up-down, Alberti, repeated notes; subdivision: 8ths, triplets, 16ths | 1 |
| V3 | Voicing and register | Chord inversion, spacing, octave of a line | Melody pitch classes, rhythm | Inversion: root, 1st, 2nd; spacing: close, open; octave shift: −1, +1 | 1 |
| V4 | Melodic sequence | Repeats the motif from another scale step | Rhythm, interval shape | Steps: −4 to +4 scale degrees (default +1); repeats: 1–3; diatonic or exact | 2 |
| V5 | Harmonic colour | Adds tones to the chords | Melody, bass | Add: 6th, 7th, 9th, sus2, sus4; apply to: all chords or the selected one | 2 |
| V6 | Simplify | Fewer, easier notes | Melody contour, harmony | Level 1: drop inner voices; level 2: also bass roots only; level 3: also rhythm rounded to 8ths and leaps over an octave folded | 2 |

**Constraints every operation respects**

- Results stay on the passage's beat grid and within the piano range (A0–C8).
- Each hand's span stays within a 10th; larger spans are flagged, not silently changed.
- Voices do not cross unless the source already crosses.
- The same input and parameters always give the same output, so the description always matches.

**Description templates**

Each description is filled from the computed difference between source and result, never written in advance:

- V1: "The melody keeps its notes; {n} notes now start an 8th early, creating syncopation on beats {beats}."
- V2: "Melody and chords are unchanged; the left hand now plays a {pattern} in {subdivision}."
- V3: "The chord in bar {bar} is now in {inversion}; the top voice moves by {interval} instead of {old\_interval}."
- V4: "The motif repeats {repeats} times, each a {step} higher, keeping its rhythm."
- V5: "Chords now include a {extension}; melody and bass are unchanged."
- V6: "Inner voices are removed; {n} notes fewer, same melody and harmony."

Ornamentation, reharmonisation and counter-melodies wait for phase 3.

## Acceptance criteria

Each criterion is testable as written; QA and automated tests use these as the definition of done for phase 1 and 2.

| ID | Covers | Given | When | Then |
| --- | --- | --- | --- | --- |
| AC-01 | FR-A1, NFR-1 | A 4-minute solo piano MP3 of good quality | The player imports it | Analysis finishes within 60 s for 9 of 10 imports and the piece shows a "Good" quality badge |
| AC-02 | FR-A3 | A recording with a quality score below 0.5 | Analysis finishes | A warning names the reason (for example heavy reverb) before a passage opens; the player can continue |
| AC-03 | FR-A2 | A speech or guitar recording | The player imports it | It is rejected within 15 s with "No piano detected" and does not count against the plan limit |
| AC-04 | FR-A4, FR-A11 | A note with confidence below 0.6 | It appears on the piano roll | It is drawn faded with a dashed outline and labelled "uncertain" |
| AC-05 | FR-B1 | A piece with detected barlines | The player drags across bars 9–12 | The passage snaps to bar 9 beat 1 through bar 13 beat 1 and is 4 bars long |
| AC-06 | FR-B3 | A loop at 50% speed | It plays | Pitch stays within ±5 cents of the original and the loop restarts with no audible gap (under 20 ms) |
| AC-07 | FR-B7 | The player leaves a passage with two hints open at 70% speed | They reopen it, on any device in phase 2 | The same hints, speed and loop are restored |
| AC-08 | FR-C1, NFR-2 | A connected MIDI keyboard | The player presses a key | The on-screen key lights within 20 ms and the note is recorded with its timestamp |
| AC-09 | FR-C3, FR-C6 | The Explorer preset | The passage opens | No notes, chord labels or note counts are visible; full reveal needs a confirmation tap and is logged as tier 4 |
| AC-10 | FR-C5 | An attempt with 2 missing notes in bar 3 and an early note in bar 1 | Feedback appears | The primary message is about the missing notes in bar 3; the rhythm error is under "More" |
| AC-11 | FR-C9, FR-F2 | A performance check scoring pitch 92, rhythm 80, completeness 95 | The attempt ends | The passage state becomes "performed from memory" |
| AC-12 | FR-E2, FR-E4 | A passage with block chords | The player applies V2 with "Alberti" | Melody notes are identical to the source, and the description names the pattern and subdivision |
| AC-13 | FR-E5 | A variation with 4 steps | The player undoes all 4 | The result equals the source exactly; the original reference never changed |
| AC-14 | FR-E6 | The compare view during playback | The player toggles A/B | Playback switches at the same musical position within 50 ms |
| AC-15 | FR-A12 | The player changes a note from E4 to F4 | They confirm the edit | Chords and hints update within 200 ms and the note shows "user-edited" |
| AC-16 | FR-E10 | An exported variation | It is opened in a DAW | It has a left-hand and a right-hand track, the passage tempo map, and matches the variation note for note |
| AC-17 | FR-G3 | A signed-in player | They delete their account | Data leaves the device at once and the servers within 30 days; they get a confirmation email |

## Edge cases

Most edge cases come from expressive piano playing and imperfect recordings; the rule is to degrade gracefully and tell the player what the app is unsure about.

| Area | Case | Expected behaviour |
| --- | --- | --- |
| Recording | Strong rubato or no steady beat (for example a nocturne) | The beat grid follows the tempo curve; if beat confidence is low, offer free-time mode: passages by seconds, rhythm judged by relative timing only |
| Recording | Pickup bar | The player can move the first downbeat; snapping respects the pickup |
| Recording | Meter change mid-piece | Meter is stored per section; a passage across the change shows both |
| Recording | Piano not tuned to A440 | Estimate the tuning offset; the reference plays at the same tuning; MIDI export is at A440 with a note |
| Recording | Faint other instruments (piano with strings) | Quality warning; analysis continues for piano only |
| Recording | Same file imported again | Reuse the stored analysis at once; does not count against the plan limit |
| Analysis | More than 30% of a passage's notes below confidence 0.6 | "Uncertain passage" badge; suggest a clearer passage or the Study preset; feedback says "may be" |
| Analysis | Very fast passage (over 12 notes a second) | Suggest 50% speed and 1-bar loops; hints offered per hand |
| Analysis | Hands cross | Hand assignment may be wrong; the player can reassign notes; hands-separate playback follows the assignment |
| Analysis | Heavy pedal blurs note ends | Durations are not judged under pedal; pedal-held tails are drawn lighter |
| Analysis | No clear key (modal or chromatic music) | The key hint shows two candidates or "no clear key"; H2.1 is disabled with a reason |
| Input | MIDI keyboard set to transpose | A consistent offset across all notes is detected and the player is asked to confirm or fix it |
| Input | Bluetooth MIDI latency | A calibration step measures latency once per device and offsets timestamps |
| Input | Small keyboard; player plays the melody an octave off | An "ignore octave" option for the melody; the octave error then does not count |
| Input | Call-and-response attempt much slower or faster | Tempo alignment handles 0.5× to 2×; beyond that, ask the player to try again |
| Input | Silence, or fewer than 3 notes | No feedback; a gentle prompt to play along |
| Workspace | Selection under 1 bar or over 16 bars | Selection is blocked with the allowed range |
| Variation | Operation on a single-line melody with no chords | V2, V3 and V5 are disabled with a reason |
| Variation | Result exceeds a hand span or the piano range | The result is flagged and kept; the player decides |
| System | Network lost during upload or analysis | Upload resumes; analysis continues on the server; the player is notified when done |
| System | App closed during an attempt | That attempt is discarded; the workspace state is restored |
| System | Same note edited on two devices | Last edit wins per note; both edits stay in the history |
| System | New analysis pipeline version | User edits are mapped to the new notes by time and pitch; edits that cannot be mapped are kept and flagged |

## Non-functional requirements

Latency matters most: a player at a keyboard notices delays above a few tens of milliseconds, so playback and input budgets are strict while analysis can take up to a minute.

| ID | Area | Requirement |
| --- | --- | --- |
| NFR-1 | Analysis time | A 4-minute piece is analysed within 60 s for 9 of 10 imports; a previously analysed file opens within 2 s |
| NFR-2 | Input latency | MIDI note to on-screen key within 20 ms; on-screen key tap to sound within 30 ms on supported devices |
| NFR-3 | Playback | Loop restart gap under 20 ms; A/B switch within 50 ms; time-stretch at 50% speed rated acceptable by 8 of 10 testers |
| NFR-4 | Feedback time | Feedback for an 8-bar attempt within 500 ms of the attempt ending |
| NFR-5 | Variation time | A variation preview plays within 300 ms of a parameter change |
| NFR-6 | Analysis accuracy | On the benchmark set: note F1 at least 0.85, beat F-measure at least 0.85, chord accuracy at least 75% per beat (see Validation Plan) |
| NFR-7 | Offline use | Once a passage is loaded, everything except import, sync and the AI tutor works offline |
| NFR-8 | Platforms | Prototype: desktop Chrome and Edge (Web MIDI). MVP: iOS 16+ and Android 10+, phones and tablets; tablet landscape is the main practice layout |
| NFR-9 | Privacy | Audio deleted within 24 hours; EU data residency; GDPR rights in the app; no user audio or notes used for model training without opt-in |
| NFR-10 | Security | TLS everywhere; signed upload URLs expire after 15 minutes; data isolated per user and encrypted at rest |
| NFR-11 | Accessibility | WCAG 2.2 AA for app screens; colour is never the only signal (error types and hands also differ by shape or label); screen-reader labels on every control; adjustable text size |
| NFR-12 | Reliability | Analysis API available 99.5% of each month; workspace autosaves within 2 s, so a crash loses at most the current attempt |
| NFR-13 | Localisation | English first, strings externalised; note names as C D E, Do Re Mi or German (H for B) |
| NFR-14 | Reproducibility | The same input and pipeline version always produce the same analysis; every result records its version |
| NFR-15 | Cost | Compute cost per analysed piece measured from the prototype; Free-plan cost per user must stay below a target set before phase 2 |
| NFR-16 | Observability | Crash reports, analysis timing and accuracy per pipeline version, and the analytics events below |

## Analytics events

These events are the minimum needed to compute every metric in the Validation Plan. They carry pseudonymous IDs only, never audio or note content, and players can opt out.

| Event | Fired when | Key properties | Feeds |
| --- | --- | --- | --- |
| piece\_imported | Analysis finishes or fails | duration\_s, quality\_score, analysis\_ms, reused, result | Activation, NFR-1 |
| passage\_created | A passage is saved | bars, origin (suggested or manual), uncertain\_share | Activation |
| playback\_used | Playback starts | source, speed, hand, loop | Listening behaviour |
| attempt\_completed | An attempt ends | mode, hand, input, preset, pitch, rhythm, completeness, hints\_open, duration\_s | Engagement, skill |
| hint\_revealed | A hint is shown | hint\_id, tier, attempts\_before, seconds\_since\_open, via\_next\_best | Hint usefulness |
| feedback\_viewed | Feedback is shown | primary\_error\_type, more\_opened | Feedback usefulness |
| explanation\_opened | An explanation is tapped | concept, confidence | Understanding |
| variation\_applied | An operation runs | op\_id, params, kept | Creativity |
| variation\_saved | A variation is saved or exported | op\_chain\_length, edited\_notes, exported | Creativity |
| compare\_used | A/B or overlay is used | sources, toggles | Creativity |
| performance\_checked | A performance check ends | target (original or variation), passed, scores | Skill, independence |
| note\_edited | The player corrects the reference | field, old\_confidence | Accuracy |
| passage\_state\_changed | State changes | from, to, max\_tier\_used | Retention, skill |
| session\_ended | App backgrounded for 5 minutes | duration\_s, passages\_touched | Retention |
| paywall\_viewed, subscription\_started | Plan screen or purchase | trigger, plan | Commercial |
