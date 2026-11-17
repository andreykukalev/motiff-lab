# Code map: music-core (`src/backend/MotiffLab.MusicCore`, `src/ipad/Core`)

Notes, time, analysis, variations and the project file: pure, deterministic, no UI, I/O, cloud or AI. The iPad reads it
only through the facade; the API (`src/backend/MotiffLab.Api`) references it.

## Folders (`MotiffLab.MusicCore.<Folder>`; `Import` and `Assistant` have sub-namespaces)

- `Time`: `Tick`, `TickSpan` (480 a quarter), `QuarterFraction`, `CanonicalTime`, `MidiTimeBase`, `MusicXmlDivisions`.
- `Events`: `Note`, `Pitch`, `Hand`, `Provenance`, `Attribution`, the player's corrections (`NoteEditLog`).
- `Import`: MIDI, MusicXML, scans (`Scan`), recordings (`Transcription`), PDFs (`Vector`), folders.
- `Analysis`: facts (`MotifFact`, `FactRanking`), harmony, keys, phrases, Piece DNA, the circle of fifths.
- `Motifs`, `Variations` (the catalogue operations, `VariationChain`), `Experiments` (`ExperimentCatalogue`, `TryChips`).
- `Notation`, `Playback`: the notes as written and heard; `Performance`: versions compared and merged.
- `Projects`: `Project`, the project file (`ProjectFile`, `Version`), sources, originals, parts, sync (`ProjectSync`).
- `Assistant` (its tools, the facts digest), `Analytics` (`AnalyticsCatalogue`), `Settings` (`SettingsCatalogue`),
  `Accounts`, `Invitations`, `Assembly`, `Export`; `Words.cs`: the sentences in both languages.
- `Facade`: `MusicCoreFacade` (0020), the one entry point: `Open`, `Call(piece, name, json)`, `Close`, `Version`,
  `Contract`. `OpenPiece*` and `OpenDocument` (a PDF piece) hold an open piece; `Request` reads a call's JSON;
  `Answer*` write the answers by hand (no reflection, for NativeAOT).

## The native build (`src/ipad/Core`)

`Exports.cs`: the C functions `ml_version`, `ml_open`, `ml_call`, `ml_close`, `ml_free` (`include/motifflab_core.h`)
over one `MusicCoreFacade`; no exception crosses them. `build.sh` packages the xcframework (the `iPad` workflow, on a
Mac); `check.py` calls a published library. The app's side: `Platform/MusicCore.swift`, `PieceCore.swift`.

## Tests

- `tests/backend/Unit/MotiffLab.MusicCore.Tests/<Folder>`, one folder per source folder; `Facade/` for calls,
  `Golden/` for saved project files of every version (`ProjectFileGoldenTests`).
- The wire contract: `tests/contracts/facade-calls.json` (`FacadeCallsContractTests`, and the Swift scan
  `tests/shared/facade-calls-contract.test.mjs`) and the answer fixtures `tests/contracts/facade-answers`
  (`FacadeAnswersContractTests`, written again with `MOTIFF_UPDATE_SNAPSHOTS=1`).

## Rules not to break

- No other project, web, cloud or AI package (the `dependencies` check); no floating point for time.
- Every note keeps its provenance and confidence; what is inferred says so. Variations are pure: same input and seed,
  same notes and diff.
- A new call goes into `facade-calls.json`; a change to the project file or to an answer the app reads is Andrei's
  decision 3 (`ProjectFile.Version`, `Contract`).
- Analytics events are built only from `AnalyticsCatalogue`; sentences come in both languages from `Words`.
