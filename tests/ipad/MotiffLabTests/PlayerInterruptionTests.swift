import AVFoundation
import UIKit
import XCTest

@testable import MotiffLab

/// The player through what the system does to the iPad's audio (v1.4 r-1b): an interruption shows the music paused at
/// once and keeps it so, the loop and the place kept, and Play goes on from there; the keeper says whether its engine runs,
/// so a Play it cannot honour is not left waiting to sound by itself; a sound that cannot load says so.
@MainActor
final class PlayerInterruptionTests: XCTestCase {
    private func interruptionBegins() {
        NotificationCenter.default.post(
            name: AVAudioSession.interruptionNotification, object: nil,
            userInfo: [AVAudioSessionInterruptionTypeKey: AVAudioSession.InterruptionType.began.rawValue]
        )
    }

    private func routeChanges(_ reason: AVAudioSession.RouteChangeReason) {
        NotificationCenter.default.post(
            name: AVAudioSession.routeChangeNotification, object: nil, userInfo: [AVAudioSessionRouteChangeReasonKey: reason.rawValue]
        )
    }

    /// The kit's piece opened and its sound loaded, playing round `loop`; the caller shuts the player down and closes the
    /// piece (`piece`).
    private func playing(_ loop: Range<Double>) async throws -> (player: Player, piece: Int32) {
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        let player = Player()
        await player.load(piece: piece.id)
        let ready = player.ready
        let problem = player.problem ?? ""
        XCTAssertTrue(ready, "the sound loaded: \(problem)")
        player.setLoop(loop, play: false)
        player.toggle()
        await until { player.state == .playing && player.tick > 480 }
        let started = player.state
        XCTAssertEqual(started, .playing)
        return (player, piece.id)
    }

    func testAnInterruptionShowsTheMusicPausedAtOnceAndPlayGoesOnFromThere() async throws {
        let loop = 0.0..<7680.0
        let (player, piece) = try await playing(loop)
        defer {
            player.shutdown()
            _ = MusicCore.close(piece: piece)
        }

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
        XCTAssertEqual(later, .stopped, "still paused a few polls later")
        XCTAssertEqual(waited, heard, accuracy: Player.heardAhead * 960, "the playhead waits where the music was heard")
        XCTAssertEqual(kept, loop, "the loop set before is kept")

        player.toggle()
        await until { player.state == .playing }
        let again = player.state
        let from = player.tick
        XCTAssertEqual(again, .playing)
        XCTAssertEqual(from, waited, accuracy: Player.heardAhead * 960, "Play goes on from where it paused, not from the loop's start")
    }

    /// v1.4 a-1, answers 17 and 19: the app gone to the background (the screen locked too) and the headphones gone pause
    /// the music at once and keep it paused, the loop kept; an output only added (headphones plugged in) plays on.
    func testTheBackgroundAndTheHeadphonesGonePauseTheMusicAndKeepItPaused() async throws {
        let loop = 0.0..<7680.0
        let (player, piece) = try await playing(loop)
        defer {
            player.shutdown()
            _ = MusicCore.close(piece: piece)
        }

        routeChanges(.newDeviceAvailable)
        await Task.yield()
        let pluggedIn = player.state
        XCTAssertEqual(pluggedIn, .playing, "an output added: the music plays on")
        routeChanges(.oldDeviceUnavailable)
        await Task.yield()
        let unplugged = player.state
        XCTAssertEqual(unplugged, .stopped, "the headphones gone: paused at once, not on from the speaker")

        player.toggle()
        await until { player.state == .playing }
        let again = player.state
        XCTAssertEqual(again, .playing)
        NotificationCenter.default.post(name: UIApplication.didEnterBackgroundNotification, object: nil)
        await Task.yield()
        let away = player.state
        XCTAssertEqual(away, .stopped, "the app gone to the background: paused at once")
        try await Task.sleep(for: .milliseconds(300))
        let back = player.state
        let kept = player.loop
        XCTAssertEqual(back, .stopped, "still paused when the player comes back")
        XCTAssertEqual(kept, loop, "the loop kept")
    }

