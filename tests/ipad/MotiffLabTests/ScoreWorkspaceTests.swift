import SwiftUI
import UIKit
import XCTest

@testable import MotiffLab

/// The score's workspace (ScoreWorkspace, the v0.7 blueprint's section 5.3 step 2): the controllers it owns get their
/// callbacks from it, and the callbacks reach the panels and the roll through it (that nothing holds it once the screen
/// lets it go, and whole sessions on it: tests/ipad/Baseline/WorkspaceScenarioTests.swift). The view's reactions are its
/// methods (ScoreWorkspaceReactions.swift, ScoreWorkspaceFollowing.swift): each runs the rule of the value it observes
/// and what goes with it. What the rules decide is in PanelStateTests; in what order
/// the view calls them, in docs/code-map/ipad/workspace.md. The screen's life and the piece's views are its methods too
/// (ScoreWorkspaceLife.swift, ScoreWorkspaceSources.swift): what is kept as the screen goes, and what a view picked does.
@MainActor
final class ScoreWorkspaceTests: XCTestCase {
    /// The windows of the scores drawn for a test, and the pieces opened in music-core for it.
    private var windows: [UIWindow] = []
    private var opened: [Int32] = []

    override func tearDown() async throws {
        windows = []
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    private func make(viewing: String? = nil) -> ScoreWorkspace {
        ScoreWorkspace(piece: OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: ""), viewing: viewing)
    }

    /// A workspace on the kit's piece with its page drawn and its first two bars selected: what Expand opens on. No
    /// reaction runs here (there is no view), so the player has not been told of the selection. `fit` sets what the
    /// library would have said of the piece (its project file, its id).
    private func selected(_ fit: (inout OpenPiece) -> Void = { _ in }) async throws -> ScoreWorkspace {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        var piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        fit(&piece)
        let workspace = await drawn(piece)
        await workspace.score.select(request: #"{"bars": {"first": 1, "last": 2}}"#, step: false)
        XCTAssertNotNil(workspace.score.focusModel.playRange, "the first two bars are selected")
        return workspace
    }

    /// A workspace on a piece open in music-core, with its page drawn.
    private func drawn(_ piece: OpenPiece) async -> ScoreWorkspace {
        let workspace = ScoreWorkspace(piece: piece)
        let frame = CGRect(x: 0, y: 0, width: 1024, height: 1366)
        workspace.score.webView.frame = frame
        let holder = UIWindow(frame: frame)
        holder.addSubview(workspace.score.webView)
        holder.isHidden = false
        windows.append(holder)
        workspace.score.show(piece, dark: false)
        let drawn = await workspace.score.whenRendered()
        XCTAssertTrue(drawn, "the score's page is drawn")
        return workspace
    }

    /// A project file's place for a test, with nothing there yet.
    private func projectFile() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("workspace-\(UUID().uuidString).motiff.json")
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
        XCTAssertTrue(workspace.score.focusModel.focused, "Expand opens on the selection")
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

    /// The bottom bar's assistant button (Andrei's v0.8 finding 8, his card of 2026-11-18 21:49 UTC): it opens the panel on
    /// the last conversation, not empty as an Ask does, and the next press closes it, keeping that conversation.
    func testTheBarsAssistantButtonOpensTheLastConversationAndClosesIt() {
        let workspace = make()
        workspace.assistant.start(AssistantExchange(id: "e0", question: "Earlier", scope: "Bar 1"), answer: "Before", role: "mentor")
        let last = workspace.assistant.currentId
        XCTAssertNotNil(last, "a conversation was had")
        workspace.assistantPressed()
        XCTAssertTrue(workspace.assistant.panelOpen)
        XCTAssertEqual(workspace.assistant.currentId, last, "the last conversation, not an empty one")
        workspace.assistantPressed()
        XCTAssertFalse(workspace.assistant.panelOpen)
        XCTAssertEqual(workspace.assistant.currentId, last, "closing keeps it")
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
        var version = FocusModel.Version(id: "v1", name: "Urtext", notes: [], sounds: true, differing: [])
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
        let plays = FocusModel.PlayRange(selection: ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 1), range: 0..<1920)
        workspace.whatPlaysChanged(from: nil, to: plays)
        XCTAssertEqual(workspace.player.selection, 0..<1920)
        workspace.whatPlaysChanged(from: plays, to: nil)
        XCTAssertNil(workspace.player.selection)
    }

