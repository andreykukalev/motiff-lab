# Code map: iPad `Platform/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Platform`.

## Files

`BackgroundTime`, `Design`, `EngineKeeper`, `Engraver`, `InterfaceLanguage`, `MotiffLabApp`, `MusicCore`, `OpenPiece`,
`PieceAnswers`, `PieceCore`, `ProjectFiles`, `ScorePage`, `ScreensHook`, `TypingFocus`, `UIText`, `ViewStates`,
`WebContentWatch` (`.swift`).

- `MusicCore.swift`: facade calls through `MusicCore.call(piece:, name, json)`, the request encoded once by
  `facadeRequest` and the answer read by `facadeObject`. `FacadeAnswer.decode` (B5) reads an answer once into its typed
  answer: a refusal is no success; a field music-core always writes, missing, is logged, asserts in Debug and shows
  `.problem` (`broken`). `FacadeAnswer(_:of:)` with `required`/`entry`/`has` (A7) is left only for the roll's payload
  (track R). `FacadeAnswer.message` is the one reading of music-core's words.
- `PieceCore.swift`: the port to music-core for an open piece (0031 step 1b; a struct, the ADR's note): the hot features'
  calls, `select`, `playback`, `sourceLayers`, `corrections` (`PieceCore.Correction`, 15 calls), `phraseLevel`, `merge`
  (`PieceCore.Merge`, the draft's 7 calls), `mergePlayback`, `mergeApply` and `mergeUndoApplied`, each answered as
  `PieceCore.Answer` (the typed value, the text, music-core's `message`). Features make these calls only here; the other
  calls move with their features in phase C.
- `PieceAnswers.swift`: the typed answers, `SelectionAnswer`, `PhrasesAnswer` and `PhraseLevelAnswer`, `PlaybackAnswer`,
  `SourceLayersAnswer`, `CorrectionsAnswer` with `EditsLayerAnswer` (also the open answer's "edits", `OpenedEdits`),
  `MergeAnswer`, `PerformanceAnswer` and `PerformancesAnswer` with `VersionHead`, mirroring their C# writers; the
  features build their own types from them. They name no feature type.
- `ScorePage.swift`: the one port to the score page (0031 step 2): `PageCommand` names `window.motiff`'s functions,
  `PageEvent` decodes its messages.
- `Design.swift` reads `design/kit/tokens.css`.
- `UIText.swift`: the words, the shared `src/shared/wwwroot/i18n/*.json` plus the iPad's own `Words/*.json`.
  `UIText.current` is in the language picked in Settings · Appearance (`motiff.language`), else the iPad's.
- `InterfaceLanguage.swift`: a new choice saves the open piece (its screen's `handOverForLanguage`), rebuilds the app
  under `.id(language)` (`MotiffLabApp`), and the library opens the piece again where it was.
- `TypingFocus.swift` (v0.6.4): the app's one keyboard rule, `Typing` (a tap outside any focused field hides the
  keyboard), `TypingShield` (over the score's and roll's web views).
- `ScreensHook.swift`: the Debug probe `ScreensProbe` (screen ready, controllers alive, renders, memory, each switch's
  phases, `ProbeCounts.mark`), under `-ml-screens`.

## The contract others may rely on

- Every facade name sent must be in `tests/contracts/facade-calls.json` (`tests/shared/facade-calls-contract.test.mjs`
  reads the Swift for them); a call `PieceCore` makes is made from no other file, and each case of its enums of calls
  sends the call it is named after.
- The typed answers decode `tests/contracts/facade-answers/*.json` (`FacadeAnswerFixturesTests`), which
  `FacadeAnswersContractTests` keeps equal to the facade's answers on the example piece.
- `PageCommand` and `PageEvent` are `tests/contracts/score-page.json`, checked by
  `tests/shared/score-page-contract.test.mjs` and `ScorePageContractTests`.
- Every `language:` sent to music-core or the assistant is `text.language`.

## The state it owns

The interface's language (`motiff.language`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: none.
