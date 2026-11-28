# Protected acceptance scenarios

What the player can rely on while the code behind it is refactored (Task Record #310; Andrei, 2026-11-07: a test
adapted by the agent that broke it proves nothing). Each scenario is the player's steps, sent through the
`MusicCoreFacade` JSON contract (0020), and the expected values come from the music, the fixture or Andrei, never from
what the code answered. The project runs in `build-test` with the rest of `dotnet test MotiffLab.slnx`.

## Protection

- `governance/risk-paths.yml` puts `tests/backend/Acceptance/**` under `guardrail`: a change here is an environment task (E3).
- CI's `build-test` fails a pull request that changes, adds, removes or renames a file under `tests/backend/Acceptance/`
  unless the pull request has the label `protected-scenarios-ok`, which only Andrei sets (step "Check approval of the
  protected scenarios" in `.github/workflows/ci.yml`; the guard test is `governance/tests/acceptance.test.mjs`). Adding
  or removing the label runs the check again. The same step guards the UX baseline: `docs/ux-baseline.md`,
  `tests/ipad/Baseline/` and `tests/contracts/page-scenarios/`.
- When a scenario fails, the code is fixed, not the scenario. An expected value changes only with Andrei's approval.
- The label approves the pull request, not one commit: a later commit on a labelled pull request passes the check too.
  Every new commit still needs the Reviewer's and QA's verdicts for its SHA (`aom-verdicts`), and Andrei removes the
  label when he wants to see the scenarios again.

## What the scenarios use

- Calls and answers of the facade only (`Player.cs`), no music-core types, so they hold whatever the code inside looks
  like. The facade is what the iPad app calls; the web app calls the same music-core (importers, project file,
  variations, Check) directly, so the rules here are the rules of both.
- Their own copies of the fixtures, in `Fixtures/`, so that a change to another test's fixture never changes these:

| Fixture | What it is | Where it comes from |
| --- | --- | --- |
| `fur-elise-original.musicxml` | Für Elise, the pickup and bars 1–8, both hands | Written by hand from the public-domain score; a copy of `tests/backend/Unit/MotiffLab.MusicCore.Tests/Experiments/Fixtures/` |
| `scale-type0.mid` | The C major scale up and down, 15 quarters at 96 ticks per quarter, 100 BPM, 4/4 | A copy of `tests/backend/Unit/MotiffLab.MusicCore.Tests/Import/Midi/Fixtures/` |
| `blank-page.pdf` | A PDF of one blank A4 page | Written for these scenarios |
| `scan-result.musicxml` | What a scan server might read from a page: 3 bars in 4/4, bar 2 one beat too long | Written for these scenarios |
| `v12-paterlini-all-my-joy.motiff.json`, `v12-einaudi-santiago.motiff.json` | Project files of version 12 that Andrei made in the app, with synthetic MIDI | Copies of the #293 golden files (`tests/backend/Unit/MotiffLab.MusicCore.Tests/Golden/README.md`) |
| `v15-worked.motiff.json` | A project file of version 15 holding every kind of entity a note piece keeps | A copy of the #293 golden file |
| `v20-future.motiff.json` | The version 15 file raised to version 20 with `minReader` 20, with unknown fields and values: newer than every build, before and after the project file's versions 16 (0.6), 17 (1.1), 18 (1.6) and 19 (1.7), and one no build of version 19 may read | The #293 golden file `v16-future.motiff.json` with its version raised to 20 and `minReader` 20 (`v19-future` with 19 and 19 until version 1.7 wrote version 19) |
| `v20-readable.motiff.json` | A project file of version 20 with `minReader` 18: a later build's file that a build of version 18 or 19 may read, read-only | A copy of the #293 golden file `v20-future.motiff.json` (`v19-worked.motiff.json` raised to 20, with unknown fields and an unknown operation; `v19-readable`, the version 18 file raised to 19, until version 1.7 wrote version 19) |
| `transkun-fur-elise.mid` | Für Elise as Transkun heard a synthesised recording of it: 904 notes in 130 s | A copy of `tests/backend/Unit/MotiffLab.MusicCore.Tests/Import/Transcription/Fixtures/` |

## The scenarios

| # | The player | Expected | Where the expected values come from |
| --- | --- | --- | --- |
| a | Opens Für Elise (MusicXML) | A pickup of two sixteenths (240 ticks) and 8 bars of 3/8 (720 ticks each), A minor, 53 notes; the opening E D♯ E D♯ E B D C in sixteenths from tick 0, then A on the downbeat of bar 2; every note imported | The score (bars, key, the opening); the fixture (53 notes, counted in the file) |
| b | Opens a MIDI file | A piece: the 15 notes C D E F G A B C B A G F E D C, one every quarter (480 ticks), in 4/4; every note imported | The fixture (its events, read byte by byte) |
| c | Opens a damaged file (a MIDI header cut short) | Refused with `import` and a message; no piece open, so nothing to save; the bytes as they were | The facade's contract (0020: a failure leaves nothing changed) |
| d | Opens a PDF | A PDF piece: a document, no notes, the same bytes back; calls about notes refused with `noNotes` | The fixture; 0023 (a PDF piece has no notes until they are read) |
| e | Selects the right hand of bars 1–2, tries "Half time" | No note added or removed; the pitches E D♯ E B D C A C E A in their order; the variation sounds different in place; the piece's own notes and sound unchanged, also after saving and reopening | The score (the right hand of bars 1–2); the operation's definition (a rhythm reshape keeps the pitches and their order) |
| f | Does e's steps twice, in two app sessions | The same variation and the same sound, to the byte | The rule that variation operations are pure (`CLAUDE.md`); since contract 10 a Try chip has fixed parameters and a fixed seed |
| g | Asks for a variation the catalogue does not have | Refused with `badRequest`; the variation tried before and its sound unchanged | The facade's contract (a failure leaves the piece as it was); since contract 10 no call takes an operation's parameters, so this replaces "an out-of-range parameter", as Andrei chose (2026-11-11, #310) |
| h | Saves a project with a selection and a variation, reopens it | The same id, selection, notes, variation steps and sound; saved again unchanged, the same file | The rule S1-P1 (the project keeps the work) |
| i | Opens the version 12 and version 15 project files | Each opens with the id, file name, motifs, variations, resume points, bookmarks and versions the file holds; the first motif back as the selection; a transcription's notes detected; saved again, the file opens | The fixtures (what each file holds) |
| j | Opens a project file of a newer version whose `minReader` is above this build's | Refused with `import` and the newer-version message; nothing open, so nothing to save over it; the bytes as they were | The #293 golden README; since 1.6 (c-14a, #315) only a file no build of this version may read is refused (PR #844, merged by Andrei, 2026-11-27) |
| k1 | Has the notes read from the PDF by a scan | The piece is `scanned`; all 11 notes detected; one open Check flag, bar 2, `barTooLong`, from the scan | The fixture (bar 2 holds five quarters in 4/4); 0011 (a scan's notes are detected, never imported) |
| k2 | Opens a recording heard in two parts | The piece is `transcribed`; every note detected; the seam between the parts flagged `partSeam`, open, kept after saving | 0014 and 0016 (a recording's notes are detected; a seam is always flagged) |
| l1–l5 | Reads in Russian or in English | The refusal of a damaged file and of a newer file, the explanation of the variation, and the Check flags' names and texts of a scan and of a recording are in that language | `CLAUDE.md` (Russian and English for the interface and all generated content) |
| m1 | Corrects the first note of a scan's bar 1 (C5 to C♯5) and has the page read again the same | The correction carried to the new reading, the toast's line «1 correction carried over»; the new notes made working have C♯5 there | The steps (the same reading again); v1.5 c-11 (`docs/ux-baseline.md` NOTE-01) |
| m2 | Does m1's correction, and the page is read again with B4 in its place, in Russian and in English | Not carried: bar 1 counted and flagged in Check, open, «Исправление не подошло» / "Your correction did not fit" | The steps (neither the value found nor the corrected one); NOTE-01 |
| m3 | Does m1's correction and marks bar 2 «Looks right», and the page is read again with C♯5 in the corrected note's place | Nothing carried, 1 already right, bar 2 marked again; the toast's line «already right in the new reading: 1 · «Looks right» carried over: 1 bar» | The steps (the new reading has the corrected value; bar 2 read as before); NOTE-01 |
| n | Bookmarks bar 2 of a scan twice (one titled «Mine», one by its bar), starts a phrase there, saves a resume point and keeps an idea with a note on it, has the page read again with a bar before the first, and makes those notes working | All follow their music to bar 3 (tick 3840): «Mine» kept, the other «bar 3»; the toast's line «moved with their bars: 2 bookmarks, 1 phrase boundary, 1 idea» | The fixture (bar 2's music is bar 3 of the longer reading, 4/4 at 480 a quarter); v1.5 c-11b (VIEW-11) |
| o1 | Selects bars 1–3 of a scan read with no key signature whose bar 2 is flagged, reads Insights and the DNA, checks bar 2 | Every doubted fact says «Rests on recognised notes still to check in bar 2.», and the DNA has a doubt; after the check neither has any | The fixture (bar 2 the one bar to check); #352, v1.5 c-15b (NOTE-02) |
| o2 | Opens Für Elise heard by Transkun, «Looks right» on a flagged bar with lengths to check, then takes it back | Its length marks go, and come back | v1.5 c-15b (NOTE-02: «Верно» clears a bar's length marks) |
| p | Opens the note editor on Für Elise with nothing selected, and changes a note there | It opens at bar 0, the pickup E5 D♯5, and the note is changed in bar 0 | The score (the pickup); v1.5 r-2a (NOTE-03) |
| r | Opens a project file of version 19 whose `minReader` is 18 | It opens read-only (`readOnly`), its notes play; the save is refused with `readOnly` and the newer-version message; the bytes as they were | v1.6 c-14a (#315, the plan's decision 10: a newer file opens read-only and is never saved; `docs/ux-baseline.md` FILE-02) |
| q1 | Tries «Half time» on the right hand of bars 1–2, saves, opens again, tries «Double time», saves | The file holds two variations on one motif | #821, v1.5 r-2a (FILE-01: a save keeps every saved variation) |
| q2 | Tries «Half time» on the right hand of bars 1–2, saves, opens again, selects bars 5–6, saves | The file holds two motifs, the first with its variation | v1.5 r-2a (FILE-01: the old motif beside a new passage when it has saved variations) |

The answers to requests no app sends (`badRequest`, such as g's unknown chip) are for the app's developers and are
not counted among the messages of l.

## Known defects

A scenario that fails on `main` because of a product defect is recorded in Task Record #310 and skipped, with the
reason, only after Andrei agrees (`KnownDefect.cs`); the task that fixes the defect removes the Skip.

None today: l2 in Russian, the last one (#310), runs since #363.

## Running them

```bash
dotnet test tests/backend/Acceptance/MotiffLab.Acceptance.Tests
```
