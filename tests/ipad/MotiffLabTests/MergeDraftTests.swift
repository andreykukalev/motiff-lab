import UIKit
import XCTest

@testable import MotiffLab

/// The merge draft's rules on the score (testing items 32–34), through `ScoreController`'s calls as Sources and the
/// merge bar make them, on the kit's piece drawn with a cover of it as another version: the draft opens on its first
/// change and ‹ › step within its ends; a choice goes for the bar shown, or with Phrase for every change of its phrase,
/// and a group for every change of its kind; Cancel leaves nothing, Apply takes the bars with a toast whose Undo gives the
/// player's notes back; what is heard is the change's bar or its phrase; Open in the draft goes to its bar on the
/// version's page. Written before `MergeModel` took the draft's state (ADR 0031, S4b) and unchanged by it.
@MainActor
final class MergeDraftTests: XCTestCase {
    private var windows: [UIWindow] = []
    private var opened: [Int32] = []

    override func tearDown() async throws {
        windows = []
        for piece in opened { MusicCore.close(piece: piece) }
        opened = []
    }

    /// The kit's piece with bars 2, 3 and 10 an octave up: music-core finds those bars (and 11, which bar 10's last notes
    /// reach) otherwise, bars 2 and 3 in the first phrase.
    private static func cover() throws -> Data {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        var xml = try XCTUnwrap(String(data: example.data, encoding: .utf8))
        for bar in [2, 3, 10] {
            let start = try XCTUnwrap(xml.range(of: "<measure number=\"\(bar)\">"))
            let end = try XCTUnwrap(xml.range(of: "</measure>", range: start.upperBound..<xml.endIndex))
            var measure = String(xml[start.lowerBound..<end.lowerBound])
            // From the highest down, so that each octave goes up once.
            for octave in (0...7).reversed() {
                measure = measure.replacingOccurrences(of: "<octave>\(octave)</octave>", with: "<octave>\(octave + 1)</octave>")
            }
            xml.replaceSubrange(start.lowerBound..<end.lowerBound, with: measure)
        }
        return Data(xml.utf8)
    }

    /// A score with the kit's piece drawn and the cover added as another version, no draft open.
    private func score() async throws -> ScoreController {
        let example = try XCTUnwrap(KitPiece.example, "the kit's piece is bundled with the tests")
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        opened.append(piece.id)
        let score = ScoreController()
        let frame = CGRect(x: 0, y: 0, width: 1024, height: 1366)
        score.webView.frame = frame
        let holder = UIWindow(frame: frame)
        holder.addSubview(score.webView)
        holder.isHidden = false
        windows.append(holder)
        score.show(piece, dark: false)
        let drawn = await score.whenRendered()
        XCTAssertTrue(drawn, "the score's page is drawn")
        await score.addVersion(fileName: "cover.musicxml", data: try Self.cover())
        XCTAssertFalse(score.versions.differing.isEmpty, "the cover has bars otherwise")
        return score
    }

    /// Waits, up to ten seconds, for what a call started.
    private func until(_ what: String, _ done: () -> Bool) async {
        for _ in 0..<1000 where !done() {
            try? await Task.sleep(for: .milliseconds(10))
        }
        XCTAssertTrue(done(), what)
    }

    /// Waits until nothing is on its way in music-core for a while.
    private func settled() async {
        var still = 0
        for _ in 0..<1000 where still < 10 {
            await Task.yield()
            try? await Task.sleep(for: .milliseconds(10))
            still = (ProbeCounts.shared.snapshot()["pending"] ?? 0) == 0 ? still + 1 : 0
        }
    }

    /// Opens the draft with Keep my notes, not applied at once.
    private func draft(_ score: ScoreController, start: String = "mine") async throws -> MergeInfo {
        score.startMerge(start, direct: false)
        await until("the draft opens") { score.merge != nil && score.mergeAt != nil }
        return try XCTUnwrap(score.merge)
    }

    func testTheDraftOpensOnItsFirstChangeAndStepsWithinItsEnds() async throws {
        let score = try await score()
        let merge = try await draft(score)
        let bars = merge.bars.map(\.bar)
        XCTAssertEqual(Array(bars.prefix(3)), [2, 3, 10])
        XCTAssertEqual(score.mergeAt, bars.first, "on its first change")
        XCTAssertEqual(score.mergeShow, "result")
        XCTAssertTrue(merge.bars.allSatisfy { $0.choice == "mine" }, "Keep my notes")
        score.stepMerge(1)
        XCTAssertEqual(score.mergeAt, bars[1])
        score.stepMerge(bars.count + 3)
        XCTAssertEqual(score.mergeAt, bars.last, "› stops at the last change")
        score.stepMerge(-(bars.count + 3))
        XCTAssertEqual(score.mergeAt, bars.first, "‹ stops at the first")
        await settled()
    }

