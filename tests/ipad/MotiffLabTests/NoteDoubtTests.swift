import XCTest

@testable import MotiffLab

/// Doubt about recognised notes as the iPad shows it (#352, ADR 0027 section 12): a recording's note lengths to check are
/// offered in Check even where no bar is flagged, and said truly there; Piece DNA keeps the doubt music-core gives a key and
/// a circle of fifths read from notes still to check. The answers are the facade's shapes (`check`, `dna`).
@MainActor
final class NoteDoubtTests: XCTestCase {
    private let english = UIText(language: "en")

    /// A recording's `check` with the given flags and, in bar 4, two notes whose length is uncertain.
    private func heard(flags: String = "[]", open: Int = 0, lengths: Bool = true) throws -> CheckShelf {
        let bar = #"{"bar": 4, "noteIds": ["n_0031", "n_0032"], "reasons": ["pedal"], "names": ["Pedal held at the end"], "texts": ["The pedal is down when these notes end."], "span": {"onset": 5760, "end": 7680}}"#
        let answer = #"{"flags": \#(flags), "open": \#(open), "lengths": [\#(lengths ? bar : "")], "pages": [], "origin": "transcribed", "engine": "transkun 2.0.1"}"#
        return try XCTUnwrap(CheckShelf(answer: answer))
    }

    func testARecordingWithNoteLengthsToCheckButNoFlagOffersCheckAndSaysWhatItHolds() throws {
        let check = try heard()

        XCTAssertFalse(check.hasFlags)
        XCTAssertTrue(check.hasEntries, "More offers Check: the lengths are only in it")
        XCTAssertEqual(check.lengths.map(\.bar), [4])
        XCTAssertEqual(check.lengths.first?.noteIds, ["n_0031", "n_0032"])
        XCTAssertEqual(check.lengths.first?.span, 5760..<7680)
        XCTAssertEqual(check.heading(english), "Note lengths to check", "not «Every bar checked»: no bar was")
        XCTAssertEqual(check.how(english), "Heard in a recording by transkun 2.0.1.", "no flagged bar to listen to")
    }

    func testOnceTheLengthsAreFixedEveryBarIsCheckedAndNothingIsOffered() throws {
        let fixed = try heard(lengths: false)

        XCTAssertFalse(fixed.hasEntries)
        XCTAssertEqual(fixed.heading(english), "Every bar checked")
        XCTAssertFalse(try XCTUnwrap(CheckShelf(answer: #"{"flags": [], "open": 0, "origin": "imported", "engine": null}"#)).hasEntries,
                       "imported notes: no Check")
    }

    func testWithAFlagTheHeadingAndTheWordsAreTheFlagsAsBefore() throws {
        let flag = #"[{"bar": 2, "source": "transcription", "reason": "partSeam", "name": "Part seam", "text": "", "resolved": false, "span": {"onset": 1920, "end": 3840}}]"#
        let check = try heard(flags: flag, open: 1)

        XCTAssertTrue(check.hasEntries)
        XCTAssertEqual(check.heading(english), english.count("check.toReview", 1))
        XCTAssertEqual(check.how(english), "Heard in a recording by transkun 2.0.1. " + english["check.howRecordingShort"])
        let resolved = flag.replacingOccurrences(of: #""resolved": false"#, with: #""resolved": true"#)
        XCTAssertEqual(try heard(flags: resolved).heading(english), "Note lengths to check", "every flag resolved, lengths left: named, not «Every bar checked»")
        XCTAssertEqual(try heard(flags: resolved, lengths: false).heading(english), "Every bar checked")
    }

    func testPieceDnaKeepsTheDoubtOfAKeyAndACircleReadFromNotesToCheck() throws {
        let doubt = "Rests on recognised notes still to check in bar 2."
        let answer = #"""
        {"firstBar": 1, "lastBar": 2, "barStarts": [0, 1920, 3840],
         "keys": {"status": "filled", "spans": [
           {"key": {"tonic": 9, "mode": "minor"}, "label": "A minor", "modeInferred": false, "keyInferred": true, "doubt": "\#(doubt)",
            "passage": {"onset": 0, "end": 3840, "firstBar": 1, "lastBar": 2}}]},
         "circles": [
           {"id": "key_1", "label": "A minor", "passage": {"onset": 0, "end": 1920, "firstBar": 1, "lastBar": 1},
            "circle": {"name": "A minor", "position": 0, "certainty": "inferred"}},
           {"id": "key_2", "label": "A minor", "passage": {"onset": 1920, "end": 3840, "firstBar": 2, "lastBar": 2}, "doubt": "\#(doubt)",
            "circle": {"name": "A minor", "position": 0, "certainty": "uncertain"}}]}
        """#
        let map = try XCTUnwrap(DnaMap(answer: answer))

        XCTAssertEqual(map.keys.map(\.doubt), [doubt])
        XCTAssertEqual(map.circles.map(\.doubt), [nil, doubt])
        XCTAssertEqual(map.circles.last?.circle.certainty, "uncertain")
    }
}
