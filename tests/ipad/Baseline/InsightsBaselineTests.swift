import XCTest

@testable import MotiffLab

/// What 1.6 added at the top of Insights (docs/ux-baseline.md INS-01 and PHR-08; the UX concept's Insights stream and the
/// lead's decision 38 of the plan of 1.6, v1.6 i-18, #318): the strip «Идеи» of the ideas kept on the passage, each on the phrase
/// of its anchor, read again when the anchors change with the sheet closed, as the screen's reaction on the phrases'
/// anchors does (`WorkspaceSession` runs it as the view does); and the words of the line «Границы фраз изменились» with
/// «Привязать заново». Taken through the score's workspace over the real music-core and the kit's piece opened in Russian.
/// What the stream draws, and an anchor that matches no phrase, are the device's.
@MainActor
final class InsightsBaselineTests: XCTestCase {
    private var opened: [Int32] = []
    private let russian = UIText(language: "ru")

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// An idea kept on `unit` as «Оставить» keeps one, which music-core attaches to the phrase's anchor: the id of the one
    /// its answer adds to `kept`.
    private func keep(on unit: PhraseUnit, of piece: Int32, besides kept: [String]) throws -> String {
        let span = #"{"span": {"onset": \#(Int(unit.onset)), "end": \#(Int(unit.end))}}"#
        _ = MusicCore.call(piece: piece, "select", span)
        let answer = MusicCore.call(piece: piece, "idea.keep", #"{"name": "Мотив", "note": "здесь", "now": "2026-11-27T12:00:00Z"}"#)
        let ideas = FacadeAnswer.decode(IdeasAnswer.self, answer, of: "idea.keep")?.ideas ?? []
        return try XCTUnwrap(ideas.first { !kept.contains($0.id) && $0.anchor != nil }?.id, answer)
    }

    /// INS-01: ideas kept on phrases 2 and 3, Insights closed; phrase 3 joins phrase 2 (its boundary removed), which keeps
    /// one anchor: the ideas are read again at once, the sheet still closed, so the strip shows both on the joined phrase
    /// when Insights opens on it, «фраза 2», under «Идеи».
    func testINS01TheIdeasStripShowsEachIdeaOnThePhraseOfItsAnchorReadAgainWhenTheAnchorsChange() async throws {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "ru", title: example.title).get()
        opened.append(piece.id)
        let session = await WorkspaceSession(piece: piece)
        let workspace = session.workspace
        let score = workspace.score
        let insights = workspace.insights
        await session.step("the screen opens, the phrases read") {
            workspace.wire()
            await score.selectionModel.loadPhrases(of: piece.id)
        }
        let second = try XCTUnwrap(score.selectionModel.phrases?.unit(2), "phrase 2 of the kit's piece")
        let third = try XCTUnwrap(score.selectionModel.phrases?.unit(3), "phrase 3 of the kit's piece")
        var ideas: [String] = []
        await session.step("an idea kept on phrase 2 and one on phrase 3, the phrases read again") {
            for unit in [second, third] { ideas.append((try? keep(on: unit, of: piece.id, besides: ideas)) ?? "") }
            await score.selectionModel.loadPhrases(of: piece.id)
        }
        XCTAssertFalse(ideas.contains(""), "both kept on an anchor")
        await session.until("the anchors' reaction read the ideas") { Set(insights.ideas.map(\.id)) == Set(ideas) }
        XCTAssertFalse(workspace.panels[.insights], "the sheet is closed")

        await session.step("phrase 3 joins phrase 2") { score.removeBoundary(third) }
        await session.until("one anchor kept") { score.selectionModel.phrases?.anchors.count == 1 }
        await session.until("the ideas read again on it, the sheet closed") { Set(insights.ideas.map(\.anchor)).count == 1 }

        let joined = try XCTUnwrap(score.selectionModel.phrases?.unit(2), "the joined phrase")
        await session.step("phrase 2 selected, Insights opened") {
            await score.select(joined.selectableOnset, joined.selectableEnd)
            workspace.change(.open(.insights))
        }
        await session.until("the stream read") { !insights.ideasShown(on: score.selectionModel.phrases).isEmpty }
        let shown = insights.ideasShown(on: score.selectionModel.phrases)
        XCTAssertEqual(shown.map(\.idea.id).sorted(), ideas.sorted(), "both on the passage")
        XCTAssertEqual(shown.map { $0.phrase?.number }, [2, 2], "each named by its anchor's phrase")
        XCTAssertEqual(russian["insights.ideas"], "Идеи")
        XCTAssertEqual(russian.format("insights.ideaPhrase", "2"), "фраза 2")
    }

    /// PHR-08: the line of the stream when anchors match no phrase, and its button, in Russian; what «Привязать заново»
    /// answers in its toast, the count and none.
    func testPHR08TheLineOfPhrasesThatChangedOffersToAttachAgainAndSaysHowMany() {
        XCTAssertEqual(russian["insights.phrasesChanged"], "Границы фраз изменились: часть идей и мест продолжения осталась без своей фразы.")
        XCTAssertEqual(russian["insights.reattach"], "Привязать заново")
        XCTAssertEqual(russian.count("phrases.reattached", 2), "Привязано заново: 2 фразы")
        XCTAssertEqual(russian["phrases.reattachedNone"], "Привязать не к чему: на этих местах фраз больше нет")
    }
}
