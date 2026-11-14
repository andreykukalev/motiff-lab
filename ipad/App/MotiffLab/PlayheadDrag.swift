import Foundation

/// A playhead held by the pen or a finger (v0.5.2, #495 UX-08 and UX-10, the architect's design playback-model-v0.5.2, slice
/// I3): which one, whether playback played as it was taken, so it plays on when it is let go (T16), and the place it was
/// last dragged to, which the release rules read before the engine has reported it.
struct PlayheadDrag: Equatable {
    var handle: PlaybackModel.Handle
    var resume: Bool
    var last: Double
}

/// What the score (score.js) and the roll (roll.js) report of a playhead's handle: `{ drag: { phase, handle, tick } }`. The
/// page puts the place on the sixteenth grid and in its bar or the selection (Scripts/playback/playhead-drag.ts) and sends
/// it at most once a frame.
enum PlayheadGesture: Equatable {
    /// T14: the handle taken, with its first move.
    case grab(PlaybackModel.Handle)
    /// T15: dragged to a tick.
    case move(Double)
    /// T16: let go, or lost (a cancel, a lost capture, the view going).
    case release

    init?(message: Any?) {
        guard let fields = message as? [String: Any], let phase = fields["phase"] as? String else { return nil }
        switch phase {
        case "grab":
            guard let handle = (fields["handle"] as? String).flatMap(PlaybackModel.Handle.init(rawValue:)) else { return nil }
            self = .grab(handle)
        case "move":
            guard let tick = (fields["tick"] as? NSNumber)?.doubleValue else { return nil }
            self = .move(tick)
        case "release":
            self = .release
        default:
            return nil
        }
    }
}

extension Player {
    /// A playhead's handle on the score or the roll (T14–T16): taken, playback stands; dragged, the position goes there; let
    /// go, the release rules run on the place it was let go at, and playback goes on when it played. A new handle taken while
    /// another is still held (its view went away mid-drag) lets the old one go first.
    func handle(_ gesture: PlayheadGesture) {
        switch gesture {
        case .grab(let which):
            if drag != nil { self.handle(.release) }
            drag = PlayheadDrag(handle: which, resume: PlaybackModel.playing(state), last: tick)
            apply(.grab)
        case .move(let at):
            guard var held = drag else { return }
            held.last = apply(.drag(held.handle, to: at), from: heldState(held)).tick
            drag = held
        case .release:
            guard let held = drag else { return }
            drag = nil
            apply(.release(held.handle, resume: held.resume), from: heldState(held))
        }
    }

    /// The model as the drag holds it: at the place last dragged to, and standing, as the grab made it, though the engine
    /// may not have reported either yet.
    private func heldState(_ held: PlayheadDrag) -> PlaybackModel.State {
        var state = model
        state.tick = held.last
        state.transport = .stopped
        return state
    }
}
