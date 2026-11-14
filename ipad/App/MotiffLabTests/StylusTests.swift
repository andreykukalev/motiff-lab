import CoreGraphics
import UIKit
import XCTest

@testable import MotiffLab

/// The stylus follows the tool (#479 item 18), the stroke written looks as the stroke kept and the eraser cuts along its
/// path (item 7), and the Pencil's double tap and squeeze open the pencil palette (item 8): the pure parts.
final class StylusTests: XCTestCase {
    // MARK: - Who writes, selects and scrolls

    func testWithAStylusTheStylusFollowsTheToolAndAFingerAlwaysScrolls() {
        let pen = InputRules(stylus: true, tool: .pen)
        XCTAssertTrue(pen.writes(.stylus))
        XCTAssertFalse(pen.writes(.finger), "a finger never writes while the iPad is used with a stylus")
        XCTAssertTrue(pen.scrolls(.finger))
        XCTAssertFalse(pen.scrolls(.stylus))
        XCTAssertEqual(pen.scrollFingers, 1)
        XCTAssertFalse(pen.taps(.stylus), "where the stylus writes, its tap does not act")
        XCTAssertTrue(pen.taps(.finger))

        let select = InputRules(stylus: true, tool: .select)
        XCTAssertFalse(select.writes(.stylus), "with Select the stylus selects and does not draw")
        XCTAssertEqual(select.selectStart(.stylus), .now)
        XCTAssertEqual(select.selectStart(.finger), .longPress)
        XCTAssertTrue(select.scrolls(.finger))
        XCTAssertFalse(select.scrolls(.stylus))

        let navigate = InputRules(stylus: true, tool: .navigate)
        XCTAssertTrue(navigate.scrolls(.stylus))
        XCTAssertEqual(navigate.selectStart(.finger), .longPress, "a long press selects in any tool")
        XCTAssertEqual(navigate.scrollFingers, 1)

        let none = InputRules(stylus: true, tool: .none)
        XCTAssertFalse(none.writes(.stylus), "before the modes, with the Pen off, the stylus does not write")
        XCTAssertTrue(none.scrolls(.stylus))
    }

    func testWithoutAStylusOneFingerTakesTheToolAndTwoScroll() {
        let pen = InputRules(stylus: false, tool: .pen)
        XCTAssertTrue(pen.writes(.finger))
        XCTAssertEqual(pen.scrollFingers, 2)
        XCTAssertEqual(pen.selectStart(.finger), .none)

        let select = InputRules(stylus: false, tool: .select)
        XCTAssertEqual(select.selectStart(.finger), .now)
        XCTAssertEqual(select.scrollFingers, 2)

        let navigate = InputRules(stylus: false, tool: .navigate)
        XCTAssertEqual(navigate.scrollFingers, 1)
        XCTAssertEqual(navigate.selectStart(.finger), .longPress)
        XCTAssertFalse(navigate.writes(.finger))
    }

    func testOnAPdfNothingIsSelectedAndSelectAndEditMoveThePages() {
        let select = InputRules(stylus: true, tool: .select, surface: .pdf)
        XCTAssertEqual(select.effectiveTool, .navigate)
        XCTAssertTrue(select.scrolls(.stylus))
        XCTAssertEqual(select.selectStart(.stylus), .none)
        XCTAssertEqual(select.selectStart(.finger), .none)
        XCTAssertEqual(InputRules(stylus: false, tool: .edit, surface: .pdf).scrollFingers, 1)
        let pen = InputRules(stylus: true, tool: .pen, surface: .pdf)
        XCTAssertTrue(pen.writes(.stylus), "the same rules hold on a PDF's pages")
        XCTAssertFalse(pen.writes(.finger))
    }

