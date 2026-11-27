import AVFoundation
import UIKit
import XCTest

@testable import MotiffLab

/// PLAY-07 (docs/ux-baseline.md; answers 17 and 19 of 1.4, built in 1.4 by a-1, #812; answers 29 and 30, built in 1.5 by
/// a-2, #823): what the player hears pauses when the app goes to the background (the screen locked too) or the headphones
/// go, and stays paused when the player comes back: the piece's notes with the metronome clicking with them, «Образец»,
/// the metronome on its own and a recording in its own player. Taken through the players the screens own, with the
/// system's notifications posted as iOS posts them; the sound itself is the device's.
@MainActor
final class PlaybackBaselineTests: XCTestCase {
    private var opened: [Int32] = []

    override func tearDown() async throws {
        Metronome.shared.stop()
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// The app gone to the background, as iOS tells it also when the screen locks.
    private func appGoesAway() async {
        NotificationCenter.default.post(name: UIApplication.didEnterBackgroundNotification, object: nil)
        await Task.yield()
    }

    /// The audio route changed for `reason`: `.oldDeviceUnavailable` is the headphones gone.
    private func route(_ reason: AVAudioSession.RouteChangeReason) async {
        let info = [AVAudioSessionRouteChangeReasonKey: reason.rawValue]
        NotificationCenter.default.post(name: AVAudioSession.routeChangeNotification, object: nil, userInfo: info)
        await Task.yield()
    }

    /// Two seconds of silence as a recording kept on this iPad, removed after the test.
    private func recording() throws -> URL {
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 8000, channels: 1))
        let seconds = try XCTUnwrap(AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 16000))
        seconds.frameLength = seconds.frameCapacity
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("play-07-\(UUID().uuidString).caf")
        try AVAudioFile(forWriting: url, settings: format.settings).write(from: seconds)
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        return url
    }

    func testPLAY07TheNotesTheirMetronomeAndTheSamplePauseInTheBackgroundAndWithTheHeadphonesGone() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        opened.append(piece.id)
        let player = Player()
        defer { player.shutdown() }
        await player.load(piece: piece.id)
        let ready = player.ready
        XCTAssertTrue(ready, "the piece's sound loaded")
        player.toggle()
        await until { player.state == .playing && player.tick > 480 }
        let metronome = Metronome.shared
        metronome.start()

        await route(.newDeviceAvailable)
        let pluggedIn = player.state
        XCTAssertEqual(pluggedIn, .playing, "headphones plugged in: it plays on")
        await route(.oldDeviceUnavailable)
        let unplugged = player.state
        let clicking = metronome.running
        XCTAssertEqual(unplugged, .stopped, "the headphones gone: paused, nothing goes on from the speaker")
        XCTAssertFalse(clicking, "the metronome clicking with the piece stops with it")
        player.toggle()
        await until { player.state == .playing }
        let again = player.state
        XCTAssertEqual(again, .playing, "Play plays on")
        await appGoesAway()
        let away = player.state
        XCTAssertEqual(away, .stopped, "the app gone to the background: paused at once")
        try await Task.sleep(for: .milliseconds(300))
        let back = player.state
        XCTAssertEqual(back, .stopped, "still paused when the player comes back")

        let answer = await PieceCore(piece: piece.id).playback()
        let score = try XCTUnwrap(answer.value.map(PlaybackScore.init), answer.message ?? "the piece's playback score")
        let sample = ReferenceController()
        defer { sample.stop() }
        await sample.listen("chords", score, loop: true)
        let sounding = sample.playing
        XCTAssertEqual(sounding, "chords")
        await appGoesAway()
        let stopped = sample.playing
        XCTAssertNil(stopped, "«Образец» stops too, its tab showing Play again")
    }

    func testPLAY07TheMetronomeOnItsOwnAndARecordingStopInTheBackgroundAndWithTheHeadphonesGone() async throws {
        let metronome = Metronome()
        defer { metronome.stop() }
        metronome.start()
        await route(.oldDeviceUnavailable)
        let unplugged = metronome.running
        XCTAssertFalse(unplugged, "the metronome on its own: stopped with the headphones gone")
        metronome.start()
        await appGoesAway()
        let away = metronome.running
        XCTAssertFalse(away, "and in the background")

        let url = try recording()
        let listening = RecordingListening()
        defer { listening.stop() }
        listening.toggle(url)
        let heard = listening.playing
        XCTAssertTrue(heard, "the recording plays in its own player")
        await route(.oldDeviceUnavailable)
        let paused = listening.playing
        XCTAssertFalse(paused, "the headphones gone: the recording pauses")
        listening.toggle(url)
        await appGoesAway()
        let inBackground = listening.playing
        let shown = listening.elapsed
        let at = listening.heardAt
        XCTAssertFalse(inBackground, "the app gone to the background: paused")
        XCTAssertEqual(shown, at, "its time shows where it paused")
    }
}
