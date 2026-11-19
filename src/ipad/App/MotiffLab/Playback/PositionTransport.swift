import SwiftUI

/// The pill's transport under the playback model (v0.5.2, #495 UX-10, PlaybackModel): one Play button whose look follows
/// the place, cobalt while it plays inside the selection (or, stopped, where Play will play the selection) and ink once
/// playback goes on through the piece, with the place as bar.beat alone («10.3», #511 section 8.2: no words that come and go,
/// at a width that does not change; VoiceOver still hears play or pause); with a selection, Цикл as its icon and its span
/// alone («9.1 – 12.3», finding 22: the word is VoiceOver's and the pointer's tip): turned on, the position goes to the
/// selection's start and the selection repeats. Space is the Play button. The model decides; this only shows it and sends
/// the presses to the player.
struct PositionTransport: View {
    @ObservedObject var player: Player
    /// Expand's passage (v0.6.4, spec 8), nil out of Expand: the model over the passage as a selection that always loops
    /// (ExpandRules.held), so Play plays it round from the playhead inside it and there is no Цикл to turn off (#533 2).
    var passage: Range<Double>?
    let text: UIText

    var body: some View {
        let view = PlaybackModel.view(of: PlaybackScope.shown(player.model, passage: passage))
        HStack(spacing: CGFloat.ds("s-1")) {
            play(view)
            if passage == nil, let selection = player.selection, let span = player.barBeats.span(selection) {
                Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
                cycle(span)
            }
        }
    }

    /// The one Play: a cobalt disc in the selection's context, an ink one in the piece's (02-design-system: cobalt is the
    /// selection, ink the piece), its words in the same colour.
    private func play(_ view: PlaybackModel.View) -> some View {
        let cobalt = view.context == .selection
        let place = player.barBeats.at(view.at)?.text ?? ""
        return Button {
            if let passage { player.playPassage(passage) } else { player.apply(.press) }
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: view.playing ? "pause" : "playf", size: CGFloat.ds("icon-s"))
                    .foregroundStyle(Color.ds(cobalt ? "on-accent" : "surface"))
                    .frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
                    .background(Color.ds(cobalt ? "accent" : "ink"), in: Circle())
                // The widest place a piece has, «888.88», holds the width, so the pill never changes size as the place moves.
                Text(verbatim: "888.88")
                    .monospacedDigit()
                    .fontWeight(.semibold)
                    .hidden()
                    .overlay(alignment: .leading) {
                        Text(place)
                            .monospacedDigit()
                            .fontWeight(.semibold)
                            .foregroundStyle(Color.ds(cobalt ? "accent" : "ink"))
                    }
                    .padding(.trailing, CGFloat.ds("s-2"))
            }
            .animation(.ds("t-fast"), value: cobalt)
        }
        .buttonStyle(.plain)
        .keyboardShortcut(.space, modifiers: [])
        .accessibilityLabel(view.playing ? text.format("pill.pauseAt", place) : text.format("pill.play", place))
        .accessibilityHint(text[cobalt ? "pill.playSelectionHint" : "pill.playPieceHint"])
    }

    /// Цикл with the selection's span: on, the position goes to the selection's start and it repeats; off, playback goes on
    /// past its end. Its icon and the span show, so the pill fits beside the assistant (finding 22); «Цикл 9.1 – 12.3» is
    /// VoiceOver's label and the pointer's tip.
    private func cycle(_ span: (first: String, last: String)) -> some View {
        let label = text.format("pill.cycle", span.first, span.last)
        return Button {
            player.apply(.cycle)
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: "loop")
                Text(text.format("pill.cycleSpan", span.first, span.last))
                    .monospacedDigit()
            }
        }
        .buttonStyle(DSItemStyle(on: player.cycle))
        .accessibilityLabel(label)
        .accessibilityAddTraits(player.cycle ? .isSelected : [])
        .accessibilityHint(text["pill.cycleHint"])
        .help(label)
    }
}

extension ScoreController {
    /// Expand's passage while it is open (#521 19, #533 2): the selection it was opened on, which plays round and round and
    /// which the playhead is kept inside; nil out of Expand.
    var expandPassage: Range<Double>? {
        PlaybackScope.expandPassage(focused: focusModel.focused, playRange: focusModel.playRange?.range)
    }

    /// Whether the playback model takes the score's selection and Цикл (T9–T11): out of Expand, whose passage loops on its
    /// own and leaves them as they are (T18).
    var holdsSelection: Bool {
        PlaybackScope.holdsSelection(focused: focusModel.focused)
    }
}

/// Where the playback model holds on the score's screen (v0.5.2; Expand since v0.6.4, spec 8), the rules apart from the
/// controller so that they are tested: one model everywhere, Expand too, which has no playback of its own; Expand holds it
/// over its passage, which always loops, and leaves the selection and Цикл of the page as they are.
enum PlaybackScope {
    /// Expand's passage while it is open: the selection it was opened on; nil out of it.
    static func expandPassage(focused: Bool, playRange: Range<Double>?) -> Range<Double>? {
        focused ? playRange : nil
    }

    /// Whether the model takes the page's selection and Цикл (T9–T11): out of Expand (T18).
    static func holdsSelection(focused: Bool) -> Bool {
        !focused
    }

    /// The model the pill's Play shows: in Expand the passage as a selection that always loops (ExpandRules.held), so its
    /// Play is the selection's, cobalt, and names where it will play from; else the player's model.
    static func shown(_ model: PlaybackModel.State, passage: Range<Double>?) -> PlaybackModel.State {
        passage.map { ExpandRules.held($0, tick: model.tick, transport: model.transport) } ?? model
    }
}