    func testTheBarsChoiceIsTheToolAndThePageIsToldHowEachPointerSelects() {
        XCTAssertEqual(InputRules.Tool(mode: .select, penOn: true), .pen, "the Pen on is the Pen in any mode")
        XCTAssertEqual(InputRules.Tool(mode: .edit, penOn: false), .edit)
        XCTAssertEqual(InputRules.Tool(mode: nil, penOn: false), .none)
        let page = InputRules(stylus: true, tool: .select).page
        let pen = page["pen"] as? [String: Any]
        let touch = page["touch"] as? [String: Any]
        XCTAssertEqual(pen?["select"] as? String, "now")
        XCTAssertEqual(pen?["tap"] as? Bool, true)
        XCTAssertEqual(touch?["select"] as? String, "longPress")
        XCTAssertEqual(InputRules.handleReach * 2, 44, "a handle's target is 44 points across")
    }

    func testInThePenTheStylusWritesOverTheHandlesAndAFingerStillTakesThem() {
        let pen = InputRules(stylus: true, tool: .pen)
        XCTAssertFalse(pen.takesHandles(.stylus), "the Pencil writes over a selection's handles in the Pen (#511 4.1)")
        XCTAssertTrue(pen.takesHandles(.finger))
        XCTAssertTrue(pen.inkTakes(.stylus, nearHandle: true), "a stroke at a handle is the pen's, so no edge moves")
        XCTAssertTrue(pen.inkTakes(.stylus, nearHandle: false))
        XCTAssertFalse(pen.inkTakes(.finger, nearHandle: true), "a finger never writes with a stylus")
        let page = pen.page
        XCTAssertEqual((page["pen"] as? [String: Any])?["handles"] as? Bool, false)
        XCTAssertEqual((page["touch"] as? [String: Any])?["handles"] as? Bool, true)

        let bare = InputRules(stylus: false, tool: .pen)
        XCTAssertFalse(bare.inkTakes(.finger, nearHandle: true), "without a stylus the finger that writes still takes a handle")
        XCTAssertTrue(bare.inkTakes(.finger, nearHandle: false))
        XCTAssertTrue(bare.inkTakes(.stylus, nearHandle: true))

        // Out of the Pen the rules come back as they were: every pointer takes the handles, none writes.
        for tool in [InputRules.Tool.select, .navigate, .none] {
            let rules = InputRules(stylus: true, tool: tool)
            XCTAssertTrue(rules.takesHandles(.stylus))
            XCTAssertTrue(rules.takesHandles(.finger))
            XCTAssertFalse(rules.inkTakes(.stylus, nearHandle: false))
        }
        XCTAssertEqual(InputRules(stylus: true, tool: .select).selectStart(.stylus), .now, "leaving the Pen restores Select")
    }

    func testWhileThePenIsOnTheStylusChangesNoSelectionOnTheRoll() {
        let on = RollInput(stylus: true, mode: .navigate, penOn: true)
        XCTAssertFalse(on.selects(.stylus))
        XCTAssertTrue(on.selects(.finger))
        XCTAssertEqual(on.page["penSelects"] as? Bool, false)
        XCTAssertEqual(RollInput(stylus: true, mode: .navigate).page["penSelects"] as? Bool, true)
    }

    func testAGestureBelongsToOneToolSoThePensStylusDoesNothingOnTheRoll() {
        // #521 10: with the Pen on the stylus neither moves the roll, nor edits, nor selects there; a finger does as before.
        let edit = RollInput(stylus: true, mode: .edit, penOn: true)
        XCTAssertFalse(edit.acts(.stylus))
        XCTAssertFalse(edit.edits(.stylus), "the Pen's stylus never edits notes")
        XCTAssertEqual(edit.page["pen"] as? Bool, false)
        XCTAssertTrue(RollInput(stylus: true, mode: .edit).edits(.stylus), "out of the Pen the stylus edits as before")
        XCTAssertTrue(RollInput(stylus: false, mode: .edit, penOn: true).edits(.finger))
        // Edit notes and the Pen put each other away.
        XCTAssertTrue(RollInput.penOff(choosing: .edit))
        XCTAssertFalse(RollInput.penOff(choosing: .navigate))
        XCTAssertTrue(RollInput.rollNavigates(penOn: true, mode: .edit))
        XCTAssertFalse(RollInput.rollNavigates(penOn: true, mode: .navigate))
        XCTAssertFalse(RollInput.rollNavigates(penOn: false, mode: .edit))
    }

    // MARK: - The stroke's outline

