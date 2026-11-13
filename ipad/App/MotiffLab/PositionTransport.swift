import SwiftUI

/// The pill's transport under the playback model (v0.5.2, #495 UX-10, PlaybackModel): one Play button whose look follows
/// the place, cobalt while it plays inside the selection (or, stopped, where Play will play the selection) and ink once
/// playback goes on through the piece, with the place as bar.beat («С 10.3», «Пауза · 10.3»); with a selection, Цикл with
/// its span («Цикл 9.1 – 12.3»): turned on, the position goes to the selection's start and the selection repeats. Space is
/// the Play button. The model decides; this only shows it and sends the presses to the player.
struct PositionTransport: View {
    @ObservedObject var player: Player
    let text: UIText

    var body: some View {
        let view = PlaybackModel.view(of: player.model)
        HStack(spacing: CGFloat.ds("s-1")) {
            play(view)
            if let selection = player.selection, let span = player.barBeats.span(selection) {
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
            player.apply(.press)
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: view.playing ? "pause" : "playf", size: CGFloat.ds("icon-s"))
                    .foregroundStyle(Color.ds(cobalt ? "on-accent" : "surface"))
                    .frame(width: CGFloat.ds("h-control"), height: CGFloat.ds("h-control"))
                    .background(Color.ds(cobalt ? "accent" : "ink"), in: Circle())
                Text(text.format(view.playing ? "pill.pauseAt" : "pill.from", place))
                    .monospacedDigit()
                    .fontWeight(.semibold)
                    .foregroundStyle(Color.ds(cobalt ? "accent" : "ink"))
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
    /// past its end.
    private func cycle(_ span: (first: String, last: String)) -> some View {
        Button {
            player.apply(.cycle)
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Icon(name: "loop")
                Text(text.format("pill.cycle", span.first, span.last))
                    .monospacedDigit()
            }
        }
        .buttonStyle(DSItemStyle(on: player.cycle))
        .accessibilityAddTraits(player.cycle ? .isSelected : [])
        .accessibilityHint(text["pill.cycleHint"])
    }
}

extension ScoreController {
    /// Whether the playback model holds (v0.5.2): the modes are on (Settings · Experimental) and the score is not in focus,
    /// which plays as before.
    var onePosition: Bool {
        mode != nil && !focused
    }
}
