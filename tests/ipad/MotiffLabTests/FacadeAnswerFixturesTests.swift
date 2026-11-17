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

    func testTheSourceLayersDecode() throws {
        let answers = try Self.answers("sources.layers")
        XCTAssertEqual(answers.count, 9, "each call's answer but the version's, which only builds the state")
        let read = try answers.map { try XCTUnwrap(SourceLayers(answer: $0.text), "\(reported)") }
        XCTAssertEqual(reported, [])
        XCTAssertEqual(read[0].views.map(\.id), ["n_1"], "the example alone: its file and its working notes")

        let layers = read[7]
        XCTAssertEqual(answers[7].request?["here"] as? [String], ["src_rec"])
        XCTAssertEqual(layers.working, "n_1")
        XCTAssertEqual(layers.hiddenCount, 1)
        XCTAssertEqual(layers.liveCount, 3, "the removed PDF is listed, never counted")
        let removed = try XCTUnwrap(layers.original("src_pdf_2"))
        XCTAssertTrue(removed.isRemoved)
        XCTAssertNotNil(removed.removedAt, "a tombstone keeps its time")
        XCTAssertNil(try XCTUnwrap(layers.original("src_pdf")).removedAt)
        XCTAssertEqual(layers.original("src_pdf")?.pages, 1)
        XCTAssertEqual(layers.original("src_pdf")?.notes, ["n_2"])
        XCTAssertEqual(layers.original("src_rec")?.durationMs, 161_000)
        XCTAssertNil(layers.original("src_rec")?.pages)
        XCTAssertTrue(layers.original("src_rec")?.isHere == true, "its sound is here")
        XCTAssertEqual(read[8].original("src_rec")?.state, "nowhere", "without its sound")

        let readAgain = try XCTUnwrap(layers.notes("n_2"))
        XCTAssertEqual(readAgain.role, "other")
        XCTAssertEqual(readAgain.engine, "homr 0.8.0")
        XCTAssertNotNil(readAgain.at)
        XCTAssertEqual(readAgain.original, "src_pdf")
        XCTAssertTrue(readAgain.hidden)
        let version = try XCTUnwrap(layers.notes("perf_1"))
        XCTAssertEqual(version.role, "version")
        XCTAssertNil(version.original)
        XCTAssertNil(version.state)
        XCTAssertNil(version.at)
        XCTAssertEqual(layers.title(of: version, UIText.current), "Official", "Notes of no original take their name")
        XCTAssertEqual(layers.listedNotes.first?.id, "n_1", "the working notes first")

        XCTAssertNil(try XCTUnwrap(layers.views.first { $0.id == "perf_1" }).group, "Notes of no original are in no group")
        XCTAssertEqual(layers.views.first { $0.id == "n_2" }?.group, "src_pdf")
        XCTAssertEqual(layers.views.first { $0.id == "src_rec" }?.kind, "performance")
        XCTAssertFalse(layers.shownViews.contains { $0.id == "n_2" }, "hidden from the switch")
        XCTAssertFalse(read[8].views.contains { $0.kind == "performance" }, "no performance view without its sound")
    }

    /// Each answer a file keeps, with its request, as the facade's text; a call that only builds the state keeps none.
    private static func answers(_ name: String) throws -> [(request: [String: Any]?, text: String)] {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("contracts/facade-answers/\(name).json")
        let file = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any], url.path)
        XCTAssertEqual(file["fixture"] as? String, "FurElise.musicxml")
        let entries = try XCTUnwrap(file["answers"] as? [[String: Any]])
        return try entries.compactMap { entry -> (request: [String: Any]?, text: String)? in
            guard let answer = entry["answer"] as? [String: Any] else { return nil }
            let text = String(decoding: try JSONSerialization.data(withJSONObject: answer), as: UTF8.self)
            return (entry["request"] as? [String: Any], text)
        }
    }
}
