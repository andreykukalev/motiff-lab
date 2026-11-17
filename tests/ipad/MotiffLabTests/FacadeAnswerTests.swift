import XCTest

@testable import MotiffLab

/// Step A7 (loud failures): the hot answers the score's screen reads (`select`, the playback score, the roll's payload,
/// `sources.layers`, `corrections.*`, `performance.merge`) are read through `FacadeAnswer`. Each reader reads music-core's
/// real answer cleanly; the same answer with a field music-core always writes renamed, as a rename in C# would send it, is
/// a broken contract the reader reports and does not read, instead of an empty panel. A refusal is neither read as a
/// success nor reported. In the app `broken` asserts; here it collects what it is told.
final class FacadeAnswerTests: XCTestCase {
    private var piece: Int32 = 0
    private var reported: [String] = []

    override func setUpWithError() throws {
        FacadeAnswer.broken = { [weak self] in self?.reported.append($0) }
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en"}"#)))
        piece = Int32((opened["piece"] as? NSNumber)?.intValue ?? 0)
        XCTAssertGreaterThan(piece, 0)
    }

    override func tearDown() {
        FacadeAnswer.broken = FacadeAnswer.loud
        MusicCore.close(piece: piece)
    }

    // MARK: - The helper

    func testARefusalIsNotASuccessAndNotABrokenAnswer() throws {
        let refusal = #"{"error": "noSelection", "message": "Select a passage first."}"#
        XCTAssertNil(FacadeAnswer(refusal, of: "select"))
        XCTAssertEqual(FacadeAnswer.problem(refusal), "Select a passage first.", "the problem line keeps music-core's words")
        XCTAssertEqual(reported, [], "a refusal is music-core's answer, not a broken one")

        XCTAssertNil(FacadeAnswer("not JSON", of: "select"))
        XCTAssertEqual(reported.count, 1)
        XCTAssertEqual(FacadeAnswer.problem("not JSON"), UIText.current["parts.pdf.failedNoWord"], "the problem line says music-core gave no answer")
    }

