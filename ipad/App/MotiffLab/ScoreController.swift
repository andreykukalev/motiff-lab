import PencilKit
import SwiftUI
import UniformTypeIdentifiers
import WebKit

/// The score and the pencil marks on it (decision record 0019, sections 1.4 and 2.2). The pages are Verovio's SVG in one
/// web view, which runs the web app's own score modules (Web/score.js over Scripts/score) to know where each bar is
/// and to draw the marks. The Apple Pencil writes with PencilKit for its low latency: when a stroke ends, its points
/// go to the page to be kept against the bar they start in, music-core saves the stroke in the project file
/// (`ink.add`, `marks.ink` in project file version 12), and the marks are drawn again from what music-core answers.
/// PencilKit's own drawing is cleared after every stroke, so a mark lives only in the project file and shows the
/// same in the web app. A finger never writes: it scrolls.
@MainActor
final class ScoreController: NSObject, ObservableObject {
    enum Tool: Equatable {
        case pen(Ink)
        case highlighter
        case eraser
    }

    /// The four inks of the design kit (0018 section 5); the names are the project file's.
    enum Ink: String, CaseIterable {
        case graphite, carmine, forest, ochre

        var labelKey: String {
            switch self {
            case .graphite: "pencil.penGraphite"
            case .carmine: "pencil.penCarmine"
            case .forest: "pencil.penForest"
            case .ochre: "pencil.penOchre"
            }
        }

        var color: UIColor { DesignTokens.color("ink-\(rawValue)") }
    }