    func testThePressureSetsTheThicknessForThePencilOnly() {
        XCTAssertEqual(InkSample.pressure(force: 1, pencil: true), 0.5)
        XCTAssertEqual(InkSample.pressure(force: 3, pencil: true), 1)
        XCTAssertEqual(InkSample.pressure(force: 0, pencil: true), 0.5, "a Pencil that reports no force is an ordinary press")
        XCTAssertEqual(InkSample.pressure(force: 1, pencil: false), 0.5)
        XCTAssertEqual(InkOutline.radius(2, pressure: 0.5, followsPressure: true), 2)
        XCTAssertEqual(InkOutline.radius(2, pressure: 1, followsPressure: true), 3)
        XCTAssertEqual(InkOutline.radius(2, pressure: 0, followsPressure: true), 1)
        XCTAssertEqual(InkOutline.radius(2, pressure: 1, followsPressure: false), 2, "the highlighter keeps its width")
        let eased = InkOutline.radii([InkSample(.zero, pressure: 0), InkSample(.zero, pressure: 1), InkSample(.zero, pressure: 0)], radius: 2, followsPressure: true)
        XCTAssertEqual(eased, [1.5, 2, 1.5], "each point's thickness is eased with its neighbours'")
    }

    func testTheOutlineIsTheStrokesWidthWithRoundEnds() {
        let line = [InkSample(CGPoint(x: 0, y: 0)), InkSample(CGPoint(x: 50, y: 0)), InkSample(CGPoint(x: 100, y: 0))]
        let box = InkOutline.path(line, radius: 2, followsPressure: true).boundingBoxOfPath
        XCTAssertEqual(box.minX, -2, accuracy: 1e-6, "a round end before the first point")
        XCTAssertEqual(box.maxX, 102, accuracy: 1e-6, "and after the last")
        XCTAssertEqual(box.minY, -2, accuracy: 1e-6)
        XCTAssertEqual(box.maxY, 2, accuracy: 1e-6)
        let dot = InkOutline.path([InkSample(CGPoint(x: 10, y: 10))], radius: 3, followsPressure: false).boundingBoxOfPath
        XCTAssertEqual(dot.minX, 7, accuracy: 1e-6, "one point is a dot")
        XCTAssertEqual(dot.width, 6, accuracy: 1e-6)
        XCTAssertEqual(dot.height, 6, accuracy: 1e-6)
        XCTAssertTrue(InkOutline.path([], radius: 3, followsPressure: false).isEmpty)
    }

    // MARK: - The eraser

    func testTheEraserCutsAStrokeWhereItPassesAndLeavesTheRest() throws {
        let line = (0...10).map { InkSample(CGPoint(x: Double($0) * 10, y: 0), pressure: 0.5) }
        let across = [CGPoint(x: 50, y: -20), CGPoint(x: 50, y: 20)]
        let pieces = try XCTUnwrap(InkSplit.pieces(line, radius: 1, path: across, reach: 5))
        XCTAssertEqual(pieces.count, 2, "a pass across the middle leaves both ends")
        XCTAssertLessThan(try XCTUnwrap(pieces.first?.last).point.x, 44)
        XCTAssertGreaterThan(try XCTUnwrap(pieces.last?.first).point.x, 56)
        XCTAssertEqual(pieces.first?.first?.point.x, 0)
        XCTAssertEqual(pieces.last?.last?.point.x, 100)

        XCTAssertNil(InkSplit.pieces(line, radius: 1, path: [CGPoint(x: 50, y: 40)], reach: 5), "a path that misses leaves the stroke alone")
        let along = [CGPoint(x: -10, y: 0), CGPoint(x: 110, y: 0)]
        XCTAssertEqual(InkSplit.pieces(line, radius: 1, path: along, reach: 5)?.count, 0, "a path along it takes all of it")
    }

