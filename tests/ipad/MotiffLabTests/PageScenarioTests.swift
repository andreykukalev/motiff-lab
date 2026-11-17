import WebKit
import XCTest

@testable import MotiffLab

/// The page scenarios in WebKit (tests/contracts/page-scenarios/README.md, the audit's C5): the same files Chromium replays on
/// Linux (tests/pages/run.mjs), here on the app's own pages in a WKWebView served by the app's scheme handler, as
/// PencilTests and EraserTests load them. The replayer (tests/pages/replay.js) runs inside the page: it stands in for the
/// app's message handlers, sends the app's commands, presses with pointer events it makes (a test cannot give WebKit real
/// touches) and compares what the page posted and drew with each scenario's expectations. A scenario marked
/// `chromiumOnly` is left to Chromium. The files are read from the repository beside this one: the simulator reads the
/// Mac's disk, so they need no place in the test bundle.
@MainActor
final class PageScenarioTests: XCTestCase {
    private var window: UIWindow?

    override func tearDown() async throws {
        window = nil
    }

    /// The repository's root: this file is tests/ipad/MotiffLabTests/PageScenarioTests.swift.
    private static let repository = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    private static let scenarios = repository.appendingPathComponent("tests/contracts/page-scenarios")

    func testThePagesKeepEveryScenario() async throws {
        let replayer = try String(contentsOf: Self.repository.appendingPathComponent("tests/pages/replay.js"), encoding: .utf8)
        let files = try FileManager.default.contentsOfDirectory(at: Self.scenarios, includingPropertiesForKeys: nil)
            .filter { $0.pathExtension == "json" }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
        XCTAssertFalse(files.isEmpty, "no scenario in \(Self.scenarios.path)")
        var played = 0
        for file in files {
            let text = try String(contentsOf: file, encoding: .utf8)
            let scenario = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any], file.lastPathComponent)
            if scenario["chromiumOnly"] as? Bool == true { continue }
            let found = try await play(scenario, text, replayer: replayer)
            let id = scenario["id"] as? String ?? file.lastPathComponent
            let protects = (scenario["protects"] as? [String] ?? []).joined(separator: ", ")
            XCTAssertTrue(found.isEmpty, "\(id) (\(protects)) in WebKit:\n" + found.joined(separator: "\n"))
            played += 1
        }
        XCTAssertGreaterThan(played, 0, "every scenario was left to Chromium")
    }

    /// One scenario on a fresh page: every difference the replayer found.
    private func play(_ scenario: [String: Any], _ text: String, replayer: String) async throws -> [String] {
        let name = try XCTUnwrap(scenario["fixture"] as? String)
        let fixture = try String(contentsOf: Self.scenarios.appendingPathComponent("fixtures").appendingPathComponent(name), encoding: .utf8)
        let roll = scenario["page"] as? String == "roll"
        let viewport = scenario["viewport"] as? [String: Any]
        let size = CGSize(width: (viewport?["width"] as? NSNumber)?.doubleValue ?? 1024, height: (viewport?["height"] as? NSNumber)?.doubleValue ?? 1366)
        let page = try await webPage(roll ? "Web/roll.html" : "Web/score.html", ready: roll ? "window.roll?.load" : "window.motiff?.show", size: size)
        // As a function's body, which answers nothing (evaluateJavaScript would have to bridge the script's undefined).
        _ = try await page.callAsyncJavaScript(replayer, arguments: [:], contentWorld: .page)
        // As text, parsed by the page: what JSON says reaches the page as it is (a true stays a boolean, not a number).
        let found = try await page.callAsyncJavaScript(
            "return await window.pageScenario.play(JSON.parse(scenario), JSON.parse(fixture))",
            arguments: ["scenario": text, "fixture": fixture], contentWorld: .page)
        return (found as? [Any] ?? ["the replayer answered nothing"]).map { "\($0)" }
    }

    /// A page of the app's in a web view on screen at the scenario's size, once its script has defined the command `ready`
    /// (not only its object: roll.html's `<div id="roll">` is `window.roll` before roll.js runs).
    private func webPage(_ path: String, ready: String, size: CGSize) async throws -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        let view = WKWebView(frame: CGRect(origin: .zero, size: size), configuration: configuration)
        // The page's viewport is the view: no safe area moves it.
        view.scrollView.contentInsetAdjustmentBehavior = .never
        window = UIWindow(frame: view.frame)
        window?.addSubview(view)
        window?.isHidden = false
        view.load(URLRequest(url: BundleSchemeHandler.url(path)))
        // WebKit starts its processes for the first web view of a run, which can take a while on a CI runner.
        let deadline = Date().addingTimeInterval(30)
        while Date() < deadline {
            if await Self.has(view, ready) {
                return view
            }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        XCTFail("The page \(path) did not load its modules")
        throw URLError(.resourceUnavailable)
    }

    /// Whether the page's script has defined that command (none yet while it loads).
    private static func has(_ view: WKWebView, _ command: String) async -> Bool {
        (try? await view.callAsyncJavaScript("return typeof \(command) === \"function\"", contentWorld: .page)) as? Bool == true
    }
}
