import XCTest

@testable import MotiffLab

/// The marks «что не так» on the iPad (v1.6 c-14b3, the 1.6 plan's decision 15, «Как напечатано» step 3): what the menu
/// offers and sends, in both languages; Check's marks as music-core answers them; a mark flagged at its first bar like a
/// Check flag, joined with the flag a bar has (E-scan-12); a mark kept, flagged, shown and taken away on notes read from a
/// PDF; and «Распознать заново с отметками» reading only the marked pages, a bar read badly on the server, and failing whole
/// when an engine reads a marked page no more (E-scan-18, E-scan-19); a PDF of other pages than the notes refused; one
/// recognition at a time with Sources (E-sources-1, E-scan-17); a bar read again reviewed as one the engines disagree on;
/// and the whole reading, from a job-read piece with a mark to its new Notes and the toast's line of the marks.
@MainActor
final class ReadingMarksTests: XCTestCase {
    private let english = UIText(language: "en")
    private let russian = UIText(language: "ru")
    private let server = ScanSupport.server
    private let notAPdf = Data("not a pdf".utf8)
    private var opened: [Int32] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    private func job() -> RecognitionJob {
        RecognitionJob(kept: nil, keep: { _ in }, hear: { _ in }, wanted: { true }, ready: { true })
    }

    private var target: ScanTarget { ScanTarget(address: server, account: Account(options: nil), session: StubAPI.session()) }

