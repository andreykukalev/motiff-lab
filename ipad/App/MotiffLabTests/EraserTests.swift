import WebKit
import XCTest

@testable import MotiffLab

/// The eraser on the score rubs out as it moves (BUG-05): the score page takes the marks under it at each move
/// (`motiff.rub`), from where the move before left off, and hands the press over as one step when it lifts
/// (`motiff.rubEnd`). The geometry is pencil-marks.ts `rubAlong` (tests/web/pencil-marks.test.mjs); the PDF's pages are
/// StylusTests' PageEraser.
@MainActor
final class EraserTests: XCTestCase {
    private var window: UIWindow?

    override func tearDown() async throws {
        window = nil
    }

    func testTheScorePageRubsOutAsTheEraserMovesAndHandsOverThePress() async throws {
        let page = try await loadedScore()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await run(page, "show(data)", ["pages": piece.pages, "timemap": timemap, "dark": false])

        // Two strokes across the third bar, one under the other, drawn as the page draws the marks music-core answers.
        let frames = try await run(page, "frames()", [:])
        let bar = try XCTUnwrap((frames as? [[String: Any]])?[2])
        let left = (bar["left"] as? NSNumber)?.doubleValue ?? 0
        let width = ((bar["right"] as? NSNumber)?.doubleValue ?? 0) - left
        let top = ((bar["staffTop"] as? NSNumber)?.doubleValue ?? 0)
        let space = (bar["space"] as? NSNumber)?.doubleValue ?? 8
        var marks: [Any] = []
        for row in [1.0, 3.0] {
            let line = (0...8).map { ["x": left + width * Double($0) / 8, "y": top + row * space, "pressure": 0.5] }
            let mark = try await run(page, "anchor(data)", ["points": line, "tool": "pen", "colour": "forest"])
            marks.append(try XCTUnwrap(mark))
        }
        _ = try await run(page, "draw(data)", ["marks": marks])
        let middle = left + width / 2

        // Coming down above both takes nothing; moving down across both takes them from the page at once, in pieces.
        let above = try await run(page, "rub(data)", ["points": [["x": middle, "y": top - 6 * space]]]) as? NSNumber
        XCTAssertEqual(above?.intValue, 2)
        let across = try await run(page, "rub(data)", ["points": [["x": middle, "y": top + 5 * space]]]) as? NSNumber
        XCTAssertEqual(across?.intValue, 4, "each stroke is cut in two while the eraser is still down")
        let drawn = try await run(page, "document.querySelectorAll('#ink path.mark').length", [:]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, 4, "the page shows the pieces, with nothing drawn for the eraser")

        // Lifted: the two strokes it cut, whole, and the four pieces, as one step; the page keeps showing the pieces.
        let lifted = try await run(page, "rubEnd()", [:])
        let press = try XCTUnwrap(lifted as? [String: Any])
        XCTAssertEqual((press["removed"] as? [[String: Any]])?.count, 2)
        let pieces = try XCTUnwrap(press["added"] as? [[String: Any]])
        XCTAssertEqual(pieces.count, 4)
        XCTAssertEqual(Set(pieces.compactMap { $0["colour"] as? String }), ["forest"])
        let after = try await run(page, "document.querySelectorAll('#ink path.mark').length", [:]) as? NSNumber
        XCTAssertEqual(after?.intValue, 4, "nothing comes back when it lifts")

        // The next press starts afresh: coming down far below takes nothing, and nothing joins it to the last press.
        let fresh = try await run(page, "rub(data)", ["points": [["x": middle, "y": top + 40 * space]]]) as? NSNumber
        XCTAssertEqual(fresh?.intValue, 4)
        let second = try await run(page, "rubEnd()", [:])
        XCTAssertEqual(((second as? [String: Any])?["removed"] as? [Any])?.count, 0)
    }

    /// The score page on screen, once its modules are there.
    private func loadedScore() async throws -> WKWebView {
        let setup = WKWebViewConfiguration()
        setup.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        let page = WKWebView(frame: CGRect(x: 0, y: 0, width: 1024, height: 1366), configuration: setup)
        let holder = UIWindow(frame: page.frame)
        holder.addSubview(page)
        holder.isHidden = false
        window = holder
        page.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
        var tries = 0
        while (try? await page.evaluateJavaScript("typeof window.motiff")) as? String != "object" {
            tries += 1
            guard tries < 300 else { throw URLError(.resourceUnavailable) }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        return page
    }

    /// Runs `motiff.<call>` (or plain script) on the page with `data`.
    private func run(_ page: WKWebView, _ call: String, _ data: [String: Any]) async throws -> Any? {
        let body = call.contains("(") && !call.hasPrefix("document") ? "return motiff.\(call)" : "return \(call)"
        return try await page.callAsyncJavaScript(body, arguments: ["data": data], contentWorld: .page)
    }
}
