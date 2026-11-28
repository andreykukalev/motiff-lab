import SwiftUI

/// The two strips of «Пюпитр» (spec score-reading §3, v1.7 e-1) and what stands at their edges over the page: the top strip
/// holds the bar (Expand's focus bar in focus) and, on a draft's view, the draft's pill under it; the bottom shelf holds
/// the pill. Under the top strip, over the page, hangs what the player opened there (the recording heard, the chosen
/// mode's options); over the foot of the page, the chip back to the playing. The bar and the pills are their own views
/// (`ScoreBar`, `FocusBar`, `Pill`, `DraftAssemblyPill`), handed what they show and the actions they call; this is where
/// they stand and what they are handed.
extension ScoreScreenContent {
    /// The top strip across the screen (`width`): the bar or the focus bar, and a draft's pill under it. The strip is the
    /// PDF's and the recording's (`dockedAtTop`), so Back, the Contents, the title and the switch stand where theirs have
    /// them (VIEW-04).
    func topStrip(width: CGFloat) -> some View {
        VStack(alignment: .trailing, spacing: CGFloat.ds("s-1")) {
            if focusModel.focused {
                FocusBar(score: score, selectionModel: selectionModel, focusModel: focusModel, player: player, variationRange: variationRange, text: text)
            } else {
                ScoreBar(title: piece.title, pages: score.pages, text: text, mode: $score.mode, penModel: penModel, marksModel: marksModel, checkModel: checkModel,
                         layers: LayersList(score: score, marksModel: marksModel, checkModel: checkModel, text: text, hasVersions: hasVersions), panels: panels, change: change, pdf: workspace.toPdf(switchView), openDna: { openDna(fromMore: true) }, toggleExtra: { marksModel.toggleExtra() }, openReference: { change(.open(.reference)) },
                         problem: player.problem ?? score.problem, askAboutPiece: askAboutPiece, sourceLayers: switchLayers, currentView: currentView, pickView: switchPick, viewing: viewing,
                         viewNote: sourcesModel.engraved.note(text), width: width - 2 * DockedStrip.margin) { dismiss() }
            }
            if !focusModel.focused, let draft = piece.draft {
                DraftAssemblyPill(score: score, model: score.draftModel, player: player, draft: draft, text: text)
            }
        }
        // The walk measures the page between the strips by the strip's identifier (`DockedStrip`).
        .dockedAtTop()
        .animation(.easeOut(duration: 0.16), value: focusModel.focused)
    }

    /// Under the top strip, over the page, what the player opened there: the recording heard and the chosen mode's options.
    @ViewBuilder var underStrip: some View {
        if !focusModel.focused {
            VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                if let heard = PerformanceCapsule.sources(over: pieceUnder, own: sources, switchOn: sourcesOn) {
                    PerformanceCapsule(sources: heard, text: text)
                }
                // The chosen mode's options under the bar (#511 4.2, option B): the Pen's palette, Select's row.
                ModeOptions(score: score, selectionModel: selectionModel, text: text, hasVersions: hasVersions, penOn: penModel.penOn,
                            marksHidden: !marksModel.hiddenLayers.isDisjoint(with: MarksModel.layers))
            }
            .hungUnderTopStrip()
            .animation(.easeOut(duration: 0.16), value: penModel.penOn)
        }
    }

    /// The question about the whole piece from the bar's More: the assistant's panel opens on it (#582 item 17.1).
    private func askAboutPiece() {
        assistant.open(wholePiece: true, selection: selectionModel.selection)
    }

    /// The bottom shelf across the screen: the pill, which always shows (the playback model, v0.5.2, #479 item 22): it is
    /// where the music plays. Above a hidden keyboard's bar (iPadOS's «RU» and Undo), never above the keyboard itself
    /// (v0.6.4), which the screen ignores.
    var bottomShelf: some View {
        Pill(player: player, selectionModel: selectionModel, focusModel: focusModel, draftModel: score.draftModel, text: text, passage: score.expandPassage, step: { focusModel.step($0) }, setPieceTempo: setPieceTempo,
             rollOpen: panel(.roll), assistantOpen: assistant.panelOpen) {
            workspace.assistantPressed()
        }
        .modifier(DockedStrip(edge: .bottom))
        .keyboardLift(barOnly: true)
    }

    /// Scrolled away from the playing, a chip over the foot of the page leads back to it (testing item 4), `bottom` above
    /// the shelf or what covers the foot, and clear of a sheet or a drawer at the sides (`leading`, `trailing`). Measured,
    /// so the selection's toast stands above it (v0.6.4, spec 7).
    func backToPlaying(bottom: CGFloat, leading: CGFloat, trailing: CGFloat) -> some View {
        VStack {
            if let away = score.playheadAway, player.state != .stopped, !focusModel.focused, let bar = player.barNumber(at: away) {
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
        }
        .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { transportHeight = $0 }
        .padding(.bottom, bottom)
        .padding(.trailing, trailing).padding(.leading, leading)
    }

    /// The piece tempo the player typed in the pill, kept with the piece (ScoreController.setPieceTempo).
    private func setPieceTempo(_ perMinute: Int?) {
        Task { [weak score] in await score?.setPieceTempo(perMinute) }
    }
}
