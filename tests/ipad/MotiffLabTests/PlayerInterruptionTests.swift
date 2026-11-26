import AVFoundation
import XCTest

@testable import MotiffLab

/// The player through what the system does to the iPad's audio (v1.4 r-1b): an interruption shows the music paused at
/// once and keeps it so, the loop and the place kept, and Play goes on from there; the keeper says whether it could start
/// the engine, so a Play it cannot honour is not left waiting to sound by itself; a sound that cannot load says so.
@MainActor
final class PlayerInterruptionTests: XCTestCase {
    /// Waits for `done`, at most `seconds`.
    private func until(_ seconds: Double = 5, _ done: () -> Bool) async {
        let end = Date().addingTimeInterval(seconds)
        while !done() && Date() < end {
            try? await Task.sleep(for: .milliseconds(10))
        }
    }

    private func interruptionBegins() {
        NotificationCenter.default.post(
            name: AVAudioSession.interruptionNotification, object: nil,
            userInfo: [AVAudioSessionInterruptionTypeKey: AVAudioSession.InterruptionType.began.rawValue]
        )
    }

    func testAnInterruptionShowsTheMusicPausedAtOnceAndPlayGoesOnFromThere() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let player = Player()
        defer { player.shutdown() }
        await player.load(piece: piece.id)
        let ready = player.ready
        let problem = player.problem ?? ""
        XCTAssertTrue(ready, "the sound loaded: \(problem)")
        let loop = 0.0..<7680.0
        player.setLoop(loop, play: false)
        player.toggle()
        await until { player.state == .playing && player.tick > 480 }
        let playing = player.state
        XCTAssertEqual(playing, .playing)

        interruptionBegins()
        // The keeper hears it on the main queue, posted from it: at once.
        await Task.yield()
        let atOnce = player.state
        let heard = player.tick
        XCTAssertEqual(atOnce, .stopped, "paused at once, not playing on until the engine reports")

        // A few polls later: still paused, where it was heard. The simulated interruption leaves the engine running, so it
        // reports where it paused, ahead of what was heard by the output's latency; never a playhead running on.
        try await Task.sleep(for: .milliseconds(300))
        let later = player.state
        let waited = player.tick
        let kept = player.loop
        XCTAssertEqual(later, .stopped, "no report rendered before the pause shows it playing again")
        XCTAssertEqual(waited, heard, accuracy: Player.heardAhead * 960, "the playhead waits where the music was heard")
        XCTAssertEqual(kept, loop, "the loop set before is kept")

        player.toggle()
        await until { player.state == .playing }
        let again = player.state
        let from = player.tick
        XCTAssertEqual(again, .playing)
        XCTAssertEqual(from, waited, accuracy: Player.heardAhead * 960, "Play goes on from where it paused, not from the loop's start")
    }

    func testTheKeeperSaysWhetherTheEngineRuns() throws {
        let engine = AVAudioEngine()
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 2))
        let node = AVAudioSourceNode(format: format) { _, _, _, _ in noErr }
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        defer { engine.stop() }

        let unwanted = EngineKeeper(engine, wanted: { false }) {}
        let refused = unwanted.restart()
        let stillStopped = !engine.isRunning
        XCTAssertFalse(refused, "an engine its owner does not want now stays stopped")
        XCTAssertTrue(stillStopped)

        let keeper = EngineKeeper(engine) {}
        let started = keeper.restart()
        let running = engine.isRunning
        let again = keeper.restart()
        XCTAssertTrue(started, "a stopped engine started again")
        XCTAssertTrue(running)
        XCTAssertTrue(again, "a running engine runs")
    }

    /// A sound that cannot load says so in the interface's language (v1.4 r-1b), not in music-core's English.
    func testASoundThatCannotLoadSaysSoInTheInterfacesLanguage() async {
        let player = Player()
        defer { player.shutdown() }
        // A piece music-core does not hold: no playback score.
        await player.load(piece: 9_000_779)
        let problem = player.problem
        let ready = player.ready
        XCTAssertEqual(problem, UIText.current["playback.failed"])
        XCTAssertFalse(problem?.contains("music-core") ?? true)
        XCTAssertFalse(ready)
    }
}
