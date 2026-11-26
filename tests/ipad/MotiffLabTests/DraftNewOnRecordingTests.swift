import XCTest

@testable import MotiffLab

/// The card «Новый черновик» with «С ассистентом · Pro» (v1.5 d-11, DRAFT-11, the lead's option «а»): the passage is
/// searched on the piece's recordings before the draft is made (`drafts.findNewOnRecording`,
/// tests/contracts/facade-answers/drafts.recording.new), the card says where, how much and how many приёмы, and only
/// then offers the assistant, which makes the draft with that stretch as ①'s variant and opens the assistant there.
@MainActor
final class DraftNewOnRecordingTests: XCTestCase {
    private var numbers: [Int32] = []
    private var folders: [URL] = []

    override func tearDown() async throws {
        for number in numbers { MusicCore.close(piece: number) }
        numbers = []
        for folder in folders { try? FileManager.default.removeItem(at: folder) }
        folders = []
    }

    /// What music-core found decodes into the card's offer: the stretch to put in, its line in both languages with its
    /// label, share and приёмы; the draft made with it answers as the sources, naming the variant put in.
    func testTheCardSaysWhereThePassageIsOnTheRecording() throws {
        let answers = try FacadeAnswerFixturesTests.answers("drafts.recording.new")
        let found = try XCTUnwrap(FacadeAnswer.decode(DraftNewRecordingAnswer.self, answers[0].text, of: "drafts.findNewOnRecording"))
        XCTAssertEqual(found.recordings, 1)
        let offer = DraftRecordingOffer(found: found.found)
        let stretch = try XCTUnwrap(offer.adds, "found: «С ассистентом · Pro» is offered")
        XCTAssertEqual(stretch.notes, "n_2")
        XCTAssertEqual(stretch.techniques, 0)
        XCTAssertEqual(offer.newDraftLine(UIText(language: "en")), "On the recording: “Recording 0–1” · 86 % the same · 0 techniques")
        XCTAssertEqual(offer.newDraftLine(UIText(language: "ru")), "На записи: «Recording 0–1» · совпадает на 86 % · 0 приёмов")
        XCTAssertNotNil(SourceLayers(answer: answers[1].text), "made: answered as the sources")
        XCTAssertEqual(facadeObject(answers[1].text)?["recording"] as? String, "v_0002", "the stretch is ①'s variant")

        // A place's search still decodes with no "techniques".
        let place = try FacadeAnswerFixturesTests.answers("drafts.recording")
        XCTAssertNil(try XCTUnwrap(FacadeAnswer.decode(DraftRecordingAnswer.self, place[0].text, of: "drafts.findOnRecording")).found.first?.techniques)
    }

    /// The card's line while it searches, when nothing is found and when the search failed, in both languages; nothing
    /// before the search and with no recording to search; only a stretch found offers the assistant.
    func testTheCardsLineForEachStateOfTheSearch() {
        for language in ["en", "ru"] {
            let text = UIText(language: language)
            XCTAssertNil(DraftRecordingOffer.ready.newDraftLine(text))
            XCTAssertNil(DraftRecordingOffer.noRecording.newDraftLine(text), "no recording: no line, no search")
            XCTAssertEqual(DraftRecordingOffer.searching.newDraftLine(text), text["drafts.recordingSearching"])
            XCTAssertEqual(DraftRecordingOffer(found: []).newDraftLine(text), text["drafts.newNotFound"])
            XCTAssertEqual(DraftRecordingOffer(found: nil).newDraftLine(text), text["drafts.recordingFailed"], "no answer: not «no recording has it»")
            XCTAssertEqual(DraftRecordingOffer(found: nil, refused: "Select a passage first.").newDraftLine(text), "Select a passage first.")
            for offer in [DraftRecordingOffer.searching, .notFound, .failed(nil)] {
                XCTAssertNil(offer.adds, "\(language): \(offer) offers no assistant")
                XCTAssertFalse(offer.newDraftLine(text)?.hasPrefix("drafts.") ?? true, "\(language): \(offer)")
            }
        }
    }

