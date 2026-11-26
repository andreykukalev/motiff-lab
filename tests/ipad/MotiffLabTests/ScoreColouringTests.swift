import XCTest

@testable import MotiffLab

/// The pen's colour bars on the score over music-core (finding 31; answer 4 (a) of the plan of 1.4, i-5): the Pen's Undo
/// puts the colours back exactly as they were before a step, a colour it pushed out or joined included, and its Redo does
/// the step again, though music-core gives every highlight laid again a new id.
@MainActor
final class ScoreColouringTests: XCTestCase {
    private var kept: PenState?

    override func setUp() async throws {
        kept = PenMemory.saved()
    }

    override func tearDown() async throws {
        if let kept { PenMemory.keep(kept) }
    }

    /// The highlights music-core answered last, by colour and span.
    private func colours(_ score: ScoreController) -> [String] {
        score.marksModel.marks.highlights.map { "\($0.colour) \(Int($0.onset))-\(Int($0.end))" }.sorted()
    }

    /// The highlights once the pen's queue has brought them to `wanted`, or as they stand after a few seconds.
    private func settled(_ score: ScoreController, at wanted: [String]) async throws -> [String] {
        for _ in 0..<100 where colours(score) != wanted { try await Task.sleep(for: .milliseconds(50)) }
        return colours(score)
    }

    func testUndoPutsTheColoursBackAsTheyWereAndRedoDoesTheStepAgain() async throws {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let score = ScoreController()
        score.scorePage = RecordingScorePage()
        score.show(piece, dark: false)
        let pen = score.penModel
        pen.pen.choose(.colour)
        pen.pen.setBarColour(.ochre)
        pen.colour(0, 3840)
        let ochre = ["ochre 0-3840"]
        let first = try await settled(score, at: ochre)
        XCTAssertEqual(first, ochre)
        pen.pen.setBarColour(.amber)
        pen.colour(1920, 2400)
        let pushed = ["amber 1920-2400", "ochre 0-1920", "ochre 2400-3840"]
        let second = try await settled(score, at: pushed)
        XCTAssertEqual(second, pushed, "the amber pushes the ochre out under it")
        pen.colour(2400, 2880)
        let joined = ["amber 1920-2880", "ochre 0-1920", "ochre 2880-3840"]
        let third = try await settled(score, at: joined)
        XCTAssertEqual(third, joined, "the amber touching it joins it")

        pen.undo()
        let undone = try await settled(score, at: pushed)
        XCTAssertEqual(undone, pushed, "Undo parts the join and gives the ochre back")
        pen.undo()
        let whole = try await settled(score, at: ochre)
        XCTAssertEqual(whole, ochre, "the ochre the amber pushed out is whole again")
        XCTAssertTrue(pen.canUndo)
        XCTAssertTrue(pen.canRedo)

        pen.redo()
        pen.redo()
        let redone = try await settled(score, at: joined)
        XCTAssertEqual(redone, joined, "Redo does both steps again")
        XCTAssertFalse(pen.canRedo)

        pen.undo()
        pen.undo()
        pen.undo()
        let bare = try await settled(score, at: [])
        XCTAssertEqual(bare, [], "back to the bare score")
        XCTAssertFalse(pen.canUndo)
    }
}
