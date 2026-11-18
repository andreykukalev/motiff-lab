import XCTest

@testable import MotiffLab

/// SourcesModel (S4, ADR 0031 section 3) on the kit's piece open in music-core: a version added is the one shown and
/// says how it lines up, the list and a version are read through music-core, a version that is not there leaves its
/// words, and another piece starts with none of the last one's versions, words, page or engraved view.
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
        model.showPage(model.versions.shownId)
        model.keepEngraved(SourcesModel.Engraved(view: "result", even: false, added: ["n_0001"], own: []))
        model.open(evenBars: true)
        XCTAssertTrue(model.versions.entries.isEmpty)
        XCTAssertNil(model.status)
        XCTAssertNil(model.page)
        XCTAssertEqual(model.engraved, SourcesModel.Engraved(view: nil, even: true), "the player's notes, as the device engraves them now")
    }
}
