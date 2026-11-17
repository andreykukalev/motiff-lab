import XCTest

@testable import MotiffLab

/// The score's workspace (ScoreWorkspace, the v0.7 blueprint's section 5.3 step 2): the controllers it owns get their
/// callbacks from it, the callbacks reach the panels and the roll through it, and nothing holds it once the screen lets
/// it go. What its methods decide by the panels is in PanelStateTests.
@MainActor
final class ScoreWorkspaceTests: XCTestCase {
    private func make(viewing: String? = nil) -> ScoreWorkspace {
        ScoreWorkspace(piece: OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: ""), viewing: viewing)
    }

    /// Sources is there for the piece, never for a view shown over it: said once, to the panels.
    func testAViewShownOverThePieceHasNoSources() {
        XCTAssertTrue(make().panels.sourcesOn)
        XCTAssertFalse(make(viewing: "view only").panels.sourcesOn)
    }

    /// A flag on the page opens Check, a version Sources, and a tap that selects nothing puts Insights away.
    func testThePageOpensAndClosesPanels() {
        let workspace = make()
        workspace.wire()
        workspace.score.checkPicked?(3)
        XCTAssertTrue(workspace.panels[.check])
        workspace.score.versionPicked?(1)
        XCTAssertTrue(workspace.panels[.versions], "an action writes only what it asks: the rules follow in the view's reactions")
        workspace.change(.open(.insights))
        workspace.score.blankTapped?()
        XCTAssertFalse(workspace.panels[.insights])
    }

    /// The bar's tool and the Pen decide on the roll too, and the roll asks the score how it selects.
    func testTheScoreAndTheRollHearEachOther() {
        let workspace = make()
        workspace.wire()
        XCTAssertEqual(workspace.roll.barTool, RollInput.barTool(mode: .navigate, penOn: false))
        workspace.score.toolChanged?(.select, false)
        XCTAssertEqual(workspace.roll.barTool, RollInput.barTool(mode: .select, penOn: false))
        workspace.score.penChanged?(true)
        XCTAssertTrue(workspace.roll.penOn)
        XCTAssertEqual(workspace.score.rollEditing?(), false)
        XCTAssertEqual(workspace.roll.byBeats?(), false, "no beat lines before the sound is read")
    }

    /// The callbacks hold the workspace weakly and the workspace holds the controllers: no ring keeps a screen alive.
    func testNothingHoldsTheWorkspace() {
        weak var held: ScoreWorkspace?
        autoreleasepool {
            let workspace = make()
            workspace.wire()
            held = workspace
        }
        XCTAssertNil(held)
    }
}
