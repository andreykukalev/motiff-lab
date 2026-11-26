# The facade contract

**Status (2026-11-26, v1.4 c-5):** contract 15 (`MusicCoreFacade.Contract`, the `version` call). The calls and their
flags below are written from the call registry (`CallRegistry`) by `FacadeCallsContractTests` and checked by it, as is
`tests/contracts/facade-calls.json`; the history is kept by hand. A call is only added (decision 3): a change of what a
call answers, or of the calls, adds its line to the history under the current number.

## How a call is answered

The iPad app calls music-core through the C functions of `src/ipad/Core` (`ml_open`, `ml_call`, `ml_close`); `ml_call`
is `MusicCoreFacade.Call(piece, name, request)`, which looks the name up in the registry for the receiver:

- **piece 0** (`piece0`): the calls that need no open piece (version, settings, library, sync, scans, Reference, …).
- **a piece with notes** (`piece`): the piece, its selection, marks, corrections, sources, assistant and drafts. A
  `drafts.*` call works on the drafts of the piece the request's `into` names, else of the piece a view was opened from,
  else of the piece itself; it is never refused as read-only, and its owner is found before the name is looked up. One
  exception before both: `drafts.add` with `file` (another piece's project file, the collage) adds to that file's
  drafts and needs no owner (`MusicCoreFacade.CallOnDrafts`).
- **a piece without notes**: a PDF piece (`pdf`) or a piece that is only its recording (`recording`).

Every answer is one JSON object. A refusal is `{"error": code, "message": text}` and leaves the piece as it was:

| Code | When |
| --- | --- |
| `unknownCall` | a name the registry does not hold for the receiver (on piece 0: «There is no call "x" without a piece.») |
| `noPiece` | the piece number is not open; on piece 0 with nothing open, a piece's call (`OpensFirst`: a name with a dot but `settings.*` and `reference.*`, or one flagged `OpenFirst`) |
| `readOnly` | a call not flagged `Showing` on Notes, a version or a draft open only to be shown |
| `noNotes` | on a piece without notes, a name it does not hold, or one flagged `NeedsPdf` on a recording |
| `unavailable` | the call cannot work now, for one: a name starting `corrections` not flagged `WhileCorrectionsOff` while the corrections are off |
| `badRequest` | the request is not a JSON object, lacks a field the call needs or has one of another type |
| `noSelection`, `noVariation`, `notFound` | the call needs a selection, a variation, or an object the request names that is not there |
| `notSupported` | `parts.split` cannot split that piece |
| `originalAway`, `otherFile` | `ml_open`: a piece whose PDF is away, and a PDF sent for it that is not its own |
| `import` | `ml_open` could not read the file: also `reason` and perhaps `tip` |
| `internal` | an exception the facade did not expect, with its type and message: a defect to fix |

## Flags

- `Showing`: answered on Notes, a version or a draft open only to be shown.
- `WhileCorrectionsOff`: answered while the corrections are off.
- `NeedsPdf`: on a piece without notes, answered only when it keeps a PDF.
- `OpenFirst`: a name without a dot that piece 0 answers `noPiece` for while nothing is open.

## Calls

<!-- The calls: written from CallRegistry by FacadeCallsContractTests (MOTIFF_UPDATE_SNAPSHOTS=1). -->

| Call | Answered on | Flags |
| --- | --- | --- |
| `assistant.address` | piece |  |
| `assistant.call` | piece |  |
| `assistant.context` | piece |  |
| `assistant.digest` | piece |  |
| `assistant.tools` | piece0 |  |
| `bookmark.add` | piece |  |
| `bookmark.change` | piece |  |
| `bookmark.form` | piece |  |
| `bookmark.move` | piece |  |
| `bookmark.remove` | piece |  |
| `bookmark.structure` | piece |  |
| `bookmarks` | piece | OpenFirst |
| `catalogue` | piece0 |  |
| `category.add` | piece |  |
| `category.remove` | piece |  |
| `category.rename` | piece |  |
| `check` | piece | Showing, OpenFirst |
| `check.resolve` | piece |  |
| `circle` | piece | OpenFirst |
| `corrections` | piece | WhileCorrectionsOff, OpenFirst |
| `corrections.acceptDraft` | piece |  |
| `corrections.add` | piece |  |
| `corrections.begin` | piece |  |
| `corrections.change` | piece |  |
| `corrections.discard` | piece |  |
| `corrections.drop` | piece | WhileCorrectionsOff |
| `corrections.layer` | piece | WhileCorrectionsOff |
| `corrections.remove` | piece |  |
| `corrections.reset` | piece |  |
| `corrections.revert` | piece |  |
| `corrections.save` | piece |  |
| `corrections.set` | piece |  |
| `corrections.undo` | piece |  |
| `corrections.undoDraft` | piece |  |
| `corrections.variant` | piece |  |
| `dna` | piece | Showing, OpenFirst |
| `document` | pdf | NeedsPdf |
| `document.bookmark.add` | pdf | NeedsPdf |
| `document.bookmark.remove` | pdf | NeedsPdf |
| `document.bookmark.rename` | pdf | NeedsPdf |
| `document.ink.add` | pdf | NeedsPdf |
| `document.ink.clear` | pdf | NeedsPdf |
| `document.ink.remove` | pdf | NeedsPdf |
| `document.marks` | pdf | NeedsPdf |
| `document.read` | pdf | NeedsPdf |
| `document.replace` | pdf | NeedsPdf |
| `drafts.accept` | piece |  |
| `drafts.add` | piece |  |
| `drafts.addFromRecording` | piece |  |
| `drafts.anchor` | piece |  |
| `drafts.base` | piece |  |
| `drafts.bridge` | piece |  |
| `drafts.call` | piece |  |
| `drafts.carry` | piece |  |
| `drafts.carryPlaces` | piece |  |
| `drafts.context` | piece |  |
| `drafts.findOnRecording` | piece |  |
| `drafts.leave` | piece |  |
| `drafts.leaveBridge` | piece |  |
| `drafts.leaveSeam` | piece |  |
| `drafts.length` | piece |  |
| `drafts.movePlace` | piece |  |
| `drafts.open` | piece |  |
| `drafts.place` | piece |  |
| `drafts.proposal` | piece |  |
| `drafts.remove` | piece |  |
| `drafts.rename` | piece |  |
| `drafts.resize` | piece |  |
| `drafts.seams` | piece |  |
| `drafts.sheet` | piece |  |
| `drafts.take` | piece |  |
| `drafts.takeFrom` | piece |  |
| `drafts.takeSure` | piece |  |
| `drafts.techniques` | piece |  |
| `drafts.tempo` | piece |  |
| `drafts.transpose` | piece |  |
| `drafts.unanchor` | piece |  |
| `drafts.undo` | piece |  |
| `fingering.remove` | piece |  |
| `fingering.set` | piece |  |
| `highlight.add` | piece |  |
| `highlight.remove` | piece |  |
| `idea.keep` | piece |  |
| `idea.note` | piece |  |
| `idea.open` | piece |  |
| `idea.remove` | piece |  |
| `idea.rename` | piece |  |
| `ideas` | piece | OpenFirst |
| `ink.add` | piece |  |
| `ink.clear` | piece |  |
| `ink.remove` | piece |  |
| `language` | piece | Showing |
| `library.confirm` | piece0 |  |
| `library.link` | piece0 |  |
| `library.linked` | piece0 |  |
| `library.plan` | piece0 |  |
| `library.relink` | piece0 |  |
| `marks` | piece | Showing, OpenFirst |
| `notation` | piece | Showing, OpenFirst |
| `note.add` | piece |  |
| `note.change` | piece |  |
| `note.remove` | piece |  |
| `parts.split` | piece0 |  |
| `performance` | piece | OpenFirst |
| `performance.add` | piece |  |
| `performance.dismiss` | piece |  |
| `performance.rename` | piece |  |
| `performance.restore` | piece |  |
| `performance.take` | piece |  |
| `performance.takeAll` | piece |  |
| `performances` | piece | OpenFirst |
| `phrases` | piece | Showing, OpenFirst |
| `phrases.edit` | piece |  |
| `piece` | piece | Showing, OpenFirst |
| `pieces.find` | piece |  |
| `pieces.save` | piece |  |
| `playback` | piece | Showing, OpenFirst |
| `project` | pdf, recording |  |
| `project.drafts` | piece0 |  |
| `project.join` | piece0 |  |
| `project.metadata` | piece0, piece, pdf, recording |  |
| `project.online` | piece0 |  |
| `project.pulled` | piece0 |  |
| `project.resolve` | piece0 |  |
| `project.same` | piece0 |  |
| `project.save` | piece, pdf, recording |  |
| `project.summary` | piece0 |  |
| `project.sync` | piece0 |  |
| `recording.parts` | piece0 |  |
| `reference.chord` | piece0 |  |
| `reference.explorer` | piece0 |  |
| `reference.key` | piece0, piece |  |
| `reference.scale` | piece0 |  |
| `reference.sound` | piece0 |  |
| `reference.sounding` | piece0 |  |
| `resume` | piece | OpenFirst |
| `resume.remove` | piece |  |
| `resume.rename` | piece |  |
| `resume.save` | piece |  |
| `roll` | piece | Showing, OpenFirst |
| `scan.insert` | piece0 |  |
| `scan.join` | piece0 |  |
| `scan.vector` | piece0 |  |
| `select` | piece | Showing, OpenFirst |
| `settings` | piece0, piece |  |
| `settings.override` | piece |  |
| `settings.state` | piece0 |  |
| `sources` | piece, pdf, recording | OpenFirst |
| `sources.document` | piece |  |
| `sources.documentNotes` | piece |  |
| `sources.held` | piece |  |
| `sources.layers` | piece, pdf, recording |  |
| `sources.makeWorking` | piece |  |
| `sources.notes.hear` | piece, pdf, recording |  |
| `sources.notes.open` | piece |  |
| `sources.notes.recognise` | piece |  |
| `sources.notes.remove` | piece, pdf, recording |  |
| `sources.original.add` | piece, pdf, recording |  |
| `sources.original.remove` | piece, pdf, recording |  |
| `sources.pdf` | piece |  |
| `sources.relink` | piece |  |
| `sources.view.hide` | piece, pdf, recording |  |
| `tempo` | piece |  |
| `tempo.set` | piece |  |
| `try` | piece | OpenFirst |
| `understand` | piece | OpenFirst |
| `variation` | piece |  |
| `variation.clear` | piece |  |
| `version` | piece0 |  |
<!-- End of the calls. -->

## History

### 15

15 renames the chosen tempo of reference.explorer's answer (version 1.4, c-5): it was a second "tempo" beside the playback's "tempo" object, which a strict JSON reader refuses, and is now "chosenTempo"; the playback's "tempo" and "tempos" and "tempoChoices" stay. The iPad reads the playback's object, so it reads the answer as before.

### 14

14 removes phrases.level and its "remember" (version 1.2), which no app sends since the score's phrase marks; it answers unknownCall. "levels" and phrases.granularity stay, so a file carrying the setting opens as before.

Also within 14 (version 1.3, ADR 0035 section 5): a page of scan.join's "pages" may be its readings by several engines, [{"engine", "musicxml"}], merged staff by staff (`ScanMerge`), and such a page answers "merge": its engines and each bar's "level" (sure, probable, check) with the engines of each staff; a page sent as text and "score" with "replace" answer as before; a form and a field added.

Also within 14 (c-1b): such a page may be {"readings": [...], "evidence": the scan server's page evidence}, the evidence settling the staves no two engines agree on (`ScanEvidence`); a form added, the answer as before.

Also within 14 (v1.3 i-2, ADR 0035 section 6): scan.join answers "fingerprint" (the score as its Notes keep it) and a merged bar to check its "variants" (the engines' other readings of its staves to check) and "crop" (its box on the page image); check answers "fingerprint", the working notes' music; corrections.variant puts a variant in that bar as the player's corrections, one group undone whole by corrections.undoDraft; fields and a call added.

Also within 14 (v1.3 i-1b, Andrei 2026-11-24): scan.insert puts a page no engine read, read again by itself, into the same Notes of a project file, the player's corrections carried over and the bars after it numbered on; a call added.

Also within 14 (version 1.4, c-2, the call registry): on a piece without notes, a name it does not hold answers
"noNotes" whatever the request's fields; a name starting "sources." with a "language" that is not text answered
"badRequest" before. No app sends such a call.

Also within 14 (version 1.4, c-3, the plan's F1 and F2): drafts.movePlace moves a place of a draft to an index among its places, dropping the «Оставить» of a seam whose place has another before it now (`DraftRequest.Move`); drafts.findOnRecording answers the stretch of each of the piece's recordings that is a place's music and drafts.addFromRecording puts one in as a variant of the place, «Найти на записи» (`DraftRecordingRequest`, `OnRecording`); both changes of structure are undone by drafts.undo while the piece is open; calls added. The project file stays version 17.

Also within 14 (version 1.4, c-3b, Andrei's answers of 2026-11-26): drafts.length answers how long a place is in the bars of its source and whether it can change, drafts.resize cuts its base again from those Notes over other bars, the steps and seams kept as they are outside the new span dropped (`DraftLengthRequest`, `PlaceLength`); drafts.carryPlaces answers where a приём of a place fits in the draft's other places and drafts.carry puts in, for one of them, a variant music-core builds with that приём (`DraftCarryRequest`, `TechniqueCarry`); drafts.movePlace also drops a bridge place whose neighbours change, and drafts.resize one whose seam's edge moved, its steps in "dropped", a bridge place itself is not moved (unavailable), and a bridge put in or left drops the «Оставить» of the seam after it; changes of structure undone by drafts.undo while the piece is open; calls added. The project file stays version 17.

Also within 14 (version 1.4, c-4, «Как напечатано» step 1): notation writes notes with a source record (read from a PDF,
or imported MusicXML) as the score prints them where the record fits, and answers "appWrittenBarCount", the bars the app
wrote where it does not (null for notes without a record); a field added.

### 13

13 removes the calls of the bar-by-bar merge, which left the iPad in version 1.1 (ADR 0034, section 8) and which no app sends: those of 7 (performance.merge, its .open, .choose, .own, .undecide, .undo, .redo, .reset, .cancel, .apply, .undoApplied, .notation and .playback, and performance.putBack) and performance.remove of 4, as 10 removed calls; they answer unknownCall, and a performance no longer tells "mergedAt" and "mergeKept". A version is removed as other Notes are (sources.notes.remove). The project file stays version 17 and reads a performance's "taken" and "merge" and writes them back as before.

Also within 13 (version 1.1, ADR 0034 section 9 step 2, the iPad's step 5): drafts.sheet, drafts.add, drafts.open, drafts.rename and drafts.remove (`DraftRequest`), on the piece open, on Notes or a version open only to be shown, or with "into" from another piece; sources.layers and the Sources sheet's answers gain "drafts", their views the kind "draft", which sources.view.hide hides as any other; calls, a field and a value added.

Also within 13 (version 1.1, ADR 0034 section 9 step 3, the iPad's step 6): drafts.techniques answers the приёмы of a place with their numbers, kinds, labels, confidence, whether sure, and where each is found, drafts.take and drafts.leave take or leave one everywhere or at one bar, drafts.takeSure takes every sure one as a group, drafts.takeFrom a hand or a span of a variant, drafts.undo the draft's last step (`DraftTakeRequest`); calls added.

Also within 13 (version 1.1, ADR 0034 section 9 step 4, the iPad's step 7): drafts.seams answers a draft's seams, what differs of key, meter and tempo either side and what is offered, drafts.transpose and drafts.tempo add the step an offer names, drafts.leaveSeam keeps a seam as it is (`DraftSeamRequest`); calls added.

Also within 13 (version 1.1, the iPad's step 7, the draft's map): drafts.open gives each place its variants and steps and the draft its shelf, drafts.place moves a variant and drafts.base chooses a place's base (`DraftRequest`); calls added; drafts.undo takes them back while the piece is open (a draft's structure is its state, not in its journal).

Also within 13 (version 1.2, the phrases spec, «Engine»): the phrases answer is the one answer of the score's marks: a phrase's start tells "bar", "beat", "found" (what a moved one replaced), "close" and "why" («Почему здесь»), a phrase its "motifs", the answer "own" and "sections" (dna's form, each with its "phrase", bar and beat); phrases.edit takes "move", "start", "remove", "putBack", "resetAll" (the phrases only, "replaced") and "restore" with "expected" (Undo, refused when stale), answering "previous" and "changed"; fields added. A piece opens in phrases, no edit moves a bookmark. 

### Earlier

Contracts 1 to 12: ADR 0020 ([0020](architecture/adr/0020-music-core-facade.md)), point 2.
