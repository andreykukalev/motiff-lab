import SwiftUI

/// Piece DNA's playhead (Andrei, v0.8 findings 14 and 15): where the player is, always shown, as the score's 2 px accent
/// line across the lanes, with a handle on top for a finger in the gap over them, drawn as the roll's marker
/// (`drawPosition` in roll-strips.ts): a disc ringed in the surface, solid while it plays, hollow while it stands. Dragged by
/// its handle it is the roll's black playhead, through the same `PlayheadGesture` and `Player.handle` (T14–T16): playback
/// stands while it is held and plays on from where it is let go, so while playing the sound moves there on release. The
/// header's Play plays from it.
struct DnaPlayhead: View {
    let map: DnaMap
    @ObservedObject var player: Player
    let text: UIText
    /// The lane names' column: the bar axis starts after it.
    let nameWidth: CGFloat
    /// The handle taken: what the map plays gives way to the piece (DnaScreen.playheadTaken).
    let taken: () -> Void

    /// Where the handle is held now, shown at once while the engine follows; nil while it is not held.
    @State private var held: Double?

    /// The roll's marker (roll-strips.ts DOT_RADIUS, roll-modes.ts RING and HOLLOW).
    private let radius: CGFloat = 6
    private let ring: CGFloat = 2
    private let hollow: CGFloat = 2
    private static let space = "dnaPlayhead"

    var body: some View {
        GeometryReader { proxy in
            let axis = max(1, proxy.size.width - nameWidth)
            let at = nameWidth + CGFloat(map.fraction(of: held ?? player.tick)) * axis
            // The handle sits in the gap over the lanes (DnaScreen's spacing), its line running down from it.
            let lift = CGFloat.ds("s-5") / 2
            ZStack(alignment: .topLeading) {
                Rectangle()
                    .fill(Color.ds("accent"))
                    .frame(width: 2, height: proxy.size.height + lift)
                    .offset(x: at - 1, y: -lift)
                    .allowsHitTesting(false)
                handle
                    .position(x: at, y: -lift)
                    .gesture(drag(axis))
            }
            .coordinateSpace(name: Self.space)
        }
        .onDisappear {
            // Lost with the view mid-drag: let go, as the score's and the roll's handles are.
            if held != nil {
                held = nil
                player.handle(.release)
            }
        }
    }

    private var handle: some View {
        let playing = player.state != .stopped
        return ZStack {
            Circle().fill(Color.ds("surface")).frame(width: 2 * (radius + ring), height: 2 * (radius + ring))
            Circle().fill(Color.ds("accent")).frame(width: 2 * radius, height: 2 * radius)
            if !playing {
                Circle().fill(Color.ds("surface")).frame(width: 2 * (radius - hollow), height: 2 * (radius - hollow))
            }
        }
        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        .contentShape(Rectangle())
        .accessibilityElement()
        .accessibilityLabel(text["dna.playhead"])
        .accessibilityValue(player.barNumber(at: player.tick).map { text.format("dna.bar", String($0)) } ?? "")
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: nudge(1)
            case .decrement: nudge(-1)
            @unknown default: break
            }
        }
    }

    /// The handle dragged along the bar axis: taken, moved to the place under the finger, let go.
    private func drag(_ axis: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .named(Self.space))
            .onChanged { value in
                let tick = map.tick(at: Double((value.location.x - nameWidth) / axis))
                if held == nil {
                    player.handle(.grab(.black))
                    taken()
                }
                held = tick
                player.handle(.move(tick))
            }
            .onEnded { _ in
                held = nil
                player.handle(.release)
            }
    }

    /// VoiceOver's swipe up or down: the playhead to the next bar's start, or back to this bar's or the one before, taken
    /// and let go as a drag is.
    private func nudge(_ direction: Int) {
        let now = player.tick
        let starts = map.barStarts.dropLast()
        guard let to = direction > 0 ? starts.first(where: { $0 > now + 0.5 }) : starts.last(where: { $0 < now - 0.5 }) else { return }
        player.handle(.grab(.black))
        taken()
        player.handle(.move(to))
        player.handle(.release)
    }
}
