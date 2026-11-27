import XCTest

@testable import MotiffLab

/// The phrase anchors on the iPad (v1.6 i-18, c-14d, #318) on the kit's piece through music-core: an Undo or Redo of a phrases
/// step sends its number, so the anchors come back exactly; «Привязать заново» attaches the anchors that match no phrase and
/// says how many; an idea shows in Insights on the phrase of its anchor, not by the bars it was kept on; a resume point in
/// phrase focus follows its phrase. Ids are read from the answers, never predicted (they are random on the iPad, c-14c).
@MainActor
final class PhraseAnchorsTests: XCTestCase {
    private var opened: [Int32] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// A piece opened from a file or the kit's piece, closed when the test ends.
    private func open(_ fileName: String? = nil, data: Data? = nil) throws -> Int32 {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let request = facadeRequest(OpenPiece.openRequest(language: "en"))
        let answer = try XCTUnwrap(facadeObject(MusicCore.open(fileName: fileName ?? example.fileName, data: data ?? example.data, request: request)))
        let piece = try XCTUnwrap((answer["piece"] as? NSNumber)?.int32Value, "\(answer)")
        opened.append(piece)
        return piece
    }

    private func model(_ piece: Int32) -> SelectionModel {
        let model = SelectionModel()
        var open = OpenPiece(id: piece, title: "Für Elise", pages: [], timemap: "")
        open.file = FileManager.default.temporaryDirectory.appendingPathComponent("anchors-\(UUID().uuidString).motiff.json")
        model.open(open)
        return model
    }

    /// A resume point open in phrase focus on `unit`, a new one each time, which music-core attaches to the phrase's anchor.
    private func resumeInFocus(on unit: PhraseUnit, of piece: Int32) {
        let request = ResumePlace.saveRequest(id: nil, passage: unit.onset..<unit.end, at: unit.onset, focused: true, tempoPercent: 100, hands: nil, loop: false, now: Date())
        _ = MusicCore.call(piece: piece, "resume.save", request)
    }