    /// Expand leaves the playback model as it is (T18): in it the player is not told of the selection, neither as what
    /// plays changes nor once the sound has loaded; out of it, it is.
    func testExpandLeavesThePlaybackModelAsItIs() async throws {
        let workspace = try await selected()
        let plays = try XCTUnwrap(workspace.score.focusModel.playRange)
        workspace.score.enterFocus()
        XCTAssertTrue(workspace.score.focusModel.focused, "Expand opens on the selection")
        workspace.applyModel()
        XCTAssertNil(workspace.player.selection, "the sound loaded in Expand")
        workspace.whatPlaysChanged(from: nil, to: plays)
        XCTAssertNil(workspace.player.selection, "what plays changed in Expand")
        workspace.score.leaveFocus()
        workspace.applyModel()
        XCTAssertEqual(workspace.player.selection, plays.range, "the sound loaded on the page")
    }

    // MARK: - The screen's life

    /// The "Notes" view and the resume point are read as the screen is now: the roll's drawer from the panels, and
    /// nothing kept looping without a selection.
    func testTheViewAndTheResumePointAreReadAsTheScreenIsNow() {
        let workspace = make()
        XCTAssertFalse(workspace.notesViewNow().rollOpen)
        workspace.change(.open(.roll))
        let view = workspace.notesViewNow()
        XCTAssertTrue(view.rollOpen)
        XCTAssertNil(view.selection)
        XCTAssertFalse(view.loop, "Цикл loops a selection: with none, nothing is kept looping")
        XCTAssertEqual(view.position, workspace.player.tick)
        let now = workspace.resumeNow()
        XCTAssertNil(now?.passage)
        XCTAssertEqual(now?.tick, workspace.player.tick)
        XCTAssertEqual(now?.focused, false)
        XCTAssertEqual(now?.tempoPercent, workspace.player.tempoPercent)
        XCTAssertEqual(now?.loop, false)
    }

    /// PLAY-06, Andrei's answer 3 (2026-11-18 12:55 UTC): a finger's tap on a beat with the Pen on places the playhead as in
    /// Navigate (#479 item 18) and does not turn Цикл off; outside the selection Play plays from there (the lead-in).
    func testAFingersTapWithThePenOnLeavesCycleOn() async throws {
        let workspace = try await selected()
        let passage = try XCTUnwrap(workspace.score.focusModel.playRange?.range)
        workspace.player.hold(selection: passage)
        XCTAssertTrue(workspace.player.cycle, "a selection made where there was none turns Цикл on")
        workspace.score.penOn = true
        workspace.placeTapped(passage.upperBound + 480..<passage.upperBound + 960)
        XCTAssertTrue(workspace.player.cycle, "the Pen's tap leaves Цикл on")
        XCTAssertTrue(workspace.player.leadIn, "Play plays from the tap into the selection")
        XCTAssertEqual(workspace.player.selection, passage)
    }

    /// Finding 2 of the 0.8 test (Andrei's answer 4, 2026-11-18): a tap on a beat of Expand's card moves the playhead there
    /// inside the passage, as on Expand's roll, and starts nothing; the page's model is left as it is (T18), so a tap past
    /// the passage makes no lead-in out of it as a tap on the page would.
    func testATapInExpandMovesThePlayheadInsideThePassageOnly() async throws {
        let workspace = try await selected()
        let passage = try XCTUnwrap(workspace.score.focusModel.playRange?.range)
        workspace.player.hold(selection: passage)
        workspace.score.enterFocus()
        XCTAssertEqual(workspace.score.expandPassage, passage, "Expand opens on the selection")
        workspace.placeTapped(passage.upperBound + 480..<passage.upperBound + 960)
        XCTAssertFalse(workspace.player.leadIn, "no lead-in: the tap stays inside the passage")
        XCTAssertTrue(workspace.player.cycle, "Цикл as it was")
        XCTAssertEqual(workspace.player.selection, passage, "the page's selection as it was")
        XCTAssertEqual(workspace.player.state, .stopped, "a tap starts nothing")
    }

