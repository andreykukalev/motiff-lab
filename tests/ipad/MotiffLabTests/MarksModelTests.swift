import XCTest

@testable import MotiffLab

/// MarksModel (S6, ADR 0031 section 3): the layers hidden are the piece's own choice, else the device's; the phrases'
/// marks hidden are the device's; extra information shows a kind only while it is on; the kinds a piece has; the highlight over the selection; a bookmark gone to stays the
/// one shown only while the selection is what it gave; a note starts over the selection. Since v1.6 i-10b against a fake
/// score (`MarksScore`): the marks music-core answered are drawn, a bookmark gone to again lifts into focus but with the
/// Pen on, and the page hides Check unless its drawer or extra information shows it. What ScoreController did before the
/// move, rule for rule.
@MainActor
final class MarksModelTests: XCTestCase {
    /// The score as the marks see it: what was sent to its page, and what was asked of it.
    @MainActor
    private final class FakeScore: MarksScore {
        @MainActor
        final class Page: ScorePage {
            private(set) var sent: [(command: PageCommand, argument: Any?)] = []
            var commands: [PageCommand] { sent.map { $0.command } }

            func run(_ command: PageCommand, _ argument: Any?) async throws -> Any? {
                sent.append((command, argument))
                return nil
            }

            func post(_ command: PageCommand, _ argument: Any) {
                sent.append((command, argument))
            }
        }

        let page = Page()
        var scorePage: ScorePage { page }
        /// No piece music-core has open: what the marks keep with the piece is refused there and changes nothing here.
        var pieceId: Int32? = 0
        var shown = true
        var penOn = false
        var checkDrawerOpen = false
        var slate = false
        /// The selection a passage selected gives.
        weak var selection: SelectionModel?
        private(set) var lifted = 0
        private(set) var versionsDrawn = 0

        func selectPassage(from start: Double, to end: Double) async {
            selection?.answered(ScoreSelection(onset: start, end: end, firstBar: 1, lastBar: 1))
        }

        func reveal(_ tick: Double) async {}

        func enterFocus() {
            lifted += 1
        }

        func drawVersions() async {
            versionsDrawn += 1
        }

        func showProblem(_ message: String?) {}
    }

