# Code map: music-core (`src/backend/MotiffLab.MusicCore`, `src/ipad/Core`)

Notes, time, analysis, variations and the project file: pure, deterministic, no UI, I/O, cloud or AI. The iPad reads it
through the facade; the API references it.

## Folders (`MotiffLab.MusicCore.<Folder>`; `Import` and `Assistant` have sub-namespaces)

- `Time`: `Tick`, `TickSpan` (480 a quarter), `QuarterFraction`, `CanonicalTime`, `MidiTimeBase`, `MusicXmlDivisions`;
  the tempo model (`PlaybackTempo`, `TempoSource`, `TempoBeat`).
- `Events`: `Note`, `Pitch`, `Hand`, `KeyMode`, `Provenance`, `Attribution`, the player's corrections (`NoteEditLog`);
  pitch spelling (`NoteNaming`, `NoteNames`, `PitchSpeller`, `SpelledPitch`, `TransposedSpelling`, `KeyChange`).
- `Pieces`: the piece as imported, the model every rule reads: `ImportedPiece` (`ImportedTrack`, `TrackHands`,
  `StartChoice`, `SourceFormat`), what each reader returns (`MidiImport`, `MusicXmlImport` and their records,
  `ImportReport`), `KeySignature`, the written marks (`PhraseMarks`, `SlurMark`, `AccentMark`), the bars of a recording
  (`BarGrid`, `GridMeter`; found and applied in `Import.Transcription`), `PageMap`, `Transcribed`, the player's
  `PieceMetadata` (`TitleOf`), `Bookmark` and `OriginalKind` (`OriginalKinds.FileKind`), where Notes came from
  (`NotesProvenance`, `ProvenanceSource`; `UnknownField` keeps a later build's field).
- `Import`: the readers: MIDI, MusicXML, scans (`Scan`), recordings (`Transcription`: `RecordingBars`, `GridSearch`,
  `TranscriptionOrigin`), PDFs (`Vector`), folders; the doubts they raise (`CheckFlag`, `CheckReason`, `CheckSource`).
  The MusicXML reader: `MusicXmlImporter` (file checks, part list) walks each part with `PartReader`
  (`PartFacts`), and `ScoreBuilder` puts them together with `ScoreNotes` (ties merged, the shared notes), `ScoreMarks`
  (grace notes, rests, slurs, repeats, bar lines, texts) and `MusicXmlTimeline` (tempos, bars, keys, via `TimedValues`).
- `Analysis`: facts (`MotifFact`, `FactRanking`), harmony, keys, phrases, Piece DNA, the circle of fifths.
- `Motifs`, `Variations` (the catalogue operations, `VariationChain`; their opening sentences in `OperationTemplates`:
  `.Rhythm`, `.Melody`, `.Texture`, `.ByHand`), `Experiments` (`ExperimentCatalogue`, `TryChips`).
- `Notation`, `Playback`: the notes as written and heard; `Performance`: versions compared (`PerformanceComparison`:
  `NoteMatcher`, `DifferenceClassifier`, `BarComparison`), merged and kept (`SavedPerformance`, `PerformanceSource`).
- The notation writer: `NotationGenerator` (`FromMidi`, `FromMusicXml`, `Generate`) composes `NotationPlan`
  (parts and bars: `WrittenBars`, `PartPlan`, `MeasurePlan`), `StaffVoices` (staves, clefs, voices), `TupletBeats`,
  `HeardRhythm` and `HeardOnsets` (notes heard), `ControlPlan` (the playing's marks per bar, `ControlEvents`, from
  `PlayingMarks` and `TempoMarks`), `StaffLayout` with `BarValues`, `Beaming` and `BarAccidentals` (a staff in a bar,
  `LayoutElements`), and `MeiWriter`.
- `Projects`: `Project` (`Project.Recording`: a recording only, v17; `PieceOrigin`), sources, originals
  (`PieceOriginals`, also the provenance the data shows), parts, `ProjectSync`, `PieceDetails`, `BookmarkCategory`;
  `ProjectFile` (`Read`, `Write`, `ReadEnvelope`, `Version`) over `Projects.File` (a `*Codec` per part,
  `UnknownEntities`, `Legacy`).
- `Assistant` (its tools, the facts digest), `Analytics` (`AnalyticsCatalogue`), `Settings` (`SettingsCatalogue`),
  `Accounts`, `Invitations`, `Assembly`, `Export`; `Words.cs`: the sentences in both languages.
- `Facade`: `MusicCoreFacade` (0020), the one entry point: `Open`, `Call(piece, name, json)`, `Close`, `Version`,
  `Contract`. `OpenPiece*` and `OpenDocument` (no notes: a PDF or a recording) hold an open piece;
  `Request` reads a call's JSON; `Answer*` write the answers by hand (for NativeAOT).

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
  (`FacadeAnswersContractTests`, written again with `MOTIFF_UPDATE_SNAPSHOTS=1`).

## Which namespace uses which

No two namespaces use each other, directly or round a loop, by `using`, code or `cref`: a comment names a type of a
higher namespace in plain text (`<c>Project.PartOf</c>`). From the bottom: `Time` uses nothing; `Events` only
`Time` and the root (`Words`); `Motifs` only `Time` and `Events`; `Pieces` those and the root; `Import` (with its
sub-namespaces) and `Playback` add nothing above `Pieces`; then the rules (`Analysis`, `Notation`, `Performance`,
`Assembly`, `Variations`, ...), `Projects`, `Assistant` and `Facade`. The `dependencies` check keeps the layers
(`CORE_LAYERS`, listed in `governance/quality/README.md`): a namespace uses only the layers below its own.

## Rules not to break

- No other project, web, cloud or AI package (the `dependencies` check); no floating point for time.
- Every note keeps its provenance and confidence; what is inferred says so. Variations are pure: same input and seed,
  same notes and diff.
- A new call goes into `facade-calls.json`; a change to the project file or to an answer the app reads is Andrei's
  decision 3 (`ProjectFile.Version`, `Contract`).
- Analytics events are built only from `AnalyticsCatalogue`; sentences come in both languages from `Words`.
