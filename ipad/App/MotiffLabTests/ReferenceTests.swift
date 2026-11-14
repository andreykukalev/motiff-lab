import XCTest

@testable import MotiffLab

/// The Reference (an experiment, Settings · Experimental): music-core's circle of fifths and key cards on piece 0
/// (`reference.key`, `reference.sound`), its chord and scale cards (`reference.chord`, `reference.scale`) and the open
/// piece's key as the iPad reads them, and where a tap lands on the circle.
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

    func testAKeyCardsChordsAndScalesNameTheirOwnCards() throws {
        let text = MusicCore.call(piece: 0, "reference.key", #"{"fifths": -3, "mode": "minor", "language": "en", "naming": "letters"}"#)
        let card = try XCTUnwrap(ReferenceAnswer(answer: text)?.card)

        XCTAssertEqual(card.chords.map(\.type), ["minor", "minor", "major"])
        XCTAssertEqual(card.chords.map { $0.root?.letter }, ["C", "F", "G"])
        XCTAssertEqual(card.scales.map(\.form), ["naturalMinor", "harmonicMinor"])
        XCTAssertEqual(card.scales.first?.root, ReferenceRoot(step: 0, alter: 0))
        XCTAssertNil(ReferenceAnswer(answer: text)?.pieceKey)
    }

    func testAChordCardHasItsFormulaAndInversions() throws {
        let text = MusicCore.call(piece: 0, "reference.chord", #"{"step": 4, "alter": 0, "type": "dominant7", "language": "ru", "naming": "syllables"}"#)
        let lookup = try XCTUnwrap(ReferenceLookup(answer: text))
        let chord = try XCTUnwrap(lookup.chord)

        XCTAssertEqual(lookup.roots.count, 17)
        XCTAssertEqual(lookup.roots.map(\.letter).prefix(3), ["C", "C♯", "D♭"])
        XCTAssertEqual(lookup.choices.count, 14)
        XCTAssertEqual(chord.symbol, "G7")
        XCTAssertEqual(chord.formula, ["1", "3", "5", "♭7"])
        XCTAssertEqual(chord.notes, ["соль", "си", "ре", "фа"])
        XCTAssertEqual(chord.positions.map(\.title), ["основной вид", "квинтсекстаккорд", "терцквартаккорд", "секундаккорд"])
        XCTAssertEqual(chord.positions[1].figure, "6/5")
        XCTAssertEqual(chord.positions[1].block.pitches, [59, 62, 65, 67])
        XCTAssertEqual(chord.positions[1].arpeggio.id, "arpeggio.1")
        XCTAssertNil(lookup.scale)
    }

    func testAScaleCardSoundsUpAndDownAndMarksTheWayUp() throws {
        let text = MusicCore.call(piece: 0, "reference.scale", #"{"step": 5, "alter": 0, "form": "melodicMinor", "language": "en", "naming": "letters"}"#)
        let scale = try XCTUnwrap(ReferenceLookup(answer: text)?.scale)

        XCTAssertEqual(scale.name, "A melodic minor")
        XCTAssertEqual(scale.notes, ["A", "B", "C", "D", "E", "F♯", "G♯"])
        XCTAssertEqual(scale.descending, ["A", "G", "F", "E", "D", "C", "B", "A"])
        XCTAssertEqual(scale.sound.pitches.count, 15)
        XCTAssertEqual(scale.sound.marked, [57, 59, 60, 62, 64, 66, 68, 69])
        let score = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: 0, "reference.sound", #"{"step": 5, "alter": 0, "form": "melodicMinor", "id": "scale"}"#)))
        XCTAssertEqual(score.notes.count, 15)
    }

    func testTheOpenPiecesKeyComesWithTheCircle() throws {
        let example = try XCTUnwrap(KitPiece.example)
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en"}"#)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        defer { MusicCore.close(piece: piece) }
        let answer = try XCTUnwrap(ReferenceAnswer(answer: MusicCore.call(piece: piece, "reference.key", #"{"language": "en"}"#)))
        let key = try XCTUnwrap(answer.pieceKey)

        XCTAssertEqual(key.name, "A minor")
        XCTAssertTrue(key.minor)
        XCTAssertEqual(key.position, 0)
        XCTAssertFalse(key.inferred)
    }

    /// #533 7: without a known key the circle opens on C major, shown only; a known key is never overridden.
    func testTheCircleOpensOnThePiecesKeyOrCMajorWhenNoneIsKnown() throws {
        let known = ReferenceAnswer.PieceKey(fifths: -3, minor: true, name: "C minor", inferred: true)
        XCTAssertEqual(ReferenceOpening.key(known).fifths, -3)
        XCTAssertTrue(ReferenceOpening.key(known).minor)
        XCTAssertEqual(ReferenceOpening.key(nil).fifths, 0)
        XCTAssertFalse(ReferenceOpening.key(nil).minor)

        XCTAssertTrue(ReferenceOpening.saysKeyUnknown(inPiece: true, pieceKey: nil))
        XCTAssertFalse(ReferenceOpening.saysKeyUnknown(inPiece: true, pieceKey: known), "a known key needs no caption")
        XCTAssertFalse(ReferenceOpening.saysKeyUnknown(inPiece: false, pieceKey: nil), "the library has no piece")

        let opening = ReferenceOpening.key(nil)
        let request = #"{"fifths": \#(opening.fifths), "mode": "major", "language": "en", "naming": "letters"}"#
        let answer = try XCTUnwrap(ReferenceAnswer(answer: MusicCore.call(piece: 0, "reference.key", request)))
        XCTAssertEqual(answer.card?.name, "C major")
        XCTAssertNil(answer.pieceKey, "C major is shown, not the piece's key")
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