    private let bars = ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 2)
    private var score: FakeScore!

    override func setUp() async throws {
        score = FakeScore()
    }

    /// A model over a selection and the fake score, with the device's settings `device` (a piece's own choices over them).
    private func made(device: [String: String] = [:]) -> (MarksModel, SelectionModel) {
        let selection = SelectionModel()
        let model = MarksModel(selection: selection) { overrides in device.merging(overrides) { _, own in own } }
        model.score = score
        score.selection = selection
        return (model, selection)
    }

    func testTheLayersHiddenAreThePiecesOwnChoiceElseTheDevices() {
        let (model, _) = made(device: ["layers.ink": "off", "layers.notes": "on"])
        model.deviceSettingsChanged()
        XCTAssertEqual(model.hiddenLayers, ["ink"], "the device's")
        model.showLayers(["ink"], true)
        model.showLayers(["notes"], false)
        XCTAssertEqual(model.hiddenLayers, ["notes"], "the piece's own over the device's")
        XCTAssertEqual(model.pieceOverrides, ["layers.ink": "on", "layers.notes": "off"])
        model.clearOverrides()
        XCTAssertEqual(model.pieceOverrides, [:])
        model.deviceSettingsChanged()
        XCTAssertEqual(model.hiddenLayers, ["ink"], "the device's again")
    }

    func testAKindOfExtraInformationShowsOnlyWhileExtraInformationIsOn() {
        let (model, _) = made(device: ["layers.extra": "on", "layers.check": "on"])
        model.deviceSettingsChanged()
        XCTAssertTrue(model.extraShown("check"))
        model.showLayers(["extra"], false)
        XCTAssertFalse(model.extraShown("check"), "extra information off hides every kind")
        model.showLayers(["extra"], true)
        model.showLayers(["check"], false)
        XCTAssertFalse(model.extraShown("check"))
        XCTAssertTrue(model.extraShown("taken"))
    }

    func testThePieceHasTheKindsOfExtraInformationItsVersionsAndCheckGive() {
        let (model, _) = made()
        XCTAssertEqual(model.pieceExtra(hasVersions: true, hasFlags: true), ["performance", "taken", "check"])
        XCTAssertEqual(model.pieceExtra(hasVersions: true, hasFlags: false), ["performance", "taken"])
        XCTAssertEqual(model.pieceExtra(hasVersions: false, hasFlags: true), ["check"])
        XCTAssertEqual(model.pieceLayers, MarksModel.layers)
        XCTAssertEqual(model.layerName("check"), UIText.current["extra.check"])
        XCTAssertEqual(model.layerName("ink"), UIText.current["layers.ink"])
    }

    /// «Фразы» in the list of extra information (the phrases spec, v1.2): the marks show by default, and hidden they stay
    /// hidden on this device for every piece; the old key of «Не показывать» is not read.
    func testHiddenPhraseMarksAreKeptOnTheDevice() {
        let before = UserDefaults.standard.object(forKey: MarksModel.phraseMarksHiddenKey)
        defer { UserDefaults.standard.set(before, forKey: MarksModel.phraseMarksHiddenKey) }
        UserDefaults.standard.removeObject(forKey: MarksModel.phraseMarksHiddenKey)
        UserDefaults.standard.set(true, forKey: "motiff.phrases.hidden")
        defer { UserDefaults.standard.removeObject(forKey: "motiff.phrases.hidden") }
        XCTAssertFalse(made().0.phraseMarksHidden, "shown by default, whatever the Phrases menu kept")
        made().0.showPhraseMarks(false)
        XCTAssertTrue(made().0.phraseMarksHidden, "the next screen finds them hidden")
        made().0.showPhraseMarks(true)
        XCTAssertFalse(made().0.phraseMarksHidden)
        XCTAssertEqual(made().0.hiddenLayers, [], "no layer of the piece changes")
    }

    func testTheMarksShownAreWhatMusicCoreAnswered() async {
        let (model, _) = made()
        let highlights: [[String: Any]] = [
            ["id": "h1", "span": ["onset": 0, "end": 1920], "colour": "amber"],
            ["id": "h2", "span": ["onset": 0, "end": 960], "colour": "forest"],
        ]
        let ink: [[String: Any]] = Array(repeating: ["id": "s"], count: 4)
        await model.draw(facadeRequest(["highlights": highlights, "ink": ink]))
        XCTAssertEqual(model.markCount, 4)
        XCTAssertEqual(model.marks.highlights.map(\.id), ["h1", "h2"])
        XCTAssertEqual(model.marks.highlights.map(\.colour), ["amber", "forest"])
        XCTAssertEqual(score.page.commands, [.draw, .notes], "the ink, then the notes' markers and the highlights")
        await model.draw(facadeRequest(["error": "refused"]))
        XCTAssertEqual(model.markCount, 4, "a refusal leaves the marks as they were")
        XCTAssertEqual(score.page.commands, [.draw, .notes], "and draws nothing")
    }

    func testThePageHidesCheckUnlessItsDrawerOrExtraInformationShowsIt() async {
        let (model, _) = made(device: ["layers.extra": "off", "layers.ink": "off"])
        model.deviceSettingsChanged()
        await until { self.score.versionsDrawn == 1 }
        XCTAssertEqual(score.versionsDrawn, 1, "the device's settings followed: the versions drawn again")
        let phrases = model.phraseMarksHidden ? ["phrases"] : []
        let before = score.page.sent.count
        await model.applyLayers()
        score.checkDrawerOpen = true
        await model.applyLayers()
        let hidden = score.page.sent.dropFirst(before).map { ($0.argument as? [String: Any])?["hidden"] as? [String] ?? [] }
        XCTAssertEqual(hidden, [["ink", "check"] + phrases, ["ink"] + phrases], "extra information off hides Check; its drawer open shows it")
        score.shown = false
        await model.applyLayers()
        XCTAssertEqual(score.page.sent.count, before + 2, "nothing goes to pages not drawn")
    }

    func testABookmarkGoneToIsShownWhileTheSelectionIsWhatItGave() async throws {
        let (model, selection) = made()
        let bookmark: [String: Any] = [
            "id": "b1", "title": "Theme", "category": "practise", "span": ["onset": 960, "end": 960], "bars": ["first": 1, "last": 1],
        ]
        let categories: [[String: Any]] = [["id": "practise", "name": "Practise", "ink": "forest"]]
        let shelf = try XCTUnwrap(BookmarkShelf(answer: facadeRequest(["categories": categories, "bookmarks": [bookmark]])))
        model.showBookmarks(shelf)
        XCTAssertEqual(model.span(ofBookmark: "b1"), 960..<961, "a bookmark of no length still has a place")
        XCTAssertNil(model.span(ofBookmark: "b2"))
        XCTAssertEqual(model.bookmarkItems.first?["ink"] as? String, "forest")
        XCTAssertFalse(model.goToBookmark("b1"), "gone to: selected, not lifted")
        await until { model.isShown("b1") }
        XCTAssertTrue(model.isShown("b1"))
        XCTAssertFalse(model.isShown("b2"))
        XCTAssertEqual(selection.selection?.onset, 960)
        score.penOn = true
        XCTAssertFalse(model.goToBookmark("b1"), "with the Pen on it is selected again, not lifted")
        score.penOn = false
        XCTAssertTrue(model.goToBookmark("b1"), "gone to again with the selection it gave: it lifts into focus")
        XCTAssertEqual(score.lifted, 1)
        selection.answered(ScoreSelection(onset: 0, end: 3840, firstBar: 1, lastBar: 2))
        XCTAssertFalse(model.isShown("b1"), "another selection: the bookmark is no longer the one shown")
        XCTAssertTrue(score.page.commands.contains(.bookmarks), "the brackets drawn on the pages shown")
    }

    func testANoteStartsOverTheSelection() {
        let (model, selection) = made()
        model.startNote()
        XCTAssertNil(model.noteDraft, "no selection, no note")
        selection.answered(bars)
        model.startNote()
        XCTAssertEqual(model.noteDraft, MarksModel.NoteDraft(id: nil, onset: 0, end: 1920, firstBar: 1, lastBar: 2, text: ""))
    }
}
