import XCTest

@testable import MotiffLab

/// The score's workspace (ScoreWorkspace, the v0.7 blueprint's section 5.3 step 2): the controllers it owns get their
/// callbacks from it, the callbacks reach the panels and the roll through it, and nothing holds it once the screen lets
/// it go. The view's reactions are its methods (ScoreWorkspaceReactions.swift): each runs the rule of the value it
/// observes and what goes with it. What the rules decide is in PanelStateTests; in what order the view calls them, in
/// docs/code-map/ipad/workspace.md.
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

    /// Each reaction on the panels or Expand runs the rule of the value it observes (PanelState.following), and no other:
    /// from panels where every rule changes something and no two rules leave the same. Not the roll's two, which show the
    /// piece's notes: what they decide is in PanelStateTests.
    func testEachReactionRunsItsRule() {
        let reactions: [(rule: PanelState.Rule, react: (ScoreWorkspace) -> Void)] = [
            (.contents(true), { $0.contentsChanged(true) }),
            (.expand(true), { $0.focusChanged(true) }),
            (.expand(false), { $0.focusChanged(false) }),
            (.insights(true), { $0.insightsChanged(true) }),
            (.placeFromMore(false), { $0.placeFromMoreChanged(false) }),
            (.check(true), { $0.checkDrawerChanged(true) }),
            (.versions(true, focused: false), { $0.sourcesChanged(true) }),
            (.otherPlace(true), { $0.otherPlaceChanged(true) }),
        ]
        var left: [PanelState] = []
        for (rule, react) in reactions {
            let workspace = make()
            // Insights, the Contents, Check and Sources open, Settings from More, the roll's drawer kept for after Expand.
            for panel in [PanelState.Panel.insights, .contents, .check, .versions, .roll] { workspace.change(.open(panel)) }
            workspace.change(.openFromMore(.settings))
            workspace.change(.expandFromDrawer)
            let before = workspace.panels
            react(workspace)
            XCTAssertEqual(workspace.panels, before.following(rule), "\(rule)")
            XCTAssertNotEqual(workspace.panels, before, "\(rule) changes these panels")
            XCTAssertFalse(left.contains(workspace.panels), "\(rule) leaves what another rule left")
            left.append(workspace.panels)
        }
    }

    /// Sources closed gives the view back as it was under it: Expand, which would keep it away, is read from the score.
    func testSourcesGivesBackWhatWasUnderIt() {
        let workspace = make()
        workspace.change(.open(.roll))
        workspace.change(.open(.versions))
        workspace.sourcesChanged(true)
        XCTAssertFalse(workspace.panels[.roll])
        workspace.change(.close(.versions))
        workspace.sourcesChanged(false)
        XCTAssertTrue(workspace.panels[.roll])
    }

    /// The Contents closed takes a bookmark being changed in it with it; one being made over the selection stays.
    func testTheContentsTakesTheBookmarkBeingChangedWithIt() {
        let workspace = make()
        workspace.contents.draft = ContentsController.Draft(id: "b1", title: "Coda", category: "practise")
        workspace.contentsChanged(true)
        XCTAssertNotNil(workspace.contents.draft, "opened, the card stays")
        workspace.contentsChanged(false)
        XCTAssertNil(workspace.contents.draft)
        workspace.contents.start(title: "Theme")
        workspace.contentsChanged(false)
        XCTAssertNotNil(workspace.contents.draft, "a bookmark being made belongs to the selection, not to the drawer")
    }

    /// Check's flags show on the page while Check is open, and the scale's button knows while the scale sounds.
    func testThePageAndInsightsFollowWhatTheViewObserves() {
        let workspace = make()
        workspace.checkDrawerChanged(true)
        XCTAssertTrue(workspace.score.checkDrawerOpen)
        workspace.checkDrawerChanged(false)
        XCTAssertFalse(workspace.score.checkDrawerOpen)
        workspace.hearingAloneChanged(true)
        XCTAssertTrue(workspace.insights.scaleHeard)
        workspace.hearingAloneChanged(false)
        XCTAssertFalse(workspace.insights.scaleHeard)
    }

    /// One thing at a time on the right: the assistant's panel opened puts the sheet away, a sheet opened the panel;
    /// neither does anything as it closes.
    func testTheAssistantAndTheSheetTakeTurns() {
        let workspace = make()
        workspace.change(.open(.insights))
        workspace.change(.open(.roll))
        workspace.assistantPanelChanged(false)
        XCTAssertTrue(workspace.panels[.insights])
        workspace.assistantPanelChanged(true)
        XCTAssertFalse(workspace.panels.sheetOpen)
        XCTAssertTrue(workspace.panels[.roll], "the roll's drawer is not on the right")
        workspace.assistant.panelOpen = true
        workspace.sheetChanged(false)
        XCTAssertTrue(workspace.assistant.panelOpen)
        workspace.sheetChanged(true)
        XCTAssertFalse(workspace.assistant.panelOpen)
    }

    /// The roll is on screen in its drawer while the drawer is open (on Expand's card: RollTests, `RollPlace.shown`).
    func testTheRollIsWhereItsDrawerIs() {
        let workspace = make()
        XCTAssertNil(workspace.rollPlace)
        XCTAssertFalse(workspace.rollShown)
        workspace.change(.open(.roll))
        XCTAssertEqual(workspace.rollPlace, .drawer)
        XCTAssertTrue(workspace.rollShown)
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
