import XCTest
@testable import MotiffLab

/// The library's search, filters and order (version 0.2), held to the web app's rules (src/MotiffLab.Web/Projects/
/// LibraryShelf.cs and tests/e2e/library.spec.mjs): letter case and accents do not matter, every word searched must be
/// found, one composer and one tag filter whatever their case, the title and composer orders follow the language, and
/// tags are cleaned and kept once as music-core keeps them.
final class LibraryShelfTests: XCTestCase {
    private let english = Locale(identifier: "en")

    private func entry(_ title: String, _ composer: String? = nil, _ tags: [String] = []) -> LibraryEntry {
        LibraryEntry(url: URL(fileURLWithPath: "/pieces/\(title).motiff.json"), title: title, composer: composer, tags: tags, modified: .distantPast)
    }

    private var shelf: [LibraryEntry] {
        [
            entry("Waltz sketch", nil, ["Exam"]),
            entry("Für Elise, opening", "Ludwig van Beethoven"),
            entry("Ode to Joy, theme", "Ludwig van Beethoven", ["exam"]),
            entry("Ёжик в тумане", "Норштейн"),
            entry("Йога"),
        ]
    }

    private func titles(_ query: LibraryQuery) -> [String] {
        LibraryShelf.apply(shelf, query, locale: english).map(\.title)
    }

    func testSearchIgnoresLetterCaseAndAccents() {
        XCTAssertEqual(titles(LibraryQuery(search: "fur elise")), ["Für Elise, opening"])
        XCTAssertEqual(titles(LibraryQuery(search: "FÜR")), ["Für Elise, opening"])
        XCTAssertEqual(titles(LibraryQuery(search: "ежи")), ["Ёжик в тумане"])
        XCTAssertEqual(titles(LibraryQuery(search: "иога")), ["Йога"])
        XCTAssertEqual(LibraryShelf.fold("Für Élise Ёлка й"), "fur elise елка и")
    }

    func testEveryWordIsFoundInTheTitleComposerOrTags() {
        XCTAssertEqual(titles(LibraryQuery(search: "beethoven ode")), ["Ode to Joy, theme"])
        XCTAssertEqual(titles(LibraryQuery(search: "exam")), ["Waltz sketch", "Ode to Joy, theme"])
        XCTAssertEqual(titles(LibraryQuery(search: "nothing like this")), [])
        XCTAssertFalse(LibraryQuery(search: "   ").narrows)
        XCTAssertTrue(LibraryQuery(tag: "Exam").narrows)
    }

    func testOneComposerAndOneTagFilterWhateverTheirCase() {
        XCTAssertEqual(titles(LibraryQuery(composer: "ludwig van beethoven")), ["Für Elise, opening", "Ode to Joy, theme"])
        XCTAssertEqual(titles(LibraryQuery(tag: "EXAM")), ["Waltz sketch", "Ode to Joy, theme"])
        XCTAssertEqual(titles(LibraryQuery(composer: "Ludwig van Beethoven", tag: "Exam")), ["Ode to Joy, theme"])
        XCTAssertEqual(LibraryQuery(search: "x", sort: .title, composer: "A", tag: "B").cleared, LibraryQuery(sort: .title))
    }

    func testOrdersByTitleAndByComposerWithPiecesWithoutOneLast() {
        XCTAssertEqual(titles(LibraryQuery()), shelf.map(\.title))
        XCTAssertEqual(titles(LibraryQuery(sort: .title)), ["Für Elise, opening", "Ode to Joy, theme", "Waltz sketch", "Ёжик в тумане", "Йога"])
        XCTAssertEqual(titles(LibraryQuery(sort: .composer)), ["Für Elise, opening", "Ode to Joy, theme", "Ёжик в тумане", "Waltz sketch", "Йога"])
    }