    /// The eraser on a PDF's page rubs out as it moves (BUG-05): each move cuts at once from where the last left off, a
    /// piece cut again is replaced, and only the kept stroke goes when it lifts.
    func testThePdfEraserRubsOutAsItMovesAndThePressIsOneStep() throws {
        // A space of this page is one point; the stroke's half-thickness is a quarter point.
        let box = CGRect(x: 0, y: 0, width: 120, height: 200)
        let line = (0...10).map { (CGPoint(x: 10 + Double($0) * 10, y: 100), 0.5) }
        let stroke = try XCTUnwrap(DocumentStroke(id: "a", tool: "pen", colour: "carmine", size: 0.16, page: 1, box: box, points: line))
        let elsewhere = try XCTUnwrap(DocumentStroke(id: "b", tool: "pen", colour: "graphite", size: 0.16, page: 2, box: box, points: line))
        let kept = [stroke, elsewhere]
        XCTAssertEqual(stroke.samples(in: box).last?.point.x ?? 0, 110, accuracy: 1e-9)

        var eraser = PageEraser(page: 1, box: box, reach: 1)
        XCTAssertTrue(eraser.move(to: [CGPoint(x: 60, y: 90), CGPoint(x: 60, y: 110)], over: kept), "the first move takes what it passes")
        XCTAssertEqual(eraser.removed.map(\.id), ["a"])
        XCTAssertEqual(eraser.pieces.count, 2, "both ends are left")
        XCTAssertEqual(eraser.shown(kept).map(\.id), ["b"] + eraser.pieces.map(\.id), "the page shows the pieces at once, the other page as it was")
        XCTAssertTrue(eraser.pieces.allSatisfy { $0.colour == "carmine" && $0.page == 1 && $0.size == 0.16 })

        // Round below the line and up across the right-hand piece.
        let right = try XCTUnwrap(eraser.pieces.last?.id)
        XCTAssertTrue(eraser.move(to: [CGPoint(x: 60, y: 120), CGPoint(x: 90, y: 120), CGPoint(x: 90, y: 90)], over: kept))
        XCTAssertEqual(eraser.removed.map(\.id), ["a"], "only the kept stroke goes when it lifts")
        XCTAssertEqual(eraser.pieces.count, 3, "the right-hand piece is cut in two")
        XCTAssertFalse(eraser.pieces.contains { $0.id == right })
        XCTAssertFalse(eraser.move(to: [CGPoint(x: 90, y: 60)], over: kept), "a move over nothing takes nothing")
        XCTAssertFalse(eraser.move(to: [], over: kept))
        let saved = eraser.shown(kept)
        XCTAssertEqual(eraser.shown(saved).count, saved.count, "pieces already kept are not shown twice")

        // A fast move: neither point is near the line, the way between them crosses it.
        var fast = PageEraser(page: 1, box: box, reach: 1)
        XCTAssertFalse(fast.move(to: [CGPoint(x: 50, y: 60)], over: kept))
        XCTAssertTrue(fast.move(to: [CGPoint(x: 52, y: 140)], over: kept))
        XCTAssertEqual(fast.pieces.count, 2)
    }

    /// Undo takes a press of the eraser back whole: the pieces go, the strokes it cut come back, drawn once at the end.
    @MainActor
    func testUndoTakesAPressOfTheEraserBackWhole() async {
        var calls: [String] = []
        let press = InkStep(added: ["p1", "p2"], removed: [["id": "a"]])
        await press.undo(remove: "ink.remove", add: "ink.add") { call, request, draws in
            calls.append("\(call) \(request.keys.sorted().joined()) \(draws)")
        }
        XCTAssertEqual(calls, ["ink.remove ids false", "ink.add strokes true"])
        calls = []
        await InkStep(added: ["s"]).undo(remove: "ink.remove", add: "ink.add") { call, _, draws in calls.append("\(call) \(draws)") }
        XCTAssertEqual(calls, ["ink.remove true"], "a stroke written goes, drawn at once")
    }

    func testTheLineIsWalkedInShortStepsWithItsPressure() {
        let walked = InkSplit.densified([InkSample(.zero, pressure: 0), InkSample(CGPoint(x: 10, y: 0), pressure: 1)], step: 2.5)
        XCTAssertEqual(walked.map(\.point.x), [0, 2.5, 5, 7.5, 10])
        XCTAssertEqual(walked.map(\.pressure), [0, 0.25, 0.5, 0.75, 1])
        XCTAssertEqual(InkSplit.distance(from: CGPoint(x: 5, y: 3), to: [.zero, CGPoint(x: 10, y: 0)]), 3, accuracy: 1e-9)
        XCTAssertEqual(InkSplit.distance(from: CGPoint(x: 3, y: 4), to: [.zero]), 5, accuracy: 1e-9)
    }

