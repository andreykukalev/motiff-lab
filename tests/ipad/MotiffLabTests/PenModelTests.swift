import XCTest

@testable import MotiffLab

/// PenModel (S6, ADR 0031 section 3) against a fake score: a stroke is anchored on the page and kept through music-core as
/// one step of the Pen's history, Undo and Redo take it back and do it again, a press of the eraser is sent as it moves and
/// saved as one step when it lifts, and the Pen turned on or off goes to the score. What ScoreController did before the
/// move, call for call. Since finding 31 (v0.10) the colour bars colour through the score and the eraser takes the colour;
/// since v1.4 (i-5) each is a step of the same history, which Undo sends back reversed and Redo as it was.
@MainActor
final class PenModelTests: XCTestCase {
    /// The score as the pen sees it: a page that answers `anchor` and `rubEnd`, and the changes asked of the marks.
    @MainActor
    private final class FakeScore: PenScore {
        @MainActor
        final class Page: ScorePage {
            var anchored: [String: Any] = ["id": "s1", "tool": "pen"]
            var rubbed: [String: Any] = [:]
            private(set) var commands: [PageCommand] = []
            private(set) var rubPoints: [[[String: Double]]] = []

            func run(_ command: PageCommand, _ argument: Any?) async throws -> Any? {
                commands.append(command)
                switch command {
                case .anchor: return anchored
                case .rubEnd: return rubbed
                case .rub:
                    rubPoints.append((argument as? [String: Any])?["points"] as? [[String: Double]] ?? [])
                    return nil
                default: return nil
                }
            }

            func post(_ command: PageCommand, _ argument: Any) {
                commands.append(command)
            }
        }

        struct Change: Equatable {
            var piece: Int32
            var call: String
            var request: String
            var draws: Bool
        }

        let page = Page()
        var scorePage: ScorePage { page }
        var pieceId: Int32? = 7
        private(set) var changes: [Change] = []
        private(set) var turned: [Bool] = []
        private(set) var colouringChanges = 0
        private(set) var coloured: [String] = []
        /// The changes Undo and Redo sent back.
        private(set) var recoloured: [Recolouring] = []
        /// What the colour bars changed, as the score answers it: nothing when it is empty, else the span laid in its colour.
        var colourChanges = true
        var changed: (() -> Void)?

        func changeMarks(_ piece: Int32, _ call: String, _ request: [String: Any], draws: Bool) async {
            changes.append(Change(piece: piece, call: call, request: facadeRequest(request), draws: draws))
            changed?()
        }

        func penTurned(_ on: Bool) {
            turned.append(on)
        }

        func colouringChanged() {
            colouringChanges += 1
        }

        func colourBars(_ span: Range<Double>, in colour: String) async -> Recolouring {
            coloured.append("\(colour) \(Int(span.lowerBound))-\(Int(span.upperBound))")
            changed?()
            return colourChanges ? Recolouring(laid: [Recolouring.Highlight(onset: span.lowerBound, end: span.upperBound, colour: colour)]) : Recolouring()
        }

        func uncolourBars(_ spans: [Range<Double>]) async -> Recolouring {
            coloured.append("off " + spans.map { "\(Int($0.lowerBound))-\(Int($0.upperBound))" }.joined(separator: " "))
            changed?()
            return Recolouring(taken: spans.map { Recolouring.Highlight(onset: $0.lowerBound, end: $0.upperBound, colour: "amber") })
        }

        func recolour(_ change: Recolouring) async {
            if !change.isEmpty { recoloured.append(change) }
            changed?()
        }
    }

    private var kept: PenState?
    private let points: [[String: Double]] = [["x": 10, "y": 20, "pressure": 0.5], ["x": 30, "y": 40, "pressure": 0.6]]

    override func setUp() async throws {
        kept = PenMemory.saved()
    }

    override func tearDown() async throws {
        if let kept { PenMemory.keep(kept) }
    }

    private func made() -> (PenModel, FakeScore) {
        let model = PenModel()
        let score = FakeScore()
        model.score = score
        model.pen.choose(.pencil)
        return (model, score)
    }

    /// Waits until the score has been asked to colour or take the colour `count` times in all.
    private func coloured(_ score: FakeScore, _ count: Int) async {
        guard score.coloured.count < count else { return }
        let done = expectation(description: "\(count) colourings")
        score.changed = { if score.coloured.count == count { done.fulfill() } }
        await fulfillment(of: [done], timeout: 5)
        score.changed = nil
    }

