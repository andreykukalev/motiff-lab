import CoreGraphics
import SwiftUI
import XCTest

@testable import MotiffLab

/// SourcesModel (S4, ADR 0031 section 3) on the kit's piece open in music-core: a version added is the one shown and
/// says how it lines up, the list and a version are read through music-core, a version that is not there leaves its
/// words, and another piece starts with none of the last one's versions, words, marker or sources, engraved as the device
/// engraves now; the piece's sources are kept as read. A version is renamed from its row in Sources and stays the one shown,
/// and removing it opens the piece again so the score loses its markers and the bars taken from it at once.
@MainActor
final class SourcesModelTests: XCTestCase {
    private let text = UIText(language: "en")

    /// The kit's piece open in music-core, closed when the test ends, with its own MusicXML added as another version.
    private func withVersion(_ model: SourcesModel) throws -> Int32 {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: nil)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0)
        addTeardownBlock { MusicCore.close(piece: piece) }
        let request = facadeRequest(["name": "Official", "file": example.data.base64EncodedString(), "fileName": example.fileName])
        XCTAssertTrue(model.read(performance: MusicCore.call(piece: piece, "performance.add", request)))
        return piece
    }

    func testAVersionAddedIsShownAndSaysHowItLinesUp() throws {
        let model = SourcesModel(evenBars: false)
        _ = try withVersion(model)
        XCTAssertEqual(model.versions.shown?.name, "Official")
        XCTAssertGreaterThan(model.versions.matched, 0)
        XCTAssertEqual(
            model.addedStatus(in: text),
            text.format("performance.added", String(model.versions.matched), String(model.versions.printed), "0"),
            "the piece's own notes: every note found, no bar differs"
        )
        XCTAssertFalse(model.read(performance: "{\"error\": \"badRequest\", \"message\": \"no\"}"), "a refusal is not a version")
        XCTAssertEqual(model.versions.shown?.name, "Official", "and leaves the one shown")
    }

    func testTheListAndAVersionAreReadThroughMusicCore() async throws {
        let model = SourcesModel(evenBars: false)
        let piece = try withVersion(model)
        let id = try XCTUnwrap(model.versions.shownId)
        await model.readList(of: piece)
        XCTAssertEqual(model.versions.entries.map(\.id), [id])
        XCTAssertEqual(model.versions.shownId, id, "the version shown stays shown while it is there")

        await model.readVersion("missing", in: piece)
        XCTAssertNotNil(model.status, "why the version could not be read")
        XCTAssertEqual(model.versions.shownId, id)
        model.say(nil)
        await model.readVersion(id, in: piece)
        XCTAssertNil(model.status)
        XCTAssertFalse(model.versions.bars.isEmpty, "compared bar by bar")
    }

    func testAnotherPieceStartsWithNoneOfTheLastOnes() throws {
        let model = SourcesModel(evenBars: false)
        _ = try withVersion(model)
        model.say("Bar 12 is now as in Official")
        model.pickMarker(SourcesModel.MarkerPick(bar: 5, left: 10, bottom: 20))
        model.keepEngraved(SourcesModel.Engraved(even: false))
        model.open(evenBars: true)
        XCTAssertTrue(model.versions.entries.isEmpty)
        XCTAssertNil(model.status)
        XCTAssertNil(model.markerPick, "no marker of the last piece's")
        XCTAssertEqual(model.engraved, SourcesModel.Engraved(even: true), "as the device engraves now")
    }

    /// A version's marker tapped on the score is picked with its place; «Источники» on its popover puts the popover away
    /// and asks the screen for Sources by the marker's bar (VIEW-06's way from a marker, since «Открыть в черновике» left
    /// with the merge in v1.1); a tap elsewhere only puts it away.
    func testAMarkersPopoverOpensSourcesAndGoes() {
        let score = ScoreController()
        var asked: [Int] = []
        score.versionPicked = { asked.append($0) }
        score.handle(.performanceBar(12, left: 40.5, bottom: 300))
        XCTAssertEqual(score.sourcesModel.markerPick, SourcesModel.MarkerPick(bar: 12, left: 40.5, bottom: 300))
        // r-1: the page scrolled or was laid out again on a rotation: the popover stays under its marker, and a phrase's
        // menu, none shown, does not open.
        score.handle(.markMoved(CGPoint(x: 52, y: 120)))
        XCTAssertEqual(score.sourcesModel.markerPick, SourcesModel.MarkerPick(bar: 12, left: 52, bottom: 120))
        XCTAssertNil(score.phraseMenu)
        score.openSourcesFromMarker()
        XCTAssertNil(score.sourcesModel.markerPick)
        XCTAssertEqual(asked, [12])
        score.openSourcesFromMarker()
        XCTAssertEqual(asked, [12], "no marker picked, nothing asked")
        score.handle(.performanceBar(3, left: 0, bottom: 0))
        score.closeMarker()
        XCTAssertNil(score.sourcesModel.markerPick)
        XCTAssertEqual(asked, [12])
    }

    /// v1.4 a-1, answer 14: the page tells how high the mark is with its place (a tap, a move), for a card opened above it;
    /// once the mark has left the visible part of the page (`markAway`: playback turned the page), its card closes, and
    /// with it the cover that would catch the next tap.
    func testACardAtAMarkKnowsItsHeightAndClosesOnceItLeavesSight() {
        let score = ScoreController()
        score.handle(.performanceBar(12, left: 40.5, bottom: 300, top: 276))
        XCTAssertEqual(score.markHeight, 24)
        score.handle(.markMoved(CGPoint(x: 40.5, y: 200), top: 152))
        XCTAssertEqual(score.markHeight, 48, "zoomed in: as the page lays it out now")
        XCTAssertNotNil(score.sourcesModel.markerPick)
        score.handle(.markAway)
        XCTAssertNil(score.sourcesModel.markerPick, "out of sight: the popover goes")
        score.phraseMenu = PhraseMarkPick(number: 1, at: CGPoint(x: 10, y: 20))
        score.handle(.markAway)
        XCTAssertNil(score.phraseMenu, "and a phrase's menu")
    }

    /// v1.4 a-1, answer 14: a card at a mark opens under it where it fits there, else above it, else as low as it stays on
    /// the screen.
    func testACardAtAMarkOpensAboveItWhereItDoesNotFitUnderIt() {
        typealias Card = DraftCardAtMark<EmptyView>
        let gap = CGFloat.ds("s-1")
        let margin = CGFloat.ds("s-3")
        XCTAssertEqual(Card.top(at: CGPoint(x: 0, y: 100), mark: 24, card: 200, area: 800), 100 + gap, "room under the mark")
        XCTAssertEqual(Card.top(at: CGPoint(x: 0, y: 700), mark: 24, card: 200, area: 800), 700 - 24 - gap - 200, "none under it: above it")
        XCTAssertEqual(Card.top(at: CGPoint(x: 0, y: 300), mark: 24, card: 700, area: 800), 800 - margin - 700, "room neither way: on the screen")
        XCTAssertEqual(Card.top(at: CGPoint(x: 0, y: 300), mark: 24, card: 900, area: 800), margin, "taller than the screen: from its top")
    }

    /// r-1: a card at a mark closes by its own cover, which the page never hears; the next `markMoved` finds no card and
    /// tells the page to stop following the mark (`markForgotten`), so a scroll (playback turning the line) posts nothing.
    func testAMarkMovedWithNoCardOpenTellsThePageToForgetTheMark() async {
        let score = ScoreController()
        let page = RecordingScorePage()
        var sent: [PageCommand] = []
        let forgotten = expectation(description: "the page told to forget the mark")
        page.called = { command, _ in
            sent.append(command)
            if command == .markForgotten { forgotten.fulfill() }
        }
        score.scorePage = page
        score.handle(.performanceBar(12, left: 40.5, bottom: 300))
        score.handle(.markMoved(CGPoint(x: 40.5, y: 200)))
        score.closeMarker()
        score.handle(.markMoved(CGPoint(x: 40.5, y: 100)))
        await fulfillment(of: [forgotten], timeout: 5)
        XCTAssertEqual(sent.filter { $0 == .markForgotten }.count, 1, "only once no card shows; the popover moved before")
        XCTAssertNil(score.sourcesModel.markerPick, "and none opens")
    }

    /// «Переименовать…» on a version's row: music-core keeps the new name, the list shows it and the version shown stays the
    /// one shown; a blank name sends nothing and a refusal says why.
    func testAVersionIsRenamedAndStaysShown() async throws {
        let model = SourcesModel(evenBars: false)
        let piece = try withVersion(model)
        let id = try XCTUnwrap(model.versions.shownId)
        await model.rename(id, to: "  Cover  ", in: piece)
        XCTAssertEqual(model.versions.entries.map(\.name), ["Cover"])
        XCTAssertEqual(model.versions.shownId, id)
        XCTAssertNil(model.status)
        await model.rename(id, to: " ", in: piece)
        XCTAssertEqual(model.versions.shown?.name, "Cover", "a blank name is not a name")
        await model.rename("missing", to: "Other", in: piece)
        XCTAssertNotNil(model.status, "why the version could not be renamed")
        let layers = try XCTUnwrap(SourceLayers(answer: MusicCore.call(piece: piece, "sources.layers")))
        XCTAssertEqual(layers.notes.first { $0.id == id }?.name, "Cover", "the row in Sources takes the new name")
    }

    /// A version removed with «Удалить ноты…» opens the piece again in its place from the file just saved, as «Сделать
    /// рабочими» does: the score drops its markers and the bars taken from it at once, which music-core's open notes still
    /// hold, and the toast with its Undo is on the next screen.
    func testRemovingAVersionOpensThePieceAgain() async throws {
        let piece = try withVersion(SourcesModel(evenBars: false))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).motiff.json")
        addTeardownBlock { try? FileManager.default.removeItem(at: file) }
        let sources = SourcesController()
        await sources.load(piece: piece, file: file)
        var reopened = 0
        sources.reopen = { reopened += 1 }
        let version = try XCTUnwrap(sources.layers.notes.first { $0.role == "version" })
        await sources.remove(version)
        XCTAssertEqual(reopened, 1, "the piece opens again without the version")
        XCTAssertFalse(sources.layers.notes.contains { $0.id == version.id })
        XCTAssertNil(sources.toast, "the toast goes to the next screen")
        guard case .restore(_)? = SourcesHandover.toast(for: file)?.undo else { return XCTFail("its Undo writes the file back") }
    }

    /// The view's note («Как напечатано», v1.4, concept section 2): the bars the app wrote itself, counted in each
    /// language's plural forms, only when there are some; none for notes with no record. Another piece starts with its own.
    func testTheViewsNoteCountsTheBarsTheAppWrote() {
        let russian = UIText(language: "ru")
        XCTAssertNil(SourcesModel.Engraved(even: false, appWrittenBars: nil).note(text), "no record, no note")
        XCTAssertNil(SourcesModel.Engraved(even: false, appWrittenBars: 0).note(text), "every bar as the source writes it")
        XCTAssertEqual(SourcesModel.Engraved(even: false, appWrittenBars: 1).note(text), "As the source writes it, 1 bar written by the app")
        XCTAssertEqual(SourcesModel.Engraved(even: false, appWrittenBars: 4).note(text), "As the source writes it, 4 bars written by the app")
        XCTAssertEqual(SourcesModel.Engraved(even: false, appWrittenBars: 1).note(russian), "По записи источника, 1 такт записан приложением")
        XCTAssertEqual(SourcesModel.Engraved(even: false, appWrittenBars: 4).note(russian), "По записи источника, 4 такта записаны приложением")
        XCTAssertEqual(SourcesModel.Engraved(even: false, appWrittenBars: 5).note(russian), "По записи источника, 5 тактов записано приложением")

        let model = SourcesModel(evenBars: false)
        model.open(evenBars: false, appWrittenBars: 4)
        XCTAssertEqual(model.engraved.note(text), "As the source writes it, 4 bars written by the app", "as the piece opened")
        model.keepEngraved(SourcesModel.Engraved(even: false, appWrittenBars: 0))
        XCTAssertNil(model.engraved.note(text), "engraved again after a correction: none left")
        model.open(evenBars: false)
        XCTAssertNil(model.engraved.appWrittenBars, "another piece has none of the last one's")
    }

    /// The score keeps the bars the app wrote as each piece is shown and as it is engraved again (a tempo set engraves it).
    func testTheScoreKeepsTheBarsTheAppWroteAsItIsEngraved() async throws {
        let score = ScoreController()
        score.scorePage = RecordingScorePage()
        score.show(OpenPiece(id: 9_000_601, title: "Printed", pages: [], timemap: "", appWrittenBars: 3), dark: false)
        XCTAssertEqual(score.sourcesModel.engraved.appWrittenBars, 3, "as the piece opened")
        score.show(OpenPiece(id: 9_000_602, title: "Heard", pages: [], timemap: ""), dark: false)
        XCTAssertNil(score.sourcesModel.engraved.appWrittenBars, "another piece, with no record, has none")

        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        let id = piece.id
        addTeardownBlock { _ = MusicCore.close(piece: id) }
        let opened = try XCTUnwrap(piece.appWrittenBars, "a MusicXML file is the notes' record")
        score.show(piece, dark: false)
        score.sourcesModel.keepEngraved(SourcesModel.Engraved(even: false, appWrittenBars: nil))
        await score.setPieceTempo(90)
        XCTAssertEqual(score.sourcesModel.engraved.appWrittenBars, opened, "engraved again: the new engraving's count")
    }

    func testThePiecesSourcesAreKeptAsReadUntilAnotherPiece() {
        let model = SourcesModel(evenBars: false)
        model.read(sources: #"{"base": "scan", "baseName": "Elise.pdf", "summary": "Read from a scan", "document": {"fileName": "elise.pdf", "pages": 2, "starts": [0, 1920]}}"#)
        XCTAssertEqual(model.info.base, "scan")
        XCTAssertEqual(model.info.document, SourcesInfo.KeptDocument(fileName: "elise.pdf", pages: 2, starts: [0, 1920]))
        model.read(sources: #"{"error": "notFound", "message": "no piece"}"#)
        XCTAssertEqual(model.info.base, "scan", "a refusal leaves the sources as they were")
        model.open(evenBars: false)
        XCTAssertEqual(model.info, SourcesInfo(), "another piece starts with none of the last one's sources")
    }
}
