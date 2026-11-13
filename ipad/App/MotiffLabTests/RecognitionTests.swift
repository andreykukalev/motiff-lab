import XCTest
@testable import MotiffLab

/// «Распознать ноты» for several pieces (version 0.5): the PDF-only pieces are picked and filtered as «Без нот», the batch
/// is kept after each piece and carried on after a relaunch, and Motiff Lab's server reads at most 100 pages a day for a
/// player, counted on this device.
@MainActor
final class RecognitionTests: XCTestCase {
    private var base: URL!
    private var defaults: UserDefaults!

    override func setUp() async throws {
        base = FileManager.default.temporaryDirectory.appendingPathComponent("RecognitionTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        defaults = UserDefaults(suiteName: "RecognitionTests-\(UUID().uuidString)")
    }

    override func tearDown() async throws {
        try? FileManager.default.removeItem(at: base)
    }

    func testTheHostedServersPagesAreCountedByPlayerAndDay() {
        let day = Date(timeIntervalSince1970: 1_800_000_000)
        let next = day.addingTimeInterval(86_400)

        DailyPages.add(41, player: "andrei", on: day, defaults: defaults)
        DailyPages.add(9, player: "andrei", on: day, defaults: defaults)

        XCTAssertEqual(DailyPages.used(player: "andrei", on: day, defaults: defaults), 50)
        XCTAssertEqual(DailyPages.used(player: "andrei", on: next, defaults: defaults), 0, "a new day starts again")
        XCTAssertEqual(DailyPages.used(player: "someone", on: day, defaults: defaults), 0, "each player has their own")
        XCTAssertEqual(DailyPages.limit, 100)
        XCTAssertEqual(DailyPages.minutes(41), 21)
        XCTAssertEqual(DailyPages.minutes(1), 1)
    }

    func testABatchIsKeptAddedToAndAfterARelaunchWaitsForContinue() {
        let queue = RecognitionQueue()
        queue.root = base
        let server = URL(string: "https://scan.example.org/")!
        let first = [RecognitionItem(file: "a.motiff.json", title: "Arabesque", pages: 5), RecognitionItem(file: "b.motiff.json", title: "Ballade", pages: 12)]

        queue.start(first, address: server, hosted: true)
        queue.start([RecognitionItem(file: "b.motiff.json", title: "Ballade", pages: 12), RecognitionItem(file: "c.motiff.json", title: "Clair", pages: 4)], address: server, hosted: true)

        XCTAssertEqual(queue.batch?.items.map(\.file), ["a.motiff.json", "b.motiff.json", "c.motiff.json"], "the same server: one batch, each piece once")
        XCTAssertEqual(queue.batch?.pagesLeft, 21)
        var kept = RecognitionStore.load(in: base)
        kept?.items[0].state = .reading
        RecognitionStore.save(kept, in: base)

        let again = RecognitionQueue()
        again.root = base
        again.restore()

        XCTAssertEqual(again.batch?.items.first?.state, .queued, "a piece being read when the app closed is read again")
        XCTAssertTrue(again.interrupted)
        XCTAssertEqual(again.status(of: URL(fileURLWithPath: "/x/c.motiff.json"))?.title, "Clair")
        again.stop()
        XCTAssertNil(RecognitionStore.load(in: base))
    }

    func testThePieceOpenOnScreenIsLeftQueuedUntilItCloses() {
        var batch = RecognitionBatch(address: "https://scan.example.org/", hosted: false, items: [
            RecognitionItem(file: "a.motiff.json", title: "Arabesque", pages: 5), RecognitionItem(file: "b.motiff.json", title: "Ballade", pages: 12),
        ])

        XCTAssertEqual(RecognitionQueue.next(in: batch, open: "a.motiff.json"), 1, "the open piece is skipped: saving the screen would cover its notes")
        XCTAssertEqual(RecognitionQueue.next(in: batch, open: nil), 0)
        batch.items[1].state = .done
        XCTAssertNil(RecognitionQueue.next(in: batch, open: "a.motiff.json"), "only the open piece is left: it waits")
        XCTAssertEqual(RecognitionQueue.next(in: batch, open: nil), 0, "read once the screen closes")
    }

    func testOnlyPiecesThatAreOnlyAPdfArePickedAndFilteredWithoutNotes() {
        let pdf = LibraryEntry(url: URL(fileURLWithPath: "/p/a.motiff.json"), title: "Arabesque", composer: nil, tags: [], modified: Date())
        var away = pdf
        away.withoutNotes = true
        away.pages = 5
        let notes = LibraryEntry(url: URL(fileURLWithPath: "/p/b.motiff.json"), title: "Ballade", composer: nil, tags: [], modified: Date())
        var query = LibraryQuery()
        query.withoutNotes = true

        let shown = LibraryShelf.apply([away, notes], query, locale: Locale(identifier: "en"))

        XCTAssertEqual(shown.map(\.title), ["Arabesque"])
        XCTAssertTrue(query.narrows)
        XCTAssertFalse(query.cleared.withoutNotes)
    }

    func testAPdfPieceOnTheShelfSaysItHasNoNotesAndItsPages() async throws {
        let made = await LibraryLink.create(fileName: "Ballade.pdf", folder: "", sha256: String(repeating: "a", count: 64), bytes: 100, pages: 12)
        let url = base.appendingPathComponent("Ballade.motiff.json")
        try Data(try XCTUnwrap(made).utf8).write(to: url)
        let read = await PieceSummary.read(url)
        let summary = try XCTUnwrap(read)

        let line = LibraryEntry.of(ProjectFiles.Entry(url: url, modified: Date()), summary)

        XCTAssertTrue(line.withoutNotes)
        XCTAssertEqual(line.pages, 12)
        XCTAssertEqual(RecognitionQueue.item(url, summary).pages, 12)
    }

    func testTheWordsOfTheBatchAreInBothLanguages() {
        var keys = ["queued", "reading", "done", "failed"].map { "recognition.state." + $0 }
        keys += ["limit", "offline", "signIn", "needsPro", "unreachable"].map { "recognition.stopped." + $0 }
        keys += ["recognition.selectAll", "folderImport.selectAll"]
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            for key in keys { XCTAssertNotEqual(text[key], key, "\(language): \(key)") }
        }
        XCTAssertEqual(UIText(language: "ru").count("recognition.send", 41), "Отправить 41 страницу")
        XCTAssertEqual(UIText(language: "ru").count("recognition.pieces", 8), "8 пьес")
    }

    func testSelectAllPicksEveryPieceShownWithoutNotesOrNone() {
        func entry(_ name: String, withoutNotes: Bool) -> LibraryEntry {
            var entry = LibraryEntry(url: URL(fileURLWithPath: "/p/\(name)"), title: name, composer: nil, tags: [], modified: Date())
            entry.withoutNotes = withoutNotes
            return entry
        }
        let shown = [entry("a", withoutNotes: true), entry("b", withoutNotes: true), entry("c", withoutNotes: false)]
        let a = shown[0].url, b = shown[1].url, other = URL(fileURLWithPath: "/p/other")

        XCTAssertEqual(LibraryMarks.all([a, other], shown: shown), [a, b, other])
        XCTAssertEqual(LibraryMarks.all([a, b, other], shown: shown), [other])
    }
}