    /// PDF | Notes (#511 section 10): the "Notes" view kept as the screen goes comes back on the screen that opens from
    /// the PDF (`Landing.kept`): the selection and the roll's drawer, and the pages show once they stand there.
    func testTheNotesViewKeptComesBack() async throws {
        let key = "workspace-tests-\(UUID().uuidString)"
        defer { UserDefaults.standard.removeObject(forKey: ViewStates.notesKey(key)) }
        let own = try XCTUnwrap(PieceSummary(answer: #"{"project": {"id": "\#(key)"}}"#))
        let left = try await selected { $0.own = own }
        let selection = try XCTUnwrap(left.score.selectionModel.selection)
        left.change(.open(.roll))
        let view = left.notesViewNow()
        XCTAssertEqual(view.selection, SelectionSpanKept(selection))
        XCTAssertEqual(view.loop, left.player.cycle, "with a selection, Цикл as it is")
        await left.keepNotesView(view)
        XCTAssertEqual(ViewStates.notes(key)?.selection, view.selection)
        XCTAssertEqual(ViewStates.notes(key)?.rollOpen, true)

        var piece = left.piece
        piece.landing = .kept
        let back = await drawn(piece)
        XCTAssertNil(back.score.selectionModel.selection)
        XCTAssertEqual(back.score.webView.alpha, 0, "the pages wait out of sight for the view kept")
        await back.returnToNotesView()
        XCTAssertEqual(back.score.selectionModel.selection?.onset, selection.onset)
        XCTAssertEqual(back.score.selectionModel.selection?.end, selection.end)
        XCTAssertTrue(back.panels[.roll], "the roll's drawer is open again")
        XCTAssertEqual(back.score.webView.alpha, 1, "the pages show once they stand where they were left")
    }

    /// Only the view kept comes back: a piece opened from the library, or at a page of the PDF, is left as it opens.
    func testOnlyAViewKeptComesBack() async {
        var piece = OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: "")
        piece.landing = .page(2)
        let workspace = ScoreWorkspace(piece: piece)
        workspace.score.webView.alpha = 0
        workspace.change(.open(.roll))
        await workspace.returnToNotesView()
        XCTAssertEqual(workspace.score.webView.alpha, 0)
        XCTAssertTrue(workspace.panels[.roll])
    }

    /// A visit keeps one resume point (#327): the second time it is brought up to date, not made again.
    func testAVisitKeepsOneResumePoint() async throws {
        let workspace = try await selected()
        let selection = try XCTUnwrap(workspace.score.selectionModel.selection)
        XCTAssertNil(workspace.resumeId)
        await workspace.keepResume(workspace.resumeNow())
        let first = try XCTUnwrap(workspace.resumeId, "music-core says which point it saved")
        await workspace.keepResume(workspace.resumeNow())
        XCTAssertEqual(workspace.resumeId, first)
        let place = try XCTUnwrap(ResumePlace(answer: MusicCore.call(piece: workspace.piece.id, "resume")))
        XCTAssertEqual(place.id, first)
        XCTAssertEqual(place.passage, selection.onset..<selection.end)
    }

    /// The app gone to the background writes the project file, the resume point in it; nothing is written before.
    func testTheAppLeavingWritesTheProjectFile() async throws {
        let file = projectFile()
        defer { try? FileManager.default.removeItem(at: file) }
        let workspace = try await selected { $0.file = file; $0.own = nil }
        workspace.scenePhaseChanged(.active)
        workspace.scenePhaseChanged(.inactive)
        try await Task.sleep(for: .milliseconds(300))
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path), "only the background writes the file")
        workspace.scenePhaseChanged(.background)
        for _ in 0..<100 where !FileManager.default.fileExists(atPath: file.path) {
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
        XCTAssertNotNil(workspace.resumeId, "the resume point is kept before the file is written")
    }

    /// Back in the app, the score and the roll read again who draws (the iPad's «Only draw with Apple Pencil» may have
    /// changed meanwhile); not on the way out.
    func testTheAppBackReadsWhoDrawsAgain() {
        let workspace = make()
        workspace.wire()
        let navigate = workspace.roll.barTool
        workspace.roll.barTool = RollInput.barTool(mode: .select, penOn: false)
        workspace.scenePhaseChanged(.inactive)
        XCTAssertNotEqual(workspace.roll.barTool, navigate)
        workspace.scenePhaseChanged(.active)
        XCTAssertEqual(workspace.roll.barTool, navigate, "the score's tool, told again")
    }

