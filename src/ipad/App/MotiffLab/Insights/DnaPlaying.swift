import SwiftUI

/// What Piece DNA plays: a tapped mark or section, a card's passage once, the map from its start; and leaving for the score.
extension DnaScreen {
    /// The first tap plays the passage; a second tap on the same one opens the score there.
    func tap(_ range: Range<Double>) {
        if heard == range {
            leave(range, insights: false)
            return
        }
        hear(range)
    }

    /// A mark or a section: its passage loops until another is chosen, the map's Stop is pressed or the map is left.
    private func hear(_ range: Range<Double>) {
        heard = range
        cardHeard = nil
        playing = true
        player.setLoop(range)
    }

    /// A card's Hear: its passage plays once from its start and stops at its end.
    func hearOnce(_ range: Range<Double>) {
        heard = range
        cardHeard = range
        playing = true
        player.setLoop(range, from: range.lowerBound, once: true)
    }

    /// Plays the piece from its start with the map following; pressed again, it stops. A card's passage has its own
    /// Stop, so while one sounds this starts the map in its place.
    func playMap() {
        if DnaPlaying.map(playing: playing, card: cardHeard, state: player.state) {
            stop()
            return
        }
        player.setLoop(nil)
        cardHeard = nil
        playing = true
        player.play(from: map.start)
    }

    func stop() {
        guard playing else { return }
        playing = false
        cardHeard = nil
        player.setLoop(nil)
        if player.state != .stopped { player.toggle() }
    }

    func leave(_ range: Range<Double>, insights: Bool) {
        stop()
        showInScore(range, insights)
    }
}

/// Which of the map's buttons reads Stop (#582 item 14): playback is stopped where it was started.
enum DnaPlaying {
    /// A card's Hear reads Stop while the passage it started plays: the engine still loops that passage, once, and sounds.
    /// It reads Hear again when the passage ends, when Stop is pressed, or when another passage or the map starts.
    static func card(_ range: Range<Double>, heard: Range<Double>?, loop: Range<Double>?, state: TransportState) -> Bool {
        heard == range && loop == range && state != .stopped
    }

    /// The map's own button reads Stop while the map, a mark or a section plays, never for a card's passage, which has
    /// its Stop on the card.
    static func map(playing: Bool, card: Range<Double>?, state: TransportState) -> Bool {
        playing && card == nil && state != .stopped
    }
}
