import Foundation

/// ScoreController's part of the playing on the page (0018 section 7, #479 item 19): where the player is, the way back to
/// it, and the one playback position drawn with the selection it turns cobalt in.
extension ScoreController {
    /// Where the player is (0018 section 7): the notes sounding at the tick turn cobalt on the page, or on the card in focus,
    /// and while playing the page keeps the system with them below the bar. `follow` (a new Play) follows again at once
    /// after the player scrolled by hand. Called with each position the player reports, about thirty times a second.
    func playhead(_ tick: Double, playing: Bool, follow: Bool = false) {
        guard shown else { return }
        // The page follows the playing unless the player turned that off ("playback.follow", testing item 4).
        let data: [String: Any] = ["tick": tick, "playing": playing, "follow": follow, "auto": DeviceSettings.shared.isOn("playback.follow")]
        scorePage.post(.playing, data)
    }

    /// Back to playback: the page scrolls to the playhead and follows again.
    func returnToPlayhead() {
        guard shown else { return }
        Task { await scorePage.send(.returnToPlayhead) }
    }

    /// The one playback position the page draws (v0.5.2, #479 item 19): `{ selection: {start, end} or null }`, inside which it
    /// is the cobalt line and outside it the ink track under its bar; nil draws the one playhead as before.
    func showPlayheads(_ data: [String: Any]?) {
        playheadsWanted = data
        guard shown else { return }
        let key = data.map { fields in fields.keys.sorted().map { name in "\(name)=\(String(describing: fields[name] ?? ""))" }.joined(separator: ",") } ?? ""
        guard key != playheadsShown else { return }
        playheadsShown = key
        Task { await sendPlayheadsNow() }
    }

    func sendPlayheadsNow() async {
        let value: Any = playheadsWanted.map { $0 as Any } ?? NSNull()
        await scorePage.send(.playheads, value)
    }
}