    /// v1.4 a-1: a metronome clicking with the piece stops with a pause the system makes (here the headphones gone) rather
    /// than click on by itself from the speaker; a pause the player makes leaves it on, as before.
    func testAMetronomeWithThePieceStopsWithASystemPauseOnly() async throws {
        let (player, piece) = try await playing(0.0..<7680.0)
        let metronome = Metronome.shared
        defer {
            metronome.stop()
            player.shutdown()
            _ = MusicCore.close(piece: piece)
        }
        metronome.start()
        let clicking = metronome.running
        XCTAssertTrue(clicking, "the metronome clicks with the piece")
        player.toggle()
        await until { player.state == .stopped }
        let byThePlayer = metronome.running
        XCTAssertTrue(byThePlayer, "paused by the player: the metronome goes on as before")
        player.toggle()
        await until { player.state == .playing }
        routeChanges(.oldDeviceUnavailable)
        await Task.yield()
        let bySystem = metronome.running
        let stopped = player.state
        XCTAssertEqual(stopped, .stopped)
        XCTAssertFalse(bySystem, "paused by the system: the metronome stops with the piece")
    }

    /// v1.5 a-2, answer 29: the metronome clicking on its own, with no piece playing, stops as the app goes to the background
    /// (the screen locked too) and as the headphones go, as the music does; an output only added leaves it clicking.
    func testTheMetronomeOnItsOwnStopsInTheBackgroundAndWithTheHeadphonesGone() async {
        let metronome = Metronome()
        defer { metronome.stop() }
        metronome.start()
        let clicking = metronome.running
        XCTAssertTrue(clicking)
        routeChanges(.newDeviceAvailable)
        await Task.yield()
        let pluggedIn = metronome.running
        XCTAssertTrue(pluggedIn, "an output added: it clicks on")
        routeChanges(.oldDeviceUnavailable)
        await Task.yield()
        let unplugged = metronome.running
        XCTAssertFalse(unplugged, "the headphones gone: stopped, not on from the speaker")

        metronome.start()
        let again = metronome.running
        XCTAssertTrue(again, "its button starts it again")
        NotificationCenter.default.post(name: UIApplication.didEnterBackgroundNotification, object: nil)
        await Task.yield()
        let away = metronome.running
        let beat = metronome.beat
        XCTAssertFalse(away, "the app gone to the background: stopped")
        XCTAssertEqual(beat, -1, "no beat shown lit")
    }

    /// v1.4 a-1: the Reference's music paused by the system (the app gone to the background) shows stopped on its sheet.
    func testTheReferencesMusicShowsStoppedWhenTheSystemPausesIt() async throws {
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: opened.id) }
        let answer = await PieceCore(piece: opened.id).playback()
        let score = try XCTUnwrap(answer.value.map(PlaybackScore.init), answer.message ?? "the piece's playback score")
        let reference = ReferenceController()
        defer { reference.stop() }
        await reference.listen("chords", score, loop: true)
        let listening = reference.playing
        XCTAssertEqual(listening, "chords")
        NotificationCenter.default.post(name: UIApplication.didEnterBackgroundNotification, object: nil)
        await Task.yield()
        let after = reference.playing
        XCTAssertNil(after, "paused by the system: the tab shows Play again")
    }

    /// A route change says the output played through went only for `oldDeviceUnavailable`.
    func testOnlyAnOutputGoneIsADeviceGone() {
        XCTAssertTrue(EngineKeeper.deviceGone([AVAudioSessionRouteChangeReasonKey: AVAudioSession.RouteChangeReason.oldDeviceUnavailable.rawValue]))
        XCTAssertFalse(EngineKeeper.deviceGone([AVAudioSessionRouteChangeReasonKey: AVAudioSession.RouteChangeReason.newDeviceAvailable.rawValue]))
        XCTAssertFalse(EngineKeeper.deviceGone([AVAudioSessionRouteChangeReasonKey: AVAudioSession.RouteChangeReason.categoryChange.rawValue]))
        XCTAssertFalse(EngineKeeper.deviceGone(nil))
    }

    /// A node that renders silence, made outside the main actor: its block runs on the audio thread (as Player.sourceNode).
    nonisolated private static func silentNode(format: AVAudioFormat) -> AVAudioSourceNode {
        AVAudioSourceNode(format: format) { _, _, _, _ in noErr }
    }

    func testTheKeeperSaysWhetherTheEngineRuns() throws {
        let engine = AVAudioEngine()
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 48_000, channels: 2))
        let node = Self.silentNode(format: format)
        engine.attach(node)
        engine.connect(node, to: engine.mainMixerNode, format: format)
        defer { engine.stop() }

        let unwanted = EngineKeeper(engine, wanted: { false }) {}
        unwanted.restart()
        let refused = unwanted.running
        XCTAssertFalse(refused, "an engine its owner does not want now stays stopped")

        let keeper = EngineKeeper(engine) {}
        keeper.restart()
        let started = keeper.running
        keeper.restart()
        let again = keeper.running
        XCTAssertTrue(started, "a stopped engine started again")
        XCTAssertTrue(again, "a running engine runs on")
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
