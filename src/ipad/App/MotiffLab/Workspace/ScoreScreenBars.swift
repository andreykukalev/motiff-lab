import SwiftUI

/// What floats over the top and the bottom of the score: the bar (Expand's in focus) with what hangs under it, and the
/// transport with the pill. The bar and the pill are their own views (`ScoreBar`, `Pill`, `FocusBar`), handed what they
/// show and the actions they call; this is where they float and what they are handed.
extension ScoreScreenContent {
    /// Under the top of the screen: the bar or the focus bar, the recording heard, a draft's pill and the mode's options;
    /// `width` is the bar's, the screen's less its margins (TopBar).
    func bars(width: CGFloat) -> some View {
        VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
            if focusModel.focused {
                FocusBar(score: score, selectionModel: selectionModel, focusModel: focusModel, player: player, variationRange: variationRange, text: text)
            } else {
                ScoreBar(title: piece.title, pages: score.pages, text: text, score: score, marksModel: marksModel, focusModel: focusModel, hasVersions: hasVersions, panels: panels, change: change, pdf: workspace.toPdf(switchView), openDna: { openDna(fromMore: true) }, toggleExtra: { score.toggleExtra() }, openReference: { change(.open(.reference)) },
                         problem: player.problem ?? score.problem, askAboutPiece: askAboutPiece, sourceLayers: switchLayers, currentView: currentView, pickView: switchPick, viewing: viewing,
                         viewNote: sourcesModel.engraved.note(text), width: width) { dismiss() }
            }
            if !focusModel.focused, let heard = PerformanceCapsule.sources(over: pieceUnder, own: sources, switchOn: sourcesOn) {
                PerformanceCapsule(sources: heard, text: text)
            }
            if !focusModel.focused, let draft = piece.draft {
                DraftAssemblyPill(score: score, model: score.draftModel, player: player, draft: draft, text: text)
                    .onGeometryChange(for: CGFloat.self) { $0.frame(in: .named(Self.layersSpace)).maxY } action: { draftPillBottom = $0 }
            }
            if !focusModel.focused {
                // The chosen mode's options under the bar (#511 4.2, option B): the Pen's palette, Select's row.
                ModeOptions(score: score, selectionModel: selectionModel, text: text, hasVersions: hasVersions, penOn: penModel.penOn,
                            marksHidden: !marksModel.hiddenLayers.isDisjoint(with: MarksModel.layers))
            }
        }
        .padding(.horizontal, CGFloat.ds("s-4"))
        .padding(.top, CGFloat.ds("s-3"))
        .animation(.easeOut(duration: 0.16), value: penModel.penOn)
        .animation(.easeOut(duration: 0.16), value: focusModel.focused)
    }

    /// The question about the whole piece from the bar's More: the assistant's panel opens on it (#582 item 17.1).
    private func askAboutPiece() {
        assistant.open(wholePiece: true, selection: selectionModel.selection)
    }

    /// Over the bottom of the page: the chip back to the playing, the pill, and in practice view its own controls (#533 11).
    @ViewBuilder private var transport: some View {
        // Scrolled away from the playing, a chip leads back to it (testing item 4).
        if let away = score.playheadAway, player.state != .stopped, !focusModel.focused, score.practiceChrome.transport, let bar = player.barNumber(at: away) {
            Button {
                score.returnToPlayhead()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "play", size: CGFloat.ds("icon-s"))
                    Text(text.format("transport.backToPlay", String(bar))).dsType(.label)
                }
                .foregroundStyle(Color.ds("accent"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: 32)
                .modifier(FloatingSurface())
            }
            .buttonStyle(.plain)
            .transition(.opacity)
        }
        // Practice view (#533 11): the pill gives way to Play/Pause and Leave, shown a few seconds after a tap.
        // The pill always shows (the playback model, v0.5.2, #479 item 22): it is where the music plays.
        if score.practiceChrome.transport {
            Pill(player: player, selectionModel: selectionModel, focusModel: focusModel, draftModel: score.draftModel, text: text, passage: score.expandPassage, step: { score.step($0) }, setPieceTempo: setPieceTempo,
                 rollOpen: panel(.roll), assistantOpen: assistant.panelOpen) {
                workspace.assistantPressed()
            }
            .transition(.opacity)
        }
        if score.practiceChrome.controls {
            PracticeControls(player: player, score: score, text: text)
                .transition(.opacity)
        }
    }

    /// The transport over the bottom of the score, `bottom` above the screen's edge and clear of a sheet or a drawer at the
    /// sides (`leading`, `trailing`).
    func transportLayer(bottom pillBottom: CGFloat, leading pillLeading: CGFloat, trailing pillTrailing: CGFloat) -> some View {
        // The pill rides above whatever comes up from the bottom, and beside a sheet or a drawer at the side. It
        // shows with a selection or while the music plays, or only while it plays (the setting
        // "guidance.transportPill", 0018 section 10).
        // Measured, so the selection's toast stands above it (v0.6.4, spec 7); above a hidden keyboard's bar (iPadOS's
        // «RU» and Undo), never above the keyboard itself (v0.6.4).
        VStack(spacing: CGFloat.ds("s-2")) { transport }.onGeometryChange(for: CGFloat.self) { $0.size.height } action: { transportHeight = $0 }.keyboardLift(barOnly: true)
        .padding(.bottom, pillBottom)
        .padding(.trailing, pillTrailing).padding(.leading, pillLeading)
    }

    /// The piece tempo the player typed in the pill, kept with the piece (ScoreController.setPieceTempo).
    private func setPieceTempo(_ perMinute: Int?) {
        Task { [weak score] in await score?.setPieceTempo(perMinute) }
    }
}
