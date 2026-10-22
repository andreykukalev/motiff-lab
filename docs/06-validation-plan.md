<!-- Exported from the Motiff Lab specification doc (https://claude.ai/artifact/8uR7nDk6Z7ar8vLjbsCMz3) on 2026-10-22. Edit here or in the doc; keep them in sync. -->

# Validation Plan

*Stage note: this plan validates Stages 2–3. Stage 1 has its own success criteria and study in MVP · Stage 1; its instruments (comparison tasks, teacher ratings, consent rules) reuse the ones defined here.*

## Hypotheses

The plan tests whether Motif Lab improves independent reconstruction and creative ability, not just whether people like it. Nine hypotheses each have a signal and a pass threshold agreed before any study runs; thresholds are starting proposals.

| ID | Hypothesis | Signal | Pass threshold | Tested in |
| --- | --- | --- | --- | --- |
| H1 | Pianists prefer guided reconstruction to an immediate transcription for music they love | Stated preference after trying both | At least 60% prefer guided, or guided first | Studies 1, 2 |
| H2 | Structural and partial hints are enough most of the time | Solved passages that needed no full reveal | At least 60% | Study 1, beta |
| H3 | Progressive disclosure improves independent reconstruction of a new passage | Transfer score, condition B vs A | B at least equal to A (no worse than 5 points) with frustration no more than 1 point higher on a 7-point scale | Study 2 |
| H4 | After reconstructing, players make variations they find musically meaningful | Self-rating and blind teacher ratings | At least 70% make one variation rated 5 or more out of 7 by themselves and by the teachers | Studies 1, 3 |
| H5 | Explanations are understood | Correct answer to a listening question after reading | At least 70% correct | Study 1 |
| H6 | Players come back with a new passage unprompted | Returns within 7 days with a new passage | At least 6 of 10 | Study 1 follow-up, beta |
| H7 | Players will pay | Price-sensitivity answers, then paid conversions | At least 40% find €5 a month acceptable; 20 paying beta users | Study 1, beta |
| H8 | Analysis is accurate enough on players' own recordings | Corrections per bar and trust rating | Median under 1 correction per bar; trust 5 or more out of 7 | Study 1, beta |
| H9 | Players become more independent over time | Help weight per solved passage | At least 25% lower on passages 8–10 than on passages 1–3 | Beta |

## Study 1: Concept and usability test

Twelve pianists use the browser prototype on a passage they chose themselves; the study finds what breaks, which hints help, and whether the journey is worth repeating. It is exploratory, not a statistically representative market study.

**Participants**

- 12 adults (range 10–15) who play piano or keyboard at late-beginner to intermediate level, want to learn by ear, and own a MIDI keyboard.
- Mix: at least 4 who never learned by ear, at least 4 who have tried; at least 3 classical and 3 pop or film players.
- Recruited through local piano teachers, music schools and online piano communities; a €25 voucher per session.

**Session (75 minutes, in person or remote with screen share)**

1. Background interview, 10 min: how they learn music today, tools used, frustrations.
2. Import a recording they brought and select a short passage, 5 min.
3. Reconstruct it with progressive hints, 20 min, thinking aloud.
4. Explore the structure (Harmony lens), 5 min, then one listening question to check an explanation (H5).
5. Create at least one variation and rate it, 10 min.
6. Play the passage or variation without visual help, 5 min.
7. Try the same passage in Study preset (full reveal) for 5 min, then state which they prefer (H1).
8. Exit interview and questionnaire, 15 min: System Usability Scale, effort and frustration (1–7), trust in the analysis (1–7), price sensitivity (H7).

**Measures**

Task completion and time per step; hints used and in what order; solves and the highest tier used; errors by type; corrections made; usability issues by severity; quotes. A follow-up message after 7 days asks whether they used the prototype again with a new passage (H6).

**Outputs**

A ranked list of usability issues, the observed hint order, tuned feedback thresholds (PRD), and a go or change decision for Study 2.

## Study 2: Learning experiment

This is the key experiment from the vision. It compares showing the estimated transcription at once (A) with an independent attempt followed by progressive hints (B), and measures what players can do afterwards on new music. The aim is to find the best balance of learning, frustration and engagement, not to prove B is always better.

**Design**

- Between-subjects, 24 participants (12 per condition), matched on a pre-test score and assigned by a random draw within pairs.
- Condition A: the Study preset; every note visible from the start, with the same loop, speed, keyboard and feedback tools.
- Condition B: the Explorer preset; an independent attempt first, then hints on request, full reveal always available.
- Both conditions practise the same 3 training passages for 20 minutes each, across two sessions within one week.

**Materials**

- Seven 4-bar passages from public-domain piano repertoire, recorded for the study: 1 pre-test, 3 training, 2 transfer, 1 variation task.
- A piano teacher matches difficulty on notes per second, range, hand independence and harmonic complexity; transfer passages share one concept with the training set (for example an Alberti bass or a melodic sequence) but not their notes.

**Procedure**

1. Pre-test: reconstruct the pre-test passage with no help, 10 min.
2. Training: 3 passages in the assigned condition, over two sessions.
3. Transfer test, immediately after training: reconstruct the 2 transfer passages with no help and no visuals, 10 min each.
4. Variation task: make one variation of a new motif with the variation tools, 10 min (rated in Study 3).
5. Retention test after 7 days: perform the training passages from memory.
6. Questionnaire after each session: effort, frustration, confidence and enjoyment (1–7).

**Measures**

- Primary: transfer score, the mean of pitch, rhythm and completeness on the transfer passages, scored by the app and checked by a teacher on 25% of attempts.
- Secondary: training completion, time to solve, hints used (B only), retention score, effort, frustration, enjoyment, and preference after a short taste of the other condition.

**Analysis**

Report differences between conditions with effect sizes and 95% confidence intervals; with 24 people the study can detect only large effects, so results guide decisions rather than prove claims. The hypotheses and analysis are written down before data collection. If results are promising, the comparison is repeated as an in-app experiment in the beta, with hundreds of players.

## Study 3: Transfer and creativity assessment

Study 3 defines how understanding, transfer and creativity are measured, so the same instruments can run inside Study 2 and later in the beta. Every measure is separate; none is folded into one overall score.

| Ability | Task | Scoring |
| --- | --- | --- |
| Independent reconstruction | Reconstruct an unseen passage with no help | App scores (pitch, rhythm, completeness), spot-checked by a teacher |
| Concept recognition (transfer) | Hear an unfamiliar passage and pick where a studied concept occurs (for example an anticipation), from 4 marked spots | Percentage correct, chance level 25% |
| Concept production (transfer) | Apply a studied concept to a given motif on the keyboard (for example turn block chords into an Alberti bass) | Teacher rating 1–7 for correctness and musicality |
| Variation quality | Make one variation of a new motif with the tools | Three piano teachers rate blind, 1–7, on coherence, originality, faithfulness to the source and playability |
| Intentionality | Explain in one sentence what was changed and why | Coded as correct, partly correct or incorrect against the actual diff |
| Independence | Practise over weeks | Help weight per solved passage; share solved without hints; performance-from-memory pass rate |

**Rater protocol for variations**

- Raters hear variations as audio only, in random order, without knowing the condition or the player.
- Agreement between raters is checked with an intraclass correlation; at least 0.7 is needed before ratings are used, otherwise rating rules are clarified and items re-rated.
- Each player also rates their own variations, which tests H4 alongside the teachers' ratings.

## Analysis accuracy benchmark

A fixed benchmark set measures every pipeline version, so accuracy is known before players see it and regressions are blocked. Targets match NFR-6 in the PRD.

**Benchmark set**

- 40 solo piano excerpts of 30 seconds.
- 20 clean recordings with note-aligned ground truth, from a public piano dataset whose licence allows this use (to be checked) or recorded on a MIDI-capable piano.
- 20 "real-world" recordings matching what players bring: phone recordings, home pianos, room echo, older recordings. A pianist annotates notes, beats and chords by hand.
- 10 excerpts are held back and used only to confirm a release.

| Metric | Definition | Target |
| --- | --- | --- |
| Note F1 | Notes matched by pitch and onset within ±50 ms | ≥ 0.85 overall, ≥ 0.75 on real-world recordings |
| Note F1 with offsets | As above, plus offset within 20% of duration | Reported, no target in MVP |
| Pedal accuracy | Pedal on and off events within ±100 ms | Reported |
| Beat F-measure | Beats within ±70 ms | ≥ 0.85 |
| Downbeat F-measure | Downbeats within ±70 ms | ≥ 0.75 |
| Key accuracy | Weighted score crediting related keys partly | ≥ 0.80 |
| Chord accuracy | Root and quality correct per beat, triads and sevenths | ≥ 75% |
| Hand accuracy | Notes assigned to the right hand | ≥ 90% |
| Motif precision | Suggested motifs judged real repeats by a pianist | ≥ 80% |
| Confidence calibration | Expected calibration error of note confidence | ≤ 0.10 |

**Rules**

- Every pipeline version runs the full set; a release is blocked if any metric drops by more than 2 points.
- Real-world accuracy is tracked in the beta through corrections per bar (H8) and compared with the benchmark.

## Product metrics

In the beta, metrics come from the analytics events in the PRD and are always split by skill level (from onboarding), preset and type of piece. The product optimises for learning signals, not for notes detected or variations generated. Targets are first proposals to revise after the first month of data.

| Area | Metric | Definition | Beta target |
| --- | --- | --- | --- |
| Activation | First passage | New players who create a passage on day 1 | 70% |
| Activation | First attempt | New players who complete an attempt on day 1 | 60% |
| Activation | Time to first attempt | Median minutes from first open to first completed attempt | 5 min or less |
| Learning | Reconstruction completion | Started passages that reach "solved" | 50% |
| Learning | Independent effort | Median attempts before the first hint on a passage | 2 or more |
| Learning | Variation creation | Solved passages with at least one saved variation | 40% |
| Learning | Variation performed | Saved variations that pass a performance check | 25% |
| Skill | Independence trend | Change in help weight per solved passage, passages 1–3 vs 8–10 (H9) | 25% lower |
| Skill | First-attempt accuracy | First-attempt transfer score on passages of similar difficulty, over time | Rising month on month |
| Skill | Transfer tasks (phase 3) | Concept recognition and production scores | Above 60% |
| Retention | Week-1 return | Players active again 2–7 days after their first day | 40% |
| Retention | Breadth | Distinct passages per returning player per month | 3 or more |
| Retention | Resume rate | Returning sessions that continue an earlier passage | 50% |
| Commercial | Conversion | Free players who start Pro within 30 days | 4% |
| Commercial | Pro retention | Pro players still paying in month 2 | 70% |
| Commercial | Relative value | Survey: "more valuable than the tools I used before" | 50% agree |

## Decision rules

Each checkpoint has three outcomes agreed in advance: continue, change, or stop and pivot. The roadmap gates in the Overview use these rules.

| Checkpoint | Continue if | Change if | Stop or pivot if |
| --- | --- | --- | --- |
| After Study 1 | At least 8 of 12 complete the journey; usability score at least 68; H5 met | Completion is low but the cause is a fixable usability or accuracy issue: fix, then run 4 more sessions | Most players abandon before a first solve even with hints, and say they do not want to work music out themselves |
| Gate 1: learning value (end of Study 2) | H1, H3 and H6 met | B matches A on transfer but frustration is higher: make Guided the default and test shorter hint paths | B is worse than A on transfer and preference: pivot to an analysis-first study tool with practice features |
| Gate 2: accuracy (end of phase 2) | Benchmark targets met and H8 met | Real-world accuracy is short: narrow supported recordings and improve the quality warning | Real-world note F1 stays below 0.7 after two model iterations: limit imports to the starter library and partner recordings |
| Gate 3: people pay (end of beta) | H7 met and Pro retention at least 70% | Interest is high but conversion is low: rework plans and limits | Few players return or pay: keep it as a free personal tool and stop investing |

Every decision is written up in this doc with the data behind it.

## Consent and data handling

Studies follow the same privacy rules as the product, plus written consent for anything recorded.

- Participants are adults; under-18s are not recruited.
- Written consent explains the purpose, what is recorded (screen, audio of the session, MIDI attempts, questionnaire answers), how long it is kept and how to withdraw at any time without losing the voucher.
- Recordings participants bring are used only during their session and deleted afterwards; study passages are public-domain compositions recorded for the study.
- Data is stored in the EU, pseudonymised with participant codes, and the key file is kept separately.
- Session recordings are deleted 12 months after the study; anonymised results may be kept.
- Conditions in Study 2 are described to participants in general terms ("two ways of practising") and fully explained in a debrief after the retention test.
- Beta players see the research use of analytics in the privacy policy and can opt out; research use of raw MIDI attempts needs a separate opt-in.
