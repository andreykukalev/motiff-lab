# Code map: iPad `Platform/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Platform`.

## Files

`BackgroundReading`, `BackgroundTime`, `Design`, `DraftAnswers`, `EngineKeeper`, `Engraver`, `HighlightInk`, `InterfaceLanguage`, `MotiffLabApp`, `MusicCore`,
`OpenPiece`, `OriginalSounds`, `ScanReview`, `PieceAnswers`, `PieceCore`, `ProjectFiles`, `RollPage`, `ScorePage`, `ScreensHook`, `TypingFocus`, `UIText`,
`ViewStates`, `WebContentWatch` (`.swift`).

- `MusicCore.swift`: facade calls through `MusicCore.call(piece:, name, json)`, the request encoded once by
  `facadeRequest` and the answer read by `facadeObject`. `FacadeAnswer.decode` (B5) reads an answer once into its typed
  answer: a refusal is no success; a field music-core always writes, missing, is logged, asserts in Debug and shows
  `.problem` (`broken`); every answer the app reads loudly is typed (the roll's too, B7). `FacadeAnswer.message` is the
  one reading of music-core's words.
- `ScanReview.swift` (v1.3 i-2): a scan's Check bars kept only on this iPad (`ScanReviewStore`, Application Support/ScanReview/<fingerprint>/: review.json, bar-<n>.png, newest 40), found by `check`'s "fingerprint"; `ScanVariant` as `scan.join` gave it.
- `PieceCore.swift`: the port to music-core for an open piece (0031 step 1b, a struct): the hot features'
  calls, `select`, `playback`, `roll`, `sourceLayers`, `corrections` (`PieceCore.Correction`, 15 calls),
  `variant` (v1.3 i-2), `draftSheet` and `draftTechniques` (v1.1), each answered as `PieceCore.Answer` (the typed value, the text,
  music-core's `message`). Features make these calls only here; the other calls move with their features in phase C.
- `FormSection.swift`: `FormSection`, a section of the form as music-core letters it (`Answer.FormSection`), one type
  for Piece DNA's form lane, the Contents' structure and the letters on the score (`PiecePhrases.sections`).
- `PieceAnswers.swift`: the typed answers, `SelectionAnswer`, `PhrasesAnswer` (units with their starts, sections), `PlaybackAnswer`,
  `RollAnswer` (passed to the roll page whole, `RollPage.load`), `SourceLayersAnswer`, `CorrectionsAnswer` with `EditsLayerAnswer` (also the open answer's "edits", `OpenedEdits`),
  `PerformanceAnswer` and `PerformancesAnswer` with `VersionHead`, mirroring their C# writers; the
  features build their own types from them. They name no feature type.
- `DraftAnswers.swift` (v1.1): `DraftSheetAnswer`, `DraftViewAnswer`, `DraftTechniquesAnswer`, `DraftVariantAnswer`,
  `DraftSeamsAnswer`; v1.2 `PieceDraftsAnswer` (`project.drafts`), `DraftAddedElsewhereAnswer`.
- `ScorePage.swift`: the one port to the score page (0031 step 2): `PageCommand` names `window.motiff`'s functions,
  `PageEvent` decodes its messages; `pageScript` writes a page call for both ports.
- `RollPage.swift`: the one port to the roll page (0031 step 12, B7): `RollCommand` names `window.roll`'s functions,
  `RollPage` calls each by a typed method and reads its answer (`RollShown`, the colouring, the frame), `RollEvent`
  decodes its messages into the roll's values (`RollRange`, `RollZoom`, `RollSpan`, `RollNotesPick`, `RollNoteEdit`). A
  message or answer the page never sends goes to `RollEvent.broken` (`FacadeAnswer.loud`: a log line, a Debug assertion);
  what the page sends and the app leaves out stays quiet. `RollEvent.broken` is a hook apart from `FacadeAnswer.broken`:
  silencing one does not silence the other.
- `BackgroundReading.swift` (1.3 hotfix, Andrei 2026-11-25): the library's recognition as one piece's screens see it,
  in the environment (`backgroundReading`): the piece recognised now (one at a time), whether it is this one, the line
  under the PDF's bar, and sending the piece or letting its notes in. `Workspace/PieceScreen` builds it from
  `Import/RecognitionQueue`, so `Pdf/` and `Sources/` name nothing of `Import/` for it.
- `OpenPiece.swift`: a piece opened in music-core: engraved, or a PDF piece (`isDocument`), or, since v1.1 (finding 29),
  a piece that is only its recording (`recording`, its original's id), neither engraved; a draft opened to be shown
  carries its places (`draft`, v1.1).
- `ProjectFiles.swift`: `ProjectFileCache` (what music-core read from each file, kept by its bytes: the library's
  summaries, other pieces' drafts), `PieceSummary.shownTitle` (a library line's title); the projects' folder, `save`, `saveNew` (a new piece saved to a new file and closed: photos, a
  JPEG or PNG, a recording); `PieceSummary` (`isRecordingOnly`: no notes, no PDF, a recording).
- `OriginalSounds.swift` (moved from `Sources/` in v1.1 so Add piece shares it): a recording's sound kept beside the
  project file on this iPad (`keep`, `file`, `folder`), and a recording picked read once (`picked`: its name, length and
  bytes for the fingerprint, up to `fingerprintLimit`).
- `Design.swift` reads `design/kit/tokens.css`.
- `HighlightInk.swift` (v0.10 d-5): the four inks a highlight is laid in and an ink's kit colour by name, the one list
  for the marks' shelf and bookmarks (Marks) and the pen's colour bars (Ink).
- `UIText.swift`: the words, the shared `src/shared/wwwroot/i18n/<language>.json` (in the app as `i18n/`), then every
  folder's `<Folder>/Words/<Folder>.<language>.json` (at the app's top) by name, merged; a key defined twice asserts in
  Debug. Which key goes where: `tests/shared/i18n.test.mjs` (B6). `UIText.current` is in the language picked in
  Settings · Appearance (`motiff.language`), else the iPad's.
- `InterfaceLanguage.swift`: a new choice saves the open piece (`ScoreWorkspace.handOverForLanguage`), rebuilds the app
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
- `RollCommand`, `RollEvent` and the examples of each call, answer and message are `tests/contracts/roll-page.json`,
  checked by `tests/shared/roll-page-contract.test.mjs` and `RollPageContractTests`; no other file builds a roll call.
- Every `language:` sent to music-core or the assistant is `text.language`.

## The state it owns

The interface's language (`motiff.language`). The rest: not recorded yet.

## Baseline entries that protect it

UX baseline: not recorded yet. Quality ratchet: none.
