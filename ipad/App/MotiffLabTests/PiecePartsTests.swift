import XCTest
@testable import MotiffLab

/// Pieces in a file (v0.4, experiment «Пьесы в файле») for a PDF or photo collection: the rows «название · с–по» filled
/// from the page bookmarks and checked (in order, not overlapping, within the file), a piece added or moved, the
/// `parts.split` request and its answer, the summary in both languages, what the iPad keeps of a split, and one piece
/// made by the native music-core.
final class PiecePartsTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")

    private func pages(_ rows: PartRows) -> [[Int]] { rows.rows.map { [$0.first, $0.last] } }

    // MARK: - The rows

    func testTheRowsStartFromThePageBookmarksInPageOrder() {
        let bookmarks = [
            PageBookmarkEntry(id: "b3", page: 12, name: " The Earth Prelude "),
            PageBookmarkEntry(id: "b1", page: 3, name: "I Giorni"),
            PageBookmarkEntry(id: "b2", page: 3, name: "twice"),
            PageBookmarkEntry(id: "b4", page: 40, name: "past the end"),
            PageBookmarkEntry(id: "b5", page: 9, name: nil),
        ]
        let rows = PartRows.fromBookmarks(bookmarks, pageCount: 14)
        XCTAssertEqual(pages(rows), [[3, 8], [9, 11], [12, 14]], "each runs to the page before the next, the last to the end")
        XCTAssertEqual(rows.rows.map(\.title), ["I Giorni", "", "The Earth Prelude"])
        XCTAssertNil(rows.problem)
        XCTAssertEqual(PartRows.fromBookmarks([], pageCount: 14).problem, .empty)
    }

    func testRowsMustBeInOrderWithoutOverlapAndWithinTheFile() {
        func problem(_ ranges: [[Int]]) -> PartRowsProblem? {
            PartRows(rows: ranges.map { PartRow(title: "", first: $0[0], last: $0[1]) }, pageCount: 10).problem
        }
        XCTAssertNil(problem([[1, 2], [3, 10]]))
        XCTAssertNil(problem([[3, 4], [7, 7]]), "pages between pieces, a cover or a contents page, belong to none")
        XCTAssertEqual(problem([[1, 4], [4, 6]]), .overlap(piece: 2, previous: 1))
        XCTAssertEqual(problem([[5, 6], [1, 2]]), .overlap(piece: 2, previous: 1), "a piece listed before an earlier one")
        XCTAssertEqual(problem([[1, 2], [3, 11]]), .outside(piece: 2))
        XCTAssertEqual(problem([[0, 2]]), .outside(piece: 1))
        XCTAssertEqual(problem([[4, 3]]), .outside(piece: 1), "the first page after the last")
        XCTAssertEqual(PartRowsProblem.overlap(piece: 2, previous: 1).text(russian, pages: 10),
                       "Пьеса 2 начинается раньше, чем кончается пьеса 1: страницы пьес идут по порядку и не пересекаются.")
        XCTAssertEqual(PartRowsProblem.outside(piece: 3).text(english, pages: 10), "Piece 3: its pages run from 1 to 10, the first not after the last.")
    }

    func testAPieceAddedInsideAnotherSplitsItAndOneAddedAfterRunsToTheEnd() {
        var rows = PartRows(rows: [PartRow(title: "A", first: 3, last: 14)], pageCount: 14)
        XCTAssertTrue(rows.add(at: 9))
        XCTAssertEqual(pages(rows), [[3, 8], [9, 14]])
        XCTAssertFalse(rows.add(at: 9), "a piece already starts there")
        XCTAssertTrue(rows.add(at: 1))
        XCTAssertEqual(pages(rows), [[1, 2], [3, 8], [9, 14]], "one before the others runs to the next one")
        XCTAssertNil(rows.problem)

        var short = PartRows(rows: [PartRow(title: "", first: 1, last: 4)], pageCount: 10)
        XCTAssertEqual(short.suggestedStart(under: 7), 7, "the page under the bar")
        XCTAssertEqual(short.suggestedStart(under: 1), 5, "or the page after the last piece")
        short.add(at: 5)
        XCTAssertEqual(pages(short), [[1, 4], [5, 10]])
        XCTAssertNil(PartRows(rows: [PartRow(title: "", first: 1, last: 10)], pageCount: 10).suggestedStart(under: 1))
        var none = PartRows(pageCount: 0)
        XCTAssertFalse(none.add(at: 1), "a file with no pages takes no piece")
    }

    func testAPiecesPagesMoveWithinTheFileAndAPieceIsRemoved() {
        var rows = PartRows(rows: [PartRow(title: "", first: 2, last: 5), PartRow(title: "", first: 6, last: 8)], pageCount: 8)
        let first = rows.rows[0].id
        rows.step(first, first: true, by: -1)
        rows.step(first, first: true, by: -1)
        rows.step(rows.rows[1].id, first: false, by: 1)
        XCTAssertEqual(pages(rows), [[1, 5], [6, 8]], "never before page 1 or after the last page")
        rows.step(first, first: false, by: 1)
        XCTAssertEqual(rows.problem, .overlap(piece: 2, previous: 1))
        rows.remove(rows.rows[1].id)
        XCTAssertEqual(pages(rows), [[1, 6]])
    }

    // MARK: - The request and the answer

    func testTheRequestSendsOnePieceWithItsOwnPagesFromThisIPad() throws {
        let row = PartRow(title: "  ", first: 6, last: 11)
        let request = PartsSplit.request(
            file: "{}", row: row, number: 3, pdf: Data("%PDF-".utf8), pages: 30,
            playlist: PartsSplit.playlist(" ", collection: "Islands"), collection: "Islands",
            now: Date(timeIntervalSince1970: 0), text: russian
        )
        let part = try XCTUnwrap((request["parts"] as? [[String: Any]])?.first)
        XCTAssertEqual(part["title"] as? String, "Islands · Пьеса 3", "a piece with no title is named as music-core would")
        XCTAssertEqual(part["pages"] as? [String: Int], ["first": 6, "last": 11])
        XCTAssertEqual(part["pdf"] as? String, Data("%PDF-".utf8).base64EncodedString())
        XCTAssertEqual(request["playlist"] as? String, "Islands", "the collection's title when no playlist is typed")
        XCTAssertEqual(request["device"] as? String, "ipad")
        XCTAssertEqual(request["pages"] as? Int, 30)
        XCTAssertEqual(request["language"] as? String, "ru")
        XCTAssertEqual(request["now"] as? String, "1970-01-01T00:00:00Z")
        XCTAssertEqual(PartsSplit.title(of: PartRow(title: " I Giorni ", first: 1, last: 1), number: 1, collection: "Islands", text: english), "I Giorni")
        XCTAssertEqual(PartsSplit.playlist(" Einaudi ", collection: "Islands"), "Einaudi")
    }

    func testTheAnswerGivesThePieceToSaveOrSaysWhyNot() {
        let answer = #"{"original": null, "playlist": "Islands", "parts": [{"number": 1, "title": "I Giorni", "pages": {"first": 3, "last": 8}, "project": {"id": "p_1"}, "file": "{\"version\": 15}"}]}"#
        XCTAssertEqual(try PartsSplit.made(answer).get(), PartsSplit.Made(id: "p_1", title: "I Giorni", file: #"{"version": 15}"#, first: 3, last: 8))
        XCTAssertEqual(PartsSplit.made(#"{"error": "badRequest", "message": "Not so."}"#), .failure(PartsSplitFailure(message: "Not so.")))
        XCTAssertEqual(PartsSplit.made("not json"), .failure(PartsSplitFailure(message: nil)))
    }

    // MARK: - The words

    func testTheSummarySaysHowManyPiecesAndThePlaylist() {
        XCTAssertEqual(PartsSplit.summary(count: 19, playlist: "Islands", text: russian), "Разделено на 19 пьес · плейлист «Islands»")
        XCTAssertEqual(PartsSplit.summary(count: 1, playlist: "Islands", text: russian), "Разделено на 1 пьесу · плейлист «Islands»")
        XCTAssertEqual(PartsSplit.summary(count: 3, playlist: "Islands", text: russian), "Разделено на 3 пьесы · плейлист «Islands»")
        XCTAssertEqual(PartsSplit.summary(count: 2, playlist: "Islands", text: english), "Split into 2 pieces · playlist “Islands”")
        XCTAssertEqual(PartsSplit.pages(first: 6, last: 11, text: russian), "с. 6–11")
        XCTAssertEqual(PartsSplit.pages(first: 6, last: 6, text: english), "p. 6")
    }

    // MARK: - What the iPad keeps

    func testASplitIsKeptOnTheIPadAndAPieceKnowsWhereItCameFrom() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("parts-\(UUID().uuidString)").appendingPathComponent("piece-parts.json")
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        XCTAssertEqual(PieceSplits.load(from: url), [:])
        let link = SplitLink(title: "Islands", playlist: "Islands", parts: [PartLink(id: "p_1", title: "I Giorni", first: 3, last: 8)])
        PieceSplits.keep(link, collection: "p_book", in: url)
        PieceSplits.keep(SplitLink(title: "Other", playlist: "Other", parts: [PartLink(id: "p_9", title: "X", first: 1, last: 1)]), collection: "p_other", in: url)
        let kept = PieceSplits.load(from: url)
        XCTAssertEqual(kept["p_book"], link)
        XCTAssertEqual(PieceSplits.origin(of: "p_1", in: kept), PartOrigin(collection: "p_book", title: "Islands", playlist: "Islands", part: link.parts[0]))
        XCTAssertNil(PieceSplits.origin(of: "p_book", in: kept))
        PieceSplits.keep(SplitLink(title: "Islands", playlist: "Islands", parts: []), collection: "p_book", in: url)
        XCTAssertNil(PieceSplits.load(from: url)["p_book"], "a split with no pieces is forgotten")
        try Data("garbage".utf8).write(to: url)
        XCTAssertEqual(PieceSplits.load(from: url), [:])
    }

    // MARK: - Through music-core

    /// A PDF of `count` pages as music-core counts them (its page objects).
    private func pdf(_ count: Int) -> Data {
        Data(("%PDF-1.4\n1 0 obj << /Type /Pages /Count \(count) >> endobj\n" + (2...(count + 1)).map { "\($0) 0 obj << /Type /Page >> endobj\n" }.joined() + "%%EOF\n").utf8)
    }

    func testMusicCoreMakesAPdfPieceOfThePagesInThePlaylist() throws {
        let opened = facadeObject(MusicCore.open(fileName: "Islands.pdf", data: pdf(14), request: #"{"language": "ru"}"#))
        let piece = try XCTUnwrap((opened?["piece"] as? NSNumber)?.int32Value)
        defer { MusicCore.close(piece: piece) }
        let file = MusicCore.call(piece: piece, "project.save", #"{"now": "2026-11-12T09:00:00Z"}"#)
        let fields = PartsSplit.request(
            file: file, row: PartRow(title: "I Giorni", first: 3, last: 8), number: 1, pdf: pdf(6), pages: 14,
            playlist: "Islands", collection: "Islands", now: Date(), text: russian
        )
        let body = String(decoding: try JSONSerialization.data(withJSONObject: fields), as: UTF8.self)
        let made = try PartsSplit.made(MusicCore.call(piece: 0, "parts.split", body)).get()
        XCTAssertEqual([made.first, made.last], [3, 8])
        XCTAssertEqual(made.title, "I Giorni")
        let summary = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: try JSONSerialization.data(withJSONObject: ["file": made.file]), as: UTF8.self))))
        XCTAssertEqual(summary.playlists, ["Islands"])
        XCTAssertEqual(summary.title, "I Giorni")
        XCTAssertTrue(summary.document)
        XCTAssertFalse(summary.hasNotes)
        XCTAssertEqual(summary.id, made.id)
    }
}
