# Code map: iPad `Platform/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Platform`.

## Files

`BackgroundTime`, `Design`, `EngineKeeper`, `Engraver`, `InterfaceLanguage`, `MotiffLabApp`, `MusicCore`, `OpenPiece`,
`PieceAnswers`, `PieceCore`, `ProjectFiles`, `RollPage`, `ScorePage`, `ScreensHook`, `TypingFocus`, `UIText`, `ViewStates`,
`WebContentWatch` (`.swift`).

- `MusicCore.swift`: facade calls through `MusicCore.call(piece:, name, json)`, the request encoded once by
  `facadeRequest` and the answer read by `facadeObject`. The hot answers' readers use `FacadeAnswer` (A7): a refusal is
  no success; a field music-core always writes, missing, is logged, asserts in Debug and shows `.problem`.
  `FacadeAnswer.decode` (B5) reads an answer once into its typed answer, reporting through the same `broken`.
- `PieceCore.swift`: the port to music-core for an open piece (0031 step 1b): `select`, `playback`, `sourceLayers` and
  `corrections` (every `corrections.*` call, named by `PieceCore.Correction`), each answered as `PieceCore.Answer` (the
  typed value, the text, music-core's `message`). Features make these calls only here; the others move here with their
  typed answers (B5).
- `PieceAnswers.swift`: the typed answers, `SelectionAnswer`, `PlaybackAnswer`, `SourceLayersAnswer`,
  `CorrectionsAnswer` with `EditsLayerAnswer` (also the open answer's "edits", `OpenedEdits`), mirroring their C#
  writers; the features build `ScoreSelection`, `PlaybackScore`, `SourceLayers`, `NoteCorrections` and
  `EditsLayerState` from them. They name no feature type.
- `ScorePage.swift`: the one port to the score page (0031 step 2): `PageCommand` names `window.motiff`'s functions,
  `PageEvent` decodes its messages; `pageScript` writes a page call for both ports.
- `RollPage.swift`: the one port to the roll page (0031 step 12, B7): `RollCommand` names `window.roll`'s functions,
  `RollPage` calls each by a typed method and reads its answer (`RollShown`, the colouring, the frame), `RollEvent`
  decodes its messages into the roll's values (`RollRange`, `RollZoom`, `RollSpan`, `RollNotesPick`, `RollNoteEdit`). A
  message or answer the page never sends goes to `RollEvent.broken` (`FacadeAnswer.loud`: a log line, a Debug assertion);
  what the page sends and the app leaves out stays quiet, as before.
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
  reads the Swift for them); a call `PieceCore` makes is made from no other file.
- The typed answers decode `tests/contracts/facade-answers/*.json` (`FacadeAnswerFixturesTests`), which
  `FacadeAnswersContractTests` keeps equal to the facade's answers on the example piece.
- `PageCommand` and `PageEvent` are `tests/contracts/score-page.json`, checked by
  `tests/shared/score-page-contract.test.mjs` and `ScorePageContractTests`.
- `RollCommand`, `RollEvent` and the examples of each call, answer and message are `tests/contracts/roll-page.json`,
  checked by `tests/shared/roll-page-contract.test.mjs` and `RollPageContractTests`; no other file builds a roll call.
- Every `language:` sent to music-core or the assistant is `text.language`.

## The state it owns

The interface's language (`motiff.language`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: none.
