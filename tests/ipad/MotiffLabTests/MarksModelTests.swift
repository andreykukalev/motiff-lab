import XCTest

@testable import MotiffLab

/// MarksModel (S6, ADR 0031 section 3): the layers hidden are the piece's own choice, else the device's; extra information
/// shows a kind only while it is on; the kinds a piece has; the highlight over the selection; a bookmark gone to stays the
/// one shown only while the selection is what it gave; a note starts over the selection. What ScoreController did before
/// the move, rule for rule.
@MainActor
final class MarksModelTests: XCTestCase {
    private let bars = ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 2)

    /// A model over a selection, with the device's settings `device` (a piece's own choices over them).
    private func made(device: [String: String] = [:]) -> (MarksModel, SelectionModel) {
        let selection = SelectionModel()
        let model = MarksModel(selection: selection) { overrides in device.merging(overrides) { _, own in own } }
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
        XCTAssertEqual(Set(model.clearOverrides()), ["layers.ink", "layers.notes"])
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

    func testALevelShownIsKeptAsThePiecesOwn() {
        let (model, _) = made()
        model.keepLevel("sentences")
        XCTAssertEqual(model.pieceOverrides[MarksModel.granularity], "sentences")
    }

    func testTheHighlightOverTheSelectionIsTheNewestLaidExactlyOverIt() {
        let (model, selection) = made()
        let span: [String: Any] = ["onset": 0, "end": 1920]
        let highlights: [[String: Any]] = [
            ["id": "h1", "span": span, "colour": "amber"],
            ["id": "h2", "span": ["onset": 0, "end": 960], "colour": "forest"],
            ["id": "h3", "span": span, "colour": "carmine"],
        ]
        let shelf = MarksShelf(object: ["highlights": highlights])
        model.show(shelf, inkCount: 4)
        XCTAssertEqual(model.markCount, 4)
        XCTAssertNil(model.selectedHighlight, "no selection, no highlight over it")
        selection.answered(bars)
        XCTAssertEqual(model.selectedHighlight?.id, "h3")
    }

    func testABookmarkGoneToIsShownWhileTheSelectionIsWhatItGave() throws {
        let (model, selection) = made()
        let bookmark: [String: Any] = [
            "id": "b1", "title": "Theme", "category": "practise", "span": ["onset": 960, "end": 960], "bars": ["first": 1, "last": 1],
        ]
        let categories: [[String: Any]] = [["id": "practise", "name": "Practise", "ink": "forest"]]
        let shelf = try XCTUnwrap(BookmarkShelf(answer: facadeRequest(["categories": categories, "bookmarks": [bookmark]])))
        model.keepBookmarks(shelf)
        XCTAssertEqual(model.span(ofBookmark: "b1"), 960..<961, "a bookmark of no length still has a place")
        XCTAssertNil(model.span(ofBookmark: "b2"))
        XCTAssertEqual(model.bookmarkItems.first?["ink"] as? String, "forest")
        selection.answered(bars)
        model.wentTo(bookmark: "b1")
        XCTAssertTrue(model.isShown("b1"))
        XCTAssertFalse(model.isShown("b2"))
        selection.answered(ScoreSelection(onset: 0, end: 3840, firstBar: 1, lastBar: 2))
        XCTAssertFalse(model.isShown("b1"), "another selection: the bookmark is no longer the one shown")
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
