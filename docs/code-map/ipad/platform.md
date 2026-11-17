# Code map: iPad `Platform/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Platform`.

## Files

`BackgroundTime`, `Design`, `EngineKeeper`, `Engraver`, `InterfaceLanguage`, `MotiffLabApp`, `MusicCore`, `OpenPiece`,
`ProjectFiles`, `ScorePage`, `ScreensHook`, `TypingFocus`, `UIText`, `ViewStates`, `WebContentWatch` (`.swift`).

- `MusicCore.swift`: facade calls through `MusicCore.call(piece:, name, json)`, the request encoded once by
  `facadeRequest` and the answer read by `facadeObject`. The hot answers' readers use `FacadeAnswer` (A7): a refusal is
  no success; a field music-core always writes, missing, is logged, asserts in Debug and shows `.problem`.
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
  reads the Swift for them).
- `PageCommand` and `PageEvent` are `tests/contracts/score-page.json`, checked by
  `tests/shared/score-page-contract.test.mjs` and `ScorePageContractTests`.
- Every `language:` sent to music-core or the assistant is `text.language`.

## The state it owns

The interface's language (`motiff.language`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: none.
