import PDFKit
import PencilKit
import UIKit

/// A stroke on a PDF's page, as music-core answers `document.marks` (0023, `DocumentMarks`): the pencil layer's shape with
/// the page in place of the bar. U is across from the page's left edge (0 to 1), V down from its top edge in the page's
/// own spaces (a hundred and twentieth of its width); the points after the first are dx, dy in spaces and a pressure.
struct DocumentStroke: Identifiable, Equatable, Sendable {
    var id: String
    var tool: String
    var colour: String
    var size: Double
    var page: Int
    var u: Double
    var v: Double
    var points: [Double]
    var pressure: Double

    /// A page's width in its own spaces, as the web app's PDF frames are.
    static let spacesAcross = 120.0

    static func list(_ marks: Any?) -> [DocumentStroke] {
        let ink = (marks as? [String: Any])?["ink"] as? [[String: Any]] ?? []
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        return ink.compactMap { entry in
            guard let id = entry["id"] as? String, let page = number(entry["page"]), let u = number(entry["u"]), let v = number(entry["v"]) else { return nil }
            return DocumentStroke(
                id: id, tool: entry["tool"] as? String ?? "pen", colour: entry["colour"] as? String ?? "graphite",
                size: number(entry["size"]) ?? 0.16, page: Int(page), u: u, v: v,
                points: (entry["points"] as? [Any] ?? []).compactMap(number), pressure: number(entry["pressure"]) ?? 0.5
            )
        }
    }

    /// The stroke's points in the page's own coordinates (PDF points, origin at the bottom left).
    func path(in box: CGRect) -> [CGPoint] {
        let space = box.width / Self.spacesAcross
        let start = CGPoint(x: box.minX + u * box.width, y: box.maxY - v * space)
        var path = [start]
        var index = 0
        while index + 1 < points.count {
            path.append(CGPoint(x: start.x + points[index] * space, y: start.y - points[index + 1] * space))
            index += 3
        }
        return path
    }

    /// What the request to keep a stroke says: the points PencilKit gave, on the page they start on.
    static func request(id: String, tool: String, colour: String, size: Double, page: Int, box: CGRect, points: [(CGPoint, Double)]) -> [String: Any]? {
        guard let first = points.first else { return nil }
        let space = box.width / spacesAcross
        var values: [Double] = []
        for (point, pressure) in points.dropFirst() {
            values += [(point.x - first.0.x) / space, (first.0.y - point.y) / space, pressure]
        }
        if values.isEmpty { values = [0, 0, first.1] }
        return [
            "id": id, "tool": tool, "colour": colour, "size": size, "page": page,
            "u": (first.0.x - box.minX) / box.width, "v": (box.maxY - first.0.y) / space,
            "points": values, "pressure": first.1,
        ]
    }
}

/// A stroke drawn by PDFKit with its page, kept only in memory: the PDF is never written back, the strokes live in the
/// project. The pen in its ink at its thickness, the highlighter a wide translucent amber band.
final class InkMark: PDFAnnotation {
    private var line: [CGPoint] = []
    private var width: CGFloat = 1
    private var colour = UIColor.black.cgColor

    init(_ stroke: DocumentStroke, on page: PDFPage) {
        let box = page.bounds(for: .cropBox)
        let points = stroke.path(in: box)
        let width = max(0.5, stroke.size * box.width / DocumentStroke.spacesAcross)
        let xs = points.map(\.x)
        let ys = points.map(\.y)
        let bounds = CGRect(x: (xs.min() ?? 0) - width, y: (ys.min() ?? 0) - width, width: (xs.max() ?? 0) - (xs.min() ?? 0) + 2 * width, height: (ys.max() ?? 0) - (ys.min() ?? 0) + 2 * width)
        super.init(bounds: bounds, forType: .square, withProperties: nil)
        line = points
        self.width = width
        let light = UITraitCollection(userInterfaceStyle: .light)
        // The page is paper in both appearances, so the inks are the daylight ones.
        colour = stroke.tool == "highlighter"
            ? DesignTokens.dynamicColor("amber").resolvedColor(with: light).withAlphaComponent(0.38).cgColor
            : DesignTokens.dynamicColor("ink-\(stroke.colour)").resolvedColor(with: light).cgColor
        shouldDisplay = true
        shouldPrint = false
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
    }

    override func draw(with box: PDFDisplayBox, in context: CGContext) {
        guard let first = line.first else { return }
        context.saveGState()
        context.setStrokeColor(colour)
        context.setLineWidth(width)
        context.setLineCap(.round)
        context.setLineJoin(.round)
        context.move(to: first)
        if line.count == 1 {
            context.addLine(to: first)
        } else {
            for point in line.dropFirst() { context.addLine(to: point) }
        }
        context.strokePath()
        context.restoreGState()
    }
}

extension PdfController: PKCanvasViewDelegate, UIPencilInteractionDelegate {
    /// The pen's default size in the page's spaces (the web app's PEN_SIZE); the sizes of every thickness are PenSizes.
    static let penSize = PenSizes.pen[PenSizes.defaultWidth]

