# Code map: the iPad app (`src/ipad/App`)

SwiftUI over the one C# music-core (NativeAOT framework, `src/ipad/Core`), facade calls through `MusicCore.call(piece:, name, json)`,
the request encoded once by `facadeRequest` and the answer read by `facadeObject` (`MusicCore.swift`). The hot answers' readers use `FacadeAnswer`
(A7): a refusal is no success; a field music-core always writes, missing, is logged, asserts in Debug and shows `.problem`.
The score and the roll are the shared TS modules in a WKWebView (`Web/score.js`, `src/shared/Scripts/score`, `roll`). Strings:
the shared `src/shared/wwwroot/i18n/*.json` plus the iPad's own `Words/*.json` (`UIText`). Tokens: `Design.swift`
reads `design/kit/tokens.css`. Tests: `tests/ipad/MotiffLabTests` (XCTest, run by the `iPad` workflow on macOS, not CI).

## The interface's language

- `UIText.swift`: the words; `UIText.current` in the language picked in Settings · Appearance (`motiff.language`), else the
  iPad's. `InterfaceLanguage.swift`: a new choice saves the open piece (its screen's `handOverForLanguage`), rebuilds the app
  under `.id(language)` (`MotiffLabApp`), and the library opens the piece again where it was. Every `language:` sent to
  music-core or the assistant is `text.language`.

## The score's screen

