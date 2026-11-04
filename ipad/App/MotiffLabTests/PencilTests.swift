import WebKit
import XCTest

@testable import MotiffLab

/// Pencil marks on the iPad (decision record 0019 section 2.2): the score page runs the web app's own score modules,
/// keeps a stroke against its bar as the web does, and music-core saves it in the project file (`marks.ink`).
@MainActor
final class PencilTests: XCTestCase {
    private var window: UIWindow?

    override func tearDown() async throws {
        window = nil
    }

    /// The score page in a web view on screen, once its modules have loaded.
    private func scorePage() async throws -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        let view = WKWebView(frame: CGRect(x: 0, y: 0, width: 1024, height: 1366), configuration: configuration)
        let window = UIWindow(frame: view.frame)
        window.addSubview(view)
        window.isHidden = false
        self.window = window
        view.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
        for _ in 0..<100 {
            if (try? await view.evaluateJavaScript("typeof window.motiff")) as? String == "object" {
                return view
            }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let problem = (try? await view.evaluateJavaScript("document.documentElement.outerHTML.slice(0, 300)")) as? String
        XCTFail("The score page did not load its modules: \(problem ?? "no page")")
        throw URLError(.resourceUnavailable)
    }

    private func call(_ view: WKWebView, _ function: String, _ data: Any) async throws -> Any? {
        try await view.callAsyncJavaScript("return motiff.\(function)(data)", arguments: ["data": data], contentWorld: .page)
    }

    func testAStrokeIsKeptAgainstItsBarSavedByMusicCoreAndDrawn() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(OpenPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }

        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        let shown = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])
        let laid = try XCTUnwrap(shown as? [String: Any])
        XCTAssertEqual((laid["bars"] as? NSNumber)?.intValue, 97, "every bar of Für Elise is placed")
        XCTAssertGreaterThan((laid["space"] as? NSNumber)?.doubleValue ?? 0, 2)

        // A stroke across the middle of the third bar.
        let placed = try await view.callAsyncJavaScript("return motiff.frames()", contentWorld: .page)
        let frames = try XCTUnwrap(placed as? [[String: Any]])
        let bar = frames[2]
        func value(_ key: String) -> Double { (bar[key] as? NSNumber)?.doubleValue ?? 0 }
        let x = (value("left") + value("right")) / 2
        let y = value("staffTop") + 2 * value("space")
        let points = [["x": x, "y": y, "pressure": 0.5], ["x": x + 20, "y": y + 4, "pressure": 0.7], ["x": x + 40, "y": y, "pressure": 0.5]]
        let anchored = try await call(view, "anchor", ["points": points, "tool": "pen", "colour": "carmine"])
        let mark = try XCTUnwrap(anchored as? [String: Any])
        XCTAssertEqual((mark["bar"] as? NSNumber)?.doubleValue, value("tick"))
        XCTAssertEqual(mark["colour"] as? String, "carmine")
        XCTAssertEqual((mark["u"] as? NSNumber)?.doubleValue ?? 0, 0.5, accuracy: 0.01)
        XCTAssertEqual((mark["v"] as? NSNumber)?.doubleValue ?? 0, 2, accuracy: 0.01)
        let nowhere = try await call(view, "anchor", ["points": [], "tool": "pen", "colour": "graphite"]) as? [String: Any]
        XCTAssertNil(nowhere?["id"], "a stroke without points is kept nowhere")

        let request = String(decoding: try JSONSerialization.data(withJSONObject: ["strokes": [mark]]), as: UTF8.self)
        let added = await MusicCore.run { MusicCore.call(piece: piece.id, "ink.add", request) }
        let answer = try XCTUnwrap(facadeObject(added))
        let ink = try XCTUnwrap(answer["ink"] as? [[String: Any]], "\(answer)")
        XCTAssertEqual(ink.count, 1)
        XCTAssertEqual(ink.first?["id"] as? String, mark["id"] as? String)

        let drawn = try await call(view, "draw", ["marks": ink]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, 1)
        let paths = try await view.evaluateJavaScript("document.querySelectorAll('#ink path.mark.pen.carmine').length") as? NSNumber
        XCTAssertEqual(paths?.intValue, 1)

        // The eraser takes it where it passes over it, and nothing elsewhere.
        let erased = try await call(view, "erase", ["points": [["x": x + 20, "y": y + 4]]]) as? [String]
        XCTAssertEqual(erased, [mark["id"] as? String ?? "?"])
        let missed = try await call(view, "erase", ["points": [["x": x, "y": y + 400]]]) as? [String]
        XCTAssertEqual(missed, [])

        // The project file keeps the stroke as it is, for the web app to draw in the same place.
        let file = await MusicCore.run { MusicCore.call(piece: piece.id, "project.save", #"{"now": "2026-11-04T12:00:00Z"}"#) }
        let saved = try XCTUnwrap(facadeObject(file))
        let kept = (saved["marks"] as? [String: Any])?["ink"] as? [[String: Any]]
        XCTAssertEqual(kept?.first?["bar"] as? NSNumber, mark["bar"] as? NSNumber)
    }

    func testTheInksAreTheDesignKits() {
        XCTAssertNotEqual(DesignTokens.color("ink-carmine"), .systemGray)
        XCTAssertNotEqual(DesignTokens.color("amber"), .systemGray)
        XCTAssertEqual(DesignTokens.color("no-such-token"), .systemGray)
    }
}