    /// Lays the canvas over the pages. It takes only the Pencil unless Pen is on: its drawing gesture is moved to the PDF
    /// view, so a finger still scrolls the pages.
    func attach(to view: PDFView) {
        canvas.backgroundColor = .clear
        canvas.isOpaque = false
        canvas.isScrollEnabled = false
        canvas.delegate = self
        canvas.frame = view.bounds
        canvas.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        canvas.isUserInteractionEnabled = false
        view.addSubview(canvas)
        view.addGestureRecognizer(canvas.drawingGestureRecognizer)
        let pencil = UIPencilInteraction()
        pencil.delegate = self
        view.addInteraction(pencil)
        applyInput()
        applyTool()
    }

    func undo() {
        guard let last = added.popLast() else { return }
        canUndo = !added.isEmpty
        Task { await change("document.ink.remove", ["ids": [last]]) }
    }

    private var scroll: UIScrollView? {
        view?.subviews.compactMap { $0 as? UIScrollView }.first
    }

    /// Who writes and who scrolls, as on the score: the Pencil only writes; with Pen on a finger writes and two scroll.
    func applyInput() {
        guard let pan = scroll?.panGestureRecognizer else { return }
        #if targetEnvironment(simulator)
        canvas.drawingPolicy = .anyInput
        pan.allowedTouchTypes = [NSNumber(value: UITouch.TouchType.indirectPointer.rawValue)]
        #else
        canvas.drawingPolicy = penOn ? .anyInput : .pencilOnly
        pan.allowedTouchTypes = [UITouch.TouchType.direct, .indirectPointer].map { NSNumber(value: $0.rawValue) }
        pan.minimumNumberOfTouches = penOn ? 2 : 1
        #endif
    }

    /// PencilKit draws the live stroke as the tool will look on the page at its size on the screen now.
    func applyTool() {
        let width = (view?.currentPage?.bounds(for: .cropBox).width ?? 595) / DocumentStroke.spacesAcross * (view?.scaleFactor ?? 1)
        switch tool {
        case .pen(let ink):
            canvas.tool = PKInkingTool(.pen, color: ink.color, width: max(1, PenSizes.pen[penWidth] * width * 1.6))
        case .highlighter:
            canvas.tool = PKInkingTool(.marker, color: DesignTokens.color("amber").withAlphaComponent(0.38), width: PenSizes.highlighter[highlighterWidth] * width)
        case .eraser:
            canvas.tool = PKInkingTool(.pen, color: DesignTokens.color("ink-graphite").withAlphaComponent(0.25), width: 20)
        }
    }

    /// A stroke PencilKit finished: its points on the page it starts on, then kept as a stroke or, with the eraser, the
    /// strokes it passed over taken away.
    func strokeEnded(_ stroke: PKStroke) {
        guard let view, let document else { return }
        let located = stroke.path.interpolatedPoints(by: .distance(1.5)).map { point in
            // UITouch force is about 1 for an ordinary press: pressure 0.5, as the web takes a pen without pressure.
            (canvas.convert(point.location.applying(stroke.transform), to: view), min(1, max(0, Double(point.force) / 2)))
        }
        guard let first = located.first, let page = view.page(for: first.0, nearest: true) else { return }
        let box = page.bounds(for: .cropBox)
        let onPage = located.map { (view.convert($0.0, to: page), $0.1) }
        let number = document.index(for: page) + 1
        switch tool {
        case .eraser:
            let ids = erased(by: onPage.map(\.0), on: number, box: box)
            guard !ids.isEmpty else { return }
            added.removeAll { ids.contains($0) }
            canUndo = !added.isEmpty
            Task { await change("document.ink.remove", ["ids": ids]) }
        case .pen(let ink):
            keep(onPage, tool: "pen", colour: ink.rawValue, size: PenSizes.pen[penWidth], page: number, box: box)
        case .highlighter:
            keep(onPage, tool: "highlighter", colour: "amber", size: PenSizes.highlighter[highlighterWidth], page: number, box: box)
        }
    }

    private func keep(_ points: [(CGPoint, Double)], tool: String, colour: String, size: Double, page: Int, box: CGRect) {
        let id = "pk_" + UUID().uuidString.prefix(12).lowercased()
        guard let request = DocumentStroke.request(id: id, tool: tool, colour: colour, size: size, page: page, box: box, points: points) else { return }
        added.append(id)
        canUndo = true
        Task { await change("document.ink.add", ["strokes": [request]]) }
    }

    /// The strokes on the page that the eraser's path passed over, within a space of their line.
    func erased(by path: [CGPoint], on page: Int, box: CGRect) -> [String] {
        let space = box.width / DocumentStroke.spacesAcross
        return strokes.filter { stroke in
            guard stroke.page == page else { return false }
            let reach = space * (1 + stroke.size / 2)
            return stroke.path(in: box).contains { point in path.contains { hypot($0.x - point.x, $0.y - point.y) <= reach } }
        }.map(\.id)
    }

    nonisolated func canvasViewDrawingDidChange(_ canvasView: PKCanvasView) {
        MainActor.assumeIsolated {
            guard !clearing, let stroke = canvasView.drawing.strokes.last else { return }
            clearing = true
            canvasView.drawing = PKDrawing()
            clearing = false
            strokeEnded(stroke)
        }
    }

    /// A double tap on the Pencil does what the player chose in the iPad's settings, as on the score.
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
