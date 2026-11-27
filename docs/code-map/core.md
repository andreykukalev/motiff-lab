# Code map: music-core (`src/backend/MotiffLab.MusicCore`, `src/ipad/Core`)

Notes, time, analysis, variations and the project file: pure, deterministic, no UI, I/O, cloud or AI. The iPad reads it
through the facade; the API references it.

## Folders (`MotiffLab.MusicCore.<Folder>`, some with sub-namespaces)

- `Time`: `Tick`, `TickSpan` (480 a quarter), `QuarterFraction`, `CanonicalTime`, `MidiTimeBase`, `MusicXmlDivisions`, `NoteValue` (named values, dots);
  the tempo model (`PlaybackTempo`, `TempoSource`, `TempoBeat`).
- `Events`: `Note`, `Pitch`, `Hand`, `KeyMode`, `Provenance`, `Attribution`, the player's corrections (`NoteEditLog`);
  pitch spelling (`NoteNaming`, `NoteNames`, `PitchSpeller`, `SpelledPitch`, `TransposedSpelling`, `KeyChange`).
- `Pieces`: the piece as imported: `ImportedPiece` (`ImportedTrack`, `TrackHands`,
  `StartChoice`, `SourceFormat`), what each reader returns (`MidiImport`, `MusicXmlImport` and their records,
  `ImportReport`), `KeySignature`, the written marks (`PhraseMarks`, `SlurMark`, `AccentMark`), the bars of a recording
  (`BarGrid`, `GridMeter`; found and applied in `Import.Transcription`), `PageMap`, `Transcribed`, the player's
  `PieceMetadata` (`TitleOf`), `Bookmark` and `OriginalKind` (`OriginalKinds.FileKind`), where Notes came from
  (`NotesProvenance`, `ProvenanceSource`; `UnknownField` keeps a later build's field).
- `Import`: the readers: MIDI, MusicXML, scans (`Scan`; `ScanMerge`: engines' readings per staff, bar levels, settled by the page: `ScanEvidence`, `ScanAssembly`, `ScanMerge.Staffing` reads a reading's clefs; `ScanMerge.Variants`: a Check bar's other readings and crop box, v1.3 i-2, put in by `corrections.variant`; `ScanMerge.Meter`: homr's time signatures over the agreement of the printed-meter engines that wrote one, #647), a page no engine read put in later by `ScanJoin.Insert` (v1.3 i-1b, `ScanPageInsert.Into`, `Project.CanInsertPage`, the bookmarks and resume points (v1.5 i-7), the phrases, ideas, motifs and marks (r-2b, notes by `NoteEditLog.IdsMovedOn`) moved on by its length by `FollowedWork.Inserted`; its corrections carried by `NoteEditLog.CarriedOver`, `NoteEditLog.Insert.cs`; facade `scan.insert`, `ScanRequest.Insert.cs`), `StaffPitch` (steps, clefs, key alterations, MIDI of a written note: MusicXML, Vector, Scan), recordings (`Transcription`: `RecordingBars`, `GridSearch`,
  `TranscriptionOrigin`), PDFs (`Vector`), folders; the player's marks «что не так» (`Scan/ReadingMark`, kept in `ScanOrigin.Marks`) put into the next reading of the pages by `ReadingMarks.Apply` (hands, clef, octave, key, meter; «Как напечатано» step 3, c-14b1; triplets in `ReadingMarks.Triplets`, c-14b2), a bar read badly read again from another engine by `ScanMerge.ReadAgain` (`ScanBefore`: the page as read before and its bad bars, scan.join's "replace" with "bad"; marked `MusicXmlImport.ReadAgainMark`, flagged `ReadAgain`), and scan.join's fingerprint and variants taken after the marks with "file" (`ScanRequest.Marks.cs`: `SourcesSession.Marked` on a project and its piece, a variant written into its page by `ScanMergedPage.WithVariant`); the doubts they raise (`CheckFlag`, `CheckReason`, `CheckSource`).
  The MusicXML reader: `MusicXmlImporter` (file checks, part list) walks each part with `PartReader` (`PartFacts`;
  `.Writing`: how each note is written, `WrittenNotes`, `Spaces`);
  `ScoreBuilder` joins them with `ScoreNotes` (ties), `ScoreMarks` (rests, slurs, texts…) and `MusicXmlTimeline`.