    /// While the piece is handed to another screen, a second press hands nothing over (#582 item 2); without a way to
    /// the other view, or a project file to open again, nothing is handed over at all.
    func testAPieceBeingHandedOverIsNotHandedAgain() async {
        var piece = OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: "")
        piece.file = projectFile()
        let workspace = ScoreWorkspace(piece: piece)
        var switched = 0
        await workspace.showPdf(nil)
        XCTAssertFalse(workspace.handingOver, "no other view to hand it to")
        workspace.handingOver = true
        await workspace.showPdf { _ in switched += 1 }
        XCTAssertEqual(switched, 0)
        let reopen = await workspace.handOverForLanguage()
        XCTAssertNil(reopen)
        XCTAssertTrue(workspace.handingOver, "the hand-over under way goes on")
        let unsaved = await make().handOverForLanguage()
        XCTAssertNil(unsaved, "no project file to open again")
    }

    /// A piece handed to its other view was saved and closed already: as its screen goes, that is asked once and the
    /// Sources sheet's way back is let go.
    func testAScreenGoingAfterAHandOverAsksOnce() async {
        let workspace = ScoreWorkspace(piece: OpenPiece(id: 9_000_002, title: "Nocturne", pages: [], timemap: ""))
        workspace.sources.reopen = {}
        await ClosedPieces.close(workspace.piece.id)
        workspace.disappeared()
        XCTAssertNil(workspace.sources.reopen)
        XCTAssertFalse(ClosedPieces.take(workspace.piece.id), "asked as the screen went")
    }

    // MARK: - More, the sources and the views

    /// ‹ More: the place opened from More goes at once, and More opens again once it has.
    func testBackToMoreOpensMoreOnceThePlaceHasGone() async throws {
        let workspace = make()
        workspace.change(.openFromMore(.settings))
        XCTAssertTrue(workspace.panels[.settings])
        workspace.backToMore(.settings)
        XCTAssertFalse(workspace.panels[.settings])
        XCTAssertFalse(workspace.panels[.more], "not while the sheet is going")
        for _ in 0..<50 where !workspace.panels[.more] {
            try await Task.sleep(for: .milliseconds(100))
        }
        XCTAssertTrue(workspace.panels[.more])
    }

    /// A view shown over the piece has no sources: nothing is read for it and it has no way to open the piece again.
    func testAViewShownOverThePieceReadsNoSources() async {
        let workspace = make(viewing: "view only")
        var picked = 0
        await workspace.loadSources(reopen: { _ in }, pick: { _ in picked += 1 })
        XCTAssertNil(workspace.sources.reopen)
        XCTAssertEqual(picked, 0)
    }

    /// A recording picked in the switch is heard from under the bar; the working notes picked put it away, and are
    /// this screen already.
    func testARecordingPickedIsHeardAndTheWorkingNotesPutItAway() {
        var piece = OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: "")
        piece.file = projectFile()
        var layers = SourceLayers()
        layers.working = "n1"
        SourcesHandover.give(layers, to: piece.file)
        defer { SourcesHandover.drop(piece.file) }
        let workspace = ScoreWorkspace(piece: piece)
        var shown = 0
        let pick: (SourceLayers.ViewEntry) -> Void = { workspace.pickView($0, switchView: nil, text: UIText.current) { _ in shown += 1 } }
        pick(SourceLayers.ViewEntry(id: "r1", kind: "performance", group: nil, working: false, hidden: false, op: "recording"))
        XCTAssertEqual(workspace.sources.performance, "r1")
        pick(SourceLayers.ViewEntry(id: "n1", kind: "notes", group: nil, working: true, hidden: false, op: "import"))
        XCTAssertNil(workspace.sources.performance)
        XCTAssertFalse(workspace.panels[.versions])
        XCTAssertEqual(shown, 0, "the working notes are this screen")
    }

    /// PDF in the bar: there with a way to the piece's other view and a PDF to go to, not without either.
    func testThePdfIsOfferedOnlyWithAWayToIt() {
        var piece = OpenPiece(id: 7, title: "Nocturne", pages: [], timemap: "")
        XCTAssertNil(ScoreWorkspace(piece: piece).toPdf { _ in }, "no PDF to go to")
        piece.other = OtherView(file: projectFile(), pages: PageMap(starts: [0]))
        XCTAssertNotNil(ScoreWorkspace(piece: piece).toPdf { _ in })
        XCTAssertNil(ScoreWorkspace(piece: piece).toPdf(nil), "no way to the other view, and no PDF linked in Files")
    }
}
