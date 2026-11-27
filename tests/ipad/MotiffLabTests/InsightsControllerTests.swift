import XCTest

@testable import MotiffLab

/// InsightsController (W2.12, v1.6 i-12) on the kit's piece through music-core: the stream's rules that moved from its
/// view into the controller, rule for rule (the filter chips, the lenses under Details folded and opened, the trial kept
/// in reach when its fact is filtered out), each written only by the controller's methods; and the robustness rules of
/// v1.6 i-12 (docs/ui-edge-cases.md, E-insights-1 to 3).
@MainActor
final class InsightsControllerTests: XCTestCase {
    private var opened: [Int32] = []

    override func tearDown() async throws {
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// The kit's piece opened with bars 4–12 selected in music-core.
    private func opened4to12() throws -> (piece: Int32, selection: ScoreSelection) {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let answer = try XCTUnwrap(facadeObject(MusicCore.open(fileName: example.fileName, data: example.data, request: #"{"language": "en", "id": "p_insights"}"#)))
        let piece = Int32(answer["piece"] as? Int ?? 0)
        XCTAssertGreaterThan(piece, 0, "\(answer)")
        opened.append(piece)
        let selection = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 4, "last": 12}}"#)))
        return (piece, selection)
    }

    /// The kit's piece with bars 4–12 selected in music-core, and the stream read for them.
    private func loaded() async throws -> (insights: InsightsController, piece: Int32) {
        let (piece, selection) = try opened4to12()
        let insights = InsightsController()
        await insights.load(piece: piece, selection: selection)
        XCTAssertNotNil(insights.understanding, "music-core understood bars 4–12")
        return (insights, piece)
    }

    func testAFilterChipNarrowsTheStreamToItsLensAndAllOrTheChipAgainListsEvery() async throws {
        let (insights, _) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        XCTAssertGreaterThan(understanding.lensesShown.count, 1, "the chips show")
        XCTAssertEqual(insights.mainShown, understanding.mainFacts)
        let lens = try XCTUnwrap(understanding.lensesShown.first)

        insights.filter(lens)
        XCTAssertEqual(insights.lens, lens)
        XCTAssertTrue(insights.mainShown.allSatisfy { $0.lens == lens }, "Main shows only the lens's facts")
        XCTAssertTrue(insights.detailsShown.allSatisfy { $0 == lens }, "Details only the lens")
        insights.filter(lens)
        XCTAssertNil(insights.lens, "the chip pressed again lists every lens")
        insights.filter(lens)
        insights.filter(nil)
        XCTAssertNil(insights.lens, "All lists every lens")
        XCTAssertEqual(insights.detailsShown, understanding.lensesShown.filter { !understanding.facts(of: $0).isEmpty })
    }

    func testALensUnderDetailsOpensFoldedOpenFilteredToOrHoldingTheFactChosen() async throws {
        let (insights, _) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        let lens = try XCTUnwrap(insights.detailsShown.first)
        let fact = try XCTUnwrap(understanding.facts(of: lens).first)
        XCTAssertFalse(insights.opens(lens), "folded to its name and count")

        insights.fold(lens)
        XCTAssertTrue(insights.opens(lens))
        insights.fold(lens)
        XCTAssertFalse(insights.opens(lens), "folded back")
        insights.filter(lens)
        XCTAssertTrue(insights.opens(lens), "the lens filtered to is open")
        insights.filter(nil)
        insights.choose(fact)
        XCTAssertTrue(insights.opens(lens), "the fact chosen on the score does not disappear")
        insights.clearChoice()
        XCTAssertFalse(insights.opens(lens))
    }

    func testAWayTriedStaysInReachWhenItsFactIsFilteredOut() async throws {
        let (insights, _) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        let fact = try XCTUnwrap(understanding.facts.first { !$0.tries.isEmpty })
        let other = try XCTUnwrap(understanding.lensesShown.first { $0 != fact.lens })
        XCTAssertFalse(insights.trialFilteredOut, "nothing tried")

        let way = try XCTUnwrap(fact.tries.first)
        await insights.tryWay(way, of: fact)
        XCTAssertEqual(insights.trial?.factId, fact.id, "\(insights.problem ?? "")")
        XCTAssertFalse(insights.trialFilteredOut)
        insights.filter(other)
        XCTAssertTrue(insights.trialFilteredOut, "its fact is filtered out: the trial stays at the top")
        insights.filter(nil)
        XCTAssertEqual(insights.opens(fact.lens), !understanding.main.contains(fact.id), "its lens opens when Main does not hold it")
        await insights.leave()
        XCTAssertNil(insights.trial)
        XCTAssertFalse(insights.trialFilteredOut)
    }

    /// E-insights-1: a step that fails says why while the facts are shown, and the next step takes the words away.
    func testAStepThatFailsSaysWhyUntilTheNextStep() async throws {
        let (insights, _) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        let fact = try XCTUnwrap(understanding.facts.first { InsightsController.opensCircle($0) }, "the harmony opens the circle")

        await insights.tryWay(Fact.Try(id: "no.such.way", label: "?"), of: fact)
        XCTAssertNil(insights.trial)
        XCTAssertNotNil(insights.problem, "music-core's refusal is kept to show")
        XCTAssertNotNil(insights.understanding, "over the facts, which stay")
        await insights.toggleCircle(fact)
        XCTAssertNotNil(insights.circle)
        XCTAssertNil(insights.problem, "the next step takes the last one's words away")
    }

    /// E-insights-2: a way that music-core is still making when the selection moves on is not tried on the next one: no
    /// trial, nothing handed to the player, and music-core holds no variation.
    func testAWayStillBeingMadeWhenTheSelectionMovesOnIsLeft() async throws {
        let (insights, piece) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        let fact = try XCTUnwrap(understanding.facts.first { !$0.tries.isEmpty })
        let way = try XCTUnwrap(fact.tries.first)
        var handed = 0
        insights.variationReady = { _, _ in handed += 1 }

        let trying = Task { await insights.tryWay(way, of: fact) }
        _ = await insights.$starting.values.first { $0 == way.id }
        XCTAssertEqual(insights.starting, way.id, "the way is under way in music-core")
        await insights.load(piece: piece, selection: nil)
        await trying.value
        XCTAssertNil(insights.trial)
        XCTAssertNil(insights.problem)
        XCTAssertEqual(handed, 0, "nothing sounds")
        XCTAssertNotNil(facadeObject(MusicCore.call(piece: piece, "variation"))?["error"], "music-core forgot the variation")
    }

    /// E-insights-3: Keep tapped again while the first tap is on its way keeps one idea.
    func testKeepTappedTwiceKeepsOneIdea() async throws {
        let (insights, piece) = try await loaded()
        let understanding = try XCTUnwrap(insights.understanding)
        let fact = try XCTUnwrap(understanding.facts.first { !$0.tries.isEmpty })
        let way = try XCTUnwrap(fact.tries.first)
        await insights.tryWay(way, of: fact)
        XCTAssertNotNil(insights.trial, "\(insights.problem ?? "")")

        let first = Task { await insights.keep() }
        await Task.yield()
        await insights.keep()
        await first.value
        XCTAssertEqual(insights.trial?.kept, true)
        let ideas = facadeObject(MusicCore.call(piece: piece, "ideas"))?["ideas"] as? [[String: Any]]
        XCTAssertEqual(ideas?.count, 1, "one idea for two taps")
    }

    /// E-insights-8: read for another selection (the sheet reopened on it, or the selection moved), the stream shows it
    /// reads it, never the facts of the passage before, whose Try and places would act on the new one.
    func testAnotherSelectionShowsItLoadsRatherThanThePassageBefore() async throws {
        let (insights, piece) = try await loaded()
        let other = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 1, "last": 2}}"#)))

        let reading = Task { await insights.load(piece: piece, selection: other) }
        _ = await insights.$loading.values.first { $0 }
        XCTAssertNil(insights.understanding, "the facts of bars 4–12 are gone while bars 1–2 are read")
        await reading.value
        XCTAssertFalse(insights.loading)
        XCTAssertNotNil(insights.understanding, "bars 1–2 understood")
    }

