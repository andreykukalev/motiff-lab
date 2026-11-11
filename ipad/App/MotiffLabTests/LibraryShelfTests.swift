import XCTest
@testable import MotiffLab

/// The library's search, filters and order (version 0.2), held to the web app's rules (src/MotiffLab.Web/Projects/
/// LibraryShelf.cs and tests/e2e/library.spec.mjs): letter case and accents do not matter, every word searched must be
/// found, one composer and one tag filter whatever their case, the title and composer orders follow the language, and
/// tags are cleaned and kept once as music-core keeps them. The tabs, the index at the list's edge and the composers
/// offered while typing follow the approved library screens (ux-prototype/v2, lib-1 and lib-4).
final class LibraryShelfTests: XCTestCase {
    private let english = Locale(identifier: "en")

    private func entry(_ title: String, _ composer: String? = nil, _ tags: [String] = [], opened: Double = 0, session: Bool = false) -> LibraryEntry {
        LibraryEntry(url: URL(fileURLWithPath: "/pieces/\(title).motiff.json"), title: title, composer: composer, tags: tags,
                     modified: Date(timeIntervalSince1970: opened), inSession: session)
    }

    private var shelf: [LibraryEntry] {
        [
            entry("Waltz sketch", nil, ["Exam"], opened: 5, session: true),
            entry("Für Elise, opening", "Ludwig van Beethoven", opened: 3),
            entry("Ode to Joy, theme", "Ludwig van Beethoven", ["exam"], opened: 9, session: true),
            entry("Ёжик в тумане", "Норштейн", opened: 1),
            entry("Йога", opened: 2),
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
        XCTAssertEqual(titles(LibraryQuery(search: "exam")), ["Ode to Joy, theme", "Waltz sketch"])
        XCTAssertEqual(titles(LibraryQuery(search: "nothing like this")), [])
        XCTAssertFalse(LibraryQuery(search: "   ").narrows)
        XCTAssertTrue(LibraryQuery(tag: "Exam").narrows)
    }

    func testOneComposerAndOneTagFilterWhateverTheirCase() {
        XCTAssertEqual(titles(LibraryQuery(composer: "ludwig van beethoven")), ["Für Elise, opening", "Ode to Joy, theme"])
        XCTAssertEqual(titles(LibraryQuery(tag: "EXAM")), ["Ode to Joy, theme", "Waltz sketch"])
        XCTAssertEqual(titles(LibraryQuery(composer: "Ludwig van Beethoven", tag: "Exam")), ["Ode to Joy, theme"])
        XCTAssertEqual(LibraryQuery(search: "x", sort: .composer, composer: "A", tag: "B").cleared, LibraryQuery(sort: .composer))
    }

    func testTheListIsOrderedByTheColumnChosenEitherWay() {
        let byTitle = ["Für Elise, opening", "Ode to Joy, theme", "Waltz sketch", "Ёжик в тумане", "Йога"]
        XCTAssertEqual(titles(LibraryQuery()), byTitle)
        XCTAssertEqual(titles(LibraryQuery().sorted(by: .title)), byTitle.reversed())
        XCTAssertEqual(titles(LibraryQuery(sort: .composer)), ["Für Elise, opening", "Ode to Joy, theme", "Ёжик в тумане", "Waltz sketch", "Йога"])
        XCTAssertEqual(titles(LibraryQuery(sort: .recent)), ["Ode to Joy, theme", "Waltz sketch", "Für Elise, opening", "Йога", "Ёжик в тумане"])
        XCTAssertEqual(LibraryQuery(sort: .title, ascending: false).sorted(by: .recent), LibraryQuery(sort: .recent))
    }

    func testRecentListsTheLastOpenedAndSessionsThePiecesWithWork() {
        XCTAssertEqual(LibraryShelf.pieces(of: .recent, shelf).map(\.title), ["Ode to Joy, theme", "Waltz sketch", "Für Elise, opening", "Йога", "Ёжик в тумане"])
        let many = (0..<30).map { entry("Piece \($0)", opened: Double($0)) }
        XCTAssertEqual(LibraryShelf.pieces(of: .recent, many).count, LibraryShelf.recentCount)
        XCTAssertEqual(LibraryShelf.pieces(of: .sessions, shelf).map(\.title), ["Ode to Joy, theme", "Waltz sketch"])
        XCTAssertEqual(LibraryShelf.pieces(of: .all, shelf), shelf)
    }

    func testTheIndexLeadsToTheFirstTitleUnderOrAfterItsLetter() {
        let byTitle = LibraryShelf.apply(shelf + [entry("Élégie")], LibraryQuery(), locale: english)
        let initials = byTitle.map(\.initial)
        XCTAssertEqual(initials, ["E", "F", "O", "W", "Е", "И"])
        XCTAssertEqual(LibraryShelf.indexLetters(initials).first, "A")
        XCTAssertTrue(LibraryShelf.indexLetters(initials).contains("И"))
        XCTAssertFalse(LibraryShelf.indexLetters(initials).contains("Й"))
        XCTAssertEqual(LibraryShelf.indexLetters(["A"]).count, 26)
        XCTAssertEqual(LibraryShelf.target("E", in: initials, locale: english), 0)
        XCTAssertEqual(LibraryShelf.target("P", in: initials, locale: english), 3)
        XCTAssertEqual(LibraryShelf.target("И", in: initials, locale: english), 5)
        XCTAssertNil(LibraryShelf.target("A", in: [], locale: english))
    }

    func testTheComposersTabCountsEachComposersPiecesAndThoseWithoutOneLast() {
        let rows = LibraryShelf.composerRows(shelf + [entry("Moonlight", "ludwig van beethoven")], locale: english)
        XCTAssertEqual(rows, [
            ComposerRow(name: "Ludwig van Beethoven", count: 3),
            ComposerRow(name: "Норштейн", count: 1),
            ComposerRow(name: nil, count: 2),
        ])
        XCTAssertEqual(rows.map(\.initial), ["L", "Н", ""])
        XCTAssertEqual(LibraryShelf.pieces(by: "LUDWIG VAN BEETHOVEN", shelf).map(\.title), ["Für Elise, opening", "Ode to Joy, theme"])
        XCTAssertEqual(LibraryShelf.pieces(by: nil, shelf).map(\.title), ["Waltz sketch", "Йога"])
    }

    func testEachTabShowsItsPiecesOrTheComposersUntilOneIsPicked() {
        let all = LibraryPage.of(shelf, LibraryQuery(), tab: .all, picked: nil, locale: english)
        XCTAssertEqual(all.pieces.map(\.title), ["Für Elise, opening", "Ode to Joy, theme", "Waltz sketch", "Ёжик в тумане", "Йога"])
        XCTAssertTrue(all.indexed)
        XCTAssertFalse(LibraryPage.of(shelf, LibraryQuery(sort: .recent), tab: .recent, picked: nil, locale: english).indexed)
        let composers = LibraryPage.of(shelf, LibraryQuery(composer: "Норштейн"), tab: .composers, picked: nil, locale: english)
        XCTAssertEqual(composers.composers?.map(\.count), [2, 1, 2])
        XCTAssertEqual(composers.initials, ["L", "Н", ""])
        XCTAssertEqual(composers.id(at: 2), AnyHashable(""))
        let picked = LibraryPage.of(shelf, LibraryQuery(search: "ode"), tab: .composers, picked: ComposerRow(name: "Ludwig van Beethoven", count: 2), locale: english)
        XCTAssertEqual(picked.pieces.map(\.title), ["Ode to Joy, theme"])
        XCTAssertNil(picked.composers)
        let sessions = LibraryPage.of(shelf, LibraryQuery(tag: "nothing"), tab: .sessions, picked: nil, locale: english)
        XCTAssertEqual(sessions.pieces.map(\.title), ["Ode to Joy, theme", "Waltz sketch"])
        XCTAssertTrue(LibraryPage.of([], LibraryQuery(), tab: .composers, picked: nil, locale: english).isEmpty)
    }

    func testComposersInTheLibraryAreOfferedWhileTyping() {
        XCTAssertEqual(LibraryShelf.composerSuggestions("bee", ["Bach", "Ludwig van Beethoven"]), ["Ludwig van Beethoven"])
        XCTAssertEqual(LibraryShelf.composerSuggestions("BACH", ["Bach"]), [])
        XCTAssertEqual(LibraryShelf.composerSuggestions(" ", ["Bach"]), [])
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
