import UIKit
import WebKit
import XCTest

@testable import MotiffLab

/// The piano roll as a place to edit notes (#479 item 23, the approved mockup v0.5.2): its own two modes and who edits in
/// them, the zoom's values in words, and the one selection a pick on the roll makes.
final class RollTests: XCTestCase {
    /// The drawer's handle (#511 section 7.1): taller up, shorter down, within a fifth and most of the screen, away when
    /// dragged nearly off, and kept on the device for each orientation.
    func testTheDrawersHandleSetsItsHeightWithinItsLimitsAndKeepsIt() throws {
        XCTAssertEqual(RollDrawerHeight.share(from: 0.4, dragged: -100, height: 1000) ?? 0, 0.5, accuracy: 1e-9, "up is taller")
        XCTAssertEqual(RollDrawerHeight.share(from: 0.4, dragged: 100, height: 1000) ?? 0, 0.3, accuracy: 1e-9, "down is shorter")
        XCTAssertEqual(RollDrawerHeight.share(from: 0.4, dragged: -900, height: 1000), RollDrawerHeight.maxShare, "most of the screen, never all")
        XCTAssertLessThan(RollDrawerHeight.maxShare, 1)
        XCTAssertGreaterThanOrEqual(RollDrawerHeight.maxShare, 0.5, "at least half the screen")
        XCTAssertEqual(RollDrawerHeight.share(from: 0.4, dragged: 250, height: 1000), RollDrawerHeight.minShare)
        XCTAssertNil(RollDrawerHeight.share(from: 0.4, dragged: 1000 * (0.4 - RollDrawerHeight.closeShare) + 1, height: 1000), "dragged nearly away it closes")
        XCTAssertEqual(RollDrawerHeight.share(from: 0.4, dragged: 0, height: 0), 0.4, "no height divides by nothing")

        let suite = "RollTests.drawer"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defaults.removePersistentDomain(forName: suite)
        defer { defaults.removePersistentDomain(forName: suite) }
        XCTAssertEqual(RollDrawerHeight.saved(landscape: true, in: defaults), 0.4, "40% in landscape the first time")
        XCTAssertEqual(RollDrawerHeight.saved(landscape: false, in: defaults), 0.3, "30% in portrait")
        RollDrawerHeight.keep(0.7, landscape: true, in: defaults)
        XCTAssertEqual(RollDrawerHeight.saved(landscape: true, in: defaults), 0.7, accuracy: 1e-9)
        XCTAssertEqual(RollDrawerHeight.saved(landscape: false, in: defaults), 0.3, "each orientation keeps its own")
        RollDrawerHeight.keep(2, landscape: false, in: defaults)
        XCTAssertEqual(RollDrawerHeight.saved(landscape: false, in: defaults), RollDrawerHeight.maxShare)
    }

    func testEditNotesLeavesTheBarForTheRoll() {
        XCTAssertEqual(InteractionMode.inBar, [.navigate, .select, .pen], "the bar has no Edit notes on the iPad")
        XCTAssertEqual(RollMode.allCases, [.navigate, .edit])
        XCTAssertEqual(RollMode.allCases.map(\.icon), ["scroll", "editnote"])
        XCTAssertEqual(RollMode.edit.label, "rollMode.edit")
        XCTAssertEqual(UIText(language: "ru")[RollMode.navigate.label], "Навигация")
        XCTAssertEqual(UIText(language: "ru")[RollMode.edit.label], "Правка нот")
        XCTAssertEqual(UIText(language: "en")[RollMode.edit.label], "Edit notes")
    }

