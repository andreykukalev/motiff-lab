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

    func testTheBarNamesThePagesUnderTheReadingLine() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(OpenPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        // In portrait the pages are one column: the first is in view, then, scrolled to the end, the last.
        let topView = try await call(view, "inView", [String: Any]())
        let top = try XCTUnwrap(topView as? [String: Any])
        XCTAssertEqual((top["first"] as? NSNumber)?.intValue, 1)
        XCTAssertEqual((top["last"] as? NSNumber)?.intValue, 1)
        XCTAssertEqual((top["count"] as? NSNumber)?.intValue, piece.pages.count)
        _ = try await view.evaluateJavaScript("window.scrollTo(0, document.documentElement.scrollHeight); 0")
        let endView = try await call(view, "inView", [String: Any]())
        let end = try XCTUnwrap(endView as? [String: Any])
        XCTAssertEqual((end["last"] as? NSNumber)?.intValue, piece.pages.count)
        let numbered = try await view.evaluateJavaScript("document.querySelector('#pages > .page:last-child').dataset.page") as? String
        XCTAssertEqual(numbered, String(piece.pages.count))
    }

    func testBracketsAndTheSelectionBandAreDrawnOverTheBars() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(OpenPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        let shown = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece.id, "phrases")))
        let units = shown.units.map { ["start": $0.onset, "end": $0.end, "number": $0.number, "kind": $0.kind, "current": $0.number == 2, "label": $0.name] as [String: Any] }
        let drawn = try await call(view, "phrases", ["units": units]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, shown.units.count)
        let brackets = try await view.evaluateJavaScript("document.querySelectorAll('body > .bracket').length") as? NSNumber
        XCTAssertGreaterThanOrEqual(brackets?.intValue ?? 0, shown.units.count, "a bracket on every system its phrase touches")
        let current = try await view.evaluateJavaScript("document.querySelector('body > .bracket.on').dataset.phrase") as? String
        XCTAssertEqual(current, "2")

        let second = try XCTUnwrap(shown.unit(2))
        let bands = try await call(view, "select", ["start": second.onset, "end": second.end, "startLabel": "start", "endLabel": "end"]) as? NSNumber
        XCTAssertGreaterThanOrEqual(bands?.intValue ?? 0, 1)
        let handles = try await view.evaluateJavaScript("document.querySelectorAll('body > .sel-handle').length") as? NSNumber
        XCTAssertEqual(handles?.intValue, 2)
        let cleared = try await call(view, "select", NSNull()) as? NSNumber
        XCTAssertEqual(cleared?.intValue, 0)
    }

    func testFocusLiftsThePhraseIntoACardEngravedAtItsWidth() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(OpenPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])
        let shown = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece.id, "phrases")))
        let second = try XCTUnwrap(shown.unit(2))

        // The page gives the web app's card layout at the card's width, and the phrase's bars engrave with it.
        let opened = try await call(view, "focus", ["open": true, "label": "In focus", "back": "Back to page"])
        let options = try XCTUnwrap((opened as? [String: Any])?["options"] as? [String: Any])
        XCTAssertEqual(options["font"] as? String, "Leland")
        XCTAssertGreaterThan((options["pageWidth"] as? NSNumber)?.intValue ?? 0, 0)
        let layout = String(decoding: try JSONSerialization.data(withJSONObject: options), as: UTF8.self)
        let id = piece.id
        let first = second.firstBar
        let last = second.lastBar
        let engraved = await MusicCore.run { Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: layout) }
        let card = try engraved.get()
        XCTAssertEqual(card.pages.count, 1, "one page as tall as the phrase needs")
        let cardMap = try JSONSerialization.jsonObject(with: Data(card.timemap.utf8))
        let notes = try await call(view, "card", ["pages": card.pages, "timemap": cardMap, "start": second.onset]) as? NSNumber
        XCTAssertGreaterThan(notes?.intValue ?? 0, 0)
        let onCard = try await view.evaluateJavaScript("document.querySelectorAll('#focus .card-score svg').length") as? NSNumber
        XCTAssertEqual(onCard?.intValue, 1)
        let veiled = try await view.evaluateJavaScript("document.getElementById('focus').hidden") as? Bool
        XCTAssertEqual(veiled, false)

        // Lead-in takes in the bar before and the bar after.
        let led = try await call(view, "lead", ["start": second.onset, "end": second.end])
        let wider = try XCTUnwrap(led as? [String: Any])
        XCTAssertLessThan((wider["start"] as? NSNumber)?.doubleValue ?? .infinity, second.onset)
        XCTAssertGreaterThan((wider["end"] as? NSNumber)?.doubleValue ?? 0, second.end)

        _ = try await call(view, "focus", ["open": false])
        let gone = try await view.evaluateJavaScript("document.getElementById('focus').hidden") as? Bool
        XCTAssertEqual(gone, true)
    }

    func testTheInksAreTheDesignKits() {
        XCTAssertNotEqual(DesignTokens.color("ink-carmine"), .systemGray)
        XCTAssertNotEqual(DesignTokens.color("amber"), .systemGray)
        XCTAssertEqual(DesignTokens.color("no-such-token"), .systemGray)
    }
}
