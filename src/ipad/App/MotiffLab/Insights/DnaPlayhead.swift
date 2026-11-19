import SwiftUI

/// Piece DNA's playhead (Andrei, v0.8 findings 14 and 15): where the player is, always shown, as the score's 2 px accent
/// line across the lanes, with a handle on top for a finger in the gap over them, drawn as the roll's marker
/// (`drawPosition` in roll-strips.ts): a disc ringed in the surface, solid while it plays, hollow while it stands. Dragged by
/// its handle it is the roll's black playhead, through the same `PlayheadGesture` and `Player.handle` (T14–T16): taken where
/// it is, it moves by the finger's travel on the roll's sixteenth grid (DnaMap.tick(at:)); playback stands while it is held
/// and plays on from where it is let go, so while playing the sound moves there on release. The header's Play plays from it.
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
    /// Where it was taken: it moves by the finger's travel from there, so a touch beside its centre does not make it jump.
    @State private var from: Double?
    /// The drag under way. SwiftUI calls no onEnded for a drag it cancels (a scroll view taking the touch, a system
    /// gesture, a call), but this goes back to false then too, and the handle is let go (PlayheadGesture.release).
    @GestureState private var dragging = false

    /// The roll's marker (roll-strips.ts DOT_RADIUS, roll-modes.ts RING and HOLLOW).
    private let radius: CGFloat = 6
    private let ring: CGFloat = 2
    private let hollow: CGFloat = 2

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
        }
        .onChange(of: dragging) { _, now in
            if !now { letGo() }
        }
        // Lost with the view mid-drag: let go, as the score's and the roll's handles are.
        .onDisappear(perform: letGo)
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

    /// The handle dragged along the bar axis: taken where it is, moved by the finger's travel from there, let go.
    private func drag(_ axis: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .updating($dragging) { _, state, _ in state = true }
            .onChanged { value in
                guard let start = from else {
                    from = player.tick
                    held = player.tick
                    player.handle(.grab(.black))
                    taken()
                    return
                }
                let tick = map.tick(at: map.fraction(of: start) + Double(value.translation.width / axis))
                guard tick != held else { return }
                held = tick
                player.handle(.move(tick))
            }
            .onEnded { _ in letGo() }
    }

    /// The handle let go, or lost; nothing when it is not held.
    private func letGo() {
        guard held != nil else { return }
        held = nil
        from = nil
        player.handle(.release)
    }

    /// VoiceOver's swipe up or down: the playhead put down at the next bar's start, or back at this bar's or the one before,
    /// without sounding the bar it passes, taken and let go as a drag is.
    private func nudge(_ direction: Int) {
        let now = player.tick
        let starts = map.barStarts.dropLast()
        guard let to = direction > 0 ? starts.first(where: { $0 > now + 0.5 }) : starts.last(where: { $0 < now - 0.5 }) else { return }
        player.handle(.grab(.black))
        taken()
        player.handle(.place(to))
        player.handle(.release)
    }
}