    func testAFieldMusicCoreAlwaysWritesIsReportedWhenMissingOrOfAnotherType() throws {
        let answer = try XCTUnwrap(FacadeAnswer(#"{"count": 2, "name": "x", "group": null, "names": {"notes": 3}}"#, of: "test"))
        XCTAssertEqual(answer.required("count", NSNumber.self)?.intValue, 2)
        XCTAssertEqual(answer.required("name", String.self), "x")
        XCTAssertEqual(answer.entry("names")?.required("notes", NSNumber.self)?.intValue, 3)
        XCTAssertTrue(answer.has("count", "name"))
        XCTAssertEqual(reported, [])

        XCTAssertNil(answer.required("name", Bool.self))
        XCTAssertNil(answer.required("total", NSNumber.self))
        XCTAssertNil(answer.entry("names")?.required("keyTicks", [Int].self))
        XCTAssertFalse(answer.has("count", "group"), "null is missing for a payload passed on")
        XCTAssertEqual(reported.count, 4, "\(reported)")
        XCTAssertTrue(reported.contains { $0.contains("test.names") && $0.contains("\"keyTicks\"") }, "the problem names where: \(reported)")
    }

    // MARK: - The hot answers

    func testTheSelection() throws {
        let answer = MusicCore.call(piece: piece, "select", #"{"bars": {"first": 4, "last": 6}, "filter": {"hand": "right"}}"#)
        let selection = try XCTUnwrap(ScoreSelection(answer: answer))
        XCTAssertEqual(selection.firstBar, 4)
        XCTAssertEqual(selection.lastBar, 6)
        XCTAssertNotNil(selection.firstBeat)
        XCTAssertEqual(selection.hand, "right")
        XCTAssertFalse(selection.noteIds.isEmpty)
        XCTAssertEqual(reported, [])

        expectBroken("firstBar") { ScoreSelection(answer: renamed(answer, "firstBar")) }
        expectBroken("noteIds") { ScoreSelection(answer: renamed(answer, "noteIds")) }
        expectBroken("selection") { ScoreSelection(answer: renamed(answer, "selection")) }

        // `phrases.level` answers null with no selection; a refusal is no selection either.
        XCTAssertNil(ScoreSelection(answer: #"{"selection": null}"#))
        let refused = MusicCore.call(piece: piece, "select", "{}")
        XCTAssertNotNil(facadeObject(refused)?["error"], refused)
        XCTAssertNil(ScoreSelection(answer: refused))
        XCTAssertEqual(reported, [])
    }

    func testThePlaybackScore() throws {
        let answer = MusicCore.call(piece: piece, "playback")
        let score = try XCTUnwrap(PlaybackScore.read(answer))
        XCTAssertGreaterThan(score.notes.count, 600)
        XCTAssertFalse(score.timeline.bars.isEmpty)
        XCTAssertEqual(score.barNumbers.count, score.timeline.bars.count)
        XCTAssertEqual(reported, [])

        expectBroken("beatsPerBar") { PlaybackScore.read(renamed(answer, "beatsPerBar")) }
        expectBroken("velocity") { PlaybackScore.read(renamed(answer, "velocity")) }
        expectBroken("tempos") { PlaybackScore.read(renamed(answer, "tempos")) }
    }

    @MainActor
    func testTheRollsPayload() throws {
        let answer = MusicCore.call(piece: piece, "roll")
        let payload = try XCTUnwrap(RollController.payload(answer))
        XCTAssertFalse((payload["ids"] as? [String] ?? []).isEmpty)
        XCTAssertEqual(reported, [])

        expectBroken("bars") { RollController.payload(renamed(answer, "bars")) }
        expectBroken("keyTicks") { RollController.payload(renamed(answer, "keyTicks")) }
        XCTAssertNil(RollController.payload(#"{"error": "noPiece", "message": "No piece."}"#))
        XCTAssertEqual(reported, [])
    }

    func testTheSourceLayers() throws {
        let answer = MusicCore.call(piece: piece, "sources.layers", #"{"device": "ipad", "here": []}"#)
        let layers = try XCTUnwrap(SourceLayers(answer: answer))
        XCTAssertNotNil(layers.working)
        XCTAssertTrue(layers.notes.contains(where: \.isWorking))
        XCTAssertFalse(layers.views.isEmpty)
        XCTAssertEqual(reported, [])

        expectBroken("role") { SourceLayers(answer: renamed(answer, "role")) }
        expectBroken("op") { SourceLayers(answer: renamed(answer, "op")) }
        expectBroken("working") { SourceLayers(answer: renamed(answer, "working")) }
        expectBroken("views") { SourceLayers(answer: renamed(answer, "views")) }
    }

    func testTheCorrections() throws {
        let answer = MusicCore.call(piece: piece, "corrections", #"{"bar": 2}"#)
        let read = try XCTUnwrap(correctionsAnswer(answer))
        let corrections = NoteCorrections(read)
        XCTAssertEqual(corrections.bar, 2)
        XCTAssertFalse(corrections.notes.isEmpty)
        XCTAssertEqual(EditsLayerState(read.edits), EditsLayerState(count: 0), "a piece opened strictly answers no layer")
        XCTAssertEqual(reported, [])

        expectBroken("canShorter") { correctionsAnswer(renamed(answer, "canShorter")) }
        expectBroken("atBar") { correctionsAnswer(renamed(answer, "atBar")) }
        expectBroken("count") { correctionsAnswer(renamed(answer, "count")) }
        // The note editor reads these of every corrections answer (ScoreController.noteEditCall).
        expectBroken("canUndo") { correctionsAnswer(renamed(answer, "canUndo")) }

        // With the layer, its corrections that moved or no longer fit come too.
        let off = MusicCore.call(piece: piece, "corrections.layer", #"{"on": false, "bar": 2}"#)
        XCTAssertEqual(correctionsAnswer(off).map { EditsLayerState($0.edits) }?.on, false)
        XCTAssertEqual(reported, [])

        let refused = MusicCore.call(piece: piece, "corrections", #"{"bar": 9999}"#)
        XCTAssertNil(correctionsAnswer(refused))
        XCTAssertEqual(FacadeAnswer.problem(refused), facadeObject(refused)?["message"] as? String)
        XCTAssertEqual(reported, [], "a refusal is not read as an empty bar")
    }

    func testTheMergeDraft() throws {
        let example = try XCTUnwrap(KitPiece.example)
        let request = facadeRequest(["name": "Official", "file": example.data.base64EncodedString(), "fileName": example.fileName])
        var shelf = VersionShelf()
        XCTAssertTrue(shelf.read(performance: MusicCore.call(piece: piece, "performance.add", request)))
        let id = try XCTUnwrap(shelf.shownId)

        let answer = MusicCore.call(piece: piece, "performance.merge.open", facadeRequest(["id": id, "start": "all"]))
        let merge = try XCTUnwrap(mergeAnswer(answer)?.merge.map(MergeInfo.init))
        XCTAssertEqual(merge.id, id)
        XCTAssertNotNil(PlaybackScore.read(MusicCore.call(piece: piece, "performance.merge.playback", #"{"view": "result"}"#)))
        XCTAssertEqual(reported, [])

        // A bar's "choice" is pinned on the fixture's draft (FacadeAnswerFixturesTests): this one, of the piece's own
        // notes, has no bar.
        expectBroken("taking") { mergeAnswer(renamed(answer, "taking")) }
        expectBroken("merge") { mergeAnswer(renamed(answer, "merge")) }

        let none = try XCTUnwrap(mergeAnswer(MusicCore.call(piece: piece, "performance.merge.cancel")))
        XCTAssertNil(none.merge)
        XCTAssertEqual(reported, [], "none open is music-core's answer, not a broken one")
        XCTAssertNil(mergeAnswer(MusicCore.call(piece: piece, "performance.merge.undo")), "with none open, a refusal")
        XCTAssertEqual(reported, [])
    }

    // MARK: - Helpers

    /// A merge draft's answer read as `PieceCore.merge` reads it.
    private func mergeAnswer(_ answer: String) -> MergeAnswer? {
        FacadeAnswer.decode(MergeAnswer.self, answer, of: "performance.merge")
    }

    /// A corrections answer read as `PieceCore.corrections` reads it.
    private func correctionsAnswer(_ answer: String) -> CorrectionsAnswer? {
        FacadeAnswer.decode(CorrectionsAnswer.self, answer, of: "corrections")
    }

    /// The answer with one of its field names changed wherever it is written, as a rename in the C# writer would send it.
    private func renamed(_ answer: String, _ key: String, file: StaticString = #filePath, line: UInt = #line) -> String {
        XCTAssertTrue(answer.contains("\"\(key)\":"), "the answer writes \"\(key)\"", file: file, line: line)
        return answer.replacingOccurrences(of: "\"\(key)\":", with: "\"\(key)Renamed\":")
    }

    /// The read gives nothing, and the broken contract it reports names the field.
    private func expectBroken<T>(_ key: String, file: StaticString = #filePath, line: UInt = #line, _ read: () -> T?) {
        reported = []
        XCTAssertNil(read(), "\"\(key)\" renamed is not read", file: file, line: line)
        XCTAssertTrue(reported.contains { $0.contains("\"\(key)\"") }, "\"\(key)\" renamed is reported: \(reported)", file: file, line: line)
        reported = []
    }
}

extension ScoreSelection {
    /// The selection in a `select` answer's text, read as `PieceCore.select` reads it, for the tests that call the facade
    /// directly; nil for a refusal, null, or an answer that cannot be read.
    init?(answer: String) {
        guard let selection = FacadeAnswer.decode(SelectionAnswer.self, answer, of: "select")?.selection else { return nil }
        self.init(selection)
    }
}