    /// Waits until Undo and Redo have sent `count` changes of the highlights back in all.
    private func recoloured(_ score: FakeScore, _ count: Int) async {
        guard score.recoloured.count < count else { return }
        let done = expectation(description: "\(count) changes sent back")
        score.changed = { if score.recoloured.count == count { done.fulfill() } }
        await fulfillment(of: [done], timeout: 5)
        score.changed = nil
    }

    /// Waits until the score has been asked for `count` changes of the marks in all.
    private func changes(_ score: FakeScore, _ count: Int) async {
        guard score.changes.count < count else { return }
        let done = expectation(description: "\(count) changes")
        score.changed = { if score.changes.count == count { done.fulfill() } }
        await fulfillment(of: [done], timeout: 5)
        score.changed = nil
    }

    func testAStrokeIsAnchoredOnThePageAndKeptAsOneStep() async {
        let (model, score) = made()
        let dropped = expectation(description: "the live stroke dropped")
        model.keep(points, in: 7) { dropped.fulfill() }
        await fulfillment(of: [dropped], timeout: 5)
        XCTAssertEqual(score.page.commands, [.anchor])
        XCTAssertEqual(score.changes, [FakeScore.Change(piece: 7, call: "ink.add", request: facadeRequest(["strokes": [score.page.anchored]]), draws: true)])
        XCTAssertTrue(model.canUndo)
        XCTAssertFalse(model.canRedo)
    }

    func testUndoTakesTheStrokeAwayAndRedoBringsItBack() async {
        let (model, score) = made()
        model.keep(points, in: 7) {}
        model.undo()
        await changes(score, 2)
        XCTAssertEqual(score.changes[1], FakeScore.Change(piece: 7, call: "ink.remove", request: facadeRequest(["ids": ["s1"]]), draws: true))
        XCTAssertFalse(model.canUndo)
        XCTAssertTrue(model.canRedo)
        model.redo()
        await changes(score, 3)
        XCTAssertEqual(score.changes[2], FakeScore.Change(piece: 7, call: "ink.add", request: facadeRequest(["strokes": [score.page.anchored]]), draws: true))
        XCTAssertTrue(model.canUndo)
        XCTAssertFalse(model.canRedo)
    }

    func testAStrokeWrittenWithTheEraserIsNotKept() async {
        let (model, score) = made()
        model.pen.choose(.eraser)
        let dropped = expectation(description: "the live stroke dropped")
        model.keep(points, in: 7) { dropped.fulfill() }
        await fulfillment(of: [dropped], timeout: 5)
        XCTAssertEqual(score.page.commands, [], "the eraser rubs out as it moves")
        XCTAssertEqual(score.changes, [])
        XCTAssertFalse(model.canUndo)
    }

    func testAPressOfTheEraserIsSentAsItMovesAndSavedAsOneStepWhenItLifts() async {
        let (model, score) = made()
        score.page.rubbed = ["removed": [["id": "a"]], "added": [["id": "b"]]]
        model.rub([["x": 1, "y": 2]], lifted: false, in: 7)
        model.rub([["x": 3, "y": 4]], lifted: true, in: 7)
        await changes(score, 2)
        XCTAssertEqual(score.page.commands, [.rub, .rubEnd])
        XCTAssertEqual(score.page.rubPoints, [[["x": 1, "y": 2], ["x": 3, "y": 4]]], "points that come while a send waits go with it")
        XCTAssertEqual(score.changes, [
            FakeScore.Change(piece: 7, call: "ink.add", request: facadeRequest(["strokes": [["id": "b"]]]), draws: false),
            FakeScore.Change(piece: 7, call: "ink.remove", request: facadeRequest(["ids": ["a"]]), draws: true),
        ], "the pieces left, then the marks cut, drawn once both are done")
        XCTAssertTrue(model.canUndo)
    }

    func testTheColourBarsColourInTheirColourAndOnlyWhileTheyAreTheTool() async {
        let (model, score) = made()
        model.colour(0, 1920)
        model.pen.choose(.colour)
        model.pen.setBarColour(.forest)
        model.colour(480, 960)
        model.colour(960, 960)
        await coloured(score, 1)
        XCTAssertEqual(score.coloured, ["forest 480-960"], "nothing with the pencil, nor for a passage of no length")
        XCTAssertEqual(score.changes, [], "the score keeps the highlights, the pen writes no stroke")
        XCTAssertTrue(model.canUndo, "a step of the Pen's history (v1.4 i-5)")
    }