    func testAChoiceGoesForTheBarShownOrWithPhraseForEveryChangeOfItsPhrase() async throws {
        let score = try await score()
        let merge = try await draft(score)
        let at = try XCTUnwrap(score.mergeAt)
        let unit = try XCTUnwrap(score.phrases?.units.first { $0.firstBar <= at && at <= $0.lastBar })
        let inPhrase = merge.bars.map(\.bar).filter { unit.firstBar <= $0 && $0 <= unit.lastBar }
        XCTAssertEqual(inPhrase, [2, 3], "the first phrase has two changes")

        score.chooseMerge("version")
        await until("the bar shown is taken from the version") { score.merge?.choice(at: at) == "version" }
        XCTAssertEqual(inPhrase.filter { score.merge?.choice(at: $0) == "version" }, [at], "the bar shown only")
        score.undoDraft()
        await until("Undo takes the choice back") { score.merge?.choice(at: at) == "mine" }

        score.mergePhrase = true
        score.chooseMerge("version")
        await until("with Phrase, the phrase's changes") { inPhrase.allSatisfy { score.merge?.choice(at: $0) == "version" } }
        let outside = try XCTUnwrap(score.merge?.bars.filter { !inPhrase.contains($0.bar) })
        XCTAssertFalse(outside.isEmpty)
        XCTAssertTrue(outside.allSatisfy { $0.choice == "mine" }, "and none of another phrase")

        let group = try XCTUnwrap(outside.first?.group)
        let kind = outside.filter { $0.group == group }.map(\.bar)
        score.chooseMergeGroup(group, "version")
        await until("a group: every change of its kind") { kind.allSatisfy { score.merge?.choice(at: $0) == "version" } }
        await settled()
    }

    func testCancelLeavesNothingAndApplyTakesTheBarsWithAToastWhoseUndoGivesThemBack() async throws {
        let score = try await score()
        _ = try await draft(score, start: "all")
        score.comparing = true
        score.showMergeView("version")
        XCTAssertEqual(score.mergeShow, "version")
        score.cancelDraft()
        await until("Cancel ends the draft") { score.merge == nil }
        XCTAssertNil(score.mergeAt)
        XCTAssertFalse(score.comparing, "Compare versions goes with it")
        XCTAssertEqual(score.mergeShow, "result")
        XCTAssertNil(score.mergeHearing)
        XCTAssertEqual(score.versions.shown?.takenBars, 0, "nothing in the piece changed")
        await settled()

        let taking = try await draft(score, start: "all").taking
        XCTAssertGreaterThan(taking, 0)
        score.applyMerge()
        await until("Apply says so") { score.mergeToast != nil }
        XCTAssertNil(score.merge, "the draft is done")
        XCTAssertEqual(score.mergeToast?.undo, .unapply)
        XCTAssertEqual(score.versions.shown?.takenBars, taking, "its bars are in the score")
        await settled()

        score.undoToast()
        await until("the toast's Undo gives the player's notes back") { score.versions.shown?.takenBars == 0 && score.mergeToast != nil }
        XCTAssertNil(score.mergeToast?.undo, "and cannot itself be undone from the toast")
        await settled()
    }

    func testWhatIsHeardIsTheChangesBarOrWithPhraseItsPhrase() async throws {
        let score = try await score()
        var heard: [Range<Double>] = []
        var inTurn: [Range<Double>] = []
        score.hearNotes = { _, span in heard.append(span) }
        score.hearInTurn = { _, span in inTurn.append(span) }
        _ = try await draft(score)
        let at = try XCTUnwrap(score.mergeAt)
        let bar = try XCTUnwrap(score.versions.bars.first { $0.bar == at }?.span)

        score.hearMerge("version")
        XCTAssertEqual(score.mergeHearing, "version")
        await until("the version is heard") { !heard.isEmpty }
        XCTAssertEqual(heard.last, bar, "over the change's bar")

        score.mergePhrase = true
        score.hearMerge("alternate")
        await until("mine and the version in turn") { !inTurn.isEmpty }
        let unit = try XCTUnwrap(score.phrases?.units.first { $0.firstBar <= at && at <= $0.lastBar })
        XCTAssertEqual(inTurn.last, unit.onset..<unit.end, "with Phrase, over its phrase")
        score.stepMerge(1)
        await until("the next change is heard the same way") { inTurn.count == 2 }

        score.cancelDraft()
        await until("Cancel ends the draft") { score.merge == nil }
        XCTAssertNil(score.mergeHearing, "and what was heard")
        await settled()
    }

    func testOpenInTheDraftGoesToItsBarOnTheVersionsPage() async throws {
        let score = try await score()
        var picked: [Int] = []
        score.versionPicked = { picked.append($0) }
        let bar = try XCTUnwrap(score.versions.differing.last?.bar)
        score.openDraft(at: bar)
        XCTAssertEqual(score.versionPage, score.versions.shownId, "Sources shows the version's page")
        XCTAssertEqual(picked, [bar])
        await until("the draft opens on that bar") { score.merge != nil && score.mergeAt == bar }
        XCTAssertEqual(score.mergeShow, "result")
        XCTAssertTrue(score.merge?.bars.allSatisfy { $0.choice == nil } ?? false, "the choices as last applied: none yet")
        await settled()
    }
}
