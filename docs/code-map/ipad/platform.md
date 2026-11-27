# Code map: iPad `Platform/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Platform`. What every folder may use; it uses none.

## Files

- Ports. `MusicCore.swift`: `MusicCore.call(piece:, name, json)`, `run` (the core's thread, one call after another),
  `facadeRequest`, `facadeObject`; `FacadeAnswer.decode` (B5) reads an answer once into its type (a missing field is
  logged, asserts in Debug, shows `.problem`), `FacadeAnswer.message` music-core's words. `PieceCore.swift`: the one way
  to a call by name (0031 step 1b), the typed calls answering `PieceCore.Answer`; `PieceCoreCalls.swift`: the calls not
  typed yet (`PieceCore.Call`, `PieceCore.NoPiece` by raw value); a helper takes a case, never a name. `ScorePage.swift`
  (`PageCommand`, `PageEvent`, `pageScript`) and `RollPage.swift` (`RollCommand`, `RollPage`, `RollEvent` with the roll's
  values and `NoteEditorState`; a message the page never sends goes to `RollEvent.broken`, a hook apart from
  `FacadeAnswer.broken`): the one port to each page (0031 steps 2, 12).
- Typed answers, naming no feature type: `PieceAnswers.swift` (`SelectionAnswer`, `PhrasesAnswer`, `PlaybackAnswer`,
  `RollAnswer`, `SourceLayersAnswer`, `CorrectionsAnswer`, `EditsLayerAnswer`, `PerformanceAnswer`, `PerformancesAnswer`,
  `VersionHead`), `DraftAnswers.swift` (the drafts' answers), `FormSection.swift` (Piece DNA's lane, the Contents, the
  letters), `CheckPageEntry.swift` (a scan's page as `check` counts it, v1.6 i-8), `VersionEntry.swift` (the version
  Expand is handed, v1.6 i-10b).
- The piece. `OpenPiece.swift`: a piece opened in music-core, engraved, a PDF piece (`isDocument`), a recording only
  (`recording`), a draft shown (`draft`); `readablePdf`, the one test of a PDF this iPad can show. `ProjectFiles.swift`:
  `ProjectFileCache`, the projects' folder, `save`, `saveNew`, `newURL(for:)` (the one naming of a new project file, by
  the work's title, `fileName(for:)` safe, numbered when taken), `PieceSummary`. `OriginalSounds.swift`: a recording's
  sound kept beside the project file, a recording picked read once. `ScanReview.swift`: a scan's Check bars kept on
  this iPad (`ScanReviewStore`), `ScanVariant`. `BackgroundReading.swift`: the library's recognition as a piece's
  screens see it (built by `Workspace/PieceScreen` from `Import/RecognitionQueue`). `EngineKeeper`, `Engraver`.
- Settings and words. `DeviceSettings.swift` (v1.6 i-14, out of `Account/SettingsView.swift`): the player's settings on
  this iPad, music-core's `settings.state` from the values kept in `motiff.settings`, each change after the one before
  (`queueChange`, E-settings-1); `resolved(overrides:)` lays a piece's own over them; read by the score, the roll, the
  assistant, scans, Piece DNA, the Reference and the app's appearance; shown by `Workspace/SettingsSheet`. `UIText.swift`:
  the shared words, then each folder's `<Folder>/Words/<Folder>.<language>.json`, merged (which key goes where:
  `tests/shared/i18n.test.mjs`); `UIText.current` in the language picked (`motiff.language`), else the iPad's.
  `InterfaceLanguage.swift`: a new choice saves the open piece, rebuilds the app under `.id(language)` and opens it again.
- Look and input. `Design.swift` reads `design/kit/tokens.css`. Kit views more than one folder uses: `FlowLayout`
  (rows that wrap, centred, v1.6 i-12), `SegmentedChoice` (ds.css `.seg`, v1.6 i-13), `FieldLook` (ds.css `.field`, the
  one-line fields of Library, Pdf and Account, v1.6 i-14). `HighlightInk.swift` (a highlight's four inks),
  `Recolouring.swift` (a change of the highlights by value, kept in the Pen's history). `TypingFocus.swift` (`Typing`,
  `TypingShield`: a tap outside a field hides the keyboard). `BundleSchemeHandler` (the app's files to the web views),
  `WebContentWatch`, `ViewStates`, `BackgroundTime`.
- `MotiffLabApp.swift` (the app, its settings and account in the environment); `ScreensHook.swift` (the Debug probe
  `ScreensProbe`, under `-ml-screens`).

## The contract others may rely on

- Every facade name sent is in `tests/contracts/facade-calls.json`; only the port's two files call `MusicCore.call`,
  each enum case named after its call (`facade-calls-contract.test.mjs`).
- The typed answers decode `tests/contracts/facade-answers/*.json` (`FacadeAnswerFixturesTests`), kept equal to the
  facade's answers by `FacadeAnswersContractTests`.
- `PageCommand`, `PageEvent` are `tests/contracts/score-page.json` (`score-page-contract.test.mjs`,
  `ScorePageContractTests`); `RollCommand`, `RollEvent` are `roll-page.json` (`roll-page-contract.test.mjs`,
  `RollPageContractTests`); no other file builds a roll call.
- Every `language:` sent to music-core or the assistant is `text.language`.

## The state it owns

The interface's language (`motiff.language`) and the device's settings (`motiff.settings`, `DeviceSettings.shared`,
written only by its `set`, `apply(preset:)` and `update`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: `SET-01` (`DeviceSettings`), `SCAN-02` (the engine's choice). Quality ratchet: none.