    @Published var tool = Tool.pen(.graphite) {
        didSet {
            if oldValue != tool { previousTool = oldValue }
            applyTool()
        }
    }
    /// The bar's Pen (0018 section 5): while it is on, a finger writes too and two fingers scroll, so the score takes
    /// marks on any iPad, with or without a Pencil. The Pencil always writes.
    @Published var penOn = false {
        didSet {
            applyInput()
            if shown { Task { _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page) } }
        }
    }
    /// The phrases at the level shown, drawn as brackets on the score (0018 section 3).
    @Published private(set) var phrases: PhraseLevelsShown?
    /// The passage selected on the score: a bracket tapped, a bar tapped, or bars chosen by a long press and a drag.
    @Published private(set) var selection: ScoreSelection?
    /// Where the selection menu's top left corner goes in the score's view, as the web app places its menu; nil while
    /// nothing is selected or a drag is on.
    @Published private(set) var menuPlace: CGPoint?
    /// A note tapped on the score: the player plays from it.
    var noteTapped: (@MainActor (Double) -> Void)?
    @Published private(set) var markCount = 0
    @Published private(set) var canUndo = false
    @Published private(set) var problem: String?
    /// The pages under the reading line, for the bar: "p. 2 of 4", or "pp. 1–2 of 4" with two to a row.
    @Published private(set) var pages = PagesInView()

    struct PagesInView: Equatable {
        var first = 0
        var last = 0
        var count = 0
    }

    let webView: WKWebView
    private let canvas = PKCanvasView()
    private var piece: OpenPiece?
    private var dark = false
    private var pageReady = false
    private var shown = false
    private var clearing = false
    private var previousTool = Tool.eraser
    private let text = UIText.current
    /// Pixels between two staff lines on screen, for the width of the live stroke.
    private var staffSpace: CGFloat = 8
    /// The strokes added here, newest last, for Undo.
    private var added: [String] = []

    override init() {
        let configuration = WKWebViewConfiguration()
        configuration.setURLSchemeHandler(BundleSchemeHandler(), forURLScheme: BundleSchemeHandler.scheme)
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.configuration.userContentController.add(MessageRelay(self), name: "score")
        webView.isOpaque = false
        webView.backgroundColor = .clear

        // The canvas lies over the web view and takes only the Pencil: its drawing gesture is moved to the web view,
        // so a finger still reaches the web view and scrolls the score.
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        applyInput()
        canvas.isScrollEnabled = false
        canvas.delegate = self
        canvas.frame = webView.bounds
        canvas.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        canvas.isUserInteractionEnabled = false
        webView.addSubview(canvas)
        webView.addGestureRecognizer(canvas.drawingGestureRecognizer)
        let pencil = UIPencilInteraction()
        pencil.delegate = self
        webView.addInteraction(pencil)
        applyTool()

        webView.load(URLRequest(url: BundleSchemeHandler.url("Web/score.html")))
    }

    /// Shows a piece's pages and its marks; again when the appearance changes.
    func show(_ piece: OpenPiece, dark: Bool) {
        guard self.piece?.id != piece.id || self.dark != dark || !shown else { return }
        self.piece = piece
        self.dark = dark
        if pageReady { Task { await render() } }
    }

    func undo() {
        guard let last = added.popLast(), let piece else { return }
        canUndo = !added.isEmpty
        Task { await change(piece.id, "ink.remove", ["ids": [last]]) }
    }

    // MARK: - The page

    /// What the page reports: the pages in view, where the menu goes, and what the player did on the score.
    fileprivate func received(_ message: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        if let report = message["pages"] as? [String: Any] {
            pages = PagesInView(first: Int(number(report["first"]) ?? 0), last: Int(number(report["last"]) ?? 0), count: Int(number(report["count"]) ?? 0))
        }
        if message.keys.contains("menu") {
            let place = message["menu"] as? [String: Any]
            menuPlace = place.flatMap { place in number(place["left"]).map { CGPoint(x: $0, y: number(place["top"]) ?? 0) } }
        }
        if let chosen = message["chosen"] as? [String: Any], let start = number(chosen["start"]), let end = number(chosen["end"]) {
            Task { await select(start, end) }
        }
        if let bar = message["bar"] as? [String: Any], let start = number(bar["start"]), let end = number(bar["end"]) {
            Task { await select(start, end) }
        }
        if let bracket = number(message["bracket"]) {
            selectPhrase(Int(bracket))
        }
        if let note = number(message["note"]) {
            noteTapped?(note)
        }
        if message["blank"] != nil {
            clearSelection()
        }
    }

    // MARK: - Phrases and the selection

    /// Shows another level: the brackets are drawn again, and the selection moves to the unit that is, encloses or starts
    /// it at the new level.
    func showLevel(_ level: String) {
        guard let piece, level != phrases?.level else { return }
        let request = "{\"level\": \"\(level)\"}"
        Task {
            let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases.level", request) }
            if let shown = PhraseLevelsShown(answer: answer) { phrases = shown }
            if selection != nil, let moved = ScoreSelection(answer: answer) { selection = moved }
            await showSelection()
        }
    }

    /// A bracket tapped: its unit is selected.
    func selectPhrase(_ number: Int) {
        guard let unit = phrases?.unit(number) else { return }
        Task { await select(unit.selectableOnset, unit.selectableEnd) }
    }

    /// Selects a passage through music-core, which keeps it within what a selection may hold.
    func select(_ start: Double, _ end: Double) async {
        guard let piece else { return }
        let request = "{\"span\": {\"onset\": \(Int(start)), \"end\": \(Int(end))}}"
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "select", request) }
        guard let chosen = ScoreSelection(answer: answer) else {
            problem = facadeObject(answer)?["message"] as? String
            return
        }
        selection = chosen
        await showSelection()
    }

    func clearSelection() {
        guard selection != nil else { return }
        selection = nil
        Task { await showSelection() }
    }

    /// The size of the selection menu, for the page to place it over the band.
    func menuSize(_ size: CGSize) {
        guard shown, size.width > 0 else { return }
        Task {
            _ = try? await webView.callAsyncJavaScript(
                "return motiff.menuSize(data)", arguments: ["data": ["width": size.width, "height": size.height]], contentWorld: .page
            )
        }
    }

    private func loadPhrases(_ piece: OpenPiece) async {
        let answer = await MusicCore.run { MusicCore.call(piece: piece.id, "phrases") }
        phrases = PhraseLevelsShown(answer: answer)
        await showSelection()
    }

    /// Draws the brackets, the selected one in the accent, and the band over the selection.
    private func showSelection() async {
        guard shown else { return }
        let units: [[String: Any]] = (phrases?.units ?? []).map { unit in
            let current = selection.map { $0.onset == unit.selectableOnset && $0.end == unit.selectableEnd } ?? false
            return ["start": unit.onset, "end": unit.end, "number": unit.number, "kind": unit.kind, "current": current, "label": label(of: unit)]
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.phrases(data)", arguments: ["data": ["units": units]], contentWorld: .page)
        var band: Any = NSNull()
        if let selection {
            band = ["start": selection.onset, "end": selection.end, "startLabel": text["selection.handleStart"], "endLabel": text["selection.handleEnd"]] as [String: Any]
        }
        _ = try? await webView.callAsyncJavaScript("return motiff.select(data)", arguments: ["data": band], contentWorld: .page)
    }

    /// What a screen reader says for a bracket: "Phrase 3, bars 9–12".
    private func label(of unit: PhraseUnit) -> String {
        let name = unit.name.prefix(1).uppercased() + unit.name.dropFirst()
        let bars = unit.firstBar == unit.lastBar
            ? text.format("bars.one", String(unit.firstBar))
            : text.format("bars.range", String(unit.firstBar), String(unit.lastBar))
        return text.format("phrases.bracketLabel", name, String(unit.number), bars)
    }

    fileprivate func pageDidLoad() {
        pageReady = true
        if piece != nil { Task { await render() } }
    }

    private func render() async {
        guard let piece else { return }
        let timemap = (try? JSONSerialization.jsonObject(with: Data(piece.timemap.utf8))) ?? []
        do {
            let laid = try await webView.callAsyncJavaScript(
                "return motiff.show(data)",
                arguments: ["data": ["pages": piece.pages, "timemap": timemap, "dark": dark]],
                contentWorld: .page
            ) as? [String: Any]
            if let space = (laid?["space"] as? NSNumber)?.doubleValue, space > 0 {
                staffSpace = space
                applyTool()
            }
            shown = true
            _ = try? await webView.callAsyncJavaScript("return motiff.pen(on)", arguments: ["on": penOn], contentWorld: .page)
            let marks = await MusicCore.run { MusicCore.call(piece: piece.id, "marks") }
            await draw(marks)
            await loadPhrases(piece)
        } catch {
            problem = error.localizedDescription
        }
    }

    /// Draws the marks music-core answered (`marks`, `ink.add`, `ink.remove`).
    private func draw(_ answer: String) async {
        guard let object = facadeObject(answer) else { return }
        if object["error"] != nil {
            problem = object["message"] as? String
            return
        }
        let ink = object["ink"] as? [Any] ?? []
        markCount = ink.count
        _ = try? await webView.callAsyncJavaScript("return motiff.draw(data)", arguments: ["data": ["marks": ink]], contentWorld: .page)
    }

    private func change(_ piece: Int32, _ call: String, _ request: [String: Any]) async {
        guard let data = try? JSONSerialization.data(withJSONObject: request) else { return }
        let text = String(decoding: data, as: UTF8.self)
        let answer = await MusicCore.run { MusicCore.call(piece: piece, call, text) }
        await draw(answer)
    }

    // MARK: - Strokes

    /// A stroke PencilKit finished: its points in the page's pixels, then kept as a mark or, with the eraser, what it
    /// passed over taken away.
    private func strokeEnded(_ stroke: PKStroke) {
        guard let piece, shown else { return }
        let zoom = max(webView.scrollView.zoomScale, 0.01)
        let points: [[String: Double]] = stroke.path.interpolatedPoints(by: .distance(1.5)).map { point in
            let inCanvas = point.location.applying(stroke.transform)
            let inPage = webView.scrollView.convert(inCanvas, from: canvas)
            // UITouch force is about 1 for an ordinary press: pressure 0.5, as the web takes a pen without pressure.
            return ["x": inPage.x / zoom, "y": inPage.y / zoom, "pressure": min(1, max(0, Double(point.force) / 2))]
        }
        guard !points.isEmpty else { return }
        let tool = tool
        Task {
            switch tool {
            case .eraser:
                let ids = try? await webView.callAsyncJavaScript("return motiff.erase(data)", arguments: ["data": ["points": points]], contentWorld: .page) as? [String]
                if let ids, !ids.isEmpty {
                    added.removeAll { ids.contains($0) }
                    canUndo = !added.isEmpty
                    await change(piece.id, "ink.remove", ["ids": ids])
                }
            case .pen(let ink):
                await add(piece.id, points, tool: "pen", colour: ink.rawValue)
            case .highlighter:
                await add(piece.id, points, tool: "highlighter", colour: "amber")
            }
        }
    }

    private func add(_ piece: Int32, _ points: [[String: Double]], tool: String, colour: String) async {
        let request: [String: Any] = ["points": points, "tool": tool, "colour": colour]
        guard let mark = try? await webView.callAsyncJavaScript("return motiff.anchor(data)", arguments: ["data": request], contentWorld: .page) as? [String: Any],
              let id = mark["id"] as? String
        else { return }
        added.append(id)
        canUndo = true
        await change(piece, "ink.add", ["strokes": [mark]])
    }

    /// Who writes and who scrolls. The Pencil only writes: the score never scrolls under it.
    private func applyInput() {
        let pan = webView.scrollView.panGestureRecognizer
        #if targetEnvironment(simulator)
        // The simulator has no Pencil and its mouse is a finger: there the mouse writes, and two fingers on the
        // trackpad (or the wheel) scroll the score.
        canvas.drawingPolicy = .anyInput
        pan.allowedTouchTypes = [NSNumber(value: UITouch.TouchType.indirectPointer.rawValue)]
        #else
        canvas.drawingPolicy = penOn ? .anyInput : .pencilOnly
        pan.allowedTouchTypes = [UITouch.TouchType.direct, .indirectPointer].map { NSNumber(value: $0.rawValue) }
        pan.minimumNumberOfTouches = penOn ? 2 : 1
        #endif
    }

    /// PencilKit draws the live stroke as the tool will look: the ink's colour, the web's thickness at this zoom.
    private func applyTool() {
        let zoom = webView.scrollView.zoomScale
        switch tool {
        case .pen(let ink):
            canvas.tool = PKInkingTool(.pen, color: ink.color, width: 0.16 * staffSpace * zoom * 1.6)
        case .highlighter:
            canvas.tool = PKInkingTool(.marker, color: DesignTokens.color("amber").withAlphaComponent(0.38), width: 1.1 * staffSpace * zoom)
        case .eraser:
            canvas.tool = PKInkingTool(.pen, color: DesignTokens.color("ink-graphite").withAlphaComponent(0.25), width: 20)
        }
    }
}