    /// The Undo of a join sends the step's number (c-14d "step"): both anchors come back, each on its own phrase, where the
    /// edits alone would leave the one kept; the restore answers the same number for the Redo, which joins them again.
    func testTheUndoOfAJoinSendsItsStepAndBringsTheSecondAnchorBack() async throws {
        let piece = try open()
        let model = model(piece)
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        let third = try XCTUnwrap(model.phrases?.unit(3))
        resumeInFocus(on: second, of: piece)
        resumeInFocus(on: third, of: piece)
        await model.loadPhrases(of: piece)
        XCTAssertEqual(model.phrases?.anchors.compactMap(\.phrase).sorted(), [2, 3], "an anchor on each phrase")

        _ = await model.editPhrases(in: piece, #"{"remove": \#(Int(third.onset))}"#, done: "removed")
        let number = try XCTUnwrap(model.phrases?.step, "the step's number")
        XCTAssertEqual(model.phraseSteps.done.last?.after?.number, number, "kept with the step")
        XCTAssertEqual(model.phrases?.anchors.count, 1, "two anchors on one phrase are one")

        let undone = await model.stepPhrases(in: piece, redo: false, done: "undone")
        XCTAssertNil(undone)
        XCTAssertEqual(model.phrases?.step, number, "the restore answers the step's number")
        XCTAssertEqual(model.phrases?.anchors.compactMap(\.phrase).sorted(), [2, 3], "the second phrase has its anchor back")
        XCTAssertEqual(model.phraseSteps.undone.last?.before?.number, number, "the Redo sends it too")

        _ = await model.stepPhrases(in: piece, redo: true, done: "redone")
        XCTAssertEqual(model.phrases?.anchors.count, 1, "Redo joins them again")
        XCTAssertEqual(model.phrases?.step, number)
    }

    /// «Привязать заново» (#318): an anchor that matches no phrase (its span changed in the file) is counted, attached again
    /// to the phrase there, and the toast says how many; a second tap while the first is on its way does nothing (E-insights-4).
    func testReattachAttachesTheAnchorsThatMatchNoPhraseAndSaysHowMany() async throws {
        let kit = try open()
        let phrases = try XCTUnwrap(PiecePhrases(answer: MusicCore.call(piece: kit, "phrases")))
        let second = try XCTUnwrap(phrases.unit(2))
        resumeInFocus(on: second, of: kit)
        var file = try XCTUnwrap(facadeObject(MusicCore.call(piece: kit, "project.save")))
        var workspace = try XCTUnwrap(file["workspace"] as? [String: Any])
        var kept = try XCTUnwrap(workspace["phrases"] as? [String: Any])
        var anchors = try XCTUnwrap(kept["anchors"] as? [[String: Any]])
        XCTAssertEqual(anchors.count, 1)
        // The anchor's span is no phrase now, as after the notes or their analysis changed.
        anchors[0]["duration"] = ((anchors[0]["duration"] as? NSNumber)?.intValue ?? 0) + 240
        kept["anchors"] = anchors
        workspace["phrases"] = kept
        file["workspace"] = workspace
        let piece = try open("anchors.motiff.json", data: Data(facadeRequest(file).utf8))
        let model = model(piece)
        await model.loadPhrases(of: piece)
        XCTAssertEqual(model.phrases?.unmatched, 1, "«Границы фраз изменились»")
        XCTAssertNil(model.phrases?.anchors.first?.phrase)

        let first = Task { await model.reattachPhrases(in: piece) { "attached \($0)" } }
        await Task.yield()
        await model.reattachPhrases(in: piece) { _ in "again" }
        await first.value
        XCTAssertEqual(model.phrases?.unmatched, 0)
        XCTAssertEqual(model.phrases?.anchors.first?.phrase, 2, "on the phrase that holds its start")
        XCTAssertEqual(model.phraseToast?.words, "attached 1", "how many, once")
        XCTAssertNil(model.phraseToast?.offer, "no Undo: no boundary moved")
    }

    /// An idea shows in the stream on the phrase of its anchor (#318), not by the bars it was kept on: kept on phrase 2, whose
    /// start then moves later, it shows with phrase 2 and not with phrase 1, which now holds those bars' start.
    func testAnIdeaIsShownOnThePhraseOfItsAnchorNotByItsBars() async throws {
        let piece = try open()
        let model = model(piece)
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        _ = MusicCore.call(piece: piece, "select", #"{"span": {"onset": \#(Int(second.onset)), "end": \#(Int(second.end))}}"#)
        let kept = MusicCore.call(piece: piece, "idea.keep", #"{"name": "Idea", "note": "here", "now": "2026-11-27T12:00:00Z"}"#)
        let idea = try XCTUnwrap(FacadeAnswer.decode(IdeasAnswer.self, kept, of: "idea.keep")?.ideas.first)
        XCTAssertNotNil(idea.anchor, "attached to its phrase's anchor")

        let later = Int(second.onset + (second.end - second.onset) / 2)
        _ = await model.editPhrases(in: piece, #"{"move": {"from": \#(Int(second.onset)), "to": \#(later)}}"#, done: "moved")
        let phrases = try XCTUnwrap(model.phrases)
        let first = try XCTUnwrap(phrases.unit(1))
        let moved = try XCTUnwrap(phrases.unit(2))
        XCTAssertGreaterThan(moved.onset, second.onset, "the start moved later")
        XCTAssertEqual(phrases.unit(anchor: idea.anchor)?.number, 2, "the anchor went with its phrase")

        let insights = InsightsController()
        await insights.load(piece: piece, selection: ScoreSelection(onset: first.onset, end: first.end, firstBar: first.firstBar, lastBar: first.lastBar))
        XCTAssertEqual(insights.ideas.map(\.id), [idea.id])
        XCTAssertTrue(insights.ideasShown(on: phrases).isEmpty, "not on phrase 1, though its bars start there")
        await insights.load(piece: piece, selection: ScoreSelection(onset: moved.onset, end: moved.end, firstBar: moved.firstBar, lastBar: moved.lastBar))
        let shown = insights.ideasShown(on: phrases)
        XCTAssertEqual(shown.map(\.idea.id), [idea.id])
        XCTAssertEqual(shown.first?.phrase?.number, 2, "named by its anchor's phrase")
    }

    /// A resume point in phrase focus follows its phrase when a boundary moves (c-14d), read from `resume` as before.
    func testAResumePointInFocusFollowsItsPhrase() async throws {
        let piece = try open()
        let model = model(piece)
        await model.loadPhrases(of: piece)
        let second = try XCTUnwrap(model.phrases?.unit(2))
        resumeInFocus(on: second, of: piece)
        let earlier = Int(second.onset) - 240
        _ = await model.editPhrases(in: piece, #"{"move": {"from": \#(Int(second.onset)), "to": \#(earlier)}}"#, done: "moved")
        let moved = try XCTUnwrap(model.phrases?.unit(2))
        XCTAssertNotEqual(moved.onset, second.onset)
        let place = try XCTUnwrap(ResumePlace(answer: MusicCore.call(piece: piece, "resume")))
        XCTAssertEqual(place.passage, moved.selectableOnset..<moved.selectableEnd, "on the phrase's new bounds, as a tap selects it")
        XCTAssertTrue(place.focused)
    }
}
