import SwiftUI

/// The two strips of «Пюпитр» (spec score-reading §3, v1.7 e-1) and what stands at their edges over the page: the top strip
/// holds the bar (Expand's focus bar in focus) and, on a draft's view, the draft's pill under it; the bottom shelf holds
/// the pill, with the chip back to the playing beside Play. Under the top strip, over the page, hangs what the player opened
/// there (the recording heard, the chosen mode's options). The bar and the pills are their own views
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
             backTo: backToBar, back: { score.returnToPlayhead() }, rollOpen: panel(.roll), assistantOpen: assistant.panelOpen) {
            workspace.assistantPressed()
        }
        .modifier(DockedStrip(edge: .bottom))
        .keyboardLift(barOnly: true)
    }

    /// The bar the music plays in while the page is scrolled away from it (testing item 4), out of Expand: the shelf's chip
    /// back to the playing says it; nil while there is none.
    private var backToBar: Int? {
        guard let away = score.playheadAway, player.state != .stopped, !focusModel.focused else { return nil }
        return player.barNumber(at: away)
    }

    /// The piece tempo the player typed in the pill, kept with the piece (ScoreController.setPieceTempo).
    private func setPieceTempo(_ perMinute: Int?) {
        Task { [weak score] in await score?.setPieceTempo(perMinute) }
    }
}
