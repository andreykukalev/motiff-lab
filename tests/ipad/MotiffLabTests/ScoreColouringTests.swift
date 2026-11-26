import Combine
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

    /// The highlights music-core answered, by colour and span.
    nonisolated private static func colours(_ marks: MarksShelf) -> [String] {
        marks.highlights.map { "\($0.colour) \(Int($0.onset))-\(Int($0.end))" }.sorted()
    }

    /// The highlights once the pen's queue has brought the marks drawn to `wanted`, or as they stand after a few seconds.
    private func settled(_ score: ScoreController, at wanted: [String]) async -> [String] {
        if Self.colours(score.marksModel.marks) != wanted {
            let done = expectation(description: "the highlights at \(wanted)")
            done.assertForOverFulfill = false
            let watch = score.marksModel.$marks.sink { marks in if Self.colours(marks) == wanted { done.fulfill() } }
            await fulfillment(of: [done], timeout: 5)
            watch.cancel()
        }
        return Self.colours(score.marksModel.marks)
    }

    /// A score over the example piece, open in music-core until `close` is called.
    private func opened() async throws -> (ScoreController, OpenPiece) {
        let example = try XCTUnwrap(OpenPiece.example, "the example piece is in the app")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        let score = ScoreController()
        score.scorePage = RecordingScorePage()
        score.show(piece, dark: false)
        return (score, piece)
    }

    func testUndoPutsTheColoursBackAsTheyWereAndRedoDoesTheStepAgain() async throws {
        let (score, piece) = try await opened()
        defer { _ = MusicCore.close(piece: piece.id) }
        let pen = score.penModel
        pen.pen.choose(.colour)
        pen.pen.setBarColour(.ochre)
        pen.colour(0, 3840)
        let ochre = ["ochre 0-3840"]
        let first = await settled(score, at: ochre)
        XCTAssertEqual(first, ochre)
        pen.pen.setBarColour(.amber)
        pen.colour(1920, 2400)
        let pushed = ["amber 1920-2400", "ochre 0-1920", "ochre 2400-3840"]
        let second = await settled(score, at: pushed)
        XCTAssertEqual(second, pushed, "the amber pushes the ochre out under it")
        pen.colour(2400, 2880)
        let joined = ["amber 1920-2880", "ochre 0-1920", "ochre 2880-3840"]
        let third = await settled(score, at: joined)
        XCTAssertEqual(third, joined, "the amber touching it joins it")

        pen.undo()
        let undone = await settled(score, at: pushed)
        XCTAssertEqual(undone, pushed, "Undo parts the join and gives the ochre back")
        pen.undo()
        let whole = await settled(score, at: ochre)
        XCTAssertEqual(whole, ochre, "the ochre the amber pushed out is whole again")
        XCTAssertTrue(pen.canUndo)
        XCTAssertTrue(pen.canRedo)

        pen.redo()
        pen.redo()
        let redone = await settled(score, at: joined)
        XCTAssertEqual(redone, joined, "Redo does both steps again")
        XCTAssertFalse(pen.canRedo)

        pen.undo()
        pen.undo()
        pen.undo()
        let bare = await settled(score, at: [])
        XCTAssertEqual(bare, [], "back to the bare score")
        XCTAssertFalse(pen.canUndo)
    }

    /// An older file's highlight names notes: music-core answers them (`MarksShelf`), the eraser's change keeps them with
    /// the highlight it takes, and the Pen's Undo lays it back with them, as music-core kept it.
    func testAHighlightTakenAndPutBackKeepsItsNotes() async throws {
        let (score, piece) = try await opened()
        defer { _ = MusicCore.close(piece: piece.id) }
        await score.changeMarks(piece.id, "highlight.add", ["span": ["onset": 0, "end": 1920], "colour": "forest", "notes": ["n1", "n2"]], draws: true)
        let older = try XCTUnwrap(score.marksModel.marks.highlights.first, "music-core lays it")
        XCTAssertEqual(older.notes, ["n1", "n2"], "and answers its notes")

        let erased = await score.uncolourBars([0..<1920], draws: true)
        XCTAssertEqual(erased.taken, [Recolouring.Highlight(onset: 0, end: 1920, colour: "forest", notes: ["n1", "n2"])])
        let gone = await settled(score, at: [])
        XCTAssertEqual(gone, [])
        await score.recolour(erased.reversed, draws: true)
        let back = try XCTUnwrap(score.marksModel.marks.highlights.first, "Undo lays it back")
        XCTAssertEqual([back.onset, back.end], [0, 1920])
        XCTAssertEqual(back.colour, "forest")
        XCTAssertEqual(back.notes, ["n1", "n2"], "with its notes")
    }
}
