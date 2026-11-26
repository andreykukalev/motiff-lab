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
- `Import`: the readers: MIDI, MusicXML, scans (`Scan`; `ScanMerge`: engines' readings per staff, bar levels, settled by the page: `ScanEvidence`, `ScanAssembly`, `ScanMerge.Staffing` reads a reading's clefs; `ScanMerge.Variants`: a Check bar's other readings and crop box, v1.3 i-2, put in by `corrections.variant`), a page no engine read put in later by `ScanJoin.Insert` (v1.3 i-1b, `ScanPageInsert.Into`, `Project.CanInsertPage`, its corrections carried by `NoteEditLog.CarriedOver`, `NoteEditLog.Insert.cs`; facade `scan.insert`, `ScanRequest.Insert.cs`), `StaffPitch` (steps, clefs, key alterations, MIDI of a written note: MusicXML, Vector, Scan), recordings (`Transcription`: `RecordingBars`, `GridSearch`,
  `TranscriptionOrigin`), PDFs (`Vector`), folders; the doubts they raise (`CheckFlag`, `CheckReason`, `CheckSource`).
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
  corrections carried to a new reading of the same pages (`ReadingCarry`, «Как напечатано» step 2: onsets matched by
  `PerformanceAlignment.AlignOnsets`; wired by `SourcesSession.CarriedOver` on sources.notes.recognise).
- The notation writer: `NotationGenerator` (`FromMidi`, `FromMusicXml`, `Generate`) composes `NotationPlan`
  (parts and bars: `WrittenBars`, `PartPlan`, `MeasurePlan`), `StaffVoices` (staves, clefs, voices), `TupletBeats`,
  `HeardRhythm` and `HeardOnsets` (notes heard), `ControlPlan` (`ControlEvents` from `PlayingMarks`,
  `TempoMarks`), `StaffLayout` with `BarValues`, `Beaming` and `BarAccidentals` (a staff in a bar,
  `LayoutElements`), and `MeiWriter`. «Как напечатано»: `PrintedNotation` (the source record by note id) and
  `PrintedBars` (`PrintedCell`, `PrintedTuplets`) pick the bars of a staff written as printed, `PrintedLayout` lays them out;
  `StaffPlan.TakePiece` numbers the pieces of ids in both ways.
- `Projects`: `Project`, a file per part (`Project.Originals`, `.Bookmarks`, `.ResumePoints`, `.Performances`,
  `.Assembly`, `.Check`, `.Settings`, `.Observations`, `.Sources`, `.Linked`, `.Recording`: a recording only, `.Drafts`),
  records apart (`ProjectSummary`, `SavedMotif`, …); `OriginalOperations`, `NotesOperations`; originals
  (`PieceOriginals`, also the provenance the data shows), parts, `ProjectSync`, `PieceDetails`, `PieceOrigin`;
  `ProjectFile` (`Read`, `Write`, …) over `Projects.File` (a `*Codec` per part,
  `UnknownEntities`, `Legacy`); `Projects.Drafts`: `Draft*`, `PlaceResult`,
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
  the chain, the working motif) owns its state, reads the piece through `SessionPiece` (`OpenPiece.State`), takes typed
  values and returns typed results, no request, JSON or clock; its JSON adapter is `OpenPiece.Selection` (`Calls.Selection`);
  `CorrectionsSession` (the note editor's draft, the draft or variant accepted last, the corrections' layer; each
  correction answers the edit log, which the open piece keeps) answers `BarCorrections` (`Answer.Corrections`), its
  adapter `OpenPiece.Corrections` (`Calls.Corrections`, every call's bar read before anything changes);
  `SourcesSession` (the drafts kept for drafts.undo; the PDF kept in the piece, the notes read from it, relink, the
  iPad's mark, «Распознать ещё раз» with the corrections carried over (`CarriedOver`), each answering the project it makes, the time taken as `now`) answers `KeptFile`,
  `ReadScore`, `DocumentVersion`; its adapter `OpenPiece.Sources` (`Calls.Sources`, reads «now»; `Changed` for the drafts);
  `PerformancesSession` (no state: the versions added, compared, their layer, bars taken and restored, dismissed, renamed,
  each against the printed notes `OpenPiece.Printed` and answering the project it makes; `OpenPiece.RefreshNotes` reads the
  notes again) answers the comparison (`Answer.Performance`) and `AddedVersion`; its adapter `OpenPiece.Performance` (`Calls.Performances`);
  `MarksSession` (no state: ink, margin notes, highlights, fingering, the bookmarks and their categories, the Structure
  bookmarks, a bookmark's default title by its bars, each answering the project it makes, the time taken as `now`); its
  adapter `OpenPiece.Marks` (`Calls.Marks`; the resume points, ideas, settings and Check stay in `OpenPiece.Project`);
  the time of a call is read once, `Request.Now` («now», else the clock) and `Request.SentNow` (project.save), by the
  adapters only (D15); the one exception is sync.online's «now», read as its other times are (`SyncRequest.Time`);
  `Request` reads a call's JSON; `Answer*` write the answers by hand (NativeAOT), a file per feature (`Answer.cs` the shared
  shapes; `.Piece`, `.Understanding`, `.KeyCircle`, `.Dna`, `.Variation`, `.Notation`, `.Playback` (play; the roll in `RollAnswer`), …), a
  stroke, filter or key signature through `ProjectFile`; phrases: `OpenPiece.Phrases`,
  `Answer.Phrases` (the marks); drafts:
  `Draft*Request` (`DraftRecordingRequest`: «Найти на записи»; `DraftLengthRequest`; `DraftCarryRequest`), `MusicCoreFacade.Drafts` (v1.2 collage: another piece's `file`, `DraftRequest.AddTo`; assistant:
  `DraftAssistantRequest`), `ShownView`.

## The native build (`src/ipad/Core`)

`Exports.cs`: the C functions `ml_version`, `ml_open`, `ml_call`, `ml_close`, `ml_free` (`include/motifflab_core.h`)
over one `MusicCoreFacade`; no exception crosses them. `build.sh` packages the xcframework (the `iPad` workflow, on a
Mac); `check.py` calls a published library. The app's side: `Platform/MusicCore.swift`, `PieceCore.swift`.

## Tests

- `tests/backend/Unit/MotiffLab.MusicCore.Tests/<Folder>`, one folder per source folder; `Facade/` for calls,
  `Golden/` for saved project files of every version (`ProjectFileGoldenTests`), `<Folder>/Golden/*.txt` for what real
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
`src/ipad/Core` and the acceptance tests `MusicCoreFacade`. W1.12 makes it so a layer at a time, top first: done for
`Facade`, `Assistant`, `Analytics`, `Projects` (v1.5 c-12w), then `Experiments`, `Assembly`, `Notation`, `Variations`,
`Invitations`, `Analysis`, `Performance` (c-12w2: public stay the analyses, facts, phrase levels, variations and saved
performances the project and the assistant name); a new type there is `internal` unless one of these needs it. A theory
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
