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
    @Published private(set) var markCount = 0
    @Published private(set) var canUndo = false
    @Published private(set) var problem: String?

    let webView: WKWebView
    private let canvas = PKCanvasView()
    private var piece: OpenPiece?
    private var dark = false
    private var pageReady = false
    private var shown = false
    private var clearing = false
    private var previousTool = Tool.eraser
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
        canvas.drawingPolicy = .pencilOnly
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
            let marks = await MusicCore.run { MusicCore.call(piece: piece.id, "marks") }
            await draw(marks)
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

/// The design kit's colours (design/kit/tokens.css, copied into the app), so Swift defines none of its own (0018).
enum DesignTokens {
    private static let light: [String: String] = {
        guard let url = Bundle.main.url(forResource: "tokens", withExtension: "css"),
              let css = try? String(contentsOf: url, encoding: .utf8) else { return [:] }
        // The first block is Daylight; the first value of each name wins.
        var values: [String: String] = [:]
        let pattern = try! NSRegularExpression(pattern: #"--([a-z0-9-]+):\s*(#[0-9A-Fa-f]{6})"#)
        for match in pattern.matches(in: css, range: NSRange(css.startIndex..., in: css)) {
            guard let name = Range(match.range(at: 1), in: css), let value = Range(match.range(at: 2), in: css) else { continue }
            let key = String(css[name])
            if values[key] == nil { values[key] = String(css[value]) }
        }
        return values
    }()

    /// A token's colour, such as "ink-carmine" or "amber"; grey when the token is missing, so a gap shows.
    static func color(_ name: String) -> UIColor {
        guard let hex = light[name], let value = UInt32(hex.dropFirst(), radix: 16) else { return .systemGray }
        return UIColor(
            red: CGFloat((value >> 16) & 0xFF) / 255,
            green: CGFloat((value >> 8) & 0xFF) / 255,
            blue: CGFloat(value & 0xFF) / 255,
            alpha: 1
        )
    }
}