    func testTheShelfsComposersAndTagsAreListedOnce() {
        XCTAssertEqual(LibraryShelf.tags(shelf, locale: english), ["Exam"])
        XCTAssertEqual(LibraryShelf.composers(shelf, locale: english), ["Ludwig van Beethoven", "Норштейн"])
        let kept = LibraryShelf.kept(LibraryQuery(composer: "ludwig van beethoven", tag: "gone"), composers: ["Ludwig van Beethoven"], tags: ["Exam"])
        XCTAssertEqual(kept, LibraryQuery(composer: "Ludwig van Beethoven"))
    }

    func testTagsAreCleanedAndKeptOnce() {
        XCTAssertEqual(LibraryShelf.clean("  Grade \t 5 \n"), "Grade 5")
        XCTAssertNil(LibraryShelf.clean("   "))
        XCTAssertEqual(LibraryShelf.adding(" exam ", to: ["Exam"]), ["Exam"])
        XCTAssertEqual(LibraryShelf.adding("waltzes", to: ["Exam"], library: ["Waltzes"]), ["Exam", "Waltzes"])
        XCTAssertEqual(LibraryShelf.adding("  ", to: ["Exam"]), ["Exam"])
        XCTAssertEqual(LibraryShelf.suggestions(["Exam", "Waltzes", "Scales"], excluding: ["exam"], typed: "al"), ["Waltzes", "Scales"])
        XCTAssertEqual(LibraryShelf.suggestions(["Exam", "Waltzes"], excluding: [], typed: ""), ["Exam", "Waltzes"])
    }

    func testALongShelfOpensAsAListUntilThePlayerPicks() {
        XCTAssertEqual(LibraryShelf.layout(chosen: nil, count: 12), .cards)
        XCTAssertEqual(LibraryShelf.layout(chosen: nil, count: 13), .list)
        XCTAssertEqual(LibraryShelf.layout(chosen: .cards, count: 200), .cards)
    }
}

/// A piece's title, composer and tags written into its project file through music-core (`project.metadata` on piece 0,
/// facade contract 12): what the player sets is kept, cleaned, and shown before what the score says, which comes back when
/// the player's is cleared; the file keeps its date, so the piece keeps its place on the shelf.
final class PieceDetailsFileTests: XCTestCase {
    func testThePlayersDetailsAreWrittenIntoTheProjectFileAndTheFilesComeBack() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data)))
        let piece = Int32(opened["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0, "\(opened)")
        defer { MusicCore.close(piece: piece) }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("details-\(UUID().uuidString).motiff.json")
        defer { try? FileManager.default.removeItem(at: url) }
        try Data(MusicCore.call(piece: piece, "project.save").utf8).write(to: url)
        let dated = Date(timeIntervalSince1970: 1_700_000_000)
        try FileManager.default.setAttributes([.modificationDate: dated], ofItemAtPath: url.path)

        let readBefore = await PieceSummary.read(url)
        let before = try XCTUnwrap(readBefore)
        XCTAssertEqual(before.title, "Für Elise")
        XCTAssertEqual(before.titleFrom, "file")
        XCTAssertEqual(before.composer, "L. van Beethoven")
        XCTAssertNil(before.playersTitle)

        try await PieceDetailsFile.save(url, title: "My Elise", composer: nil, tags: [" exam ", "Exam", "Grade  5"])
        let readAfter = await PieceSummary.read(url)
        let after = try XCTUnwrap(readAfter)
        XCTAssertEqual(after.playersTitle, "My Elise")
        XCTAssertEqual(after.composerFrom, "file")
        XCTAssertEqual(after.tags, ["exam", "Grade 5"])
        XCTAssertEqual(try url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, dated)
        let readFromFile = await PieceDetailsFile.fromFile(url)
        let fromFile = try XCTUnwrap(readFromFile)
        XCTAssertEqual(fromFile.title, "Für Elise")

        try await PieceDetailsFile.save(url, title: nil, composer: "Beethoven", tags: [])
        let readCleared = await PieceSummary.read(url)
        let cleared = try XCTUnwrap(readCleared)
        XCTAssertEqual(cleared.title, "Für Elise")
        XCTAssertEqual(cleared.titleFrom, "file")
        XCTAssertEqual(cleared.playersComposer, "Beethoven")
        XCTAssertEqual(cleared.tags, [])
    }
}
