import XCTest
@testable import MotiffLab

/// The waveform of a piece that is only its recording (v1.1, finding 29): its columns from the loudness of every 100 ms,
/// and a tap across it putting the sound at the moment under it, kept within the recording.
final class RecordingWaveTests: XCTestCase {
    func testEachColumnIsItsLoudestStretchAsAShareOfTheLoudest() {
        let wave = RecordingWave(levels: [0, 100, 50, 200, 0, 0, 400, 100])
        XCTAssertEqual(wave.columns(4), [0.25, 0.5, 0, 1])
        XCTAssertEqual(wave.columns(1), [1])
        XCTAssertEqual(wave.columns(16).count, 16, "more columns than levels repeat a level")
        XCTAssertEqual(wave.columns(0), [])
        XCTAssertEqual(RecordingWave(levels: []).columns(4), [])
        XCTAssertEqual(RecordingWave(levels: [0, 0]).columns(2), [0, 0], "silence draws no division by nothing")
    }

    func testATapPutsTheSoundAtTheMomentUnderItWithinTheRecording() {
        XCTAssertEqual(RecordingWave.moment(at: 0.5, length: 60_000), 30_000)
        XCTAssertEqual(RecordingWave.moment(at: -0.2, length: 60_000), 0)
        XCTAssertEqual(RecordingWave.moment(at: 1.4, length: 60_000), 60_000)
        XCTAssertEqual(RecordingWave.share(of: 15_000, length: 60_000), 0.25)
        XCTAssertEqual(RecordingWave.share(of: 90_000, length: 60_000), 1)
        XCTAssertEqual(RecordingWave.share(of: 1_000, length: 0), 0, "a recording not loaded yet keeps the cursor at the start")
    }

    /// The waveform is read a block at a time and kept only as its levels (an hour of sound is never held whole), the same
    /// levels as the whole sound read gives.
    func testTheLevelsReadABlockAtATimeAreTheWholeSoundsLevels() async throws {
        let tone = (0..<44_100).map { Int16(Double($0 % 22_050) / 3 * sin(Double($0) * 2 * .pi * 440 / RecordingSound.sampleRate)) }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("wave-\(UUID().uuidString).wav")
        try RecordingSound.wav(tone[...]).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let sound = try XCTUnwrap(RecordingSound.read(url))
        let levels = try XCTUnwrap(RecordingSound.levels(of: url))
        XCTAssertEqual(levels, sound.levels(from: 0, to: sound.milliseconds))
        XCTAssertEqual(levels.count, 20, "one level every 100 ms of two seconds")
        let wave = await RecordingWave.read(url)
        XCTAssertEqual(wave?.levels, levels)
        let nothing = FileManager.default.temporaryDirectory.appendingPathComponent("wave-\(UUID().uuidString).wav")
        try Data([1, 2, 3]).write(to: nothing)
        defer { try? FileManager.default.removeItem(at: nothing) }
        XCTAssertNil(RecordingSound.levels(of: nothing), "a file with no sound the iPad reads")
    }

    func testVoiceOverStepsThroughTheRecordingFiveSecondsAtATime() {
        XCTAssertEqual(RecordingWave.step, 5_000)
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["recording.wave", "recording.waveHint", "recording.noContents", "recording.unreadable"] {
                XCTAssertNotEqual(text[key], key, "\(language) \(key)")
            }
        }
    }
}
