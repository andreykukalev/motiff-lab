import XCTest

@testable import MotiffLab

/// The Reference (an experiment, Settings · Experimental): music-core's circle of fifths and key cards on piece 0
/// (`reference.key`, `reference.sound`) as the iPad reads them, and where a tap lands on the circle.
final class ReferenceTests: XCTestCase {
    func testTheCircleComesWithTheFifteenSignaturesAndNoCardUntilAKeyIsChosen() throws {
        let answer = try XCTUnwrap(ReferenceAnswer(answer: MusicCore.call(piece: 0, "reference.key", #"{"language": "en"}"#)))

        XCTAssertEqual(answer.signatures.map(\.label), ["0", "1♯", "2♯", "3♯", "4♯", "5♯", "6♯", "7♯", "1♭", "2♭", "3♭", "4♭", "5♭", "6♭", "7♭"])
        XCTAssertEqual(answer.sectors.count, 12)
        XCTAssertNil(answer.card)
        XCTAssertEqual(answer.circle.position, -1)
    }

    func testFiveSharpsAreBMajorWithItsSignatureScaleAndMainChords() throws {
        let text = MusicCore.call(piece: 0, "reference.key", #"{"fifths": 5, "mode": "major", "language": "en", "naming": "letters"}"#)
        let card = try XCTUnwrap(ReferenceAnswer(answer: text)?.card)

        XCTAssertEqual(card.name, "B major")
        XCTAssertEqual(card.relative.name, "G♯ minor")
        XCTAssertEqual(card.enharmonic?.name, "C♭ major")
        XCTAssertEqual(card.signatureLine, "5 sharps: F♯, C♯, G♯, D♯, A♯")
        XCTAssertEqual(card.trebleSteps, [8, 5, 9, 6, 3])
        XCTAssertEqual(card.scales.first?.notes, ["B", "C♯", "D♯", "E", "F♯", "G♯", "A♯"])
        XCTAssertEqual(card.chords.map(\.numeral), ["I", "IV", "V"])
        XCTAssertEqual(card.chords.map(\.title), ["B", "E", "F♯"])
        XCTAssertEqual(card.chords.last?.notes, ["F♯", "A♯", "C♯"])
    }

    func testAMinorKeyInRussianHasTheHarmonicMinorsLeadingNote() throws {
        let text = MusicCore.call(piece: 0, "reference.key", #"{"fifths": 6, "mode": "minor", "language": "ru", "naming": "syllables"}"#)
        let card = try XCTUnwrap(ReferenceAnswer(answer: text)?.card)

        XCTAssertEqual(card.name, "ре-диез минор")
        XCTAssertEqual(card.scales.count, 2)
        XCTAssertEqual(card.scales.last?.notes.last, "до𝄪")
        XCTAssertEqual(card.chords.map(\.functionName), ["тоника", "субдоминанта", "доминанта"])
    }

    func testAChordSoundsAsAPlaybackScore() throws {
        let score = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: 0, "reference.sound", #"{"fifths": 0, "mode": "major", "id": "chord.V"}"#)))

        XCTAssertEqual(score.notes.map(\.pitch), [55, 59, 62])
        XCTAssertEqual(score.timeline.tempos.first?.microsecondsPerQuarter, 600_000)
    }

    func testATapOnTheCircleFindsThePlaceAndTheRing() {
        let size = CGSize(width: 236, height: 236)

        // Straight up from the centre: C major outside, A minor inside; straight right: A major (position 3).
        XCTAssertEqual(ReferenceSheet.place(at: CGPoint(x: 118, y: 118 - 84), in: size)?.position, 0)
        XCTAssertEqual(ReferenceSheet.place(at: CGPoint(x: 118, y: 118 - 84), in: size)?.minor, false)
        XCTAssertEqual(ReferenceSheet.place(at: CGPoint(x: 118, y: 118 - 50), in: size)?.minor, true)
        XCTAssertEqual(ReferenceSheet.place(at: CGPoint(x: 118 + 84, y: 118), in: size)?.position, 3)
        XCTAssertNil(ReferenceSheet.place(at: CGPoint(x: 118, y: 118), in: size))
    }
}
