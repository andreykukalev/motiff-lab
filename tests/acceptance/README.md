# Protected acceptance scenarios

What the player can rely on while the code behind it is refactored (Task Record #310; Andrei, 2026-11-07: a test
adapted by the agent that broke it proves nothing). Each scenario is the player's steps, sent through the
`MusicCoreFacade` JSON contract (0020), and the expected values come from the music, the fixture or Andrei, never from
what the code answered. The project runs in `build-test` with the rest of `dotnet test MotiffLab.slnx`.

## Protection

- `governance/risk-paths.yml` puts `tests/acceptance/**` under `guardrail`: a change here is an environment task (E3).
- CI's `build-test` fails a pull request that changes, adds, removes or renames a file under `tests/acceptance/`
  unless the pull request has the label `acceptance-approved`, which only Andrei sets (step "Check approval of the
  protected scenarios" in `.github/workflows/ci.yml`; the guard test is `governance/tests/acceptance.test.mjs`). Adding
  or removing the label runs the check again.
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
| `fur-elise-original.musicxml` | Für Elise, the pickup and bars 1–8, both hands | Written by hand from the public-domain score; a copy of `tests/MotiffLab.MusicCore.Tests/Experiments/Fixtures/` |
| `scale-type0.mid` | The C major scale up and down, 15 quarters at 96 ticks per quarter, 100 BPM, 4/4 | A copy of `tests/MotiffLab.MusicCore.Tests/Import/Midi/Fixtures/` |
| `blank-page.pdf` | A PDF of one blank A4 page | Written for these scenarios |
| `scan-result.musicxml` | What a scan server might read from a page: 3 bars in 4/4, bar 2 one beat too long | Written for these scenarios |
| `v12-paterlini-all-my-joy.motiff.json`, `v12-einaudi-santiago.motiff.json` | Project files of version 12 that Andrei made in the app, with synthetic MIDI | Copies of the #293 golden files (`tests/MotiffLab.MusicCore.Tests/Golden/README.md`) |
| `v15-worked.motiff.json` | A project file of version 15 holding every kind of entity a note piece keeps | A copy of the #293 golden file |
| `v16-future.motiff.json` | The version 15 file raised to version 16, with unknown fields and values | A copy of the #293 golden file |

## The scenarios

| # | The player | Expected | Where the expected values come from |
| --- | --- | --- | --- |
| a | Opens Für Elise (MusicXML) | A pickup of two sixteenths (240 ticks) and 8 bars of 3/8 (720 ticks each), A minor, 53 notes; the opening E D♯ E D♯ E B D C in sixteenths from tick 0, then A on the downbeat of bar 2; every note imported | The score (bars, key, the opening); the fixture (53 notes, counted in the file) |
| b | Opens a MIDI file | A piece: the 15 notes C D E F G A B C B A G F E D C, one every quarter (480 ticks), in 4/4; every note imported | The fixture (its events, read byte by byte) |
| c | Opens a damaged file (a MIDI header cut short) | Refused with `import` and a message; no piece open, so nothing to save; the bytes as they were | The facade's contract (0020: a failure leaves nothing changed) |
| d | Opens a PDF | A PDF piece: a document, no notes, the same bytes back; calls about notes refused with `noNotes` | The fixture; 0023 (a PDF piece has no notes until they are read) |
| e | Selects the right hand of bars 1–2, tries "Half time" | No note added or removed; the pitches E D♯ E B D C A C E A in their order; the variation sounds different in place; the piece's own notes and sound unchanged, also after saving and reopening | The score (the right hand of bars 1–2); the operation's definition (a rhythm reshape keeps the pitches and their order) |
| f | Does e's steps twice, in two app sessions | The same variation and the same sound, to the byte | The rule that variation operations are pure (`CLAUDE.md`); since contract 10 a Try chip has fixed parameters and a fixed seed |
| g | Asks for a variation the catalogue does not have | Refused with `badRequest`; the variation tried before and its sound unchanged | The facade's contract (a failure leaves the piece as it was); since contract 10 no call takes an operation's parameters, so this replaces "an out-of-range parameter", waiting for Andrei (#310) |
| h | Saves a project with a selection and a variation, reopens it | The same id, selection, notes, variation steps and sound; saved again unchanged, the same file | The rule S1-P1 (the project keeps the work) |
| i | Opens the version 12 and version 15 project files | Each opens with the id, file name, motifs, variations, resume points, bookmarks and versions the file holds; the first motif back as the selection; a transcription's notes detected; saved again, the file opens | The fixtures (what each file holds) |
| j | Opens a project file of version 16 | Refused with `import` and the newer-version message; nothing open, so nothing to save over it; the bytes as they were | Today's rule in the #293 golden README; the tolerant-reader task changes it to read-only with Andrei's approval |
| k1 | Has the notes read from the PDF by a scan | The piece is `scanned`; all 11 notes detected; one open Check flag, bar 2, `barTooLong`, from the scan | The fixture (bar 2 holds five quarters in 4/4); 0011 (a scan's notes are detected, never imported) |
| k2 | Opens a recording heard in two parts | The piece is `transcribed`; every note detected; the seam between the parts flagged `partSeam`, open, kept after saving | 0014 and 0016 (a recording's notes are detected; a seam is always flagged) |
| l1–l5 | Reads in Russian or in English | The refusal of a damaged file and of a newer file, the explanation of the variation, and the Check flags' names and texts of a scan and of a recording are in that language | `CLAUDE.md` (Russian and English for the interface and all generated content) |

The answers to requests no app sends (`badRequest`, such as g's unknown chip) are for the app's developers and are
not counted among the messages of l.

## Known defects

A scenario that fails on `main` because of a product defect is recorded in Task Record #310 and skipped, with the
reason, only after Andrei agrees (`KnownDefect.cs`); the task that fixes the defect removes the Skip.

| Scenario | Defect | Status |
| --- | --- | --- |
| l2, in Russian | The refusal of a newer project file is in English only: the text comes from `ProjectFile.Read` | Waiting for Andrei's agreement (#310) |

## Running them

```bash
dotnet test tests/acceptance
```
