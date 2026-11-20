import Foundation

/// A passage practised on the score: the selection just made played by the playback model, which «↻ Место» on a draft's
/// view and the assistant's practice action (v1.2, `practice.suggest`) both use, so there is one way to loop a passage.
extension ScoreController {
    /// The selection just made played by the playback model from its start: round and round with Цикл on (`looped`), or
    /// once with it off. Changes the player's selection, Цикл and transport.
    func playSelection(looped: Bool, on player: Player) {
        player.hold(selection: focusModel.playRange?.range)
        if player.cycle != looped { player.apply(.cycle) }
        if player.state == .stopped { player.apply(.press) }
    }

    /// The assistant's practice action tapped: its bars selected and brought into view, the tempo and, where the notes
    /// carry both hands, the hand set as a resume point sets them, and the bars played (`playSelection`). Changes the
    /// selection, the player's tempo, hands, Цикл and transport.
    func practise(_ offer: AssistantPracticeOffer, on player: Player) async {
        player.tempoPercent = offer.tempo
        if player.handsHeld.canSeparate { player.hands = PlayerHands(rawValue: offer.hand) ?? .both }
        await select(request: Self.barsRequest(offer.first, offer.last))
        guard let selection = selectionModel.selection else { return }
        await reveal(selection.onset)
        playSelection(looped: offer.loop, on: player)
    }
}
