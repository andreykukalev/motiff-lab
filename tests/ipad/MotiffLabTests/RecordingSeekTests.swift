import AVFoundation
import XCTest
@testable import MotiffLab

/// The «Исполнение» capsule's slider (Andrei, v0.8 finding 20): it moves the recording's own player to a place, kept within
/// the recording, and the time beside it says where.
@MainActor
final class RecordingSeekTests: XCTestCase {
    func testTheSliderMovesTheSoundWithinTheRecording() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("seek-\(UUID().uuidString).caf")
        defer { try? FileManager.default.removeItem(at: url) }
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 8000, channels: 1))
        let buffer = try XCTUnwrap(AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 16000))
        buffer.frameLength = 16000
        do {
            let file = try AVAudioFile(forWriting: url, settings: format.settings)
            try file.write(from: buffer)
        }
        let listening = RecordingListening()
        listening.load(url)
        XCTAssertEqual(listening.length, 2000, "two seconds of silence")
        listening.seek(to: 1200)
        XCTAssertEqual(listening.elapsed, 1200)
        listening.seek(to: 9000)
        XCTAssertEqual(listening.elapsed, 2000, "never past the end")
        listening.seek(to: -5)
        XCTAssertEqual(listening.elapsed, 0, "never before the start")
    }
}
