# The facade contract

**Status (2026-11-27, v1.6 c-17a):** contract 17 (`MusicCoreFacade.Contract`, the `version` call). The calls and their
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
| `readOnly` | a call not flagged `Showing` on Notes, a version or a draft open only to be shown; and a call that answers the project file (`project.save`, `project.metadata`, the library's, `document.read`, `scan.insert`, drafts', parts' and sync's) for a project of a later version open read-only (`ProjectFile.Save`, project file 18) |
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
| `bookmarks` | piece | OpenFirst |
| `catalogue` | piece0 |  |
| `category.add` | piece |  |
| `category.remove` | piece |  |
| `category.rename` | piece |  |
| `check` | piece | Showing, OpenFirst |
| `check.mark` | piece |  |
| `check.resolve` | piece |  |
| `check.unmark` | piece |  |
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
| `drafts.findNewOnRecording` | piece |  |
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
| `phrases.reattach` | piece |  |
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

### 17

Also within 17 (version 1.6, c-17a, #321, #322, the 1.6 plan's decisions 9, 14 and 16): project.resolve takes "base", the copy both last agreed on, and "sendsFile" (true: the app writes "file" here and sends it when "upload" is true; without it, as the iPad before c-17b, it sends "here", so "upload" is true only when "here" was saved last and "copy" then keeps the online copy unless it adds nothing; when "upload" is false, "copy" keeps "here" unless it adds nothing to the online copy, as the file such an app writes is never sent and the next download writes over it), and merges the two copies entity by entity against it (`ProjectMerge`): it answers "conflicts" ([{"in", "id"}], what of the copy that lost "file" does not hold; "id" is null for a field merged whole and for all the corrections of a copy whose notes are others; "copy" is null when there is none, and set whenever there is one), "merged" (false when the merge failed its self-check, 0026 C5: the one saved last was kept with the other's Notes, "copy" holds the other unless it adds nothing, and "upload" is false; false too when either copy is a later build's, which is never merged: "file" is the online copy as project.pulled keeps it and "copy" this device's, a later build's read-only, else unless it adds nothing, and "upload" is false) and "readOnly" (true when "file" holds a copy a later build saved: kept here, never sent; "upload" is then false). The marks on a PDF's pages ("originals" → "marks") merge mark by mark, three-way against "base" (one erased on one side and unchanged on the other goes) or, without it, as a union (a stroke by id, a bookmark by page; "in": "originals.marks.ink" or "originals.marks.bookmarks"), the online copy bringing its own; project.pulled keeps the online copy's, unless it has none (v1.6 r-3: two iPads that hold the PDF both write them). project.pulled answers a later build's copy as it came, with "readOnly": true, rather than refusing it; project.join leaves such a file out rather than failing as a whole. project.sync takes "deletedHere" (the player removed the piece here) and answers "deleteOnline" for it, or "download" when it changed online since. A copy re-keyed from the online id (`formerIds`) resolves with it under the online id. Fields and a step added; the project file stays version 18.

17 removes "level" and "levels" from the phrases answer (version 1.6, c-13, the 1.6 plan's decision 18), and so from every answer that carries the phrases (phrases.edit, phrases.reattach): the level shown, always "phrase" since 14, and the three levels with "available" and "count", which no app read. "units" (each with its "level"), "edits", "evidence", "own", "sections" and the anchors answer as before; a request may still ask for the units of a level with "level". A change of what a call answers; the project file stays version 18.

### 16

Also within 16 (version 1.6, c-14d, #318, 0026 C1): a phrase is an object of the project, a stored anchor `ph_` (32 hex digits of the SHA-256 of "phrase|{onset}|{end}", taken when it is minted, so two devices mint one id for one phrase; it never changes). idea.keep attaches the idea to the anchor of its phrase (`Observation.Home`), resume.save a point open in phrase focus on a phrase to that phrase's, each minted when the phrase has none; ideas and resume points answer "anchor" (null when attached to none). phrases.edit carries the anchors with the player's boundaries (a moved start takes its phrase with it; two anchors on one phrase become one, the smaller id kept) and a point in phrase focus with its phrase; each step answers "step", its number in this opening, and a "restore" (Undo, Redo) that sends it back puts the anchors back as that step found them (when the edits now are those it left) or left them (when they are those it found); with no "step", one of an earlier opening or one past the latest 100, the anchors follow the edits as any other's, and the answer's "step" is null. The phrases answer says each phrase's "anchor" (null while nothing is attached to it), "anchors" (each with "id", "onset", "end" and "phrase", the number of the phrase it is, null when the notes or their analysis changed so it is no phrase any more) and "unmatched", how many are null; the new call phrases.reattach attaches all of those again, each to the phrase that holds its start, else that it overlaps most, as one step, answering the phrases with "reattached". A call and fields added; the project file stays version 18 (`phrases.anchors`, an idea's and a resume point's `anchor`).

Also within 16 (version 1.6, c-14c, #317, 0026 S3 and C4): the ids of what a piece makes (motifs, variations, ideas, resume points, bookmarks of the notes and of a PDF's pages, categories, margin notes, highlights, drafts with their places and variants, groups of steps, Notes after the first, versions compared, every original but the first PDF) come from the facade's `IdSource`: the native build's is random ("b_" and 32 hex digits, `IdSource.Random`), a facade made without one numbers them as before ("b_0002", `IdSource.Numbered`), so the answers of the fixtures do not change; drafts.bridge, drafts.carry, sources.notes.hear and sources.notes.recognise answer the ids the change made. `open` takes "onlineIds", the ids of the app's projects saved online: a project file whose id is numbered ("p_0003") and not among them opens re-keyed to the C4 id (`ProjectIds.Rekeyed`), its project answering the new id and "formerIds" (the ids it had, oldest first; left out when none), and is saved so; without "onlineIds", for a piece saved online or a file of a later version, the id stays. Fields added; the project file stays version 18 ("formerIds").

Also within 16 (version 1.6, c-14b2, «Как напечатано» step 3, the 1.6 plan's decisions 15 and 16): sources.notes.recognise puts a triplets mark in (a voice with no triplet that runs to half as long again as its bar re-timed in 3 in the time of 2 with its tuplets written, for "no" one all in triplets that runs to two thirds of it taken out of them, exact in the divisions; otherwise `MarkUnfit`, the notes as read) and counts a bar read badly as used only where the merge read it again from another engine, else `MarkUnfit` (a bar read badly that was used is not carried to the new Notes; one not used is); a page of scan.join's "replace" may be {"page", "readings", "evidence", "bad": [bars as "score" numbers them]}, merged as a page of "pages" (it then answers "merge"), each bad bar taking another engine's reading than "score" (the working notes' music) has there (a reading is the one taken before when the score with it gives the bar's notes, repaired, or repaired with the marks of "file" put in), level "check" with its variants (the reading taken before among them) and crop, marked read again in the score (`<other-direction print-object="no">Motiff Lab scan: read again from another engine</other-direction>`), which check flags `readAgain` (`CheckReason.ReadAgain`); with "file" (the piece's project file) and "original", scan.join answers the bars, check and "fingerprint" of the notes with the working notes' marks put in, as sources.notes.recognise makes them, so Check finds the crops and the engines' readings kept by that fingerprint, and each variant as those notes would be with it chosen (the page merged again with the variant's staves, the marks put in): the staves where it then differs, a variant that no longer differs left out. Fields and a reason added; the project file stays version 18.

Also within 16 (version 1.6, c-14b1, «Как напечатано» step 3, the v1.4 plan's answer 5): check.mark and check.unmark keep and take away the player's marks «что не так» on working notes read from a PDF or photos ({"kind": "meter", "key", "clef", "octave", "triplets", "hands" or "badlyRead", "bar", "last", "staff" for a clef or octave, "value": "5/4", "-3", "treble"/"bass", "8va"/"8vb", "yes"/"no"}; one of a kind at the same bar and staff takes the earlier one's place, and over bars it joins the marks of its kind, staff and value it overlaps and takes its bars from those of another value; other notes refuse them, "unavailable"), each answering as check, which answers "marks" (each as the file keeps it, with "text"); sources.notes.recognise puts the working notes' marks of the PDF read again into the new reading (hands, clef, octave, key, time signature; triplets and a bar read badly are kept and wait for c-14b2), carries them to the new Notes on their bars (a mark whose bar's music starts no bar there placed where its music lies and not put in), flags a bar whose mark the reading cannot take (`MarkUnfit`) and answers "marked": {"used", "unfit", "bars", "text"}. Calls and fields added; the project file stays version 18 (`scan.marks`). (c-14b2 puts triplets and a bar read badly in, above.)

Also within 16 (version 1.6, c-14a, #314, #315): the project file is version 18. The project answer says `"readOnly": true` for a file of a later version whose `minReader` this build meets, opened read-only, and lists in `"newerVariations"` and `"newerIdeas"` the ids of the variations and ideas kept whole for an operation this build does not know; a call that answers the file refuses such a project (`readOnly`). The settings catalogue no longer holds `phrases.granularity`, so overriding it answers `badRequest`; the answers' `level` / `levels` fields stay. `document.read` takes `"unread"` and `"read"`, the PDF pages no engine read and those of them read since, kept in the scan's notes; `scan.insert` takes `"pdfPage"`, the PDF page it puts in, which counts as read; check answers `"unreadPages"` and `"readPages"`. Fields added.

16 removes bookmark.structure (version 1.5, c-16b, #366), a Structure bookmark for each detected sentence, which no app sends since the Contents files the form's sections with bookmark.form (#347); it answers unknownCall, as the calls 14 and 10 removed do. bookmark.form answers as before, and the project file stays version 17: Structure bookmarks the removed call made open and save as any other bookmark.

Also within 16 (version 1.5, c-11b, the v1.4 plan's answer 5): sources.makeWorking moves the work kept in ticks with the piece (bookmarks, a title still by their bars retitled, phrase boundaries at every level, resume points, ideas kept with a note alone, the versions compared: their anchors, bars dismissed and Check flags) into the new working notes by bars, as the two are matched for the corrections carried (`ReadingMatch`, `FollowedWork`), a place in a bar with no match kept where the music before it went; when there was such work it answers "followed": "moved" (how many bookmarks, phrase boundaries of the level shown and ideas moved with their bars), "near" (how many were in a bar with no match), "bars" (where those are now) and "text" (the toast's line, or null); without such work the answer is as before. A field added; the project file stays version 17.

Also within 16 (version 1.5, d-11, DRAFT-11: «С ассистентом · Pro» on the card «Новый черновик»): drafts.findNewOnRecording answers, before a draft is made and nothing changed, the stretch of each of the piece's recordings that is the music of the fragment the «В черновик» sheet reads, as drafts.findOnRecording answers it for a place (the Notes the fragment is taken from left out, "recordings" how many were searched), each with "techniques", the приёмы the new draft's first place would have with it; drafts.add of a new draft takes "recording", those Notes, and makes the draft with that stretch as a variant of its first place in one change, answering "recording", the variant's id, and refuses it ("badRequest") into a draft that is there, this piece's or another file's (`DraftRecordingRequest.FindNew`, `WithStretch`, `OnRecording`); a call and a field added. The project file stays version 17.

Also within 16 (version 1.5, r-2a, #821): project.save never writes over a saved variation but the working chain's own earlier save: the chain shown goes under the id of the variation it was opened from, and a chain that went (try, select, idea.open, variation.clear, a correction) leaves its saved copy as it was, the next one saved beside it; select or idea.open on another passage, while the working motif has variations saved, leaves it in the file as it was and saves the passage beside it under a new id, which passes the motifs this build does not know by. No field changes; the project file stays version 17.

Also within 16 (version 1.5, r-2a): "bar" is optional in every corrections call, as it was in corrections.layer and .drop; without it the call answers for the piece's first bar, 0 for a pickup, so the note editor with no selection sends none rather than a bar 1 the piece may not have. A field made optional; nothing else changes.

Also within 16 (version 1.5, i-7, the v1.4 plan's answer 9): check answers "pageStarts", where each page of a scan's notes starts and ends as scan.join joined the pages read ("page", "bar", "tick", "lastBar", "lastTick"; empty for other notes), so the app marks a page of the PDF no engine read at its place; scan.insert no longer refuses a page for the bookmarks, which move on by the page's length with the resume points (a span across its place takes it in, a title still by its bars retitled; `FollowedWork.Inserted`), where it put resume points at or after the page back to the start; and a page merged from several engines' readings whose backbone is homr takes the time signature that two or more of the engines reading the printed signature agree on, counting only engines that wrote a signature at or before the bar (#647, `ScanMerge.PrintedMeters`); and scan.insert no longer refuses a page sent without "reports" into notes that keep reports: the notes then keep none (one report per page or none). Fields added; the project file stays version 17.

Also within 16 (version 1.5, r-2b, the lead's decision of 2026-11-27): scan.insert no longer refuses a page for phrases, ideas, motifs, variations or marks on the notes: they move on by the page's length by the same rule as the bookmarks (`FollowedWork.Inserted`), what names a note by its id (a motif, an idea's variation, a highlight, a finger) naming it among the notes with the page (`NoteEditLog.IdsMovedOn`); it refuses ("unavailable") while the notes are compared with a version, assembled, in a draft or keep what a later build wrote, and when a motif or an idea's variation stands across the page's place or names a note not there the same. No field changes; the project file stays version 17.

### 15

15 renames the chosen tempo of reference.explorer's answer (version 1.4, c-5): it was a second "tempo" beside the playback's "tempo" object, which a strict JSON reader refuses, and is now "chosenTempo"; the playback's "tempo" and "tempos" and "tempoChoices" stay. The iPad reads the playback's object, so it reads the answer as before.

Also within 15 (version 1.5, c-15b, #352): a circle of dna's "circles" that rests on recognised notes still to check, and is drawn as uncertain for it, answers "doubt", where they are (as its keys and chords do since 11); written only then, so the answer about imported notes is as before; a field added.

Also within 15 (version 1.5, c-11, «Как напечатано» step 2): sources.notes.recognise carries the working notes' corrections and the bars the player confirmed over to the new Notes when the working notes are a reading of the same original (`ReadingCarry`), the corrections carried in one group, and then answers "carried": "carried", "byReading" and "notCarried" (how many), "bars" (those with a correction not carried), "confirmed" (the bars confirmed again) and "text" (the toast's line, or null); without a carry the answer is as before. A field added.

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
