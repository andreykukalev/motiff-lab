import SwiftUI

/// The pill's Play under the one playback position (v0.5.2, #479 item 22, approved mockup): with a selection its cobalt Play
/// with the span ("9.1 – 12.3", or "10.3 · 9.1–12.3" while it plays inside), Цикл, and the piece's ink Play from the
/// position ("С 16.2"); without one only the ink Play ("С 16.2", "Пауза · 16.2"). The Play that has the position plays in
/// its colour; Space plays the selection when there is one, else the piece. TransportPill decides what each shows.
struct PositionTransport: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        let range = score.playRange?.range
        let span = range.flatMap { player.barBeats.span($0) }
        let pill = TransportPill.of(
            position: player.placeLabel,
            span: span,
            inside: PlaybackPosition.inside(player.tick, range),
            playing: player.state != .stopped,
            cycle: player.cycle
        )
        HStack(spacing: CGFloat.ds("s-1")) {
            if let range, let span, let button = pill.selection {
                selectionPlay(button, range: range, span: span)
                cycle(range)
                Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            }
            piecePlay(pill.piece, range: range, alone: pill.selection == nil)
        }
    }

    /// The selection's cobalt Play.
    private func selectionPlay(_ button: TransportPill.SelectionButton, range: Range<Double>, span: (first: String, last: String)) -> some View {
        Button {
            player.playSelection(range)
        } label: {
            HStack(spacing: 6) {
                Icon(name: button.playing ? "pause" : "playf", size: CGFloat.ds("icon-s"))
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
                Text(selectionWords(button.label))
                    .monospacedDigit()
                    .fontWeight(.semibold)
                    .foregroundStyle(Color.ds("accent"))
                    .padding(.trailing, CGFloat.ds("s-2"))
            }
        }
        .buttonStyle(.plain)
        .keyboardShortcut(.space, modifiers: [])
        .accessibilityLabel(button.playing ? text["transport.pause"] : text.format("pill.playSelection", span.first, span.last))
        .accessibilityHint(text["pill.playSelectionHint"])
    }

    /// Цикл: the selection goes round while it is on.
    private func cycle(_ range: Range<Double>) -> some View {
        Button {
            player.toggleCycle(selection: range)
        } label: {
            HStack(spacing: 6) {
                Icon(name: "loop")
                Text(text["focus.loop"])
            }
        }
        .buttonStyle(DSItemStyle(on: player.cycle))
        .accessibilityAddTraits(player.cycle ? .isSelected : [])
        .accessibilityHint(text["pill.cycleHint"])
    }

    /// The piece's ink Play: filled while it has the position or stands alone, else a ring beside the cobalt one.
    private func piecePlay(_ button: TransportPill.PieceButton, range: Range<Double>?, alone: Bool) -> some View {
        let icon = button.icon == .pause ? "pause" : button.icon == .play ? "playf" : "play"
        return Button {
            player.playPiece(selection: range)
        } label: {
            HStack(spacing: 6) {
                Icon(name: icon, size: CGFloat.ds("icon-s"))
                    .foregroundStyle(button.filled ? Color.ds("surface") : Color.ds("ink"))
                    .frame(width: 40, height: 40)
                    .background(button.filled ? Color.ds("ink") : Color.clear, in: Circle())
                    .overlay(Circle().strokeBorder(Color.ds("ink"), lineWidth: button.filled ? 0 : 1.5))
                Text(pieceWords(button.label))
                    .monospacedDigit()
                    .fontWeight(.semibold)
                    .foregroundStyle(Color.ds("ink"))
                    .padding(.trailing, CGFloat.ds("s-2"))
            }
        }
        .buttonStyle(.plain)
        .keyboardShortcut(alone ? KeyboardShortcut(.space, modifiers: []) : nil)
        .accessibilityLabel(button.icon == .pause ? text["transport.pause"] : text.format("pill.playPiece", player.placeLabel))
        .accessibilityHint(alone ? "" : text["pill.playPieceHint"])
    }

    private func selectionWords(_ label: TransportPill.SelectionLabel) -> String {
        switch label {
        case .span(let first, let last):
            return text.format("pill.span", first, last)
        case .inside(let position, let first, let last):
            return text.format("pill.inside", position, first, last)
        }
    }

    private func pieceWords(_ label: TransportPill.PieceLabel) -> String {
        switch label {
        case .from(let position):
            return text.format("pill.from", position)
        case .pauseAt(let position):
            return text.format("pill.pauseAt", position)
        }
    }
}

extension ScoreController {
    /// Whether the one playback position holds (v0.5.2): the modes are on (Settings · Experimental) and the score is not in
    /// focus, which plays as before.
    var onePosition: Bool {
        mode != nil && !focused
    }
}

extension Player {
    /// Space under the one playback position: the selection's Play when there is one, else the piece's.
    func space(selection: Range<Double>?) {
        if let selection {
            playSelection(selection)
        } else {
            playPiece(selection: nil)
        }
    }
}
