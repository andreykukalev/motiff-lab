import AVFoundation
import XCTest

extension XCTestCase {
    /// Two seconds of silence in a sound file of the temporary folder, removed after the test: a recording to load and play.
    func silence() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("silence-\(UUID().uuidString).caf")
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        let format = try XCTUnwrap(AVAudioFormat(standardFormatWithSampleRate: 8000, channels: 1))
        let buffer = try XCTUnwrap(AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 16000))
        buffer.frameLength = 16000
        let file = try AVAudioFile(forWriting: url, settings: format.settings)
        try file.write(from: buffer)
        return url
    }
}
