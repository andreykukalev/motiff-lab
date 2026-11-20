# Code map: music-core (`src/backend/MotiffLab.MusicCore`, `src/ipad/Core`)

Notes, time, analysis, variations and the project file: pure, deterministic, no UI, I/O, cloud or AI. The iPad reads it
through the facade; the API references it.

## Folders (`MotiffLab.MusicCore.<Folder>`, some with sub-namespaces)

- `Time`: `Tick`, `TickSpan` (480 a quarter), `QuarterFraction`, `CanonicalTime`, `MidiTimeBase`, `MusicXmlDivisions`;
  the tempo model (`PlaybackTempo`, `TempoSource`, `TempoBeat`).
- `Events`: `Note`, `Pitch`, `Hand`, `KeyMode`, `Provenance`, `Attribution`, the player's corrections (`NoteEditLog`);
  pitch spelling (`NoteNaming`, `NoteNames`, `PitchSpeller`, `SpelledPitch`, `TransposedSpelling`, `KeyChange`).
- `Pieces`: the piece as imported: `ImportedPiece` (`ImportedTrack`, `TrackHands`,
  `StartChoice`, `SourceFormat`), what each reader returns (`MidiImport`, `MusicXmlImport` and their records,
  `ImportReport`), `KeySignature`, the written marks (`PhraseMarks`, `SlurMark`, `AccentMark`), the bars of a recording
  (`BarGrid`, `GridMeter`; found and applied in `Import.Transcription`), `PageMap`, `Transcribed`, the player's
  `PieceMetadata` (`TitleOf`), `Bookmark` and `OriginalKind` (`OriginalKinds.FileKind`), where Notes came from
  (`NotesProvenance`, `ProvenanceSource`; `UnknownField` keeps a later build's field).
- `Import`: the readers: MIDI, MusicXML, scans (`Scan`), recordings (`Transcription`: `RecordingBars`, `GridSearch`,
  `TranscriptionOrigin`), PDFs (`Vector`), folders; the doubts they raise (`CheckFlag`, `CheckReason`, `CheckSource`).
  The MusicXML reader: `MusicXmlImporter` (file checks, part list) walks each part with `PartReader` (`PartFacts`);
  `ScoreBuilder` joins them with `ScoreNotes` (ties), `ScoreMarks` (rests, slurs, texts…) and `MusicXmlTimeline`.
- `Analysis`: facts (`MotifFact`, `FactRanking`), harmony, keys, Piece DNA, the circle of fifths; `.Phrasing`: the
  phrase levels, one edit algebra (`PhraseEdits`, `LevelEditor`; D13).
- `Motifs`, `Variations` (the catalogue operations, `VariationChain`; their opening sentences in `OperationTemplates`:
  `.Rhythm`, `.Melody`, `.Texture`, `.ByHand`), `Experiments` (`ExperimentCatalogue`, `TryChips`).
- `Notation`, `Playback`: the notes as written and heard; `Performance`: versions compared (`PerformanceComparison`:
  `NoteMatcher`, `DifferenceClassifier`, `BarComparison`) and kept (`SavedPerformance`, `PerformanceSource`).
- The notation writer: `NotationGenerator` (`FromMidi`, `FromMusicXml`, `Generate`) composes `NotationPlan`
  (parts and bars: `WrittenBars`, `PartPlan`, `MeasurePlan`), `StaffVoices` (staves, clefs, voices), `TupletBeats`,
  `HeardRhythm` and `HeardOnsets` (notes heard), `ControlPlan` (`ControlEvents` from `PlayingMarks`,
  `TempoMarks`), `StaffLayout` with `BarValues`, `Beaming` and `BarAccidentals` (a staff in a bar,
  `LayoutElements`), and `MeiWriter`.
- `Projects`: `Project`, a file per part (`Project.Originals`, `.Bookmarks`, `.ResumePoints`, `.Performances`,
  `.Assembly`, `.Check`, `.Settings`, `.Observations`, `.Sources`, `.Linked`, `.Recording`: a recording only, `.Drafts`),
  records apart (`ProjectSummary`, `SavedMotif`, …); `OriginalOperations`, `NotesOperations`; originals
  (`PieceOriginals`, also the provenance the data shows), parts, `ProjectSync`, `PieceDetails`, `PieceOrigin`;
  `ProjectFile` (`Read`, `Write`, …) over `Projects.File` (a `*Codec` per part,
  `UnknownEntities`, `Legacy`); `Projects.Drafts`: `Draft*`, `PlaceResult`,
  `PlaceTechniques`, `VariantRecording`, `DraftSeams`.
- `Assistant` (its tools, the facts digest), `Analytics` (`AnalyticsCatalogue`), `Settings` (`SettingsCatalogue`),
  `Accounts`, `Invitations`, `Assembly`, `Export`; `Words.cs`: the sentences in both languages.
- `Facade`: `MusicCoreFacade` (0020), the one entry point: `Open`, `Call(piece, name, json)`, `Close`, `Version`,
  `Contract`. `OpenPiece*` and `OpenDocument` (no notes: a PDF or a recording) hold an open piece;
  `Request` reads a call's JSON; `Answer*` write the answers by hand (NativeAOT); drafts:
  `Draft*Request`, `MusicCoreFacade.Drafts`, `ShownView`.

## The native build (`src/ipad/Core`)

`Exports.cs`: the C functions `ml_version`, `ml_open`, `ml_call`, `ml_close`, `ml_free` (`include/motifflab_core.h`)
over one `MusicCoreFacade`; no exception crosses them. `build.sh` packages the xcframework (the `iPad` workflow, on a
Mac); `check.py` calls a published library. The app's side: `Platform/MusicCore.swift`, `PieceCore.swift`.

## Tests

- `tests/backend/Unit/MotiffLab.MusicCore.Tests/<Folder>`, one folder per source folder; `Facade/` for calls,
  `Golden/` for saved project files of every version (`ProjectFileGoldenTests`), `<Folder>/Golden/*.txt` for what real
  pieces give (`TextGoldens`); all written again with `MOTIFF_UPDATE_SNAPSHOTS=1`.
- The wire contract: `tests/contracts/facade-calls.json` (`FacadeCallsContractTests`, and the Swift scan
  `tests/shared/facade-calls-contract.test.mjs`) and the answer fixtures `tests/contracts/facade-answers`
  (`FacadeAnswersContractTests`, written again as above).

## Which namespace uses which

No two namespaces use each other, even round a loop, by `using`, code or `cref` (a comment names a higher one's
type as text). Bottom up: `Time`, `Events`, `Motifs`, `Pieces`, `Import`, `Playback`, rules (`Analysis`,
`Notation`…), `Projects`, `Assistant`, `Facade`; kept by `dependencies` (`CORE_LAYERS`), which counts a sub-namespace
as its parent, missing that `Analysis` and `.Phrasing` use each other (`MotifFact`, `ReadKey`; `MotifAnalyzer`,
`FormAnalyzer`, `PieceDna`).

## Rules not to break

- No other project, web, cloud or AI package; no floating point for time.
- Every note keeps its provenance and confidence; what is inferred says so. Variations are pure: same input and seed,
  same notes and diff.
- A new call goes into `facade-calls.json`; a change to the project file or to an answer the app reads is Andrei's
  decision 3 (`ProjectFile.Version`, `Contract`).
- Analytics events are built only from `AnalyticsCatalogue`; sentences come in both languages from `Words`.