    func testAPdfStrokesPointsCarryTheirPressure() {
        let stroke = DocumentStroke(id: "a", tool: "pen", colour: "graphite", size: 0.16, page: 1, u: 0, v: 0, points: [10, 0, 0.8, 20, 5, 0.2], pressure: 0.4)
        let box = CGRect(x: 0, y: 0, width: 120, height: 200)
        let samples = stroke.samples(in: box)
        XCTAssertEqual(samples.map(\.pressure), [0.4, 0.8, 0.2])
        XCTAssertEqual(samples.map(\.point), [CGPoint(x: 0, y: 200), CGPoint(x: 10, y: 200), CGPoint(x: 20, y: 195)])
        XCTAssertEqual(stroke.radius(in: box), 0.25, accuracy: 1e-9, "never thinner than a quarter point")
        XCTAssertEqual(stroke.radius(in: CGRect(x: 0, y: 0, width: 1200, height: 200)), 0.8, accuracy: 1e-9, "half its size in the page's spaces")
    }

    // MARK: - The Pencil's double tap and squeeze

    func testTheIPadsChoiceForTheDoubleTapIsKeptAndEveryPaletteOpensTheApps() {
        XCTAssertEqual(PencilGesture.action(for: .switchEraser), .eraser)
        XCTAssertEqual(PencilGesture.action(for: .switchPrevious), .previous)
        XCTAssertEqual(PencilGesture.action(for: .ignore), .nothing)
        XCTAssertEqual(PencilGesture.action(for: .showColorPalette), .palette)
        XCTAssertEqual(PencilGesture.action(for: .showInkAttributes), .palette)
        if #available(iOS 17.5, *) {
            XCTAssertEqual(PencilGesture.action(for: .showContextualPalette), .palette)
            XCTAssertEqual(PencilGesture.action(for: .runSystemShortcut), .nothing)
        }
    }

    @MainActor
    func testTheGestureOpensThePaletteAndAToolChosenInItTurnsThePenOn() {
        let pdf = PdfController()
        let saved = PenMemory.saved()
        defer { PenMemory.keep(saved) }
        pdf.pen = PenState(active: .marker, previous: .eraser, pencil: ToolSettings(width: 0, ink: .carmine))
        pdf.pencilGesture(.palette, at: CGPoint(x: 300, y: 400))
        XCTAssertEqual(pdf.pencilPalette, PencilPaletteCall(at: CGPoint(x: 300, y: 400)))
        pdf.pencilGesture(.palette, at: nil)
        XCTAssertNil(pdf.pencilPalette, "a second double tap closes it")
        pdf.pencilGesture(.palette, at: nil)
        pdf.chooseFromPalette(.pencil)
        XCTAssertEqual(pdf.tool, .pen(.carmine), "the pencil comes back in its own ink")
        XCTAssertTrue(pdf.penOn, "a tool chosen there writes at once")
        XCTAssertEqual(pdf.pencilPalette, PencilPaletteCall(at: nil), "the palette stays while the player chooses (#543 8)")
        pdf.penOn = false
        pdf.chooseFromPalette(width: 2)
        XCTAssertEqual(pdf.pen.active, .pencil, "a thickness chosen there keeps the tool")
        XCTAssertEqual(pdf.pen.pencil.width, 2)
        XCTAssertTrue(pdf.penOn)
        XCTAssertNotNil(pdf.pencilPalette, "a setting chosen there leaves it open")
        pdf.chooseFromPalette(ink: .forest)
        XCTAssertEqual(pdf.tool, .pen(.forest))
        XCTAssertNotNil(pdf.pencilPalette, "an ink chosen there leaves it open")
        pdf.pencilGesture(.palette, at: nil)
        XCTAssertNil(pdf.pencilPalette, "the double tap still closes it; a touch outside does too (PaletteOutside)")
        pdf.pencilGesture(.eraser, at: nil)
        XCTAssertEqual(pdf.tool, .eraser)
        pdf.pencilGesture(.eraser, at: nil)
        XCTAssertEqual(pdf.tool, .pen(.carmine), "the eraser again goes back to the tool before")
        pdf.pencilGesture(.previous, at: nil)
        XCTAssertEqual(pdf.tool, .eraser, "«switch to previous» goes to the tool before")
    }

    func testThePaletteSpreadsItsButtonsOverAHalfCircleAndKeepsInside() {
        let arc = PaletteLayout.arc(count: 3, radius: 10, upward: true)
        XCTAssertEqual(arc[0].x, -10, accuracy: 1e-9)
        XCTAssertEqual(arc[1].y, -10, accuracy: 1e-9, "upward, the middle button is above")
        XCTAssertEqual(arc[2].x, 10, accuracy: 1e-9)
        XCTAssertEqual(PaletteLayout.arc(count: 3, radius: 10, upward: false)[1].y, 10, accuracy: 1e-9)
        XCTAssertEqual(PaletteLayout.arc(count: 1, radius: 10, upward: true)[0].y, -10, accuracy: 1e-9)
        XCTAssertTrue(PaletteLayout.arc(count: 0, radius: 10, upward: true).isEmpty)

        // One tool's controls side by side about the top, left to right, a spacing apart (#521 11).
        let fan = PaletteLayout.fan(count: 3, radius: 100, spacing: 50, upward: true)
        XCTAssertEqual(fan[1].x, 0, accuracy: 1e-9)
        XCTAssertEqual(fan[1].y, -100, accuracy: 1e-9)
        XCTAssertLessThan(fan[0].x, 0)
        XCTAssertGreaterThan(fan[2].x, 0)
        XCTAssertEqual(hypot(fan[1].x - fan[0].x, fan[1].y - fan[0].y), 50, accuracy: 1e-6)
        XCTAssertEqual(PaletteLayout.fan(count: 1, radius: 100, spacing: 50, upward: false)[0].y, 100, accuracy: 1e-9)
        XCTAssertTrue(PaletteLayout.fan(count: 0, radius: 100, spacing: 50, upward: true).isEmpty)

        let size = CGSize(width: 1000, height: 800)
        let top = PaletteLayout.centre(CGPoint(x: 500, y: 50), in: size, reach: 100)
        XCTAssertFalse(top.upward, "no room above: it opens downward")
        XCTAssertEqual(top.point, CGPoint(x: 500, y: 50))
        let edge = PaletteLayout.centre(CGPoint(x: 20, y: 400), in: size, reach: 100)
        XCTAssertTrue(edge.upward)
        XCTAssertEqual(edge.point, CGPoint(x: 100, y: 400), "kept inside by its reach")
    }

    // MARK: - The first touch outside the palette (#543 8)

    func testATouchIsOnThePaletteOnlyOnOneOfItsRoundButtons() {
        let places = [CGPoint(x: -60, y: 0), CGPoint(x: 0, y: -60), CGPoint(x: 60, y: 0), .zero]
        XCTAssertTrue(PaletteLayout.onButtons(CGPoint(x: 0, y: -60), places: places, diameter: 44), "on a button's middle")
        XCTAssertTrue(PaletteLayout.onButtons(CGPoint(x: 60, y: 22), places: places, diameter: 44), "at its edge")
        XCTAssertTrue(PaletteLayout.onButtons(CGPoint(x: 5, y: 5), places: places, diameter: 44), "on Undo in the middle")
        XCTAssertFalse(PaletteLayout.onButtons(CGPoint(x: 60 + 20, y: 20), places: places, diameter: 44), "a round button's corner is not it")
        XCTAssertFalse(PaletteLayout.onButtons(CGPoint(x: -30, y: -30), places: places, diameter: 44), "the gap between two buttons is the workspace")
        XCTAssertFalse(PaletteLayout.onButtons(CGPoint(x: 400, y: 300), places: places, diameter: 44), "far away is the workspace")
        XCTAssertFalse(PaletteLayout.onButtons(.zero, places: [], diameter: 44), "no buttons, nothing is the palette")
    }
}
