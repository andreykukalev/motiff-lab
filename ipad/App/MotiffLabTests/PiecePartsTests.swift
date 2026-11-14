import XCTest
@testable import MotiffLab

/// Pieces in a file (v0.4, experiment «Пьесы в файле») for a PDF or photo collection: the rows «название · с–по» filled
/// from the page bookmarks and checked (in order, not overlapping, within the file), a piece added or moved, the
/// `parts.split` request and its answer, the summary in both languages, what the library tells of a split from the pieces'
/// `partOf`, and one piece made by the native music-core.
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

    func testAPieceSaysWhereItCameFromWithItsPlaylistAndPages() {
        XCTAssertEqual(PartsSplit.from(PartOrigin(title: "Islands", playlist: "Einaudi", pages: 6...11), text: russian), "Выделена из «Islands» · плейлист «Einaudi» · с. 6–11")
        XCTAssertEqual(PartsSplit.from(PartOrigin(title: "Islands", playlist: nil, pages: 6...6), text: english), "Split from “Islands” · p. 6")
        XCTAssertEqual(PartsSplit.from(PartOrigin(title: "Concert", playlist: nil, pages: nil), text: russian), "Выделена из «Concert»", "a stretch of a recording has no pages")
        XCTAssertEqual(PartsSplit.summary(count: 5, playlist: nil, text: russian), "Разделено на 5 пьес")
    }

    // MARK: - What the library knows

    /// A summary as music-core answers it, with its originals and partOf.
    private func described(id: String, title: String, playlists: [String] = [], originals: [(id: String, sha256: String)], partOf: String = "null") -> PieceSummary? {
        let listed = originals.map { #"{"id": "\#($0.id)", "kind": "pdf", "fileName": "Islands.pdf", "sha256": "\#($0.sha256)"}"# }.joined(separator: ", ")
        let names = playlists.map { #""\#($0)""# }.joined(separator: ", ")
        return PieceSummary(answer: #"{"project": {"id": "\#(id)", "title": "\#(title)", "playlists": [\#(names)], "originals": [\#(listed)], "partOf": \#(partOf)}}"#)
    }

    func testTheLibraryTellsWhatWasSplitFromWhatByTheOriginalsFingerprint() throws {
        let book = try XCTUnwrap(described(id: "p_book", title: "Islands", originals: [("src_pdf", "aa")]))
        let first = try XCTUnwrap(described(id: "p_1", title: "I Giorni", playlists: ["Islands"], originals: [("src_pdf", "b1"), ("src_book", "aa")],
                                          partOf: #"{"original": "src_book", "pages": {"first": 3, "last": 8}, "milliseconds": null}"#))
        let second = try XCTUnwrap(described(id: "p_2", title: "Nuvole bianche", playlists: ["Recital", "Islands"], originals: [("src_pdf", "b2"), ("src_book", "aa")],
                                           partOf: #"{"original": "src_book", "pages": {"first": 9, "last": 11}, "milliseconds": null}"#))
        let stranger = try XCTUnwrap(described(id: "p_x", title: "Other", playlists: ["Islands"], originals: [("src_pdf", "cc"), ("src_book", "zz")],
                                             partOf: #"{"original": "src_book", "pages": {"first": 1, "last": 2}, "milliseconds": null}"#))
        XCTAssertEqual(first.partOf, PartOfEntry(original: "src_book", pages: 3...8))
        XCTAssertNil(book.partOf)
        let shelf = URL(fileURLWithPath: "/shelf")
        let urls = ["Islands", "I Giorni", "Nuvole", "Other"].map { shelf.appendingPathComponent("\($0).motiff.json") }
        let summaries = [urls[0]: book, urls[1]: first, urls[2]: second, urls[3]: stranger]

        let split = try XCTUnwrap(PieceFamily.of(urls[0], in: summaries).split)
        XCTAssertEqual(split.parts.map(\.title), ["I Giorni", "Nuvole bianche"], "in page order, the other collection's piece left out")
        XCTAssertEqual(split.parts.map(\.pages), [3...8, 9...11])
        XCTAssertEqual(split.playlist, "Islands", "the playlist every piece is on")
        XCTAssertEqual(PartsSplit.summary(count: split.parts.count, playlist: split.playlist, text: russian), "Разделено на 2 пьесы · плейлист «Islands»")
        XCTAssertNil(PieceFamily.of(urls[0], in: summaries).origin)

        let origin = try XCTUnwrap(PieceFamily.of(urls[1], in: summaries).origin)
        XCTAssertEqual(origin, PartOrigin(title: "Islands", playlist: "Islands", pages: 3...8))
        XCTAssertNil(PieceFamily.of(urls[1], in: summaries).split, "a piece's own PDF is not the collection's")
        XCTAssertEqual(PieceFamily.of(urls[3], in: summaries).origin?.title, "Islands", "a collection not in this library is named by its original")
        XCTAssertEqual(PieceFamily.of(shelf.appendingPathComponent("Gone.motiff.json"), in: summaries), PieceFamily())
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
        XCTAssertEqual(summary.partOf, PartOfEntry(original: "src_book", pages: 3...8), "the piece's file says where it is in the collection")

        // The collection says it was split, and the piece where it came from, from their summaries alone.
        let collection = try XCTUnwrap(PieceSummary(answer: MusicCore.call(piece: 0, "project.summary", String(decoding: try JSONSerialization.data(withJSONObject: ["file": file]), as: UTF8.self))))
        let shelf = URL(fileURLWithPath: "/shelf")
        let book = shelf.appendingPathComponent("Islands.motiff.json"), part = shelf.appendingPathComponent("I Giorni.motiff.json")
        let summaries = [book: collection, part: summary]
        XCTAssertEqual(PieceFamily.of(book, in: summaries).split, SplitLink(playlist: "Islands", parts: [PartLink(id: made.id, title: "I Giorni", pages: 3...8)]))
        XCTAssertEqual(PieceFamily.of(part, in: summaries).origin?.pages, 3...8)
        XCTAssertEqual(PieceFamily.of(part, in: summaries).origin?.playlist, "Islands")
        XCTAssertEqual(PieceFamily.of(part, in: [part: summary]).origin?.title, "Islands", "without the collection here, its original is named by its title")
    }

    // MARK: - The pieces layer of a PDF with notes read (version 0.5)

    func testSuggestedRowsComeFromTheProposalsPagesAndAPageStaysWithTheFirstPieceOnIt() throws {
        let answer = #"""
        {"proposals": [
            {"number": 1, "title": "Piece 1", "firstBar": 0, "pages": {"first": 1, "last": 3}},
            {"number": 2, "title": " Nocturne ", "firstBar": 40, "pages": {"first": 3, "last": 6}},
            {"number": 3, "title": "Piece 3", "firstBar": 52, "pages": {"first": 3, "last": 9}},
            {"number": 4, "title": "Waltz", "firstBar": 90, "pages": {"first": 10, "last": 14}}
        ], "layer": []}
        """#
        let rows = try XCTUnwrap(PiecesLayer.rows(answer, pageCount: 14))
        XCTAssertEqual(pages(rows), [[1, 2], [3, 9], [10, 14]], "each to the page before the next; two starting on page 3 are one row")
        XCTAssertEqual(rows.rows.map(\.title), ["Piece 1", "Nocturne", "Waltz"])
        XCTAssertEqual(rows.rows.map(\.firstBar), [0, 40, 90])
        XCTAssertNil(rows.problem)
    }

    func testTheKeptLayerComesBeforeTheProposalsAndNotesWithoutPagesSuggestNothing() {
        let kept = #"{"proposals": [{"title": "A", "firstBar": 0, "pages": {"first": 1, "last": 14}}], "layer": [{"title": "Prelude", "firstBar": 0, "pages": {"first": 1, "last": 4}}, {"title": "Fugue", "firstBar": 30, "pages": {"first": 5, "last": 14}}]}"#
        XCTAssertEqual(PiecesLayer.rows(kept, pageCount: 14).map { $0.rows.map(\.title) }, ["Prelude", "Fugue"])
        XCTAssertNil(PiecesLayer.rows(#"{"proposals": [{"title": "A", "firstBar": 0, "pages": null}], "layer": []}"#, pageCount: 14))
        XCTAssertNil(PiecesLayer.rows(#"{"error": "badRequest", "message": "No."}"#, pageCount: 14))
        XCTAssertNil(PiecesLayer.rows(#"{"proposals": [], "layer": []}"#, pageCount: 14))
    }

    func testConfirmSendsTheBarProposedWhileTheRowStartsOnItsPageElseTheFirstPage() throws {
        let rows = [
            PartRow(title: " Prelude ", first: 1, last: 4, firstBar: 0, barPage: 1),
            PartRow(title: "", first: 6, last: 14, firstBar: 30, barPage: 5),
        ]
        let request = PiecesLayer.saveRequest(rows, now: Date(timeIntervalSince1970: 0))
        let object = try XCTUnwrap(facadeObject(request))
        let pieces = try XCTUnwrap(object["pieces"] as? [[String: Any]])
        XCTAssertEqual(pieces[0]["firstBar"] as? Int, 0)
        XCTAssertEqual(pieces[0]["title"] as? String, "Prelude")
        XCTAssertNil(pieces[1]["firstBar"], "the row was moved off its bar's page")
        XCTAssertEqual(pieces[1]["firstPage"] as? Int, 6)
        XCTAssertEqual(object["now"] as? String, "1970-01-01T00:00:00Z")
        XCTAssertEqual(try PiecesLayer.kept(#"{"layer": [{}, {}]}"#).get(), 2)
        XCTAssertEqual(PiecesLayer.kept(#"{"error": "badRequest", "message": "Out of order."}"#), .failure(PartsSplitFailure(message: "Out of order.")))
    }

    func testCheckByPieceReadsThePiecesOfTheLayerAndKeepsTheListToOne() throws {
        let answer = #"{"flags": [{"bar": 3, "reason": "barTooLong", "resolved": false}, {"bar": 25, "reason": "barTooLong", "resolved": false}], "open": 2, "pieces": [{"title": "Prelude", "firstBar": 1, "lastBar": 20, "open": 1, "total": 1}, {"title": "Fugue", "firstBar": 21, "lastBar": 40, "open": 1, "total": 1}]}"#
        let check = try XCTUnwrap(CheckShelf(answer: answer))
        XCTAssertEqual(check.pieces.map(\.title), ["Prelude", "Fugue"])
        XCTAssertEqual(check.openBars(in: check.pieces[1]).map(\.bar), [25])
        XCTAssertEqual(check.openBars(in: nil).map(\.bar), [3, 25])
        XCTAssertEqual(CheckShelf(answer: #"{"flags": [], "open": 0}"#)?.pieces, [], "no layer, no pieces")
    }
}