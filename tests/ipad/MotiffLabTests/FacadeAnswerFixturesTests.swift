import XCTest

@testable import MotiffLab

/// The facade's answers as data (audit C3, step B5): tests/contracts/facade-answers/*.json, which music-core's
/// FacadeAnswersContractTests writes and keeps equal to what the facade answers on the example piece, decode into the app's
/// typed answers (PieceAnswers.swift) through `FacadeAnswer.decode`, as `PieceCore` decodes them, and from those into what
/// the features read. A field renamed or retyped in C# changes the file there, and fails the decode here on the field the
/// app needs. The files are read where they lie in the repository (`#filePath`).
final class FacadeAnswerFixturesTests: XCTestCase {
    private var reported: [String] = []

    override func setUp() {
        FacadeAnswer.broken = { [weak self] in self?.reported.append($0) }
    }

    override func tearDown() {
        FacadeAnswer.broken = FacadeAnswer.loud
    }

    func testTheSelectionsDecode() throws {
        let answers = try Self.answers("select")
        XCTAssertEqual(answers.count, 2)
        for (request, text) in answers {
            let selection = try XCTUnwrap(FacadeAnswer.decode(SelectionAnswer.self, text, of: "select")?.selection.map(ScoreSelection.init), "\(reported)")
            let bars = try XCTUnwrap(request?["bars"] as? [String: Int])
            XCTAssertEqual(selection.firstBar, bars["first"])
            XCTAssertEqual(selection.lastBar, bars["last"])
            XCTAssertLessThan(selection.onset, selection.end)
            XCTAssertNotNil(selection.firstBeat)
            XCTAssertNotNil(selection.lastBeat)
            XCTAssertFalse(selection.noteIds.isEmpty)
            XCTAssertEqual(selection.hand, (request?["filter"] as? [String: String])?["hand"], "the filter names the hand only when narrowed")
        }
        XCTAssertEqual(reported, [])
    }

    func testThePlaybackScoreDecodes() throws {
        let (_, text) = try XCTUnwrap(Self.answers("playback").first)
        let answer = try XCTUnwrap(FacadeAnswer.decode(PlaybackAnswer.self, text, of: "playback"), "\(reported)")
        let kept = try XCTUnwrap(facadeObject(text))
        let score = PlaybackScore(answer)
        XCTAssertEqual(score.notes.count, (kept["notes"] as? [Any])?.count)
        XCTAssertEqual(score.timeline.tempos.count, (kept["tempos"] as? [Any])?.count)
        XCTAssertEqual(score.timeline.bars.count, (kept["bars"] as? [Any])?.count)
        XCTAssertEqual(score.barNumbers.count, score.timeline.bars.count)
        XCTAssertEqual(score.barBeats.marks.count, (kept["beats"] as? [Any])?.count, "every beat tells its bar and beat")
        XCTAssertTrue(score.hands.canSeparate, "the example's notes carry both hands")
        let tempo = try XCTUnwrap(score.tempo, "the tempo shown is read")
        XCTAssertEqual(tempo.source, "notated")
        XCTAssertEqual(reported, [])
    }

    /// Each answer a file keeps, with its request, as the facade's text.
    private static func answers(_ name: String) throws -> [(request: [String: Any]?, text: String)] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/facade-answers/\(name).json")
        let file = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any], url.path)
        XCTAssertEqual(file["fixture"] as? String, "FurElise.musicxml")
        let entries = try XCTUnwrap(file["answers"] as? [[String: Any]])
        return try entries.map { entry -> (request: [String: Any]?, text: String) in
            let answer = try XCTUnwrap(entry["answer"] as? [String: Any])
            let text = String(decoding: try JSONSerialization.data(withJSONObject: answer), as: UTF8.self)
            return (entry["request"] as? [String: Any], text)
        }
    }
}