    /// A piece with no recording heard: nothing is searched; a draft asked for with a stretch anyway is refused in
    /// music-core's words, and no draft is made.
    func testNoRecordingNoSearchAndNoDraftWithOne() async throws {
        let sources = try await piece()
        XCTAssertFalse(sources.hasHeardNotes)
        let offer = await sources.findNewOnRecording(span: 0..<1920, part: "all", from: sources.piece)
        XCTAssertEqual(offer, .noRecording)
        XCTAssertNil(offer.newDraftLine(sources.text))

        let stray = #"{"recordings": 1, "found": [{"notes": "n_9", "label": "Recording 1–4", "percent": 80, "variant": null, "techniques": 2}]}"#
        let gone = try XCTUnwrap(FacadeAnswer.decode(DraftNewRecordingAnswer.self, stray, of: "drafts.findNewOnRecording")?.found.first)
        let new = DraftChoice(part: "all", into: .new, way: DraftChoice.Way(drop: .place))
        let made = await sources.createWithRecording(new, found: gone, span: 0..<1920, from: sources.piece)
        XCTAssertNil(made)
        XCTAssertEqual(sources.status, "The recording has no stretch of the music selected.")
        XCTAssertEqual(sources.layers.drafts.count, 0, "no draft made")
        XCTAssertFalse(sources.stepping)
    }

    /// A second tap of «Создать» while the first is on its way makes no second draft and is not told as refused.
    func testASecondCreateWhileTheFirstIsOnItsWayMakesNoSecondDraft() async throws {
        let sources = try await piece()
        let new = DraftChoice(part: "all", into: .new, way: DraftChoice.Way(drop: .place))
        let before = sources.status
        let first = Task { await sources.addToDraft(new, span: 0..<1920, from: sources.piece) }
        for _ in 0..<1000 where !sources.stepping { await Task.yield() }
        let second = await sources.addToDraft(new, span: 0..<1920, from: sources.piece)
        XCTAssertFalse(second, "held while the first is on its way")
        XCTAssertEqual(sources.status, before, "not refused")
        let made = await first.value
        XCTAssertTrue(made, sources.status ?? "")
        XCTAssertEqual(sources.layers.drafts.count, 1, "one draft")
        XCTAssertFalse(sources.stepping)
    }

    /// The draft's view opened by «С ассистентом · Pro»: the assistant is open on ① in the place assembler's role, and it
    /// stays as the visit's first piece is read; a piece read after it closes it, as before. A first place of its base
    /// alone opens none.
    func testTheDraftsViewOpensWithTheAssistantOnItsFirstPlace() {
        let assistant = controller()
        let base = DraftViewAnswer.Variant(id: "v_0001", label: "Notes 1–4", base: true, steps: 0, bridge: nil, explanation: nil, anchors: nil)
        let heard = DraftViewAnswer.Variant(id: "v_0002", label: "Recording 0–1", base: false, steps: 0, bridge: nil, explanation: nil, anchors: [])
        let first = DraftViewAnswer.Place(id: "pl_0001", label: "① Notes 1–4 + Recording · 2 variants", onset: 0, end: 7680, firstBar: 1, lastBar: 4, steps: 0, bridge: false, variants: [base, heard])
        let draft = DraftViewAnswer.Draft(id: "d_0001", name: "Draft 1", places: [first], shelf: [])

        ShownDraftScreen.assist(assistant, on: draft)
        XCTAssertEqual(assistant.inDraft?.place, DraftAssistantSection.named(first, of: draft))
        XCTAssertEqual(assistant.inDraft?.chat.role, AssistantDraftPlace.role)
        assistant.load(piece: "p_draft")
        XCTAssertNotNil(assistant.inDraft, "the visit's first piece read: it stays")
        assistant.load(piece: "p_other")
        XCTAssertNil(assistant.inDraft, "another piece: it goes")

        let alone = DraftViewAnswer.Place(id: "pl_0001", label: "① Notes 1–4", onset: 0, end: 7680, firstBar: 1, lastBar: 4, steps: 0, bridge: false, variants: [base])
        let other = controller()
        ShownDraftScreen.assist(other, on: DraftViewAnswer.Draft(id: "d_0001", name: "Draft 1", places: [alone], shelf: []))
        XCTAssertNil(other.inDraft, "a place of its base alone offers no assistant")
        ShownDraftScreen.assist(other, on: nil)
        XCTAssertNil(other.inDraft)
    }

    // MARK: - Helpers

    private func controller() -> AssistantController {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("assistant-\(UUID().uuidString)")
        folders.append(folder)
        let defaults = UserDefaults(suiteName: "assistant-\(UUID().uuidString)")!
        return AssistantController(store: AssistantStore(folder: folder), defaults: defaults)
    }

    /// The kit's piece open in music-core in English, with its sources read.
    private func piece() async throws -> SourcesController {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let opened = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        numbers.append(opened.id)
        let sources = SourcesController()
        await sources.load(piece: opened.id, file: nil)
        return sources
    }
}
