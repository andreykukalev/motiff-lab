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
        try await webPage("Web/score.html", ready: "window.motiff")
    }

    /// A page of the app's in a web view on screen, once the object it fills has appeared.
    private func webPage(_ path: String, ready: String) async throws -> WKWebView {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        let view = WKWebView(frame: CGRect(x: 0, y: 0, width: 1024, height: 1366), configuration: configuration)
        let window = UIWindow(frame: view.frame)
        window.addSubview(view)
        window.isHidden = false
        self.window = window
        view.load(URLRequest(url: BundleSchemeHandler.url(path)))
        // The first web view of a run can take a while on a CI runner, as WebKit starts its processes.
        for _ in 0..<300 {
            if (try? await view.evaluateJavaScript("typeof \(ready)")) as? String == "object" {
                return view
            }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let problem = (try? await view.evaluateJavaScript("document.documentElement.outerHTML.slice(0, 300)")) as? String
        XCTFail("The page did not load its modules: \(problem ?? "no page")")
        throw URLError(.resourceUnavailable)
    }

    private func call(_ view: WKWebView, _ function: String, _ data: Any) async throws -> Any? {
        try await view.callAsyncJavaScript("return motiff.\(function)(data)", arguments: ["data": data], contentWorld: .page)
    }

    func testAStrokeIsKeptAgainstItsBarSavedByMusicCoreAndDrawn() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
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

    /// A stroke begun in a line's margin (v0.4) is kept against the line's first bar (u below 0) or its last (u above 1),
    /// drawn where it was written, and music-core keeps it thinned.
    func testAStrokeInALinesMarginIsKeptAgainstTheLinesEdgeBar() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        let placed = try await view.callAsyncJavaScript("return motiff.frames()", contentWorld: .page)
        let frames = try XCTUnwrap(placed as? [[String: Any]])
        func value(_ frame: [String: Any], _ key: String) -> Double { (frame[key] as? NSNumber)?.doubleValue ?? .nan }
        let line = frames.filter { value($0, "line") == 0 }
        let first = try XCTUnwrap(line.min { value($0, "left") < value($1, "left") })
        let last = try XCTUnwrap(line.max { value($0, "right") < value($1, "right") })
        XCTAssertGreaterThan(line.count, 1, "the first line has several bars")
        XCTAssertEqual(value(first, "lineLeft"), value(first, "left"))
        XCTAssertEqual(value(last, "lineRight"), value(last, "right"))

        // A short line down the margin, a few pixels out from each edge of the line, at the height of its middle staff line.
        func stroke(_ x: Double, _ frame: [String: Any]) -> [[String: Double]] {
            let y = value(frame, "staffTop") + 2 * value(frame, "space")
            return (0..<40).map { ["x": x, "y": y + Double($0) * 0.5, "pressure": 0.5] }
        }
        let leftX = value(first, "left") - 12
        let rightX = value(last, "right") + 12
        let leftAnchored = try await call(view, "anchor", ["points": stroke(leftX, first), "tool": "pen", "colour": "graphite"])
        let rightAnchored = try await call(view, "anchor", ["points": stroke(rightX, last), "tool": "pen", "colour": "carmine"])
        let left = try XCTUnwrap(leftAnchored as? [String: Any])
        let right = try XCTUnwrap(rightAnchored as? [String: Any])
        XCTAssertEqual((left["bar"] as? NSNumber)?.doubleValue, value(first, "tick"))
        XCTAssertLessThan((left["u"] as? NSNumber)?.doubleValue ?? 0, 0)
        XCTAssertEqual((right["bar"] as? NSNumber)?.doubleValue, value(last, "tick"))
        XCTAssertGreaterThan((right["u"] as? NSNumber)?.doubleValue ?? 0, 1)

        // The mark goes to music-core as the page made it, with the margin it was begun in (v0.4): set here where the page
        // does not set it yet.
        var leftMark = left, rightMark = right
        if leftMark["margin"] == nil { leftMark["margin"] = "left" }
        if rightMark["margin"] == nil { rightMark["margin"] = "right" }
        let request = String(decoding: try JSONSerialization.data(withJSONObject: ["strokes": [leftMark, rightMark]]), as: UTF8.self)
        let added = await MusicCore.run { MusicCore.call(piece: piece.id, "ink.add", request) }
        let answer = try XCTUnwrap(facadeObject(added))
        let ink = try XCTUnwrap(answer["ink"] as? [[String: Any]], "\(answer)")
        XCTAssertEqual(ink.count, 2)
        XCTAssertEqual(ink.compactMap { $0["margin"] as? String }.sorted(), ["left", "right"], "music-core keeps and answers each stroke's margin")
        // Forty points on a straight line are kept as its two ends.
        XCTAssertEqual((ink.first?["points"] as? [Any])?.count, 3, "the straight stroke is thinned to its ends")

        let drawn = try await call(view, "draw", ["marks": ink]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, 2)
        // Each is drawn where it was written, in the margin outside the line.
        for (id, x) in [(left["id"] as? String ?? "?", leftX), (right["id"] as? String ?? "?", rightX)] {
            let box = try await view.evaluateJavaScript("(() => { const b = document.querySelector('#ink path[data-id=\"\(id)\"]').getBBox(); return [b.x, b.width]; })()") as? [NSNumber]
            let centre = (box?[0].doubleValue ?? 0) + (box?[1].doubleValue ?? 0) / 2
            XCTAssertEqual(centre, x, accuracy: 2, "the stroke \(id) sits where it was written")
        }
    }

    func testTheBarNamesThePagesUnderTheReadingLine() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
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
        let example = try XCTUnwrap(KitPiece.example)
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
        let example = try XCTUnwrap(KitPiece.example)
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
        let onCard = try await view.evaluateJavaScript("document.querySelectorAll('#focus .card-score > .card-page > svg').length") as? NSNumber
        XCTAssertEqual(onCard?.intValue, card.pages.count)
        let veiled = try await view.evaluateJavaScript("document.getElementById('focus').hidden") as? Bool
        XCTAssertEqual(veiled, false)

        // A variation tried is engraved above the phrase as an ossia, framed and named on the card.
        _ = MusicCore.call(piece: piece.id, "select", "{\"span\": {\"onset\": \(Int(second.selectableOnset)), \"end\": \(Int(second.selectableEnd))}}")
        let understanding = try XCTUnwrap(Understanding(answer: MusicCore.call(piece: piece.id, "understand")))
        let way = try XCTUnwrap(understanding.facts.first { !$0.tries.isEmpty }?.tries.first)
        XCTAssertNil(facadeObject(MusicCore.call(piece: piece.id, "try", "{\"chip\": \"\(way.id)\"}"))?["error"])
        let withOssia = await MusicCore.run { Engraver.engraveCard(piece: id, firstBar: first, lastBar: last, options: layout, ossia: true) }
        let ossiaCard = try withOssia.get()
        let ossias = ossiaCard.ossias.map { ["staves": $0.staves, "prefix": $0.prefix, "label": "Ossia · \(way.label)"] as [String: Any] }
        XCTAssertFalse(ossias.isEmpty)
        let ossiaMap = try JSONSerialization.jsonObject(with: Data(ossiaCard.timemap.utf8))
        _ = try await call(view, "card", ["pages": ossiaCard.pages, "timemap": ossiaMap, "start": second.onset, "ossias": ossias])
        let frames = try await view.evaluateJavaScript("document.querySelectorAll('#focus .ossia-frame').length") as? NSNumber
        XCTAssertGreaterThan(frames?.intValue ?? 0, 0)
        _ = MusicCore.call(piece: piece.id, "variation.clear")

        // Lead-in takes in the bar before and the bar after.
        let led = try await call(view, "lead", ["start": second.onset, "end": second.end])
        let wider = try XCTUnwrap(led as? [String: Any])
        XCTAssertLessThan((wider["start"] as? NSNumber)?.doubleValue ?? .infinity, second.onset)
        XCTAssertGreaterThan((wider["end"] as? NSNumber)?.doubleValue ?? 0, second.end)

        _ = try await call(view, "focus", ["open": false])
        let gone = try await view.evaluateJavaScript("document.getElementById('focus').hidden") as? Bool
        XCTAssertEqual(gone, true)
    }

    func testThePianoRollDrawsMusicCoresNotesAndPicksBars() async throws {
        let view = try await webPage("Web/roll.html", ready: "window.roll")
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let roll = try XCTUnwrap(facadeObject(MusicCore.call(piece: piece.id, "roll")))
        XCTAssertEqual((roll["ids"] as? [Any])?.count, (roll["notes"] as? [Any]).map { $0.count / 7 })

        let loaded = try await view.callAsyncJavaScript(
            "return roll.load(data)", arguments: ["data": ["roll": roll, "labels": RollController.labels(UIText(language: "en")), "noteNames": true, "dark": false]], contentWorld: .page
        )
        let shown = try XCTUnwrap(loaded as? [String: Any])
        XCTAssertEqual(shown["colourBy"] as? String, "hand", "Für Elise has two hands")
        let canvases = try await view.evaluateJavaScript("document.querySelectorAll('#roll > canvas.roll-notes, #roll > canvas.roll-keys').length") as? NSNumber
        XCTAssertEqual(canvases?.intValue, 2, "the notes and the keyboard")
        let zoom = try await view.evaluateJavaScript("document.querySelector('#roll > .roll-zoom')?.textContent ?? ''") as? String
        XCTAssertEqual(zoom?.contains("100 %"), true, "the floating zoom over the notes")
        // In phrase focus the roll lays each selection across itself (testing item 12).
        _ = try await view.callAsyncJavaScript("return roll.fits(on)", arguments: ["on": true], contentWorld: .page)
        let firstBar: [String: Any] = ["selected": ["start": 0, "end": 1440, "ids": Array((roll["ids"] as? [String] ?? []).prefix(4))]]
        _ = try await view.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": firstBar], contentWorld: .page)
        _ = try await view.callAsyncJavaScript("return roll.fits(on)", arguments: ["on": false], contentWorld: .page)
        // Read by touch (#338), the drawer's roll fits each new selection too: the zoom leaves 100 %, and the same
        // selection sent again (as a loop starting does) leaves it where it is.
        let twoBars: [String: Any] = ["selected": ["start": 0, "end": 2880, "ids": Array((roll["ids"] as? [String] ?? []).prefix(8))]]
        _ = try await view.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": twoBars], contentWorld: .page)
        try await Task.sleep(nanoseconds: 300_000_000)
        let fitted = try await view.evaluateJavaScript("document.querySelector('#roll .roll-zoom-level')?.textContent ?? ''") as? String
        XCTAssertNotEqual(fitted?.contains("100 %"), true, "a new selection is fitted on the roll")
        _ = try await view.callAsyncJavaScript("return roll.selection(data)", arguments: ["data": twoBars], contentWorld: .page)
        try await Task.sleep(nanoseconds: 300_000_000)
        let kept = try await view.evaluateJavaScript("document.querySelector('#roll .roll-zoom-level')?.textContent ?? ''") as? String
        XCTAssertEqual(kept, fitted, "the same selection again leaves the roll as it is")
        let colour = try await view.callAsyncJavaScript("return roll.nextColour()", contentWorld: .page) as? String
        XCTAssertNotNil(colour)

        // The places of a fact chosen in Insights light on the roll too.
        let place: [String: Any] = ["onset": 0, "end": 1440, "noteIds": Array((roll["ids"] as? [String] ?? []).prefix(4))]
        _ = try await view.callAsyncJavaScript("return roll.highlight(data)", arguments: ["data": ["places": [place]]], contentWorld: .page)
        _ = try await view.callAsyncJavaScript("return roll.highlight(data)", arguments: ["data": ["places": [Any]()]], contentWorld: .page)
    }

    func testAFactRingsItsPlacesOnTheScore() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])
        let second = try XCTUnwrap(PhraseLevelsShown(answer: MusicCore.call(piece: piece.id, "phrases"))?.unit(2))
        _ = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece.id, "select", "{\"span\": {\"onset\": \(Int(second.selectableOnset)), \"end\": \(Int(second.selectableEnd))}}")))
        let understanding = try XCTUnwrap(Understanding(answer: MusicCore.call(piece: piece.id, "understand")))

        // The motif's returns: every engraved note of every place is ringed, each with its box.
        let returns = try XCTUnwrap(understanding.facts.first { $0.id == "structure.recurrences" })
        let places = returns.places.map { ["onset": $0.onset, "end": $0.end, "noteIds": $0.noteIds] as [String: Any] }
        let ringed = try await call(view, "highlight", ["places": places, "scroll": true]) as? NSNumber
        XCTAssertGreaterThanOrEqual(ringed?.intValue ?? 0, Set(returns.places.flatMap(\.noteIds)).count)
        let boxes = try await view.evaluateJavaScript("document.querySelectorAll('body > .ring').length") as? NSNumber
        XCTAssertEqual(boxes?.intValue, ringed?.intValue)

        // A place without notes, such as a phrase, rings every note in its stretch of time.
        let phrases = try XCTUnwrap(understanding.facts.first { $0.places.contains { $0.noteIds.isEmpty } })
        let stretch = try XCTUnwrap(phrases.places.first { $0.noteIds.isEmpty })
        let timed = try await call(view, "highlight", ["places": [["onset": stretch.onset, "end": stretch.end, "noteIds": [String]()]], "scroll": false]) as? NSNumber
        XCTAssertGreaterThan(timed?.intValue ?? 0, 0)

        let cleared = try await call(view, "highlight", ["places": [Any](), "scroll": false]) as? NSNumber
        XCTAssertEqual(cleared?.intValue, 0)
        let left = try await view.evaluateJavaScript("document.querySelectorAll('body > .ring, .highlight').length") as? NSNumber
        XCTAssertEqual(left?.intValue, 0)
    }

    func testBookmarksAreBracketsAboveThePhrasesAndThePagesHavePictures() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        // The form found in the piece, filed as Structure bookmarks: a bracket for each, titled at its left end.
        let shelf = try XCTUnwrap(BookmarkShelf(answer: MusicCore.call(piece: piece.id, "bookmark.form")))
        XCTAssertFalse(shelf.bookmarks.isEmpty)
        let items = shelf.bookmarks.map { bookmark in
            ["id": bookmark.id, "start": bookmark.onset, "end": bookmark.end, "title": bookmark.title, "ink": shelf.category(of: bookmark)?.ink ?? "graphite", "label": bookmark.title] as [String: Any]
        }
        let drawn = try await call(view, "bookmarks", ["items": items]) as? NSNumber
        XCTAssertGreaterThanOrEqual(drawn?.intValue ?? 0, items.count)
        // Every run is titled: one across a system break repeats its title after "…" on the next system.
        let titled = try await view.evaluateJavaScript("document.querySelectorAll('body > .bmb .w').length") as? NSNumber
        XCTAssertEqual(titled?.intValue, drawn?.intValue)
        let continued = try await view.evaluateJavaScript("[...document.querySelectorAll('body > .bmb .w')].filter(w => w.textContent.startsWith('…')).length") as? NSNumber
        XCTAssertEqual(continued?.intValue, (drawn?.intValue ?? 0) - items.count)
        let lifted = try await view.evaluateJavaScript("getComputedStyle(document.querySelector('body > .bmb')).borderTopStyle") as? String
        XCTAssertEqual(lifted, "solid")

        // The Pages tab: each page with its stretch of the piece and its picture.
        let pages = try await call(view, "outline", ["width": 280]) as? [[String: Any]]
        XCTAssertEqual(pages?.count, piece.pages.count)
        XCTAssertTrue((pages?.first?["image"] as? String ?? "").hasPrefix("data:image/png"), "the page is drawn as a picture")
        XCTAssertEqual((pages?.first?["start"] as? NSNumber)?.intValue, 0)
        let shown = try await call(view, "showPage", ["page": 2]) as? Bool
        XCTAssertEqual(shown, true)
    }

    func testMarginNotesAreMarkersHighlightsAreBandsAndLayersHide() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        let bars = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece.id, "select", #"{"bars": {"first": 2, "last": 3}}"#)))
        let onset = bars.onset
        let end = bars.end
        let drawn = try await call(view, "notes", [
            "notes": [["id": "n1", "tick": onset, "number": 1, "label": "Note 1"], ["id": "n2", "tick": onset, "number": 2, "label": "Note 2"]],
            "highlights": [["id": "h1", "start": onset, "end": end, "ink": "forest"]],
        ]) as? NSNumber
        XCTAssertGreaterThanOrEqual(drawn?.intValue ?? 0, 3)
        let markers = try await view.evaluateJavaScript("[...document.querySelectorAll('body > .marker')].map(m => m.textContent + '@' + Math.round(parseFloat(m.style.left)))") as? [String]
        XCTAssertEqual(markers?.count, 2)
        XCTAssertEqual(markers?.map { String($0.prefix(1)) }, ["1", "2"])
        XCTAssertNotEqual(markers?.first?.split(separator: "@").last, markers?.last?.split(separator: "@").last, "two notes on one bar sit side by side")
        let band = try await view.evaluateJavaScript("document.querySelector('body > .hl.ink-forest') !== null") as? Bool
        XCTAssertEqual(band, true)

        // Hidden layers leave a clean page; shown again, the marks come back.
        let hidden = try await call(view, "layers", ["hidden": ["notes", "ink"]]) as? [String]
        XCTAssertEqual(hidden, ["ink", "notes"])
        let gone = try await view.evaluateJavaScript("getComputedStyle(document.querySelector('body > .marker')).display") as? String
        XCTAssertEqual(gone, "none")
        _ = try await call(view, "layers", ["hidden": [String]()])
        let back = try await view.evaluateJavaScript("getComputedStyle(document.querySelector('body > .marker')).display") as? String
        XCTAssertNotEqual(back, "none")
    }

    func testCheckFlagsSitAtTheBarsTopRightBesideItsNotesAndHideAsALayer() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        let bar = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece.id, "select", #"{"bars": {"first": 3, "last": 3}}"#)))
        let drawn = try await call(view, "checks", ["flags": [["bar": 3, "tick": bar.onset, "label": "Check bar 3: Bar too long"]]]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, 1)
        _ = try await call(view, "notes", ["notes": [["id": "n1", "tick": bar.onset, "number": 1, "label": "Note 1"]], "highlights": [[String: Any]]()])
        let flagLeft = try await view.evaluateJavaScript("parseFloat(document.querySelector('body > .check-flag').style.left)") as? NSNumber
        let noteLeft = try await view.evaluateJavaScript("parseFloat(document.querySelector('body > .marker').style.left)") as? NSNumber
        XCTAssertLessThan(noteLeft?.doubleValue ?? .infinity, flagLeft?.doubleValue ?? 0, "the note sits to the flag's left")

        _ = try await call(view, "layers", ["hidden": ["check"]])
        let hidden = try await view.evaluateJavaScript("getComputedStyle(document.querySelector('body > .check-flag')).display") as? String
        XCTAssertEqual(hidden, "none")
        // Settings can turn the phrase brackets off, and a slate page shows light notes on a dark page.
        let hiddenNow = try await call(view, "layers", ["hidden": ["brackets"], "slate": true]) as? [String]
        XCTAssertEqual(hiddenNow, ["brackets"])
        let page = try await view.evaluateJavaScript("document.documentElement.getAttribute('data-page')") as? String
        XCTAssertEqual(page, "slate")
        _ = try await call(view, "checks", ["flags": [[String: Any]]()])
        let gone = try await view.evaluateJavaScript("document.querySelectorAll('body > .check-flag').length") as? NSNumber
        XCTAssertEqual(gone?.intValue, 0)
    }

    func testAnotherVersionsLabelsSitAboveItsBarsAndItsMissingNotesAreTinted() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        let bar = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece.id, "select", #"{"bars": {"first": 5, "last": 5}}"#)))
        let missing = try XCTUnwrap(bar.noteIds.first)
        let marker: [String: Any] = ["bar": 5, "tick": bar.onset, "text": "+3 · added notes", "label": "Bar 5 in Cover: +3 · added notes"]
        let drawn = try await call(view, "performance", ["markers": [marker], "unheard": [missing]]) as? NSNumber
        XCTAssertEqual(drawn?.intValue, 1)
        let label = try await view.evaluateJavaScript("document.querySelector('body > .perf-marker').getAttribute('aria-label')") as? String
        XCTAssertEqual(label, "Bar 5 in Cover: +3 · added notes")
        let tinted = try await view.evaluateJavaScript("document.querySelectorAll('#pages .not-heard').length") as? NSNumber
        XCTAssertGreaterThan(tinted?.intValue ?? 0, 0, "the printed note the version does not have is tinted")
        // A Check flag on the same bar keeps its place: the label stays to its left.
        _ = try await call(view, "checks", ["flags": [["bar": 5, "tick": bar.onset, "label": "Check bar 5"]]])
        let labelLeft = try await view.evaluateJavaScript("parseFloat(document.querySelector('body > .perf-marker').style.left)") as? NSNumber
        let flagLeft = try await view.evaluateJavaScript("parseFloat(document.querySelector('body > .check-flag').style.left)") as? NSNumber
        XCTAssertLessThan(labelLeft?.doubleValue ?? .infinity, flagLeft?.doubleValue ?? 0)

        // A bar taken from the version: a thin plum line under it (testing item 33).
        _ = try await call(view, "performance", ["markers": [[String: Any]](), "unheard": [String](), "taken": [bar.onset]])
        let lines = try await view.evaluateJavaScript("document.querySelectorAll('body > .taken-bar').length") as? NSNumber
        XCTAssertEqual(lines?.intValue, 1)

        _ = try await call(view, "performance", ["markers": [[String: Any]](), "unheard": [String]()])
        let gone = try await view.evaluateJavaScript("document.querySelectorAll('body > .perf-marker, #pages .not-heard, body > .taken-bar').length") as? NSNumber
        XCTAssertEqual(gone?.intValue, 0)
    }

    func testTheNotesSoundingAreMarkedWhilePlayingAndThePageFollows() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        // Bar 80, past the first page: its notes turn cobalt and the page follows them down (a system already in view
        // stays where it is, as on the web). The scroll is smooth, so the test checks where it goes, not where it is yet.
        let bar = try XCTUnwrap(ScoreSelection(answer: MusicCore.call(piece: piece.id, "select", #"{"bars": {"first": 80, "last": 80}}"#)))
        let followed = try await call(view, "playing", ["tick": bar.onset + 1, "playing": true, "follow": true]) as? NSNumber
        let marked = try await view.evaluateJavaScript("document.querySelectorAll('#pages .playing').length") as? NSNumber
        XCTAssertGreaterThan(marked?.intValue ?? 0, 0, "the notes sounding are marked")
        let colour = try await view.evaluateJavaScript("getComputedStyle(document.querySelector('#pages .playing')).fill") as? String
        XCTAssertNotNil(colour)
        let pageHeight = try await view.evaluateJavaScript("document.querySelector('#pages > .page').getBoundingClientRect().height") as? NSNumber
        let seen = try await view.evaluateJavaScript("JSON.stringify({ height: innerHeight, y: scrollY, playing: [...document.querySelectorAll('#pages .playing')].map((e) => `${e.id} ${e.tagName} ${Math.round(e.getBoundingClientRect().top)}`) })") as? String
        XCTAssertGreaterThan(followed?.doubleValue ?? 0, pageHeight?.doubleValue ?? .infinity, "the page follows the music past the first page: \(seen ?? "")")
        // A scroll by hand wins: the next notes are marked but the page stays where the player put it.
        _ = try await view.evaluateJavaScript("window.dispatchEvent(new Event('wheel')); 0")
        let later = try await call(view, "playing", ["tick": bar.onset + 960 * 4, "playing": true, "follow": false])
        XCTAssertTrue(later is NSNull || later == nil, "a scroll by hand is not undone")

        _ = try await call(view, "playing", ["tick": bar.onset + 1, "playing": false])
        let cleared = try await view.evaluateJavaScript("document.querySelectorAll('#pages .playing').length") as? NSNumber
        XCTAssertEqual(cleared?.intValue, 0, "nothing is marked once stopped")
        // The playhead stays where playing stopped, in graphite, so the place is not lost (testing item 28).
        let resting = try await view.evaluateJavaScript("(() => { const line = document.querySelector('body > .playhead'); return line !== null && !line.hidden && line.classList.contains('resting'); })()") as? Bool
        XCTAssertEqual(resting, true, "the playhead rests where playing stopped")
    }

    func testTheNotesPagesLeadToAndFromThePdfsPages() async throws {
        let view = try await scorePage()
        let example = try XCTUnwrap(KitPiece.example)
        let piece = try await OpenPiece.open(fileName: example.fileName, data: example.data, language: "en", title: example.title).get()
        defer { _ = MusicCore.close(piece: piece.id) }
        let timemap = try JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))
        _ = try await call(view, "show", ["pages": piece.pages, "timemap": timemap, "dark": false])

        // Where the notes on a page start, for PDF from Notes; a place in the piece brings its page up, for Notes from PDF.
        let first = try await call(view, "firstTickOn", ["page": 1]) as? NSNumber
        XCTAssertEqual(first?.doubleValue, 0)
        let onSecond = try await call(view, "firstTickOn", ["page": 2]) as? NSNumber
        let second = try XCTUnwrap(onSecond)
        XCTAssertGreaterThan(second.doubleValue, 0)
        let none = try await call(view, "firstTickOn", ["page": 99])
        XCTAssertTrue(none == nil || none is NSNull, "a page the score does not have starts nowhere")
        let shown = try await call(view, "showTick", ["tick": second.doubleValue + 1]) as? Bool
        XCTAssertEqual(shown, true)
    }

    func testTheInksAreTheDesignKits() {
        XCTAssertNotEqual(DesignTokens.color("ink-carmine"), .systemGray)
        XCTAssertNotEqual(DesignTokens.color("amber"), .systemGray)
        XCTAssertEqual(DesignTokens.color("no-such-token"), .systemGray)
    }
}
