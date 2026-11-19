import UIKit
import XCTest

/// The UI walk (the `Screens` scheme, run by the `iPad` workflow by hand and for a version tag): the journeys of the
/// architecture blueprint's layer T4, each asserting what a player does and sees through the controls and the app's Debug
/// probe (ScreensProbe), with the screenshots for the redesign's mockups taken on the way. A control or a screen a journey
/// expects and does not find fails the test, with the baseline entries (docs/ux-baseline.md) the journey checks.
///
/// A journey stops at its first miss: its later steps build on that one and would only fail after it, each waiting out its
/// own timeout. The test goes on (`continueAfterFailure`) and the app is launched again for the next journey, so a missing
/// button fails its journey and costs that journey's pictures, never the other journeys' checks.
///
/// Landscape and light always; portrait and the dark appearance as well when the pictures are asked for (SCREENS_DIR, which
/// the workflow passes as TEST_RUNNER_SCREENS_DIR), so the journeys alone take a few minutes; the other places of the
/// score (`picturesOnly`) are visited only then. The pictures go to that folder and are attached to the test result.
@MainActor
final class ScreensUITests: XCTestCase {
    private struct Journey {
        let name: String
        /// The entries of the UX baseline (docs/ux-baseline.md) it checks, by their ids. An entry whose «Protected by» names
        /// the journey (`ScreensUITests.swift` journey `<name>`) must be listed here: governance/tests/baseline.test.mjs
        /// reads this declaration.
        let protects: [String]
        let start: Walk.Start
        /// Where it leaves the app when it passes, if not where it started.
        var end: Walk.Start?
        /// Walked only for the pictures (SCREENS_DIR): the places it visits are the mockups', not a journey to assert.
        var picturesOnly = false
        let run: @MainActor (Walk) throws -> Void
    }

    private static let journeys: [Journey] = [
        Journey(name: "open-and-play", protects: [], start: .fresh, end: .example, run: { try $0.openExampleAndPlay() }),
        Journey(name: "select-loop-change", protects: ["PLAY-04"], start: .example, run: { try $0.selectLoopAndChange() }),
        Journey(name: "select-by-drag", protects: ["SEL-03"], start: .example, run: { try $0.selectByDragAndOver() }),
        Journey(name: "roll-and-expand", protects: ["ROLL-01", "PLAY-03"], start: .example, run: { try $0.rollAndExpand() }),
        Journey(name: "insights-and-try", protects: [], start: .example, run: { try $0.insightsAndTry() }),
        Journey(name: "pen-and-eraser", protects: ["INK-01"], start: .example, run: { try $0.penAndEraser() }),
        Journey(name: "assistant-panel", protects: [], start: .example, run: { try $0.assistantPanel() }),
        Journey(name: "other-places", protects: [], start: .example, end: .library, picturesOnly: true,
                run: { try $0.otherPlaces() }),
        Journey(name: "pdf-and-notes", protects: ["VIEW-01", "VIEW-04"], start: .notesAndPdf, run: { try $0.pdfAndNotes() }),
        Journey(name: "sources-sheet", protects: [], start: .notesAndPdf, run: { try $0.sourcesSheet() }),
        Journey(name: "pdf-piece", protects: [], start: .pdf, run: { try $0.pdfPiece() }),
    ]

    func testLandscape() { walk(.landscapeLeft, dark: false) }

    func testPortrait() throws {
        try picturesAsked()
        walk(.portrait, dark: false)
    }

    func testLandscapeDark() throws {
        try picturesAsked()
        walk(.landscapeLeft, dark: true)
    }

    func testPortraitDark() throws {
        try picturesAsked()
        walk(.portrait, dark: true)
    }

    private func picturesAsked() throws {
        guard let folder = ProcessInfo.processInfo.environment["SCREENS_DIR"], !folder.isEmpty else {
            throw XCTSkip("portrait and the dark appearance are walked when the pictures are asked for (SCREENS_DIR)")
        }
    }

    /// Every journey in one appearance, one after another; a journey that fails is written down and the walk goes on.
    private func walk(_ orientation: UIDeviceOrientation, dark: Bool) {
        continueAfterFailure = true
        let tag = (orientation.isLandscape ? "land" : "port") + (dark ? "-dark" : "")
        XCUIDevice.shared.orientation = orientation
        let walk = Walk(tag: tag, dark: dark, test: self)
        let began = Date()
        var failed: [String] = []
        let journeys = Self.journeys.filter { walk.pictures || !$0.picturesOnly }
        for journey in journeys {
            let start = Date()
            do {
                try walk.arrive(at: journey.start)
                try journey.run(walk)
                walk.state = journey.end ?? journey.start
                print("SCREEN-JOURNEY \(tag) \(journey.name): passed in \(seconds(since: start)) s")
            } catch {
                walk.state = nil
                failed.append(journey.name)
                let ids = journey.protects.isEmpty ? "" : " (\(journey.protects.joined(separator: ", ")))"
                print("SCREEN-FAIL \(tag) \(journey.name)\(ids) after \(seconds(since: start)) s: \(error)")
                walk.failed(journey.name)
                XCTFail("journey \(journey.name)\(ids), \(tag): \(error)")
            }
        }
        print("SCREEN-WALK \(tag): \(journeys.count) journeys, \(failed.count) failed \(failed), in \(seconds(since: began)) s")
        walk.app.terminate()
        XCUIDevice.shared.orientation = .portrait
    }

    private func seconds(since date: Date) -> Int {
        Int(Date().timeIntervalSince(date).rounded())
    }
}