    /// E-insights-8 with a way tried: the stream reads the other selection from its first step, while the trial is left, so
    /// a second load for it meanwhile (the sheet reopened) passes by rather than leaving and reading again.
    func testAnotherSelectionWhileAWayIsTriedShowsItLoadsFromItsFirstStep() async throws {
        let (insights, piece) = try await loaded()
        let fact = try XCTUnwrap(insights.understanding?.facts.first { !$0.tries.isEmpty })
        await insights.tryWay(try XCTUnwrap(fact.tries.first), of: fact)
        XCTAssertNotNil(insights.trial, "\(insights.problem ?? "")")
        let other = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece, "select", #"{"bars": {"first": 1, "last": 2}}"#)))

        let reading = Task { await insights.load(piece: piece, selection: other) }
        _ = await insights.$understanding.values.first { $0 == nil }
        XCTAssertTrue(insights.loading, "the trial is being left and the stream already reads bars 1–2")
        await reading.value
        XCTAssertNil(insights.trial)
        XCTAssertFalse(insights.loading)
        XCTAssertNotNil(insights.understanding, "bars 1–2 understood")
    }

    /// E-insights-2 with the sheet closed: another selection while a way is being made leaves it through the workspace
    /// (`selectionChanged`), so the way that lands plays nothing and shows nothing.
    func testAWayStillBeingMadeIsLeftByAnotherSelectionWithTheSheetClosed() async throws {
        let (piece, selection) = try opened4to12()
        let workspace = ScoreWorkspace(piece: OpenPiece(id: piece, title: "Für Elise", pages: [], timemap: ""))
        workspace.wire()
        let insights = workspace.insights
        await insights.load(piece: piece, selection: selection)
        let fact = try XCTUnwrap(insights.understanding?.facts.first { !$0.tries.isEmpty })
        let way = try XCTUnwrap(fact.tries.first)
        XCTAssertFalse(workspace.panels[.insights], "the sheet is closed")

        let trying = Task { await insights.tryWay(way, of: fact) }
        _ = await insights.$starting.values.first { $0 == way.id }
        workspace.selectionChanged(ScoreSelection(onset: 0, end: 1920, firstBar: 1, lastBar: 1))
        await trying.value
        XCTAssertNil(insights.trial)
        XCTAssertNil(insights.starting, "no way shows as busy")
        XCTAssertFalse(workspace.player.hasVariation, "nothing plays")
        XCTAssertNotNil(facadeObject(MusicCore.call(piece: piece, "variation"))?["error"], "music-core forgot the variation")
    }
}
