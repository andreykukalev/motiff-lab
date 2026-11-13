import PDFKit
import PencilKit
import UIKit

/// A stroke on a PDF's page, as music-core answers `document.marks` (0023, `DocumentMarks`): the pencil layer's shape with
/// the page in place of the bar. U is across from the page's left edge (0 to 1), V down from its top edge in the page's
/// own spaces (a hundred and twentieth of its width); the points after the first are dx, dy in spaces and a pressure. A
/// stroke begun beside the page (U below 0 or above 1) is in its "left" or "right" margin (v0.4, within contract 12): the
/// page is its own line, so it is drawn where it was written either way.
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
    /// "left" or "right" for a stroke begun in a margin, as music-core answers it; nil for one begun on the page (and an
    /// unknown value, as pencil-marks.ts drops it).
    var margin: String? = nil

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
                points: (entry["points"] as? [Any] ?? []).compactMap(number), pressure: number(entry["pressure"]) ?? 0.5,
                margin: (entry["margin"] as? String).flatMap { ["left", "right"].contains($0) ? $0 : nil }
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
        let u = (first.0.x - box.minX) / box.width
        var request: [String: Any] = [
            "id": id, "tool": tool, "colour": colour, "size": size, "page": page,
            "u": u, "v": (box.maxY - first.0.y) / space,
            "points": values, "pressure": first.1,
        ]
        if let side = Self.margin(u: u) { request["margin"] = side }
        return request
    }

    /// The margin a stroke begun at `u` across its page is in: "left" before the page's left edge, "right" past its right.
    static func margin(u: Double) -> String? {
        u < 0 ? "left" : u > 1 ? "right" : nil
    }
}

/// Strokes drawn by PDFKit with their page, kept only in memory: the PDF is never written back, the strokes live in the
/// project. A pen stroke is one mark in its ink at its thickness. All the highlighter's strokes on a page are one mark, a
/// single layer: drawn opaque together, then laid on the page once at the highlighter's alpha, so where strokes overlap
/// the amber is as light as anywhere else (#479 item 9: each stroke translucent on its own darkened the overlaps).
final class InkMark: PDFAnnotation {
    private var lines: [(points: [CGPoint], width: CGFloat)] = []
    private var colour = UIColor.black.cgColor
    /// The alpha the layer is laid at: 1 for the pen, the highlighter's for its layer.
    private var layerAlpha: CGFloat = 1

    /// The marks a page's strokes are drawn as: each pen stroke on its own, the highlighter's strokes as one layer.
    static func marks(_ strokes: [DocumentStroke], on page: PDFPage) -> [InkMark] {
        let highlights = strokes.filter { $0.tool == "highlighter" }
        let pens = strokes.filter { $0.tool != "highlighter" }.map { InkMark([$0], on: page) }
        return highlights.isEmpty ? pens : [InkMark(highlights, on: page)] + pens
    }

    /// The strokes as one mark, in the first one's tool and ink.
    init(_ strokes: [DocumentStroke], on page: PDFPage) {
        let box = page.bounds(for: .cropBox)
        let lines: [(points: [CGPoint], width: CGFloat)] = strokes.map { stroke in
            (points: stroke.path(in: box), width: max(0.5, CGFloat(stroke.size) * box.width / CGFloat(DocumentStroke.spacesAcross)))
        }
        let bounds = lines.reduce(CGRect.null) { union, line in
            let xs = line.points.map(\.x)
            let ys = line.points.map(\.y)
            let extent = CGRect(x: (xs.min() ?? 0) - line.width, y: (ys.min() ?? 0) - line.width, width: (xs.max() ?? 0) - (xs.min() ?? 0) + 2 * line.width, height: (ys.max() ?? 0) - (ys.min() ?? 0) + 2 * line.width)
            return union.union(extent)
        }
        super.init(bounds: bounds.isNull ? .zero : bounds, forType: .square, withProperties: nil)
        self.lines = lines
        let light = UITraitCollection(userInterfaceStyle: .light)
        // The page is paper in both appearances, so the inks are the daylight ones.
        if strokes.first?.tool == "highlighter" {
            colour = DesignTokens.dynamicColor("amber").resolvedColor(with: light).cgColor
            layerAlpha = PenSizes.highlighterAlpha
        } else {
            colour = DesignTokens.dynamicColor("ink-\(strokes.first?.colour ?? "graphite")").resolvedColor(with: light).cgColor
        }
        shouldDisplay = true
        shouldPrint = false
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
    }

    override func draw(with box: PDFDisplayBox, in context: CGContext) {
        guard !lines.isEmpty else { return }
        context.saveGState()
        // The strokes are drawn opaque into one transparency layer, which is composited once at the layer's alpha.
        context.setAlpha(layerAlpha)
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        context.setStrokeColor(colour)
        context.setLineCap(.round)
        context.setLineJoin(.round)
        for line in lines {
            guard let first = line.points.first else { continue }
            context.setLineWidth(line.width)
            context.move(to: first)
            if line.points.count == 1 {
                context.addLine(to: first)
            } else {
                for point in line.points.dropFirst() { context.addLine(to: point) }
            }
            context.strokePath()
        }
        context.endTransparencyLayer()
        context.restoreGState()
    }
}

extension PdfController: PKCanvasViewDelegate, UIPencilInteractionDelegate {
    /// The pen's default size in the page's spaces (the web app's PEN_SIZE); the sizes of every thickness are PenSizes.
    static let penSize = PenSizes.pen[PenSizes.defaultWidth]

    /// Lays the canvas over the pages. It takes only the Pencil unless Pen is on: its drawing gesture is moved to the PDF
    /// view, so a finger still scrolls the pages.
    func attach(to view: PDFView) {
        guard inkable else { return }
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

    /// PencilKit draws the live stroke as the tool will look on the page at its size on the screen now. The highlighter
    /// draws opaque amber on a canvas laid at the highlighter's alpha, so a stroke crossing itself stays one flat band, as
    /// the page draws the kept ones (#479 item 9).
    func applyTool() {
        let width = (view?.currentPage?.bounds(for: .cropBox).width ?? 595) / DocumentStroke.spacesAcross * (view?.scaleFactor ?? 1)
        canvas.alpha = tool == .highlighter ? PenSizes.highlighterAlpha : 1
        switch tool {
        case .pen(let ink):
            canvas.tool = PKInkingTool(.pen, color: ink.color, width: max(1, PenSizes.pen[penWidth] * width * 1.6))
        case .highlighter:
            canvas.tool = PKInkingTool(.marker, color: DesignTokens.color("amber"), width: PenSizes.highlighter[highlighterWidth] * width)
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
