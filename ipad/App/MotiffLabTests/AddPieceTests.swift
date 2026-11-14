import XCTest
@testable import MotiffLab

/// Add piece (v0.5.6, #533 section 8): From a recording is a card that picks the recording on a tap, with no switch to let
/// the sound go; what hearing needs first is said on the card in place of where the sound goes.
@MainActor
final class AddPieceTests: XCTestCase {
    func testABuildWithoutMotiffLabsServerSaysSoOnTheCardAndItWaits() throws {
        let text = UIText(language: "en")
        let blocked = try XCTUnwrap(RecordingChooser.blocker(Account(options: nil), text))

        XCTAssertEqual(blocked.words, text["version.recordingNoServer"])
        XCTAssertFalse(blocked.pro)
    }

    func testTheCardSaysWhereTheSoundGoesInBothLanguages() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in ["library.fromRecording", "library.fromRecordingHint", "library.fromRecordingLineDevice", "transcribe.choose", "transcribe.signInPrompt", "transcribe.proPrompt"] {
                XCTAssertNotEqual(text[key], key, "\(language) \(key)")
            }
        }
        XCTAssertTrue(UIText(language: "en")["library.fromRecordingLineDevice"].contains("Motiff Lab's scan server"))
    }

    func testAWayInCardTakesAProTagAndASecondLine() {
        let card = AddRoute(icon: "mic", title: "From a recording", hint: "Audio or video", pro: true, line: "Where the sound goes")
        XCTAssertTrue(card.pro)
        XCTAssertEqual(card.line, "Where the sound goes")
        XCTAssertNil(AddRoute(icon: "file", title: "Open a file", hint: "MIDI, MusicXML or PDF").line)
    }
}