    func testChoosingAModeOpensTheEditorAndNavigateKeepsADraftWithChanges() {
        XCTAssertEqual(RollMode.choose(.edit, editorOpen: false, changed: false).openEditor, true)
        XCTAssertEqual(RollMode.choose(.edit, editorOpen: true, changed: true).openEditor, false, "an open editor is not opened again")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: true, changed: false).closeEditor, true, "nothing changed: the editor closes")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: true, changed: true).closeEditor, false, "changes wait for Cancel or Save")
        XCTAssertEqual(RollMode.choose(.navigate, editorOpen: false, changed: false).closeEditor, false)
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: true), .edit)
        XCTAssertEqual(RollMode.shown(chosen: .edit, editorOpen: false), .navigate, "Edit notes holds only while the editor is open")
        XCTAssertEqual(RollMode.shown(chosen: .navigate, editorOpen: true), .navigate)
    }

    func testInNavigateEveryPointerMovesTheRollWithOneFinger() {
        for stylus in [true, false] {
            let rules = RollInput(stylus: stylus, mode: .navigate)
            XCTAssertFalse(rules.edits(.stylus))
            XCTAssertFalse(rules.edits(.finger))
            XCTAssertEqual(rules.scrollFingers, 1)
        }
    }

    func testInEditNotesTheStylusEditsAndAFingerMovesTheRoll() {
        let rules = RollInput(stylus: true, mode: .edit)
        XCTAssertTrue(rules.edits(.stylus))
        XCTAssertFalse(rules.edits(.finger), "a finger moves the roll while the iPad is used with a stylus")
        XCTAssertEqual(rules.scrollFingers, 1)
        XCTAssertEqual(rules.page["pen"] as? Bool, true)
        XCTAssertEqual(rules.page["touch"] as? Bool, false)
        XCTAssertEqual(rules.page["scrollFingers"] as? Int, 1)
    }

    func testWithoutAStylusOneFingerEditsAndTwoMoveTheRoll() {
        let rules = RollInput(stylus: false, mode: .edit)
        XCTAssertTrue(rules.edits(.finger))
        XCTAssertTrue(rules.edits(.stylus), "without a stylus the Pencil goes as a finger does")
        XCTAssertEqual(rules.scrollFingers, 2)
        XCTAssertEqual(rules.page["touch"] as? Bool, true)
        XCTAssertEqual(rules.page["scrollFingers"] as? Int, 2)
    }

    func testOnTheRollOnlyTheBarsSelectSelectsUnderTheModes() {
        // #543 2–4: the bar's tool decides on the roll; the roll's own Navigate and Edit notes are about editing notes.
        for mode in RollMode.allCases {
            let select = RollInput(stylus: true, mode: mode, tool: .select)
            XCTAssertTrue(select.selects(.stylus))
            XCTAssertTrue(select.selects(.finger))
            XCTAssertEqual(select.page["mode"] as? String, "select")
            let navigate = RollInput(stylus: true, mode: mode, tool: .navigate)
            XCTAssertFalse(navigate.selects(.stylus), "Navigate protects the selection")
            XCTAssertFalse(navigate.selects(.finger))
            XCTAssertEqual(navigate.page["mode"] as? String, "navigate")
        }
        let pen = RollInput(stylus: true, mode: .navigate, penOn: true, tool: .pen)
        XCTAssertFalse(pen.selects(.finger), "under the Pen a finger goes as in Navigate")
        XCTAssertFalse(pen.selects(.stylus))
        XCTAssertEqual(pen.page["mode"] as? String, "pen")
        XCTAssertEqual(RollInput(stylus: false, mode: .navigate, tool: .select).page["mode"] as? String, "select")
    }

    func testTheRollReadsTheBarsTool() {
        let opened = RollInput(stylus: true, mode: .navigate)
        XCTAssertFalse(opened.selects(.finger), "until the bar's tool comes, Navigate: nothing selects")
        XCTAssertEqual(opened.page["mode"] as? String, "navigate")
        XCTAssertEqual(RollInput.barTool(mode: .select, penOn: false), InputRules.Tool.select)
        XCTAssertEqual(RollInput.barTool(mode: .select, penOn: true), InputRules.Tool.pen)
        XCTAssertEqual(RollInput.barTool(mode: .navigate, penOn: false), InputRules.Tool.navigate)
        XCTAssertEqual(RollInput(stylus: true, mode: .navigate, tool: .edit).page["mode"] as? String, "navigate", "the iPad's bar has no Edit notes")
    }

    func testTheZoomSaysBarsAndOctavesInWords() {
        let ru = UIText(language: "ru")
        XCTAssertEqual(RollZoom.barsText(9, ru), "9 тактов")
        XCTAssertEqual(RollZoom.barsText(1, ru), "1 такт")
        XCTAssertEqual(RollZoom.barsText(3, ru), "3 такта")
        XCTAssertEqual(RollZoom.barsText(21, ru), "21 такт")
        XCTAssertEqual(RollZoom.octavesText(3, ru), "3 октавы")
        XCTAssertEqual(RollZoom.octavesText(5, ru), "5 октав")
        XCTAssertEqual(RollZoom.octaveWord(3, ru), "октавы")
        XCTAssertEqual(RollZoom.octaveWord(1, ru), "октава")
        let en = UIText(language: "en")
        XCTAssertEqual(RollZoom.barsText(9, en), "9 bars")
        XCTAssertEqual(RollZoom.barsText(1, en), "1 bar")
        XCTAssertEqual(RollZoom.octavesText(2, en), "2 octaves")
        XCTAssertEqual(RollZoom.octaveWord(1, en), "octave")
    }

    func testADisabledTimeStepSaysWhy() {
        let ru = UIText(language: "ru")
        let en = UIText(language: "en")
        XCTAssertNil(RollZoom.timeStepReason(RollZoom(bars: 4, octaves: 3), inward: true, en), "a step that is on says nothing more")
        XCTAssertNil(RollZoom.timeStepReason(RollZoom(bars: 4, octaves: 3), inward: false, en))
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 1, octaves: 3, timeIn: false), inward: true, en), "One bar already fills the roll")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 1, octaves: 3, timeIn: false), inward: true, ru), "Один такт уже на всю ширину ролла")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 3, octaves: 3, timeIn: false), inward: true, en), "The roll is at its greatest zoom")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 3, octaves: 3, timeIn: false), inward: true, ru), "Масштаб уже наибольший")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 0, octaves: 3, timeIn: false), inward: true, en), "The roll is at its greatest zoom", "no bar in view is not one bar")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 12, octaves: 3, timeOut: false), inward: false, en), "The roll is at its least zoom")
        XCTAssertEqual(RollZoom.timeStepReason(RollZoom(bars: 12, octaves: 3, timeOut: false), inward: false, ru), "Масштаб уже наименьший")
        XCTAssertNil(RollZoom.timeStepReason(RollZoom(bars: 0, octaves: 3, timeOut: false), inward: false, en), "with no bar in view no reason is true")
    }

    func testTheZoomIsReadFromTheRollsReport() throws {
        let read = try XCTUnwrap(RollZoom(["bars": 9, "octaves": 3, "timeIn": true, "timeOut": false, "pitchIn": true, "pitchOut": true]))
        XCTAssertEqual(read, RollZoom(bars: 9, octaves: 3, timeOut: false))
        XCTAssertNil(RollZoom(["bars": 9]), "a report without the octaves is not read")
        XCTAssertFalse(read.zoomOut, "Zoom Out hidden unless the roll says it shows")
        let fitted = try XCTUnwrap(RollZoom(["bars": 4, "octaves": 2, "zoomOut": true]))
        XCTAssertEqual(fitted, RollZoom(bars: 4, octaves: 2, zoomOut: true), "after Fit of a selection (#521 2.2)")
    }

    func testADragOnTheRollSelectsByBeatsWhereTheScoreDoesAndATapSelectsItsBar() throws {
        let drag = try XCTUnwrap(RollRange(["first": 9, "last": 12, "extend": false, "click": false, "from": 6720, "to": 5760]))
        XCTAssertEqual(RollPick(drag, byBeats: true), .beats(5760..<6720), "the ticks either way round, for the one selection by beats")
        XCTAssertEqual(RollPick(drag, byBeats: false), .bars(first: 9, last: 12, extend: false), "by bars where the score selects bars")
        let tap = try XCTUnwrap(RollRange(["first": 9, "last": 9, "extend": false, "click": true, "from": 5760, "to": 5760]))
        XCTAssertEqual(RollPick(tap, byBeats: true), .bars(first: 9, last: 9, extend: false), "a tap on a bar number selects that bar")
        let still = try XCTUnwrap(RollRange(["first": 9, "last": 9, "click": false, "from": 5760, "to": 5760]))
        XCTAssertEqual(RollPick(still, byBeats: true), .beats(5760..<5761), "never an empty stretch")
        XCTAssertNil(RollRange(["from": 0, "to": 10]), "no bars, no pick")
    }

    /// #495 BUG-01: the roll's one web view is in one place, the drawer while it is open, else the card in focus as a roll.
    func testTheRollIsInOnePlaceTheDrawerFirst() {
        XCTAssertEqual(RollPlace.shown(drawerOpen: true, focused: false, focusRoll: false), .drawer)
        XCTAssertEqual(RollPlace.shown(drawerOpen: true, focused: true, focusRoll: true), .drawer, "the card shows the roll once the drawer is gone")
        XCTAssertEqual(RollPlace.shown(drawerOpen: false, focused: true, focusRoll: true), .focus)
        XCTAssertNil(RollPlace.shown(drawerOpen: false, focused: true, focusRoll: false), "the phrase in focus as notes")
        XCTAssertNil(RollPlace.shown(drawerOpen: false, focused: false, focusRoll: true), "Expand before focus has come")
    }

    /// #495 BUG-01: of the holders, only the newest of the place shown has the web view, so the drawer sliding away while the
    /// card comes, updated thirty times a second while the music plays, never takes it back.
    func testOnlyTheNewestHolderOfThePlaceShownHasTheWebView() {
        let expanding: [(place: RollPlace, made: Int)] = [(.drawer, 1), (.focus, 2)]
        XCTAssertEqual(RollPlace.holder(for: .focus, among: expanding), 2, "Expand: the card's holder, not the drawer's going")
        XCTAssertEqual(RollPlace.holder(for: .drawer, among: expanding), 1, "before focus has come, the drawer keeps it")
        let reopened: [(place: RollPlace, made: Int)] = [(.drawer, 3), (.focus, 2), (.drawer, 4)]
        XCTAssertEqual(RollPlace.holder(for: .drawer, among: reopened), 4, "the drawer opened again while the last one still slides away")
        XCTAssertNil(RollPlace.holder(for: .focus, among: [(.drawer, 1)]), "the card's holder is not made yet: it takes the web view when it is")
        XCTAssertNil(RollPlace.holder(for: nil, among: expanding), "the roll on no place: nobody takes the web view")
    }
}

