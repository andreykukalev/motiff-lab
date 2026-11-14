import XCTest

@testable import MotiffLab

/// The practice experiments (Settings · Experimental) as the iPad reads music-core: the resume point that brings a piece
/// back where the player stopped (#327), one hand heard alone (#328) and a selection that snaps to beats (#349).
final class PracticeTests: XCTestCase {
    private var piece: Int32 = 0

    override func setUpWithError() throws {
        piece = try open(KitPiece.example?.data, fileName: "elise.musicxml", request: #"{"language": "en", "id": "p_practice"}"#)
    }

    override func tearDown() {
        MusicCore.close(piece: piece)
    }

    // MARK: - #327

    func testAPieceWithoutAResumePointOpensAtItsStart() {
        XCTAssertNil(ResumePlace(answer: MusicCore.call(piece: piece, "resume")))
    }

    func testTheResumePointKeepsThePassageTempoLoopAndHandsThroughTheProjectFile() throws {
        let passage = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 5, "last": 8}}"#)))
        let range = passage.onset..<passage.end
        let request = ResumePlace.saveRequest(
            id: nil, passage: range, at: 0, focused: true, tempoPercent: 75, hands: .left, loop: true, now: Date(timeIntervalSince1970: 1_800_000_000)
        )
        let saved = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "resume.save", request))?["saved"] as? [String: Any])
        XCTAssertNotNil(ResumePlace(point: saved))

        // The project file written and opened again, as the web app or another visit opens it.
        let reopened = try open(Data(MusicCore.call(piece: piece, "project.save").utf8), fileName: "elise.motiff.json", request: #"{"language": "en"}"#)
        defer { MusicCore.close(piece: reopened) }
        let place = try XCTUnwrap(ResumePlace(answer: MusicCore.call(piece: reopened, "resume")))
        XCTAssertEqual(place.passage, range)
        XCTAssertEqual(place.at, passage.onset)
        XCTAssertTrue(place.focused)
        XCTAssertEqual(place.tempoPercent, 75)
        XCTAssertEqual(place.hands, .left)
        XCTAssertTrue(place.loop)
    }

    func testAResumePointWithNothingSelectedKeepsThePlaceAndLeavesTheHandsAsTheyWere() throws {
        let first = ResumePlace.saveRequest(id: nil, passage: 0..<1440, at: 0, focused: false, tempoPercent: 90, hands: .right, loop: true, now: Date())
        let saved = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece, "resume.save", first))?["saved"] as? [String: Any])
        let id = try XCTUnwrap(ResumePlace(point: saved)?.id)

        let second = ResumePlace.saveRequest(id: id, passage: nil, at: 2880, focused: true, tempoPercent: 400, hands: nil, loop: true, now: Date())
        _ = MusicCore.call(piece: piece, "resume.save", second)
        let place = try XCTUnwrap(ResumePlace(answer: MusicCore.call(piece: piece, "resume")))
        XCTAssertEqual(place.id, id)
        XCTAssertNil(place.passage)
        XCTAssertEqual(place.at, 2880)
        XCTAssertFalse(place.focused, "nothing selected, nothing in focus")
        XCTAssertFalse(place.loop, "nothing selected, nothing loops")
        XCTAssertEqual(place.tempoPercent, ResumePlace.tempoRange.upperBound)
        XCTAssertEqual(place.hands, .right)
    }

    // MARK: - #328

    func testOneHandAloneIsTheNotesMusicCoreGivesThatHand() throws {
        let all = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback")))
        XCTAssertTrue(all.hands.canSeparate, "the kit's piece has both hands")
        let right = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback", Player.handsRequest(.right))))
        let left = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback", Player.handsRequest(.left))))

        XCTAssertFalse(right.notes.isEmpty)
        XCTAssertFalse(left.notes.isEmpty)
        XCTAssertEqual(right.notes.count + left.notes.count, all.notes.count)
        XCTAssertEqual(right.timeline.end, all.timeline.end, "one hand alone keeps the piece's length")
        XCTAssertNil(Player.handsRequest(.both))
    }

    // MARK: - #349

    func testASelectionByBeatsStartsAndEndsOnBeatLines() throws {
        let lines = try XCTUnwrap(PlaybackScore.read(MusicCore.call(piece: piece, "playback"))).timeline.beats.map(\.at)
        XCTAssertGreaterThan(lines.count, 12)
        let request = #"{"span": {"onset": \#(Int(lines[5]) + 5), "end": \#(Int(lines[10]) + 5)}, "snap": "beat"}"#
        let selection = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", request)))

        XCTAssertEqual(selection.onset, lines[5])
        XCTAssertEqual(selection.end, lines[11])
        XCTAssertNotNil(selection.firstBeat)
        XCTAssertNotNil(selection.lastBeat)
        let span = try XCTUnwrap(selection.beatSpan(UIText(language: "en")))
        XCTAssertTrue(span.hasPrefix("Bar \(selection.firstBar), beat \(selection.firstBeat ?? 0)"), span)
        XCTAssertNil(ScoreSelection(onset: 0, end: 480, firstBar: 1, lastBar: 1).beatSpan(UIText(language: "en")))
    }

    private func open(_ data: Data?, fileName: String, request: String) throws -> Int32 {
        let data = try XCTUnwrap(data, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: fileName, data: data, request: request)))
        XCTAssertNil(opened["error"], "\(opened)")
        let id = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(id, 0)
        return id
    }
}