extension ScoreController: PKCanvasViewDelegate {
    nonisolated func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
        MainActor.assumeIsolated {
            guard !clearing, let stroke = canvasView.drawing.strokes.last else { return }
            clearing = true
            canvasView.drawing = PKDrawing()
            clearing = false
            strokeEnded(stroke)
        }
    }
}

extension ScoreController: UIPencilInteractionDelegate {
    /// A double tap on the Pencil does what the player chose in the iPad's settings, as in 0015.
    nonisolated func pencilInteractionDidTap(_ interaction: UIPencilInteraction) {
        MainActor.assumeIsolated {
            switch UIPencilInteraction.preferredTapAction {
            case .switchEraser:
                tool = tool == .eraser ? previousTool : .eraser
            case .switchPrevious:
                tool = previousTool
            default:
                break
            }
        }
    }
}

/// Passes the page's messages on without the web view keeping the controller alive.
private final class MessageRelay: NSObject, WKScriptMessageHandler {
    weak var controller: ScoreController?

    init(_ controller: ScoreController) {
        self.controller = controller
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        if message.body as? String == "ready" {
            MainActor.assumeIsolated { controller?.pageDidLoad() }
        } else if let body = message.body as? [String: Any] {
            MainActor.assumeIsolated { controller?.received(body) }
        }
    }
}

/// Serves the app's own files to the web view as `motiff://app/<path>`, so the score's modules load as modules do on
/// the web: Web/score.html, score/*.js (the web app's compiled Scripts/score) and tokens.css.
final class BundleSchemeHandler: NSObject, WKURLSchemeHandler {
    static let scheme = "motiff"

    static func url(_ path: String) -> URL {
        URL(string: "\(scheme)://app/\(path)")!
    }

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url, let root = Bundle.main.resourceURL else {
            task.didFailWithError(URLError(.badURL))
            return
        }
        let file = root.appendingPathComponent(String(url.path.drop { $0 == "/" })).standardizedFileURL
        guard file.path.hasPrefix(root.standardizedFileURL.path), let data = try? Data(contentsOf: file) else {
            task.didFailWithError(URLError(.fileDoesNotExist))
            return
        }
        let type: String = switch file.pathExtension {
        case "html": "text/html"
        case "js", "mjs": "text/javascript"
        case "css": "text/css"
        case "json": "application/json"
        default: UTType(filenameExtension: file.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        }
        task.didReceive(URLResponse(url: url, mimeType: type, expectedContentLength: data.count, textEncodingName: "utf-8"))
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}
}
