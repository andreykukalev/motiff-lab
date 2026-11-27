import UIKit
import WebKit

/// ScoreController's part of the pen and the input on the page (#479 items 7 and 18, BUG-05; PenModel owns the pen): a
/// stroke finished or the eraser moved, in the page's pixels; who writes, selects and scrolls (InputRules) and the mode
/// as the page has it; the Pen on and turned; where the handles are on screen; how the stroke being written looks.
extension ScoreController {
    /// A stroke the pen finished (InkInput), its points in the web view's coordinates: in the page's pixels, then kept as
    /// a mark. The pen covers the whole page, so the Pencil writes in the margins too: a stroke begun left or right of a
    /// line is kept against the line's first or last bar and drawn from that edge of its line in any layout (`anchorMark`
    /// and `placeMark` in pencil-marks.ts), and music-core keeps its points thinned (`ink.add`). The stroke written stays
    /// over the page until the page draws it as kept.
    func strokeEnded(_ samples: [InkSample]) {
        guard let piece, shown, !samples.isEmpty else {
            ink.live.dropOldest()
            return
        }
        let points: [[String: Double]] = samples.map { sample in
            var point = pagePoint(sample.point)
            point["pressure"] = sample.pressure
            return point
        }
        penModel.keep(points, in: piece.id) { [weak self] in self?.ink.live.dropOldest() }
    }

    /// A point of the web view in the page's pixels, as the page measures its bars and marks.
    private func pagePoint(_ point: CGPoint) -> [String: Double] {
        let zoom = max(webView.scrollView.zoomScale, 0.01)
        let inPage = webView.scrollView.convert(point, from: webView)
        return ["x": inPage.x / zoom, "y": inPage.y / zoom]
    }

    /// The eraser moved (InkInput, BUG-05): its points go to the pen in the page's pixels (PenModel.rub).
    func rubbed(_ points: [CGPoint], lifted: Bool) {
        penModel.rub(points.map(pagePoint), lifted: lifted, in: piece?.id)
    }

    /// Who writes, selects and scrolls (InputRules, #479 item 18): the pen's gesture and the scroll view take the
    /// pointers the rules give them, and the page is told how a finger and the Pencil select and tap. Again when the app
    /// comes back, as the iPad's setting «Only draw with Apple Pencil» may have changed.
    func applyInput() {
        let tool = InputRules.Tool(mode: mode, penOn: penModel.penOn)
        let rules = InputRules(stylus: InkInput.stylusPreferred, tool: tool, colour: penModel.pen.colouring?.rawValue)
        ink.apply(rules)
        toolChanged?(mode, penModel.penOn)
        guard shown else { return }
        Task { await scorePage.send(.input, rules.page) }
    }

    /// Whether the bar's Pen is on (PenModel.penOn), for Expand and the marks (`FocusScore`, `MarksScore`), which may not
    /// name the pen.
    var penOn: Bool { penModel.penOn }

    /// The bar's Pen turned on or off (PenModel.penOn): who writes and scrolls, the roll's rules and the page follow.
    func penTurned(_ on: Bool) {
        applyInput()
        penChanged?(on)
        if shown { Task { await scorePage.send(.pen, on) } }
        sendMode()
    }

    /// Where the Pencil hovers over the window, for the stylus menu's place over the roll and the rest of the screen (#521 7).
    func watchStylus(in window: UIWindow?) {
        ink.pencil.watch(window)
    }

    /// A stroke of the pen began or ended (#521 10): while it is written playback does not move the page under it, and it
    /// follows again as long after as after a scroll by hand.
    func penDown(_ down: Bool) {
        guard shown else { return }
        Task { await scorePage.send(.writing, ["on": down]) }
    }

    /// Handles, given in the document's pixels, where they are on the screen now, in the web view's coordinates.
    func inView(_ spots: [CGPoint]) -> [CGPoint] {
        let zoom = webView.scrollView.zoomScale
        return spots.map { webView.scrollView.convert(CGPoint(x: $0.x * zoom, y: $0.y * zoom), to: webView) }
    }

    /// The mode as the page has it: "pen" while the Pen is on.
    func sendMode() {
        guard shown else { return }
        Task { await sendModeNow() }
    }

    func sendModeNow() async {
        let data: [String: Any] = ["name": penModel.penOn ? InteractionMode.pen.rawValue : mode.rawValue]
        await scorePage.send(.mode, data)
    }

    /// How the stroke beginning now looks while it is written, at the page's zoom and in its appearance (PenModel.liveStyle).
    func liveStyle(at point: CGPoint) -> LiveInkView.Style? {
        penModel.liveStyle(
            zoom: webView.scrollView.zoomScale, staffSpace: staffSpace, dark: dark,
            origin: webView.scrollView.convert(CGPoint.zero, to: webView)
        )
    }
}
