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
}