    private var jobServer: StubAPI.Answer {
        ScanSupport.answer(200, #"{"engine": "homr", "version": "0.7.0", "engines": [{"engine": "homr", "secondsPerPage": 75}], "jobs": {"queue": 0, "workers": 1}}"#)
    }

    private var done: StubAPI.Answer { ScanSupport.answer(200, #"{"id": "j1", "state": "done", "etaSeconds": 0, "pages": []}"#) }

    /// The scan server's answers to a job `id` that reads `pages`, and to its deletion.
    private func jobAnswers(_ id: String, _ pages: [(page: Int, step: String?)]) -> [String: StubAPI.Answer] {
        [
            "http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "\#(id)"}"#),
            "http://scan.test/jobs/\(id)": ScanSupport.answer(200, #"{"id": "\#(id)", "state": "done", "etaSeconds": 0, "pages": []}"#),
            "http://scan.test/jobs/\(id)/result": ScanSupport.result(id, pages), "DELETE http://scan.test/jobs/\(id)": ScanSupport.answer(204, ""),
        ]
    }

    func testTheMenuOffersEachKindWithTheValuesMusicCoreTakesInBothLanguages() {
        XCTAssertEqual(ReadingMarkChoice.kinds, ["meter", "key", "clef", "octave", "triplets", "hands", "badlyRead"])
        XCTAssertEqual(ReadingMarkChoice.choices("meter").map { $0.label(english) }.prefix(4), ["2/4", "3/4", "4/4", "5/4"])
        let keys = ReadingMarkChoice.choices("key")
        XCTAssertEqual(keys.count, 15, "none, then 1 to 7 sharps, then 1 to 7 flats")
        XCTAssertEqual(keys.compactMap(\.value).first, "0")
        XCTAssertEqual(keys.map { $0.label(russian) }.prefix(3), ["Без знаков", "1 диез", "2 диеза"])
        XCTAssertEqual(keys[12].label(english), "5 flats")
        XCTAssertEqual(keys[12].label(russian), "5 бемолей")
        // E-scan-14: a clef or an octave sign on the upper or the lower staff.
        let clefs = ReadingMarkChoice.choices("clef")
        XCTAssertEqual(clefs.map(\.staff), [1, 1, 2, 2])
        XCTAssertEqual(clefs[3].label(russian), "Басовый ключ, нижний стан")
        XCTAssertEqual(ReadingMarkChoice.choices("octave")[0].label(english), "8va (an octave higher), upper staff")
        XCTAssertEqual(ReadingMarkChoice.choices("triplets").map { $0.label(english) }, ["Triplets here", "No triplets here"])
        XCTAssertEqual(ReadingMarkChoice.choices("hands").map { $0.label(russian) }, ["Руки перепутаны"], "one item, nothing more to say")
        XCTAssertEqual(ReadingMarkChoice.choices("badlyRead").map { $0.label(english) }, ["Bar read badly"])
        for key in ["whatsWrong.menu", "whatsWrong.title", "whatsWrong.markedNotice", "markedReading.readAgain", "markedReading.needsJobs", "markedReading.pagesDiffer", "scanMarked.notRead"] {
            XCTAssertNotEqual(english[key], key)
            XCTAssertNotEqual(russian[key], key)
        }
    }

    func testAMarkSendsItsBarsStaffAndValueAndAMeterOrKeyItsFirstBarAlone() {
        let octave = ReadingMarkChoice(kind: "octave", staff: 2, value: "8vb").fields(first: 3, last: 5)
        XCTAssertEqual(octave["kind"] as? String, "octave")
        XCTAssertEqual(octave["bar"] as? Int, 3)
        XCTAssertEqual(octave["last"] as? Int, 5)
        XCTAssertEqual(octave["staff"] as? Int, 2)
        XCTAssertEqual(octave["value"] as? String, "8vb")
        // E-scan-13: a meter holds from its bar on, so a selection of bars gives it the first.
        let meter = ReadingMarkChoice(kind: "meter", value: "5/4").fields(first: 3, last: 5)
        XCTAssertNil(meter["last"])
        XCTAssertNil(meter["staff"])
        let hands = ReadingMarkChoice(kind: "hands").fields(first: 4, last: 4)
        XCTAssertEqual(hands.keys.sorted(), ["bar", "kind"], "one bar: no last, no value")
    }

    func testCheckReadsTheMarksAndOffersCheckForThemAlone() throws {
        let answer = #"{"flags": [], "open": 0, "origin": "scanned", "marks": [{"kind": "hands", "bar": 3, "last": 4, "text": "Hands swapped, bars 3–4"}, {"kind": "clef", "bar": 6, "last": 6, "staff": 2, "value": "bass", "text": "Bass clef here, lower staff, bar 6"}]}"#
        let check = try XCTUnwrap(CheckShelf(answer: answer))

        XCTAssertEqual(check.marks.map(\.id), ["hands-3-0", "clef-6-2"])
        XCTAssertEqual(check.marks[0].last, 4)
        XCTAssertTrue(check.hasEntries, "More offers Check for a mark alone")
        XCTAssertFalse(check.hasFlags)
        XCTAssertTrue(check.takesMarks)
        XCTAssertTrue(check.marks[0].lies(on: 4...9))
        XCTAssertFalse(check.marks[0].lies(on: 5...9))
        XCTAssertEqual(check.marks[1].unmarkFields["staff"] as? Int, 2)
        XCTAssertNil(check.marks[0].unmarkFields["staff"])
        XCTAssertFalse(try XCTUnwrap(CheckShelf(answer: #"{"flags": [], "open": 0, "origin": "imported"}"#)).takesMarks, "only notes read from a PDF")
    }

    func testAMarkIsFlaggedAtItsFirstBarAndJoinsTheFlagThatBarHas() {
        // E-scan-12, as E-scan-1: one flag a bar, its label telling both.
        let flags = CheckModel.adding([(bar: 3, tick: 3840, label: "Hands swapped, bars 3–4"), (bar: 6, tick: 9600, label: "Read badly, bar 6")], to: [["bar": 3, "tick": 3840, "label": "Check bar 3: notes"]])

        XCTAssertEqual(flags.count, 2)
        XCTAssertEqual(flags[0]["label"] as? String, "Check bar 3: notes · Hands swapped, bars 3–4")
        XCTAssertEqual(flags[1]["bar"] as? Int, 6)
        XCTAssertEqual(flags[1]["tick"] as? Double, 9600)
        XCTAssertEqual(CheckModel.adding([], to: []).count, 0)
        // The toast of the notes read again tells what became of the marks as it tells the corrections carried.
        let marked: [String: Any] = ["used": 1, "unfit": 0, "text": "1 mark used"]
        XCTAssertEqual(SourcesController.toastText("Read again", adding: ["text": "2 corrections carried over"] as [String: Any], marked), "Read again · 2 corrections carried over · 1 mark used")
        XCTAssertEqual(SourcesController.toastText("Read again", adding: nil, marked), "Read again · 1 mark used")
    }

    func testOnlyThePagesAMarkLiesOnAreReadAgainEachWithItsBarsReadBadly() throws {
        let starts = [
            CheckPageStart(page: 1, bar: 1, tick: 0, lastBar: 4, lastTick: 5760), CheckPageStart(page: 2, bar: 5, tick: 7680, lastBar: 8, lastTick: 13440),
            CheckPageStart(page: 3, bar: 9, tick: 15360, lastBar: 12, lastTick: 21120),
        ]
        let marks = [
            ReadingMarkEntry(kind: "meter", bar: 2, last: 2, value: "5/4", text: ""), ReadingMarkEntry(kind: "badlyRead", bar: 8, last: 10, text: ""),
        ]

        let pages = try XCTUnwrap(MarkedPages.of(marks, starts: starts, read: [1, 3, 4], pdfPages: 4))

        XCTAssertEqual(pages, [
            ScanMarkedPages.Page(place: 1, pdf: 1), ScanMarkedPages.Page(place: 2, pdf: 3, bad: [8]), ScanMarkedPages.Page(place: 3, pdf: 4, bad: [9, 10]),
        ], "a mark across two pages reads both; each PDF page as the notes have it")
        XCTAssertEqual(MarkedPages.of(Array(marks.prefix(1)), starts: starts, read: [], pdfPages: 3)?.map(\.pdf), [1], "without the pages kept, the same numbers")
        XCTAssertNil(MarkedPages.of(marks, starts: starts, read: [1, 2], pdfPages: 4), "the notes' pages and the PDF's disagree: none")
        // A second reading names no pages read: a PDF of another number of pages than the notes could be read on the wrong page.
        XCTAssertNil(MarkedPages.of(marks, starts: starts, read: [], pdfPages: 4))
        XCTAssertEqual(MarkedPages.of([], starts: starts, read: [], pdfPages: 3), [], "no mark, no page")
        // E-scan-19: a page the vector reader reads stays here unless one of its bars is marked as read badly.
        let split = ScanMarkedPages.split(pages, device: [1: "<a/>", 3: "<b/>"])
        XCTAssertEqual(split.device.map(\.place), [1])
        XCTAssertEqual(split.server.map(\.place), [2, 3])
    }

    func testTheWorkingNotesMusicIsReadFromTheProjectFile() {
        let music = Data("<score-partwise/>".utf8).base64EncodedString()
        let file = #"{"workspace": {"working": "n_2"}, "notes": [{"id": "n_1", "music": ""}, {"id": "n_2", "music": "\#(music)"}]}"#
        XCTAssertEqual(MarkedPages.workingMusic(file), "<score-partwise/>")
        XCTAssertNil(MarkedPages.workingMusic(#"{"notes": [{"id": "n_1", "music": "\#(music)"}]}"#), "no working notes named")
    }

    func testOnlyTheMarkedPagesGoToTheServerWithTheirBarsReadBadly() async throws {
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#), "http://scan.test/jobs/j1": done,
                       "http://scan.test/jobs/j1/result": ScanSupport.result("j1", [(3, "E")])])
        var kept: [ScanJobRecord] = []
        let follow = RecognitionJob(kept: nil, keep: { kept.append($0) }, hear: { _ in }, wanted: { true }, ready: { true })

        let plan = await ScanMarkedPages.plan([ScanMarkedPages.Page(place: 2, pdf: 3, bad: [5])], pdf: notAPdf, pdfPages: 3)
        XCTAssertEqual(plan.server.map(\.pdf), [3], "a PDF the vector reader cannot open goes to the server")
        let outcome = await ScanMarkedPages.read(plan, pdf: notAPdf, to: .success(target), job: follow, text: english)

        guard case .success(let read) = outcome else { return XCTFail("read: \(outcome)") }
        let form = String(decoding: StubAPI.bodies[StubAPI.requests.firstIndex { $0.httpMethod == "POST" } ?? 0], as: UTF8.self)
        XCTAssertTrue(form.contains("name=\"pages\"\r\n\r\n3\r\n"), "only the marked page")
        XCTAssertEqual(kept, [ScanJobRecord(id: "j1", pages: [3])], "the job, for the caller to delete")
        XCTAssertEqual(read.replace.count, 1)
        XCTAssertEqual(read.replace[0]["page"] as? Int, 2, "its place among the notes' pages")
        XCTAssertEqual(read.replace[0]["bad"] as? [Int], [5])
        XCTAssertEqual((read.replace[0]["readings"] as? [[String: String]])?.first?["engine"], "homr")
        XCTAssertEqual(read.engines, ["homr 0.7.0"])
    }

    func testAMarkedPageNoEngineReadsAgainFailsTheWholeReadingAndAServerNeededIsAskedFirst() async throws {
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#), "http://scan.test/jobs/j1": done,
                       "http://scan.test/jobs/j1/result": ScanSupport.result("j1", [(1, "C"), (2, nil)])])
        let pages = [ScanMarkedPages.Page(place: 1, pdf: 1), ScanMarkedPages.Page(place: 2, pdf: 2)]

        let plan = await ScanMarkedPages.plan(pages, pdf: notAPdf, pdfPages: 2)
        let outcome = await ScanMarkedPages.read(plan, pdf: notAPdf, to: .success(target), job: job(), text: russian)

        // E-scan-18: page 1 read alone would keep page 2's old reading beside it; nothing goes in.
        guard case .failure(let problem) = outcome else { return XCTFail("read: \(outcome)") }
        XCTAssertEqual(problem, .server(russian.format("scanMarked.notRead", "2")))
        // E-scan-19: a page that has to go to the server, and no server that may read it: why, and nothing sent.
        StubAPI.reset()
        let refused = await ScanMarkedPages.read(plan, pdf: notAPdf, to: .failure(.server("no server")), job: job(), text: english)
        guard case .failure(let why) = refused else { return XCTFail("read: \(refused)") }
        XCTAssertEqual(why, .server("no server"))
        XCTAssertEqual(StubAPI.requests.count, 0)
    }

    func testAPdfTheVectorReaderReadsIsReadAgainOnThisIPadWithNoServer() async throws {
        StubAPI.reset()
        let engraved = try Data(contentsOf: ScanSupport.promenade)

        let plan = await ScanMarkedPages.plan([ScanMarkedPages.Page(place: 2, pdf: 2)], pdf: engraved, pdfPages: 2)
        XCTAssertEqual(plan.server, [], "no page leaves the iPad, so no server is named before the press")
        XCTAssertEqual(plan.hereLine(english), english.count("scanJob.readHere", 1))
        XCTAssertEqual(plan.hereLine(russian), russian.count("scanJob.readHere", 1))
        let outcome = await ScanMarkedPages.read(plan, pdf: engraved, to: .failure(.needsPro), job: job(), text: english)

        guard case .success(let read) = outcome else { return XCTFail("read: \(outcome)") }
        XCTAssertEqual(StubAPI.requests.count, 0, "nothing leaves the iPad, and no Pro is needed")
        XCTAssertEqual(read.replace.first?["page"] as? Int, 2)
        XCTAssertNotNil(read.replace.first?["musicxml"] as? String)
        XCTAssertTrue(read.engines.first?.hasPrefix("vector") == true, "\(read.engines)")
        XCTAssertEqual(read.pdfPages, 2)
    }

    func testAMarkOnNotesReadFromAPdfIsKeptFlaggedShownAndTakenAway() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("marks-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = try ScanSupport.pdfPiece(in: folder)
        StubAPI.reset(["http://scan.test/health": jobServer, "POST http://scan.test/jobs": ScanSupport.answer(202, #"{"id": "j1"}"#), "http://scan.test/jobs/j1": done,
                       "http://scan.test/jobs/j1/result": ScanSupport.result("j1", [(1, "C"), (2, "E")]), "DELETE http://scan.test/jobs/j1": ScanSupport.answer(204, "")])
        guard case .success = await RecognitionQueue.recognise(url, address: server, account: Account(options: nil), links: folder, folders: folder, job: job(), session: StubAPI.session(), isOpen: { false }) else {
            return XCTFail("the notes read from the PDF")
        }
        let piece = try await OpenPiece.open(fileName: url.lastPathComponent, data: Data(contentsOf: url), language: "en", title: "Pages").get()
        opened.append(piece.id)
        let fake = FakeCheckScore()
        fake.pieceId = piece.id
        let model = CheckModel(selection: SelectionModel()) { false }
        model.score = fake
        await model.load(in: piece.id)
        XCTAssertTrue(model.check.takesMarks, "notes read from a PDF")
        let second = try XCTUnwrap(model.check.pageStarts.last, "two pages")

        model.mark(ReadingMarkChoice(kind: "badlyRead"), first: second.bar, last: second.bar, told: true)
        try await ScanSupport.wait("the mark kept and told") { !fake.notices.isEmpty }

        XCTAssertEqual(model.check.marks.map(\.kind), ["badlyRead"])
        XCTAssertEqual(fake.notices, ["whatsWrong.markedNotice"], "made from the selection menu, the pill says so")
        let flag = try XCTUnwrap(model.flags(english).first { $0["bar"] as? Int == second.bar }, "flagged like a Check flag")
        XCTAssertEqual(flag["tick"] as? Double, second.tick)
        let words = model.check.marks[0].text
        XCTAssertTrue((flag["label"] as? String)?.hasSuffix(words) == true, "in music-core's words: \(flag)")
        model.showMark(model.check.marks[0])
        XCTAssertEqual(fake.selected, [second.bar...second.bar], "its bar selected, as the roll selects bars")
        if model.check.flags.allSatisfy({ $0.bar != second.bar }) && model.check.lengths.allSatisfy({ $0.bar != second.bar }) {
            model.showCheckBar(second.bar)
            XCTAssertEqual(fake.selected.count, 2, "its flag tapped, with no other flag there, selects it the same way")
        }
        XCTAssertEqual(MarkedPages.of(model.check.marks, starts: model.check.pageStarts, read: model.check.readPages, pdfPages: 2), [ScanMarkedPages.Page(place: 2, pdf: 2, bad: [second.bar])], "only the marked page")

        model.unmark(model.check.marks[0])
        try await ScanSupport.wait("the mark taken away") { model.check.marks.isEmpty }
        XCTAssertFalse(model.flags(english).contains { ($0["label"] as? String)?.contains(words) == true }, "its flag goes")
        XCTAssertEqual(fake.problems, [])
    }

    func testABarReadAgainShowsItsCropAndReadingsAsOneTheEnginesDisagreeOn() throws {
        let answer = #"{"flags": [{"bar": 2, "reason": "readAgain", "name": "Read by another engine", "text": "", "resolved": false, "span": {"onset": 1920, "end": 3840}}, {"bar": 3, "reason": "barTooLong", "name": "Bar too long", "text": "", "resolved": false, "span": {"onset": 3840, "end": 5760}}], "open": 2}"#
        let shelf = try XCTUnwrap(CheckShelf(answer: answer))

        XCTAssertEqual(shelf.openBars.first?.flags.first?.reason, CheckShelf.readAgain)
        XCTAssertEqual(shelf.openBars.map(\.reviewed), [true, false], "a bar read again, so the earlier reading can be taken back; not a bar too long")
        XCTAssertTrue(CheckShelf.reviewed.contains(CheckShelf.enginesDisagree), "as before")
    }

    func testOneRecognitionAtATimeSourcesAndTheMarkedReadingWaitForEachOther() async {
        let henle = SourceLayers.Original(
            id: "src_a", kind: "pdf", shown: "Henle", fileName: "Henle.pdf", state: "here", removedAt: nil, kept: "file",
            pages: 1, durationMs: nil, hidden: false, notes: [], pageMarks: 0
        )
        var layers = SourceLayers()
        layers.originals = [henle]
        let sources = SourcesController(layers: layers)
        let waits = UIText.current.format("sources.sheet.readingOther", "Henle")
        StubAPI.reset()
        var during: String?
        var sent: String?

        let why = await sources.setReading(henle) {
            during = sources.reading
            sent = await sources.recognise(henle, address: server, account: Account(options: nil))
            return nil
        }

        XCTAssertNil(why)
        XCTAssertEqual(during, "src_a", "the one owner of which original is with the scan server")
        XCTAssertEqual(sent, waits, "Sources' «Распознать» waits, of the same PDF too")
        XCTAssertNil(sources.reading, "given back after the run")
        // A recognition in Sources goes on: the marked reading waits for it and sends nothing.
        let reading = MarkedReading()
        let pages = ScanMarkedPages.Plan(device: [], server: [ScanMarkedPages.Page(place: 1, pdf: 1, bad: [1])], vector: ScanJobPlan(), pdfPages: 1)
        _ = await sources.setReading(henle) {
            reading.start(MarkedPlan(original: henle, pdf: notAPdf, pages: pages), check: CheckShelf(), sources: sources, target: .success(target), text: english)
            await until { !reading.busy }
            return nil
        }
        XCTAssertEqual(reading.problem, waits)
        XCTAssertEqual(StubAPI.requests.count, 0, "nothing was sent, no quota charged twice")
    }

    func testReadingAgainWithTheMarksKeepsNewNotesBesideTheWorkingOnesAndToldWhatBecameOfTheMarks() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("marked-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = try ScanSupport.pdfPiece(in: folder, pdf: ScanSupport.scannedPdf(pages: 2))
        StubAPI.reset(jobAnswers("j1", [(1, "C"), (2, "E")]))
        guard case .success = await RecognitionQueue.recognise(url, address: server, account: Account(options: nil), links: folder, folders: folder, job: job(), session: StubAPI.session(), isOpen: { false }) else {
            return XCTFail("the notes read from the PDF by a job")
        }
        let piece = try await OpenPiece.open(fileName: url.lastPathComponent, data: Data(contentsOf: url), language: "en", title: "Pages").get()
        opened.append(piece.id)
        let sources = SourcesController()
        await sources.load(piece: piece.id, file: url)
        let working = try XCTUnwrap(sources.layers.working, "the notes read")
        let fake = FakeCheckScore()
        fake.pieceId = piece.id
        let model = CheckModel(selection: SelectionModel()) { false }
        model.score = fake
        await model.load(in: piece.id)
        let second = try XCTUnwrap(model.check.pageStarts.last, "two pages")
        model.mark(ReadingMarkChoice(kind: "badlyRead"), first: second.bar, last: second.bar)
        try await ScanSupport.wait("the mark kept") { !model.check.marks.isEmpty }
        let reading = MarkedReading()
        let planned = await reading.plan(model.check, sources: sources, text: english)
        guard case .success(let plan) = planned else { return XCTFail("the plan: \(planned)") }
        XCTAssertEqual(plan.pages.server.map(\.place), [2], "only the marked page, its bar read badly by the server's engines")
        StubAPI.reset(jobAnswers("j2", [(2, "G")]))

        reading.start(plan, check: model.check, sources: sources, target: .success(target), text: english)
        try await ScanSupport.wait("read again") { !reading.busy }

        XCTAssertNil(reading.problem)
        XCTAssertNil(sources.reading, "given back")
        let form = String(decoding: StubAPI.bodies[StubAPI.requests.firstIndex { $0.httpMethod == "POST" } ?? 0], as: UTF8.self)
        XCTAssertTrue(form.contains("name=\"pages\"\r\n\r\n2\r\n"), "the whole file, with only the marked page to read")
        let fresh = try XCTUnwrap(sources.fresh, "new Notes, by the id music-core gave them")
        XCTAssertNotEqual(fresh, working)
        XCTAssertEqual(sources.layers.working, working, "the working notes stay as they are")
        XCTAssertEqual(sources.layers.original(plan.original.id)?.notes.contains(fresh), true, "under the PDF")
        let toast = try XCTUnwrap(sources.toast?.text)
        XCTAssertTrue(toast.hasPrefix(english.count("markedReading.toast", 1) + " · "), "music-core's line of the marks follows: \(toast)")
        XCTAssertTrue(toast.contains("used"), "what became of the mark: \(toast)")
        try await ScanSupport.wait("the job deleted on the server") { ScanSupport.sent("DELETE", "jobs/j2") == 1 }
    }
}
