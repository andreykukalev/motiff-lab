import UIKit
import XCTest

/// PDF | Notes switched again and again (v0.6.4 spec sections 11 and 13): a piece with notes and a scanned-size PDF inside
/// it, opened on its notes, goes to its PDF and back `rounds` times. Each switch must land within `limit` seconds by the
/// app's own clock (from the press to the screen ready: the PDF's pages laid out, or the notes drawn and shown), the app
/// must answer a tap on More in between. Every switch
/// is written down as a SCREEN-SWITCH line with the app's own probe (ScreensProbe, Debug builds): its memory, the
/// controllers alive, the score's renders, music-core's longest call and the phases since the press, so the log shows
/// whether anything piles up. The test's own wait is longer than the app's time: each read of the probe is a query of
/// the whole screen. Runs in the `Screens` scheme only when the runner has `SWITCH_TEST` (`TEST_RUNNER_SWITCH_TEST=1`):
/// the two take about 20 minutes in the workflow's simulator, more than the screenshot walk's job has left.
@MainActor
final class SwitchUITests: XCTestCase {
    private var app: XCUIApplication!
    /// The app's own time for one switch in the workflow's simulator; a freeze guard, not the iPad's time (v0.6.4 review).
    private let limit: TimeInterval = 10

    /// The switch of all views (the modes and Sources, permanent since v0.6.5), as Andrei uses the app.
    func testSwitchWithSources() throws { try asked(); switchAgainAndAgain() }

    /// As above in a library of 15 more pieces with notes and a scanned PDF each, as a player's library is.
    func testSwitchInAFullLibrary() throws { try asked(); switchAgainAndAgain(copies: 15, rounds: 8) }

    private func asked() throws {
        guard ProcessInfo.processInfo.environment["SWITCH_TEST"] != nil else { throw XCTSkip("the switch test runs when asked (SWITCH_TEST)") }
    }

    private func switchAgainAndAgain(copies: Int = 0, rounds: Int = 20) {
        continueAfterFailure = true
        let tag = "sources" + (copies > 0 ? "-library\(copies)" : "")
        XCUIDevice.shared.orientation = .landscapeLeft
        guard let pdf = ScreensPdf.write(name: "Elise-\(tag).pdf", scanned: true) else { return XCTFail("no PDF") }
        let settings = "{\"appearance.theme\" = \"daylight\";}"
        app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages", "(ru)", "-AppleLocale", "ru_RU", "-motiff.settings", settings,
                               "-ml-screens", "-ml-screens-reset", "-ml-screens-both", "-ml-screens-open", pdf.path, "-ml-screens-copies", String(copies)]
        app.launch()
        defer {
            app.terminate()
            XCUIDevice.shared.orientation = .portrait
        }

        guard let opened = until("score", timeout: 120) else {
            print("SCREEN-SWITCH \(tag) never opened: \(probe() ?? "no probe")")
            return XCTFail("the piece did not open on its notes")
        }
        print("SCREEN-SWITCH \(tag) opened in \(ms(opened.seconds)) ms: \(opened.line)")

        var times: [TimeInterval] = []
        var slow = 0
        for round in 1...rounds {
            for (target, button) in [("pdf", pdfButton), ("score", notesButton)] {
                let control = button()
                guard control.waitForExistence(timeout: limit), control.isHittable else {
                    print("SCREEN-SWITCH \(tag) \(round) \(target): no switch in the bar: \(probe() ?? "no probe")")
                    return XCTFail("round \(round): the switch to \(target) is missing")
                }
                control.tap()
                guard let landed = until(target, timeout: 30) else {
                    print("SCREEN-SWITCH \(tag) \(round) \(target): FROZEN, not ready in 30 s: \(probe() ?? "no answer")")
                    return XCTFail("round \(round): the switch to \(target) did not finish in 30 s")
                }
                let own = appTime(landed.line) ?? landed.seconds
                times.append(own)
                if own > limit { slow += 1 }
                let answer = answersTap()
                print("SCREEN-SWITCH \(tag) \(round) \(target) \(ms(own)) ms (test \(ms(landed.seconds)) ms), tap \(answer.map { "\(ms($0)) ms" } ?? "NO ANSWER"): \(landed.line)")
                XCTAssertLessThanOrEqual(own, limit, "round \(round): the switch to \(target) took \(ms(own)) ms")
                XCTAssertNotNil(answer, "round \(round): the app did not answer a tap on More after the switch to \(target)")
            }
        }
        let sorted = times.sorted()
        let line = probe() ?? ""
        print("SCREEN-SWITCH-SUMMARY \(tag): \(times.count) switches, median \(ms(sorted[sorted.count / 2])) ms, slowest \(ms(sorted.last ?? 0)) ms, over \(Int(limit)) s: \(slow) (the app's own times); \(line)")
    }

    // MARK: - Helpers

    /// The switch's PDF, in either bar.
    private func pdfButton() -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label == 'PDF'")).firstMatch
    }

    /// The switch's Notes («Ноты», or «Ноты, рабочие» in the switch of all views).
    private func notesButton() -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Ноты'")).firstMatch
    }

    /// Waits until the probe says the screen is shown and ready; how long that took and the probe's line, or nil.
    private func until(_ screen: String, timeout: TimeInterval) -> (seconds: TimeInterval, line: String)? {
        let start = Date()
        while Date().timeIntervalSince(start) < timeout {
            if let line = probe(), line.hasPrefix("screen=\(screen) ready=1") {
                return (Date().timeIntervalSince(start), line)
            }
            Thread.sleep(forTimeInterval: 0.1)
        }
        return nil
    }

    /// More opened and put away again: how long the app took to show its popover, or nil when it did not.
    private func answersTap() -> TimeInterval? {
        let more = app.buttons["Ещё"].firstMatch
        guard more.waitForExistence(timeout: 3), more.isHittable else { return nil }
        let start = Date()
        more.tap()
        let region = app.otherElements["PopoverDismissRegion"]
        guard region.waitForExistence(timeout: 5) else { return nil }
        let seconds = Date().timeIntervalSince(start)
        region.tap()
        let gone = Date()
        while region.exists && Date().timeIntervalSince(gone) < 3 { Thread.sleep(forTimeInterval: 0.1) }
        return seconds
    }

    /// The app's own time from the press to the screen ready: the last of the probe's phases (`name@ms`), in seconds.
    private func appTime(_ line: String) -> TimeInterval? {
        guard let phases = line.components(separatedBy: "phases=").last, line.contains("phases=") else { return nil }
        let marks = phases.split(separator: ",").compactMap { $0.split(separator: "@").last.flatMap { Double($0) } }
        return marks.max().map { $0 / 1000 }
    }

    private func probe() -> String? {
        let element = app.descendants(matching: .any)["ml-screens-probe"]
        return element.exists ? element.label : nil
    }

    private func ms(_ seconds: TimeInterval) -> Int { Int(seconds * 1000) }
}
