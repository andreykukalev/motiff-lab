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

    func testVoiceOverStepsThroughTheRecordingFiveSecondsAtATime() {
        XCTAssertEqual(RecordingWave.step, 5_000)
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["recording.wave", "recording.waveHint", "recording.noContents"] {
                XCTAssertNotEqual(text[key], key, "\(language) \(key)")
            }
        }
    }
}