- `Analysis`: facts (`MotifFact`, `FactRanking`), harmony, keys, Piece DNA (its form: `FormAnalyzer`, the returns of
  the opening or the sentences, #365), the circle of fifths; `.Phrasing`: the phrase levels, one edit algebra
  (`PhraseEdits`, `LevelEditor`; D13); the marks' edits in `PhraseMap` (`Move`, `PutBack`, `Replaced`, `OwnCount`) and
  `PhraseLevels` (`NoteAt`, `NoteFrom`, `WhyHere`).
- `Motifs`, `Variations` (the catalogue operations, `VariationChain`; their opening sentences in `OperationTemplates`:
  `.Rhythm`, `.Melody`, `.Texture`, `.ByHand`), `Experiments` (`ExperimentCatalogue`, `TryChips`).
- `Notation`, `Playback`: the notes as written and heard (the roll's data `PianoRollData`, with `VoiceNames`: upper and lower
  voice where the file says which, v1.5 #330); `Performance`: versions compared (`PerformanceComparison`:
  `NoteMatcher`, `DifferenceClassifier`, `BarComparison`) and kept (`SavedPerformance`, `PerformanceSource`); the player's
  corrections carried to a new reading of the same pages (`ReadingCarry`, «Как напечатано» step 2: the two readings matched
  once by `ReadingMatch`, onsets by `PerformanceAlignment.AlignOnsets`, bars by their music; wired by
  `SourcesSession.CarriedOver` on sources.notes.recognise).
- The notation writer: `NotationGenerator` (`FromMidi`, `FromMusicXml`, `Generate`) composes `NotationPlan`
  (parts and bars: `WrittenBars`, `PartPlan`, `MeasurePlan`), `StaffVoices` (staves, clefs, voices), `TupletBeats`,
  `HeardRhythm` and `HeardOnsets` (notes heard), `ControlPlan` (`ControlEvents` from `PlayingMarks`,
  `TempoMarks`), `StaffLayout` with `BarValues`, `Beaming` and `BarAccidentals` (a staff in a bar,
  `LayoutElements`), and `MeiWriter`. «Как напечатано»: `PrintedNotation` (the source record by note id) and
  `PrintedBars` (`PrintedCell`, `PrintedTuplets`) pick the bars of a staff written as printed, `PrintedLayout` lays them out;
  `StaffPlan.TakePiece` numbers the pieces of ids in both ways.
- `Projects`: `Project`, a file per part (`Project.Originals`, `.Bookmarks`, `.ResumePoints`, `.Performances`,
  `.Assembly`, `.Check`, `.Settings`, `.Observations`, `.Sources`, `.Linked`, `.Recording`: a recording only, `.Drafts`),
  records apart (`ProjectSummary`, `SavedMotif`, …); `OriginalOperations`, `NotesOperations` (`MakeWorking`: the work in
  ticks follows the music by bars, `FollowedWork` over `ReadingMatch`, v1.5 c-11b); originals
  (`PieceOriginals`, also the provenance the data shows), parts, `ProjectSync`, `PieceDetails`, `PieceOrigin`;
  `ProjectFile` (`Read`, `Write`, `Save`: refuses a file of a later version, `NewerFile` on `Project.Newer`, opened
  read-only since version 18) over `Projects.File` (a `*Codec` per part, `UnknownEntities` with `Unknowns`, which also
  keeps the unknown fields of workspace entities, `Legacy`); `ScanOrigin` keeps the PDF pages no engine read (`Unread`,
  `ReadPages`); `Projects.Drafts`: `Draft*`, `PlaceResult`,
  `PlaceTechniques`, `VariantRecording`, `DraftSeams` (with the register), `DraftBridges` (bridge candidates at a
  seam), `DraftAnchors` (anchors the player confirms), `OnRecording` (the stretch of a recording that is a place's
  music, «Найти на записи»), `PlaceLength` (a place's base cut again over other bars), `TechniqueCarry` (a приём carried
  into another place as a variant); a place's order is `Draft.WithPlaceAt` (`Draft.Reshaped` drops what a seam lost).
- `Assistant` (its tools; the digest `FactsDigest`, `.Piece`, `.Draft`; the template's `NearestFacts`; `.Drafts`, in a
  draft: `DraftScope`, `DraftTools`, `DraftProposal`), `Analytics`
  (`AnalyticsCatalogue`), `Settings` (`SettingsCatalogue`), `Accounts`, `Invitations`, `Assembly`, `Export`;
  `Words.cs`: the sentences in both languages; `JsonWrite.cs`: the JSON primitives of the answers and the file (D16).
- `Facade`: `MusicCoreFacade` (0020), the one entry point: `Open`, `Call(piece, name, json)`, `Close`, `Version`,
  `Contract`. `Call` dispatches by `CallRegistry` alone: each call's name, handler (`FacadeCall<T>`) and `CallFlags`,
  gathered from the `…Calls` lists each feature keeps beside its handlers (the piece's in `OpenPiece.Calls`, a nested
  partial class that reaches its handlers, its lists in `OpenPiece.Dispatch` and, for the selection, the corrections, the sources, the performances and the marks, `OpenPiece.Selection`, `OpenPiece.Corrections`, `OpenPiece.Sources`, `OpenPiece.Performance` and `OpenPiece.Marks`; `SourceRequest.Calls` on both kinds of piece through `SourceOwner`, in
  `SourceRequest.cs`); the receivers `PieceCall` and `DraftCall` are in `FacadeCall.cs`. One name is read outside the
  registry: `drafts.add` with `file` (`CallOnDrafts`), answered before a draft's owner is looked for. `OpenPiece*` and `OpenDocument` (no notes: a PDF or a recording) hold an open piece;
  the typed sessions (W1.7) in `Facade/Sessions`: `SelectionSession` (the selection, what was understood, the Try chip,
  the chain, the working motif; a save keeps the working motif's other variations, another passage is saved beside it,
  #821) owns its state, reads the piece through `SessionPiece` (`OpenPiece.State`), takes typed
  values and returns typed results, no request, JSON or clock; its JSON adapter is `OpenPiece.Selection` (`Calls.Selection`);
  `CorrectionsSession` (the note editor's draft, the draft or variant accepted last, the corrections' layer; each
  correction answers the edit log, which the open piece keeps) answers `BarCorrections` (`Answer.Corrections`), its
  adapter `OpenPiece.Corrections` (`Calls.Corrections`, every call's bar read before anything changes);
  `SourcesSession` (the drafts kept for drafts.undo; the PDF kept in the piece, the notes read from it, relink, the
  iPad's mark, «Распознать ещё раз» with the corrections carried over (`CarriedOver`), each answering the project it makes, the time taken as `now`) answers `KeptFile`,
  `ReadScore`, `DocumentVersion`; the working notes' marks placed on the new reading and carried (`Marked`, `WithMarks`, `MarksCarried`); its adapter `OpenPiece.Sources` (`Calls.Sources`, reads «now»; `Changed` for the drafts);
  `PerformancesSession` (no state: the versions added, compared, their layer, bars taken and restored, dismissed, renamed,
  each against the printed notes `OpenPiece.Printed` and answering the project it makes; `OpenPiece.RefreshNotes` reads the
  notes again) answers the comparison (`Answer.Performance`) and `AddedVersion`; its adapter `OpenPiece.Performance` (`Calls.Performances`);
  `MarksSession` (no state: ink, margin notes, highlights, fingering, the bookmarks and their categories, the Structure
  bookmarks, a bookmark's default title by its bars, each answering the project it makes, the time taken as `now`); its
  adapter `OpenPiece.Marks` (`Calls.Marks`; the resume points, ideas, settings and Check stay in `OpenPiece.Project`);
  the time of a call is read once, `Request.Now` («now», else the clock) and `Request.SentNow` (project.save), by the
  adapters only (D15); the one exception is sync.online's «now», read as its other times are (`SyncRequest.Time`);
  check.mark and check.unmark in `ReadingMarkRequest` (with check's "marks");
  `Request` reads a call's JSON; `Answer*` write the answers by hand (NativeAOT), a file per feature (`Answer.cs` the shared
  shapes; `.Piece`, `.Understanding`, `.KeyCircle`, `.Dna`, `.Variation`, `.Notation`, `.Playback` (play; the roll in `RollAnswer`), …), a
  stroke, filter or key signature through `ProjectFile`; phrases: `OpenPiece.Phrases`,
  `Answer.Phrases` (the marks); drafts:
  `Draft*Request` (`DraftRecordingRequest`: «Найти на записи», and since v1.5 a new draft's passage, `FindNew`, `WithStretch`; `DraftLengthRequest`; `DraftCarryRequest`), `MusicCoreFacade.Drafts` (v1.2 collage: another piece's `file`, `DraftRequest.AddTo`; assistant:
  `DraftAssistantRequest`), `ShownView`.

## The native build (`src/ipad/Core`)

`Exports.cs`: the C functions `ml_version`, `ml_open`, `ml_call`, `ml_close`, `ml_free` (`include/motifflab_core.h`)
over one `MusicCoreFacade`; no exception crosses them. `build.sh` packages the xcframework (the `iPad` workflow, on a
Mac); `check.py` calls a published library. The app's side: `Platform/MusicCore.swift`, `PieceCore.swift`.

## Tests

- `tests/backend/Unit/MotiffLab.MusicCore.Tests/<Folder>`, one folder per source folder; `Facade/` for calls,
  `Golden/` for saved project files of every version (`ProjectFileGoldenTests`, a later version in its `.Newer`), `<Folder>/Golden/*.txt` for what real
  pieces give (`TextGoldens`); all written again with `MOTIFF_UPDATE_SNAPSHOTS=1`.
- The wire contract: `tests/contracts/facade-calls.json` and the calls of `docs/facade-contract.md`, both checked
  against the registry and the facade's answers (`FacadeCallsContractTests`; the Swift scan
  `tests/shared/facade-calls-contract.test.mjs`) and the answer fixtures `tests/contracts/facade-answers`
  (`FacadeAnswersContractTests`, written again as above).

## Which namespace uses which

No two namespaces use each other, even round a loop, by `using`, code or `cref` (a comment names a higher one's
type as text). Bottom up: `Time`, `Events`, `Motifs`, `Pieces`, `Import`, `Playback`, rules (`Analysis`,
`Notation`…), `Projects`, `Assistant`, `Facade`; kept by `dependencies` (`CORE_LAYERS`), which counts a sub-namespace
as its parent, missing that `Analysis` and `.Phrasing` use each other (`MotifFact`, `ReadKey`; `MotifAnalyzer`,
`FormAnalyzer`, `PieceDna`).
The root's classes (`Words`, `JsonWrite`) are called by their name, never through `using static`, which the check
reads as a namespace.

## What is public

Only what another project uses, and the types its public signatures name; the rest is `internal`, seen by
`MotiffLab.MusicCore.Tests` (`InternalsVisibleTo`). The API uses `Accounts`, `Assistant.Conversation`, `ProjectFile`
(`ReadEnvelope`), `ProjectEnvelope`, `ProjectSummary` and `Words`; its tests also `Project.Create`, `ProjectFile.Write`
and the assistant run in-process (`AssistantPiece`, `AssistantContext`, `TurnRunner`, `FactsDigest`, `PieceImporter`);
`src/ipad/Core` and the acceptance tests `MusicCoreFacade`. W1.12 made it so (done, v1.5), a layer at a time, top
first: `Facade`, `Assistant`, `Analytics`, `Projects` (c-12w), then `Experiments`, `Assembly`, `Notation`, `Variations`,
`Invitations`, `Analysis`, `Performance` (c-12w2: public stay the analyses, facts, phrase levels, variations and saved
performances the project and the assistant name), then `Import`, `Playback`, `Pieces`, `Motifs`, `Export`, `Events`,
`Time`, `Settings` (c-12w3: public stay what the API's tests use, `PieceImporter` with its result, `ImportedPiece`,
`Tick`, `TickSpan`, and the notes, motifs, imported records, marks, edit log and provenance the layers above name;
`Playback`, `Export`, `Settings` are all internal); `Accounts` and the root stay public. A new type is `internal` unless one of these needs it. A theory
test takes an internal enum by its name (`nameof`, parsed in the test), since xUnit runs public methods only.

## Rules not to break

- No other project, web, cloud or AI package; no floating point for time.
- Every note keeps its provenance and confidence; what is inferred says so. Variations are pure: same input and seed,
  same notes and diff. A phrase edit keeps the player's boundaries as saved and never moves the selection or a bookmark.
- A session's state is written only by the session's own methods; its adapter reads the request and writes the answer.
- A call is a registry entry in its feature's list, never a name compared elsewhere; a new one goes into
  `facade-calls.json` and `docs/facade-contract.md` (written again as above, its history line by hand); a change to the project file or to an answer the app reads is Andrei's
  decision 3 (`ProjectFile.Version`, `Contract`).
- Analytics events are built only from `AnalyticsCatalogue`; sentences come in both languages from `Words`.