- `ScoreScreen.swift`: `ScoreScreen` (l. 14): `body` (GeometryReader, ignores the keyboard), `layers` (l. ~250: the web view and
  everything floating over it: selection menu at `score.menuPlace`, toasts, roll drawer, sheets, Contents, pill), `wire()`
  (l. ~437: the controllers' callbacks), `ScoreBar` (top bar, More, Ask about the piece), `SelectionMenu` (the menu over
  the selection; its More holds Highlight and Ask), `FocusBar` (Expand's bar). `ScoreScreenViews.swift`, `TopBar.swift`.
- `ScoreController.swift`: the page (selection, menu place and drag, marks, corrections, merge, focus/Expand).
- `ScorePage.swift`: the one port to the page (0031 step 2): `PageCommand` names `window.motiff`'s functions, `PageEvent`
  decodes its messages; both lists are `tests/contracts/score-page.json`, checked by `tests/shared/score-page-contract.test.mjs`
  and `ScorePageContractTests`.
- `EditsLayer.swift` (v0.6.5, «Мои правки»): `EditsLayerState` (the open answer's "edits" and every corrections answer:
  "count", "layer", "moved", "unfit"), `EditsLayerSection` in More (the switch, its line, the misfits with «Убрать»).
  `OpenPiece.openRequest` opens a project file with "edits": "piece" (the file keeps "off"); `ScoreController.switchEdits`
  / `dropUnfitEdits` (`corrections.layer` / `.drop`) engrave again and save at once (`editsKept`). While off, `editsOn` is
  false: no Fix notes, no note editor, no draft Accept (music-core refuses them too).
- `PracticeView.swift`: the two visibilities (`selectionControlsShown`, `playbackControlsShown`, #582 item 10), two switches
  under the bar's button, nothing over them, kept on the device (`PracticeView.saved`/`keep`, v0.6.4 spec 9).
- `PositionTransport.swift`: the pill's one Play; `PlaybackScope`: the one model everywhere (the modes, `ScoreController.mode`
  never nil since v0.6.5), Expand over its passage (`expandPassage`); the page's selection and Цикл only out of it (`holdsSelection`).
  The card's black playhead: `score.js` `drawCardHeads`/`cardGrabs` (v0.6.4 spec 8).
- `Insights.swift`: `InsightsController` (facts, Try ways, `trial`), `InsightsSheet` (the stream; Ask's foot).
- `InkInput.swift`, `InputRules.swift`, `PencilPalette.swift`: who writes and who scrolls, Pencil gestures.
- `PlaceSelection.swift` (v0.6.2; Select's only way since v0.6.3): the one selection (span, hand, notes picked) and
  `ScoreController.selectNotes`; the page's rule is `Scripts/score/selection-places.ts` (map: `docs/code-map/score.md`).
- The roll in the drawer (`RollController`, `RollDrawer`, `RollTools`, `RollPlace`, `Web/roll.js`): `docs/code-map/roll.md`.

## PDF | Notes (lifecycle, `docs/code-map/ui-review-2026-11.md`)

- `LibraryView.switchView` → `openFile` → `show`: opens the other view, reads only that file's summary, closes the old
  piece (`ClosedPieces`), swaps the navigation item. The screen's model is set in its `init` (`ScoreController()` in Navigate,
  the handed `SourcesHandover.layers`), never after an await; `onDisappear` clears `sources.reopen` (it held the screen).
- `MotiffLabScreens/SwitchUITests` (with `SWITCH_TEST`) switches it again and again; the Debug probe `ScreensProbe` gives
  the screen ready, controllers alive, renders, memory and each switch's phases (`ProbeCounts.mark`).

## The UI walk (what protects the journeys)

- `tests/ipad/MotiffLabScreens/ScreensUITests` (the `Screens` scheme): the journeys of the blueprint's layer T4 in a table,
  each naming the baseline ids it protects; a journey stops at its first miss (`WalkMiss`), the app is launched again for
  the next, and every miss fails the test. `Walk.swift` drives the app and reads the probe; `ScoreJourneys.swift` (open and
  play, select-loop-change, drag, roll and Expand, Insights and Try, pen and eraser, the assistant's panel, other places),
  `SwitchJourneys.swift` (PDF | Notes, Sources, a PDF piece). A selection is made as a player makes one, by a drag in
  Select (`Walk.choosePhrase`); the Debug phrase chooser `ml-screens-phrase` is not used (no selection followed its tap in
  the first run). The screenshots for the mockups are taken on the way, only when SCREENS_DIR asks for them, and the
  other places of the score (`picturesOnly`) are visited only then. What the app does now and Andrei is asked about is
  walked as it is and printed as `SCREEN-QUESTION` each run: the roll's zoom control (bars within one, ROLL-01) and the
  PDF metronome's pill (a strict, narrow `XCTExpectFailure`: it has never shown in the simulator).
- The probe (`ScreensHook.swift`, Debug only, under `-ml-screens`): `ScreensProbe.line()` on the hidden element
  `ml-screens-probe`; on the score's screen `workspace()` adds the selection and Play's range in ticks, Expand, the mode, the
  Pen and its ink, the scroll, the roll's place and zoom, the transport and its tick, Цикл, the sound, a variation, the
  assistant's panel (`ScoreScreen.wire` hands the player, roll and assistant over). Identifiers the walk uses besides the
  labels: `insights.tryWay`, `roll.more`.

## The assistant

- `AskField.swift`: the chat's interaction model `AskFlow` (open → focus → keyboard → type → hide → go on), `AskField`
  (UITextField, Scribble declined so the Pencil opens the keyboard; focused only by a press), `AskInputRow` (the kit's
  field `DSFieldLook` + microphone + Send), `KeyboardRoom`. `TypingFocus.swift` (v0.6.4): the app's one keyboard rule,
  `Typing` (a tap outside any focused field hides the keyboard), `TypingShield` (over the score's and roll's web views).
  `Dictation.swift` (always on since v0.6.5): `DictationFlow`, `Dictation` (SFSpeech on the device only).
- Every Ask opens the real assistant, no switch (v0.6.3): selection menu → `AssistantHereCard`, others → the panel.
- `AssistantStates.swift`: `AssistantScope` (core AskScope), `AssistantPhase` (no streaming), `AssistantRecovery`.
- `AssistantController.swift`: panel state, `scope`, `phase`, `standing` (`check`: `/assistant/status`), the piece's
  conversations, `ask` (status → `assistant.context` → turns), `forget` (Try again), `add` («Добавить в контекст»). `AssistantTurns.swift`: one question's
  turns, tool calls run by `assistant.call`. `AssistantChat.swift`: `JSONValue`, the turn format v1, cited facts.
  `AssistantRelay.swift`: `/assistant/status`, `/assistant/turn`. `AssistantStore.swift`: conversations by piece in
  Application Support (never the project file). `AssistantPanel.swift`, `AssistantSide.swift`: the side panel (the score
  narrowed in `ScoreScreen.layers`), the consent, the list, `AssistantPendingView`, `AssistantProblemLine`. `AssistantContextual.swift`: the small chat at the selection
  (`SelectionMenu` turns into it), `KeyboardLift`. `AssistantDrafts.swift` (the proposer, the only role since v0.6.5): the drafts
  a question's tools made, Accept by `corrections.acceptDraft` (`ScoreController.acceptAssistantDraft`).
- `Recording.swift`: a recording heard opens as a new piece with `HeardRecording.transcription` ("tempo": "live", no
  "starts", no "hint": there is no score yet); a version from a recording keeps `request` (`performance.add`).
- `Account.swift`: MSAL sign-in, `apiRequest(path)` (the API with the player's token), plan and Pro features.

## Rules not to break

- The score's screen never resizes for the keyboard: a chat keeps itself clear of it (`AskFlow`, `KeyboardRoom`); the
  pill rises only above a hidden keyboard's bar (`KeyboardLift(barOnly:)`). The tap that hides the keyboard never acts on
  the music (`TypingShield`); no field takes the focus by itself. Dictation never leaves the device.
- Nothing in `src/ipad/App` changes analysis or variation data; drafts come from music-core and only the player accepts them.
- Every interface string in English and Russian; colours, sizes and timings are tokens.