    /// Answer 4 (a) of the plan of 1.4: the Pen's Undo and Redo take back and do again what the colour bars coloured, in
    /// the one history with the strokes, newest first, and its buttons follow.
    func testUndoAndRedoTakeBackAndDoAgainWhatTheColourBarsColoured() async {
        let (model, score) = made()
        model.keep(points, in: 7) {}
        model.pen.choose(.colour)
        model.pen.setBarColour(.forest)
        model.colour(480, 960)
        await coloured(score, 1)
        let laid = Recolouring(laid: [Recolouring.Highlight(onset: 480, end: 960, colour: "forest")])
        model.undo()
        await recoloured(score, 1)
        XCTAssertEqual(score.recoloured, [laid.reversed], "the newest step first: the colour, not the stroke")
        XCTAssertEqual(score.changes.count, 1, "the stroke is still there")
        XCTAssertTrue(model.canUndo)
        XCTAssertTrue(model.canRedo)
        model.redo()
        await recoloured(score, 2)
        XCTAssertEqual(score.recoloured, [laid.reversed, laid], "Redo colours it again")
        XCTAssertFalse(model.canRedo)
        model.undo()
        model.undo()
        await changes(score, 2)
        XCTAssertEqual(score.changes[1].call, "ink.remove", "then the stroke before it")
        XCTAssertFalse(model.canUndo)
        XCTAssertTrue(model.canRedo)
        model.redo()
        await changes(score, 3)
        model.colour(0, 480)
        await coloured(score, 2)
        XCTAssertFalse(model.canRedo, "a new colouring drops what Redo had")
    }

    func testAColouringThatChangedNothingIsNoStep() async {
        let (model, score) = made()
        score.colourChanges = false
        model.pen.choose(.colour)
        model.colour(0, 1920)
        await coloured(score, 1)
        let settled = expectation(description: "the queue's turn is over")
        model.keep(points, in: 7) { settled.fulfill() }
        await fulfillment(of: [settled], timeout: 5)
        XCTAssertFalse(model.canUndo, "nothing to take back: nothing was coloured, and a stroke written with the colour bars is not kept")
    }

    func testTheScoreHearsWhenTheColourBarsComeOrGoOrChangeColour() {
        let (model, score) = made()
        let before = score.colouringChanges
        model.pen.setWidth(2)
        model.pen.setBarColour(.forest)
        XCTAssertEqual(score.colouringChanges, before, "the pencil's thickness, or a colour while the pencil writes, colours nothing")
        model.pen.choose(.colour)
        model.pen.setBarColour(.ochre)
        model.pen.choose(.eraser)
        XCTAssertEqual(score.colouringChanges, before + 3)
    }

    func testTheEraserTakesTheColourItPassedOverWhenItLifts() async {
        let (model, score) = made()
        score.page.rubbed = ["uncoloured": [["start": 240, "end": 360], ["start": 480, "end": 480], ["start": 600, "end": 720]]]
        model.rub([["x": 1, "y": 2]], lifted: true, in: 7)
        await coloured(score, 1)
        XCTAssertEqual(score.coloured, ["off 240-360 600-720"], "a stretch of no length is left out")
        XCTAssertEqual(score.changes, [], "no ink was cut")
        XCTAssertTrue(model.canUndo, "the colour it took is a step (v1.4 i-5)")
    }

    /// A press of the eraser that cut a stroke and took a colour is one step: Undo puts back both, Redo takes both again.
    func testAPressThatCutInkAndTookAColourIsOneStep() async {
        let (model, score) = made()
        score.page.rubbed = ["removed": [["id": "a"]], "uncoloured": [["start": 0, "end": 480]]]
        model.rub([["x": 1, "y": 2]], lifted: true, in: 7)
        await coloured(score, 1)
        let taken = Recolouring(taken: [Recolouring.Highlight(onset: 0, end: 480, colour: "amber")])
        model.undo()
        await recoloured(score, 1)
        XCTAssertEqual(score.changes.map(\.call), ["ink.remove", "ink.add"], "the stroke cut comes back")
        XCTAssertEqual(score.recoloured, [taken.reversed], "and the colour, in the same Undo")
        XCTAssertFalse(model.canUndo, "one step")
        model.redo()
        await recoloured(score, 2)
        XCTAssertEqual(score.changes.map(\.call), ["ink.remove", "ink.add", "ink.remove"])
        XCTAssertEqual(score.recoloured, [taken.reversed, taken])
    }

    func testThePenTurnedOnOrOffGoesToTheScore() {
        let (model, score) = made()
        model.penOn = true
        model.penOn = false
        XCTAssertEqual(score.turned, [true, false])
    }
}
