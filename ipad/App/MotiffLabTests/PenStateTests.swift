import XCTest

@testable import MotiffLab

/// The pen's state (v0.5.2, #495 UX-06): exactly one active tool, a thickness or an ink is a setting of that tool and
/// never switches tools, and each tool keeps its own settings. Every highlight of the pen palettes is read from it.
final class PenStateTests: XCTestCase {
    func testExactlyOneToolIsChosenAndTheEraserLeavesWhollyForThePencil() {
        var pen = PenState()
        XCTAssertEqual(pen.active, .pencil, "the pencil the first time")
        pen.choose(.eraser)
        XCTAssertEqual(ActiveTool.allCases.filter { pen.active == $0 }, [.eraser])
        XCTAssertNil(pen.current, "the eraser has no settings to show")
        XCTAssertFalse(PenSizes.pen.indices.contains { pen.isChosen(width: $0) }, "with the eraser no thickness shows as chosen")
        XCTAssertFalse(ScoreController.Ink.allCases.contains { pen.isChosen(ink: $0) }, "nor an ink")
        XCTAssertEqual(pen.stroke, .eraser)

        pen.choose(.pencil)
        XCTAssertEqual(ActiveTool.allCases.filter { pen.active == $0 }, [.pencil])
        XCTAssertEqual(pen.stroke, .pen(.graphite))
        XCTAssertEqual(PenSizes.pen.indices.filter { pen.isChosen(width: $0) }, [PenSizes.defaultWidth], "one thickness, the pencil's")
        XCTAssertEqual(ScoreController.Ink.allCases.filter { pen.isChosen(ink: $0) }, [.graphite], "one ink, the pencil's")
    }

    func testAThicknessNeverSwitchesTheTool() {
        var pen = PenState()
        pen.choose(.eraser)
        pen.setWidth(2)
        XCTAssertEqual(pen.active, .eraser, "a thickness does not take the eraser back to the tool before")
        XCTAssertEqual(pen.pencil.width, PenSizes.defaultWidth, "and changes no other tool's thickness")
        XCTAssertEqual(pen.marker.width, PenSizes.defaultWidth)

        pen.choose(.marker)
        pen.setWidth(0)
        XCTAssertEqual(pen.active, .marker)
        XCTAssertEqual(pen.marker.width, 0)
        XCTAssertEqual(pen.pencil.width, PenSizes.defaultWidth, "the marker's thickness is its own")
        pen.setWidth(7)
        XCTAssertEqual(pen.marker.width, 0, "a thickness that does not exist changes nothing")
    }

    func testAnInkIsThePencilsAndNeverSwitchesTheTool() {
        var pen = PenState()
        pen.setInk(.forest)
        XCTAssertEqual(pen.stroke, .pen(.forest))
        pen.choose(.marker)
        pen.setInk(.carmine)
        XCTAssertEqual(pen.active, .marker, "an ink does not switch to the pencil")
        XCTAssertEqual(pen.stroke, .highlighter)
        XCTAssertFalse(ScoreController.Ink.allCases.contains { pen.isChosen(ink: $0) }, "the marker shows no ink as chosen")
        pen.choose(.pencil)
        XCTAssertEqual(pen.stroke, .pen(.carmine))
    }

    func testEachToolKeepsItsSettingsWhenAnotherIsChosen() {
        var pen = PenState()
        pen.setInk(.ochre)
        pen.setWidth(2)
        pen.choose(.marker)
        pen.setWidth(0)
        pen.choose(.eraser)
        pen.choose(.pencil)
        XCTAssertEqual(pen.pencil, ToolSettings(width: 2, ink: .ochre))
        XCTAssertEqual(pen.current, pen.pencil)
        pen.choose(.marker)
        XCTAssertEqual(pen.current, ToolSettings(width: 0, ink: nil))
        XCTAssertEqual(PenSizes.pen.indices.filter { pen.isChosen(width: $0) }, [0], "the marker's own thickness shows")
    }

    func testTheEraserAndThePreviousToolAreTheWaysBack() {
        var pen = PenState(active: .marker, previous: .eraser)
        pen.toggleEraser()
        XCTAssertEqual(pen.active, .eraser)
        pen.toggleEraser()
        XCTAssertEqual(pen.active, .marker, "the eraser again goes back to the tool before")
        pen.choosePrevious()
        XCTAssertEqual(pen.active, .eraser)
        pen.choosePrevious()
        XCTAssertEqual(pen.active, .marker)
        pen.choose(.marker)
        XCTAssertEqual(pen.previous, .eraser, "choosing the tool already chosen changes nothing")

        var lost = PenState(active: .eraser, previous: .eraser)
        lost.toggleEraser()
        XCTAssertEqual(lost.active, .pencil, "from the eraser with no other tool before, the pencil")
    }

    func testSettingsOutOfRangeAreTheMiddleOnes() {
        let pen = PenState(pencil: ToolSettings(width: 9, ink: nil), marker: ToolSettings(width: -1, ink: .carmine))
        XCTAssertEqual(pen.pencil, ToolSettings(width: PenSizes.defaultWidth, ink: .graphite))
        XCTAssertEqual(pen.marker, ToolSettings(width: PenSizes.defaultWidth, ink: nil), "the marker has no ink")
    }

    /// The pen's tool and each tool's settings are kept on the device (#479 item 11), so PDF | Notes and opening a piece
    /// again keep what the player chose; what an iPad kept before UX-06 is read the same way.
    func testThePensStateIsKeptOnTheDeviceAndTheOldKeysStillRead() throws {
        let suite = "motiff.tests.pen"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defaults.removePersistentDomain(forName: suite)
        defer { defaults.removePersistentDomain(forName: suite) }

        XCTAssertEqual(PenMemory.saved(in: defaults), PenState(), "the graphite pencil, medium, the first time")

        var pen = PenState()
        pen.setInk(.carmine)
        pen.setWidth(2)
        pen.choose(.marker)
        pen.setWidth(0)
        PenMemory.keep(pen, in: defaults)
        XCTAssertEqual(PenMemory.saved(in: defaults), pen, "the marker chosen, the pencil kept carmine and thick")

        defaults.removePersistentDomain(forName: suite)
        defaults.set("highlighter", forKey: PenMemory.toolKey)
        defaults.set("pen.forest", forKey: PenMemory.previousKey)
        defaults.set(2, forKey: PenMemory.penWidthKey)
        let old = PenMemory.saved(in: defaults)
        XCTAssertEqual(old.active, .marker)
        XCTAssertEqual(old.previous, .pencil)
        XCTAssertEqual(old.pencil, ToolSettings(width: 2, ink: .forest), "the ink of the tool before, as kept before UX-06")

        for tool in ActiveTool.allCases {
            XCTAssertEqual(PenMemory.tool(PenMemory.code(tool, ink: .ochre))?.tool, tool)
        }
        XCTAssertNil(PenMemory.tool("pen.violet"), "an ink the app does not have is not a tool")
        defaults.set(7, forKey: PenMemory.penWidthKey)
        XCTAssertEqual(PenMemory.savedWidth(highlighter: false, in: defaults), PenSizes.defaultWidth, "a thickness out of range is the middle one")
    }
}