/// The roll's load (v1.4 r-1b): one that fails says so in the roll's place, and the next one that shows notes takes the
/// failure away, so the drawer never says the roll could not be shown over notes it shows.
@MainActor
final class RollLoadTests: XCTestCase {
    private var window: UIWindow?

    override func tearDown() async throws {
        window?.isHidden = true
        window = nil
        try await super.tearDown()
    }

    func testAFailedLoadSaysSoAndALoadThatShowsNotesTakesItAway() async throws {
        let roll = RollController()
        let shown = UIWindow(frame: CGRect(x: 0, y: 0, width: 1024, height: 400))
        roll.webView.frame = shown.bounds
        shown.addSubview(roll.webView)
        shown.isHidden = false
        window = shown
        // The page tells the controller it is ready as it defines `window.roll`.
        for _ in 0..<300 {
            let found = try? await roll.webView.callAsyncJavaScript("return typeof window.roll", arguments: [:], contentWorld: .page)
            if found as? String == "object" { break }
            try? await Task.sleep(for: .milliseconds(100))
        }
        try? await Task.sleep(for: .milliseconds(200))

        // A piece music-core does not hold: no roll.
        roll.show(OpenPiece(id: 9_000_777, title: "None", pages: [], timemap: ""), dark: false)
        await until { roll.problem != nil }
        let failedFirst = roll.failed
        XCTAssertTrue(failedFirst, "the roll says it could not be shown")

        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        roll.show(piece, dark: false)
        await until { roll.problem == nil }
        let failedAfter = roll.failed
        XCTAssertNil(roll.problem, "a load that showed the notes leaves no failure behind")
        XCTAssertFalse(failedAfter)
    }
}
