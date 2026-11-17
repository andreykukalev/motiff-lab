import UIKit
import XCTest

@testable import MotiffLab

/// The score's workspace (ScoreWorkspace, the v0.7 blueprint's section 5.3 step 2): the controllers it owns get their
/// callbacks from it, the callbacks reach the panels and the roll through it, and nothing holds it once the screen lets
/// it go. The view's reactions are its methods (ScoreWorkspaceReactions.swift, ScoreWorkspaceFollowing.swift): each runs
/// the rule of the value it observes and what goes with it. What the rules decide is in PanelStateTests; in what order
/// the view calls them, in docs/code-map/ipad/workspace.md.
@MainActor
final class ScoreWorkspaceTests: XCTestCase {
    /// The window of a score drawn for a test, and the pieces opened in music-core for it.
    private var window: UIWindow?
    private var opened: [Int32] = []

    override func tearDown() async throws {
        window = nil
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    private func make(viewing: String? = nil) -> ScoreWorkspace {
        ScoreWorkspace(piece: OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: ""), viewing: viewing)
    }

    /// A workspace on the kit's piece with its page drawn and its first two bars selected: what Expand opens on. No
    /// reaction runs here (there is no view), so the player has not been told of the selection.
    private func selected() async throws -> ScoreWorkspace {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let workspace = ScoreWorkspace(piece: piece)
        let frame = CGRect(x: 0, y: 0, width: 1024, height: 1366)
        workspace.score.webView.frame = frame
        let holder = UIWindow(frame: frame)
        holder.addSubview(workspace.score.webView)
        holder.isHidden = false
        window = holder
        workspace.score.show(piece, dark: false)
        let drawn = await workspace.score.whenRendered()
        XCTAssertTrue(drawn, "the score's page is drawn")
        await workspace.score.select(request: #"{"bars": {"first": 1, "last": 2}}"#, step: false)
        XCTAssertNotNil(workspace.score.playRange, "the first two bars are selected")
        return workspace
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

    /// Sources closed gives the view back as it was under it, out of Expand (in Expand: the next test).
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

    /// Sources closed while Expand is open gives nothing back: Expand, which keeps the drawer away, is read from the score.
    func testSourcesClosedInExpandGivesNothingBack() async throws {
        let workspace = try await selected()
        workspace.change(.open(.roll))
        workspace.change(.open(.versions))
        workspace.sourcesChanged(true)
        workspace.score.enterFocus()
        XCTAssertTrue(workspace.score.focused, "Expand opens on the selection")
        workspace.change(.close(.versions))
        workspace.sourcesChanged(false)
        XCTAssertFalse(workspace.panels[.roll], "Expand keeps what was under Sources away")
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

    /// In focus the other version is there to hear in the variation's place, when it sounds in the phrase; it goes as the
    /// focus does, and a variation the player tried is not taken for it.
    func testTheVersionInFocusSoundsInTheVariationsPlace() {
        let workspace = make()
        var version = ScoreController.FocusVersion(id: "v1", name: "Urtext", notes: [], sounds: true, differing: [])
        workspace.focusVersionChanged(version)
        XCTAssertTrue(workspace.player.hasVariation)
        XCTAssertTrue(workspace.player.secondIsVersion)
        workspace.focusVersionChanged(nil)
        XCTAssertFalse(workspace.player.hasVariation, "the version goes with the focus")
        version.sounds = false
        workspace.focusVersionChanged(version)
        XCTAssertFalse(workspace.player.hasVariation, "a version with no notes in the phrase is not heard there")
        workspace.player.setVariation([])
        workspace.focusVersionChanged(nil)
        XCTAssertTrue(workspace.player.hasVariation, "a variation tried is not a version: it stays")
    }

    /// A bookmark being made and a note being written belong to the selection they were started on: they go when it
    /// changes. A bookmark being changed in the Contents is not the selection's, and the note over this very span stays.
    func testWhatWasStartedOnASelectionGoesWithIt() {
        let workspace = make()
        let note = ScoreController.NoteDraft(id: nil, onset: 0, end: 1920, firstBar: 1, lastBar: 1, text: "")
        workspace.contents.start(title: "Theme")
        workspace.score.noteDraft = note
        workspace.selectionChanged(ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 1))
        XCTAssertNil(workspace.contents.draft)
        XCTAssertEqual(workspace.score.noteDraft, note, "the note is over this very span")
        workspace.contents.draft = ContentsController.Draft(id: "b1", title: "Coda", category: "practise")
        workspace.selectionChanged(ScoreSelection(onset: 0, end: 3840, firstBar: 1, lastBar: 2))
        XCTAssertNotNil(workspace.contents.draft, "a bookmark being changed in the drawer stays")
        XCTAssertNil(workspace.score.noteDraft, "the same start, another end: another selection")
        workspace.score.noteDraft = note
        workspace.selectionChanged(ScoreSelection(onset: 960, end: 1920, firstBar: 1, lastBar: 1))
        XCTAssertNil(workspace.score.noteDraft, "another start, the same end: another selection")
        workspace.score.noteDraft = note
        workspace.selectionChanged(nil)
        XCTAssertNil(workspace.score.noteDraft, "no selection, no note over it")
    }

    /// The roll's one web view is held where the roll is shown, and the roll edits notes only while the editor is open.
    func testTheRollFollowsItsPlaceAndTheNoteEditor() {
        let workspace = make()
        workspace.rollPlaceChanged(.focus)
        XCTAssertEqual(workspace.roll.place, .focus)
        workspace.rollPlaceChanged(nil)
        XCTAssertNil(workspace.roll.place)
        workspace.roll.mode = .edit
        XCTAssertFalse(workspace.rollEditing, "Edit notes chosen, no editor open yet")
        workspace.noteEditorChanged(NoteEditorState())
        XCTAssertTrue(workspace.rollEditing)
        workspace.noteEditorChanged(nil)
        XCTAssertEqual(workspace.roll.mode, .navigate, "the editor closed, the roll is back in Navigate")
    }

    /// Out of Expand the player takes what plays for the selection (the playback model, T9–T11), and lets it go with it.
    func testThePlayerTakesWhatPlaysForTheSelection() {
        let workspace = make()
        let plays = ScoreController.PlayRange(selection: ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 1), range: 0..<1920)
        workspace.whatPlaysChanged(from: nil, to: plays)
        XCTAssertEqual(workspace.player.selection, 0..<1920)
        workspace.whatPlaysChanged(from: plays, to: nil)
        XCTAssertNil(workspace.player.selection)
    }

    /// Expand leaves the playback model as it is (T18): in it the player is not told of the selection, neither as what
    /// plays changes nor once the sound has loaded; out of it, it is.
    func testExpandLeavesThePlaybackModelAsItIs() async throws {
        let workspace = try await selected()
        let plays = try XCTUnwrap(workspace.score.playRange)
        workspace.score.enterFocus()
        XCTAssertTrue(workspace.score.focused, "Expand opens on the selection")
        workspace.applyModel()
        XCTAssertNil(workspace.player.selection, "the sound loaded in Expand")
        workspace.whatPlaysChanged(from: nil, to: plays)
        XCTAssertNil(workspace.player.selection, "what plays changed in Expand")
        workspace.score.leaveFocus()
        workspace.applyModel()
        XCTAssertEqual(workspace.player.selection, plays.range, "the sound loaded on the page")
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
