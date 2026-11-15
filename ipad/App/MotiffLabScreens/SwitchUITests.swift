import UIKit
import XCTest

/// PDF | Notes switched again and again (v0.6.4 spec sections 11 and 13): a piece with notes and a scanned-size PDF inside
/// it, opened on its notes, goes to its PDF and back `rounds` times. Each switch must land within `limit` seconds (the
/// screen ready: the PDF's pages laid out, or the notes drawn and shown), the app must answer a tap on More in between, and
/// the bar's old Play must never be drawn while the modes are on. Every switch is written down as a SCREEN-SWITCH line
/// with the app's own probe (ScreensProbe, Debug builds): its memory, the controllers alive, the score's renders and
/// music-core's longest call, so the iPad workflow's log shows whether anything piles up. Runs in the `Screens` scheme.
@MainActor
final class SwitchUITests: XCTestCase {
    private var app: XCUIApplication!
    private let limit: TimeInterval = 5

    /// The modes on, the sources off: the bar's PDF | Notes.
    func testSwitchPdfNotes() { switchAgainAndAgain(sources: false) }

    /// The modes and the sources on: the switch of all views (`experimental.sources`), as Andrei uses the app.
    func testSwitchWithSources() { switchAgainAndAgain(sources: true) }

    /// As above in a library of 15 more pieces with notes and a scanned PDF each, as a player's library is.
    func testSwitchInAFullLibrary() { switchAgainAndAgain(sources: true, copies: 15, rounds: 8) }

    private func switchAgainAndAgain(sources: Bool, copies: Int = 0, rounds: Int = 20) {
        continueAfterFailure = true
        let tag = (sources ? "sources" : "plain") + (copies > 0 ? "-library\(copies)" : "")
        XCUIDevice.shared.orientation = .landscapeLeft
        guard let pdf = ScreensPdf.write(name: "Elise-\(tag).pdf", scanned: true) else { return XCTFail("no PDF") }
        let settings = "{\"appearance.theme\" = \"daylight\"; \"experimental.playheads\" = \"on\"; \"experimental.sources\" = \"\(sources ? "on" : "off")\";}"
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
                times.append(landed.seconds)
                if landed.seconds > limit { slow += 1 }
                let answer = answersTap()
                print("SCREEN-SWITCH \(tag) \(round) \(target) \(ms(landed.seconds)) ms, tap \(answer.map { "\(ms($0)) ms" } ?? "NO ANSWER"): \(landed.line)")
                XCTAssertLessThanOrEqual(landed.seconds, limit, "round \(round): the switch to \(target) took \(ms(landed.seconds)) ms")
                XCTAssertNotNil(answer, "round \(round): the app did not answer a tap on More after the switch to \(target)")
            }
        }
        let sorted = times.sorted()
        let line = probe() ?? ""
        print("SCREEN-SWITCH-SUMMARY \(tag): \(times.count) switches, median \(ms(sorted[sorted.count / 2])) ms, slowest \(ms(sorted.last ?? 0)) ms, over \(Int(limit)) s: \(slow); \(line)")
        XCTAssertFalse(line.contains("oldbar=") && !line.contains("oldbar=0"), "the old Play was drawn while the modes were on: \(line)")
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

    private func probe() -> String? {
        let element = app.descendants(matching: .any)["ml-screens-probe"]
        return element.exists ? element.label : nil
    }

    private func ms(_ seconds: TimeInterval) -> Int { Int(seconds * 1000) }
}
