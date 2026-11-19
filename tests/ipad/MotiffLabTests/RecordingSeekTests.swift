import AVFoundation
import XCTest
@testable import MotiffLab

/// The «Исполнение» capsule's slider (Andrei, v0.8 finding 20): it moves the recording's own player to a place, kept within
/// the recording, on release, and the time beside it says where.
@MainActor
final class RecordingSeekTests: XCTestCase {
    /// Two seconds of silence in a file the test removes after it.
    private func silence() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("seek-\(UUID().uuidString).caf")
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 8000, channels: 1))
        let buffer = try XCTUnwrap(AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 16000))
        buffer.frameLength = 16000
        let file = try AVAudioFile(forWriting: url, settings: format.settings)
        try file.write(from: buffer)
        return url
    }

    func testTheSliderMovesTheSoundWithinTheRecording() throws {
        let listening = RecordingListening()
        listening.load(try silence())
        XCTAssertEqual(listening.length, 2000, "two seconds of silence")
        listening.seek(to: 1200)
        XCTAssertEqual(listening.elapsed, 1200)
        XCTAssertEqual(listening.heardAt, 1200, "the player itself is there")
        listening.seek(to: 9000)
        XCTAssertEqual(listening.heardAt, 2000, "never past the end")
        XCTAssertEqual(listening.elapsed, 2000)
        XCTAssertFalse(listening.playing, "at the end the sound stops")
        listening.seek(to: -5)
        XCTAssertEqual(listening.heardAt, 0, "never before the start")
    }

    /// While the finger holds the slider only the time shown moves; the sound follows on release, so it does not stutter.
    func testTheSoundMovesOnReleaseNotOnEveryStep() throws {
        let listening = RecordingListening()
        listening.load(try silence())
        listening.seek(to: 1200)
        listening.hold()
        listening.seek(to: 300)
        listening.seek(to: 500)
        XCTAssertEqual(listening.elapsed, 500, "the time shown follows the finger")
        XCTAssertEqual(listening.heardAt, 1200, "the sound stays until release")
        listening.release()
        XCTAssertEqual(listening.heardAt, 500)
    }

    /// v0.10, Andrei's finding 24: the sound goes on while a capsule shows it, also as the screen hands the piece over to
    /// its other view, whichever capsule goes or comes first, and under a view shown over the piece; it stops once none
    /// shows it.
    func testTheRecordingGoesOnFromOneScreenToTheNext() throws {
        let url = try silence()
        let listening = RecordingListening()
        listening.shown(url)
        listening.toggle(url)
        XCTAssertTrue(listening.playing)
        listening.handOver()
        listening.hidden()
        XCTAssertTrue(listening.playing, "handed over, the old screen's capsule leaves it on")
        listening.shown(url)
        XCTAssertTrue(listening.playing, "the next screen hears the same sound")
        listening.handOver()
        listening.shown(url)
        listening.hidden()
        XCTAssertTrue(listening.playing, "the next screen's capsule came first")
        listening.shown(url)
        listening.hidden()
        XCTAssertTrue(listening.playing, "a view shown over the piece goes, the screen's capsule stays")
        listening.hidden()
        XCTAssertFalse(listening.playing, "no capsule shows it: the sound stops")
    }

    /// v0.10 s-2 review: once no capsule shows the recording, the sound stops and the player lets its file go.
    func testARecordingNoCapsuleShowsIsLetGo() throws {
        let url = try silence()
        let listening = RecordingListening()
        listening.shown(url)
        listening.toggle(url)
        listening.hidden()
        XCTAssertFalse(listening.playing)
        XCTAssertEqual(listening.length, 0, "the player let the file go")
        listening.shown(url)
        XCTAssertEqual(listening.length, 2000, "the next capsule loads it again")
    }

    /// v0.10 s-2 review: a hand-over that came to nothing (the next screen went while it opened, or the switch never ran)
    /// stops the sound once no capsule shows it, and ✕ is never stuck behind it.
    func testAHandOverThatCameToNothingIsNotHeardOn() throws {
        let url = try silence()
        let listening = RecordingListening()
        listening.shown(url)
        listening.toggle(url)
        listening.handOver()
        listening.hidden()
        XCTAssertTrue(listening.playing, "on its way to the next screen")
        listening.cancelHandOver()
        XCTAssertFalse(listening.playing, "the next screen went before it showed the recording")

        listening.shown(url)
        listening.toggle(url)
        listening.handOver()
        listening.cancelHandOver()
        XCTAssertTrue(listening.playing, "the switch never ran: the screen's capsule still shows it")
        listening.hidden()
        XCTAssertFalse(listening.playing, "and its Back stops it")
    }

    /// v0.10 s-2 review: the next screen's sources read, the recording handed over is heard on when its switch shows it; it
    /// stops when the switch does not, when the sources were not read, or when the screen went while they were read.
    func testTheNextScreenHearsTheRecordingHandedOrStopsIt() throws {
        let url = try silence()
        let file = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("take-handed.motiff.json")
        var layers = SourceLayers()
        layers.working = "n1"
        layers.views = [SourceLayers.ViewEntry(id: "r1", kind: "performance", group: nil, working: false, hidden: false, op: "recording")]
        let cases: [(read: Bool, gone: Bool, views: [SourceLayers.ViewEntry], adopted: Bool)] = [
            (true, false, layers.views, true), (true, true, layers.views, false), (false, false, layers.views, false), (true, false, [], false),
        ]
        for (read, gone, views, adopted) in cases {
            var shown = layers
            shown.views = views
            let listening = RecordingListening()
            listening.shown(url)
            listening.toggle(url)
            listening.handOver()
            listening.hidden()
            let next = SourcesController(layers: shown)
            next.listening = listening
            SourcesHandover.give(performance: "r1", to: file)
            next.takeHanded(by: file, read: read, gone: gone)
            XCTAssertEqual(next.performance, adopted ? "r1" : nil, "read \(read), gone \(gone), shown \(!views.isEmpty)")
            XCTAssertEqual(listening.playing, adopted, "heard on only where it is shown")
            XCTAssertNil(SourcesHandover.performance(for: file), "read once")
            listening.stop()
        }
    }

    /// v0.10 s-2 review: a screen that goes without handing the piece over takes the recording with it, and withdraws
    /// what it handed to a screen that never came.
    func testAScreenLeavingWithoutHandingOverStopsTheRecording() throws {
        let url = try silence()
        let file = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("leave.motiff.json")
        let other = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("leave-pdf.motiff.json")
        let listening = RecordingListening()
        let sources = SourcesController()
        sources.listening = listening
        sources.performance = "r1"
        listening.shown(url)
        listening.toggle(url)
        sources.handOver(to: other)
        listening.hidden()
        sources.leave(file)
        XCTAssertFalse(listening.playing)
        XCTAssertNil(SourcesHandover.performance(for: other), "the screen that never came does not hear it later")
    }
}
