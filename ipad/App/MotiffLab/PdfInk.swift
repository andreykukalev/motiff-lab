import PDFKit
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

    /// The stroke's points in the page's own coordinates with their pressure, for its outline (InkOutline).
    func samples(in box: CGRect) -> [InkSample] {
        let located = path(in: box)
        return located.indices.map { index in
            let at = index * 3 - 1
            return InkSample(located[index], pressure: index == 0 || at >= points.count ? pressure : points[at])
        }
    }

    /// The stroke's half-thickness on the page at pressure 0.5, in the page's points.
    func radius(in box: CGRect) -> Double {
        max(0.25, size * box.width / Self.spacesAcross / 2)
    }

    /// What the request to keep a stroke says: the points the pen gave, on the page they start on.
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
/// project. Each is InkOutline's shape, as the stroke looked while it was written (#479 item 7). A pen stroke is one mark
/// in its ink, with its pressure and the pencil's grain, tiled from the page's top left corner as the stroke written was.
/// All the highlighter's strokes on a page are one mark, a single layer: drawn opaque together, then laid on the page
/// once at the highlighter's alpha, so where strokes overlap the amber is as light as anywhere else (#479 item 9).
final class InkMark: PDFAnnotation {
    private var outlines: [CGPath] = []
    private var colour = UIColor.black.cgColor
    /// The alpha the layer is laid at: 1 for the pen, the highlighter's for its layer.
    private var layerAlpha: CGFloat = 1
    private var grain: (origin: CGPoint, tile: CGFloat)?

    /// The marks a page's strokes are drawn as: each pen stroke on its own, the highlighter's strokes as one layer.
    static func marks(_ strokes: [DocumentStroke], on page: PDFPage) -> [InkMark] {
        let highlights = strokes.filter { $0.tool == "highlighter" }
        let pens = strokes.filter { $0.tool != "highlighter" }.map { InkMark([$0], on: page) }
        return highlights.isEmpty ? pens : [InkMark(highlights, on: page)] + pens
    }

    /// The strokes as one mark, in the first one's tool and ink.
    init(_ strokes: [DocumentStroke], on page: PDFPage) {
        let box = page.bounds(for: .cropBox)
        let highlighter = strokes.first?.tool == "highlighter"
        let outlines = strokes.map { stroke in
            InkOutline.path(stroke.samples(in: box), radius: stroke.radius(in: box), followsPressure: !highlighter)
        }
        let bounds = outlines.reduce(CGRect.null) { $0.union($1.boundingBoxOfPath) }.insetBy(dx: -2, dy: -2)
        super.init(bounds: bounds.isNull ? .zero : bounds, forType: .square, withProperties: nil)
        self.outlines = outlines
        let light = UITraitCollection(userInterfaceStyle: .light)
        // The page is paper in both appearances, so the inks are the daylight ones.
        if highlighter {
            colour = DesignTokens.dynamicColor("amber").resolvedColor(with: light).cgColor
            layerAlpha = PenSizes.highlighterAlpha
        } else {
            colour = DesignTokens.dynamicColor("ink-\(strokes.first?.colour ?? "graphite")").resolvedColor(with: light).cgColor
            // The page's coordinates run up from its bottom edge: the first tile's bottom is a tile below the top.
            grain = (origin: CGPoint(x: box.minX, y: box.maxY - InkPaint.tile), tile: InkPaint.tile)
        }
        shouldDisplay = true
        shouldPrint = false
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
    }

    override func draw(with box: PDFDisplayBox, in context: CGContext) {
        guard !outlines.isEmpty else { return }
        InkPaint.fill(outlines, colour: colour, alpha: layerAlpha, grain: grain, in: context)
    }
}

extension PdfController {
    /// The pen's default size in the page's spaces (the web app's PEN_SIZE); the sizes of every thickness are PenSizes.
    static let penSize = PenSizes.pen[PenSizes.defaultWidth]

    /// The pen over the pages (InkInput): it writes, and the PDF view's scroll view scrolls, as InputRules say.
    func attach(to view: PDFView) {
        guard inkable else { return }
        ink.attach(to: view, pan: scroll?.panGestureRecognizer)
        ink.style = { [weak self] point in self?.liveStyle(at: point) }
        ink.ended = { [weak self] samples in self?.strokeEnded(samples) }
        ink.pencil.perform = { [weak self] action, point in self?.pencilGesture(action, at: point) }
        applyInput()
    }

    func undo() {
        guard let last = added.popLast() else { return }
        canUndo = !added.isEmpty
        Task { await change("document.ink.remove", ["ids": [last]]) }
    }

    private var scroll: UIScrollView? {
        view?.subviews.compactMap { $0 as? UIScrollView }.first
    }

    /// Who writes and who scrolls, as on the score (InputRules): the stylus follows the tool, a finger scrolls; on an
    /// iPad without a stylus one finger writes with Pen on and two scroll. Again when the app comes back, as the iPad's
    /// setting «Only draw with Apple Pencil» may have changed.
    func applyInput() {
        ink.apply(InputRules(stylus: InkInput.stylusPreferred, tool: .init(mode: nil, penOn: penOn), surface: .pdf))
    }

    /// The page under a point of the PDF view, with its box and its space (a hundred and twentieth of its width).
    private func page(at point: CGPoint) -> (page: PDFPage, box: CGRect, space: Double)? {
        guard let view, let page = view.page(for: point, nearest: true) else { return nil }
        let box = page.bounds(for: .cropBox)
        return (page, box, Double(box.width) / DocumentStroke.spacesAcross)
    }

    /// How the stroke beginning at a point looks while it is written: as the page will draw it at this zoom (InkOutline),
    /// the pencil with the grain from the page's top left corner, the highlighter opaque on a layer as translucent as the
    /// page's, the eraser a light trail as wide as it reaches.
    private func liveStyle(at point: CGPoint) -> LiveInkView.Style? {
        guard let view, let on = page(at: point) else { return nil }
        let scale = Double(view.scaleFactor)
        let light = UITraitCollection(userInterfaceStyle: .light)
        switch tool {
        case .pen(let colour):
            let origin = view.convert(CGPoint(x: on.box.minX, y: on.box.maxY), from: on.page)
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("ink-\(colour.rawValue)").resolvedColor(with: light),
                radius: max(0.25, PenSizes.pen[penWidth] * on.space / 2) * scale, followsPressure: true,
                grain: (origin: origin, tile: InkPaint.tile * CGFloat(scale))
            )
        case .highlighter:
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("amber").resolvedColor(with: light),
                radius: PenSizes.highlighter[highlighterWidth] * on.space / 2 * scale, followsPressure: false,
                alpha: PenSizes.highlighterAlpha
            )
        case .eraser:
            return LiveInkView.Style(
                colour: DesignTokens.color("ink-graphite").withAlphaComponent(0.25),
                radius: on.space * scale, followsPressure: false
            )
        }
    }

    /// A stroke the pen finished, its points in the PDF view's: on the page it starts on, then kept as a stroke or, with
    /// the eraser, what it passed over cut away. The stroke written stays over the page until the page draws it as kept.
    private func strokeEnded(_ samples: [InkSample]) {
        guard let view, let document, let first = samples.first, let on = page(at: first.point) else {
            ink.live.dropOldest()
            return
        }
        let onPage = samples.map { InkSample(view.convert($0.point, to: on.page), pressure: $0.pressure) }
        let number = document.index(for: on.page) + 1
        let tool = tool
        Task {
            switch tool {
            case .eraser:
                ink.live.dropOldest()
                await erase(onPage.map(\.point), on: number, box: on.box)
            case .pen(let colour):
                await keep(onPage, tool: "pen", colour: colour.rawValue, size: PenSizes.pen[penWidth], page: number, box: on.box)
                ink.live.dropOldest()
            case .highlighter:
                await keep(onPage, tool: "highlighter", colour: "amber", size: PenSizes.highlighter[highlighterWidth], page: number, box: on.box)
                ink.live.dropOldest()
            }
        }
    }

    private func keep(_ samples: [InkSample], tool: String, colour: String, size: Double, page: Int, box: CGRect) async {
        let id = Self.newId()
        let points = samples.map { ($0.point, $0.pressure) }
        guard let request = DocumentStroke.request(id: id, tool: tool, colour: colour, size: size, page: page, box: box, points: points) else { return }
        added.append(id)
        canUndo = true
        await change("document.ink.add", ["strokes": [request]])
    }

    private static func newId() -> String {
        "pk_" + UUID().uuidString.prefix(12).lowercased()
    }

    /// The eraser cuts the strokes it passed over (#479 item 7): what is left of each is kept as new strokes, then the
    /// strokes it touched go.
    private func erase(_ path: [CGPoint], on page: Int, box: CGRect) async {
        let cut = erasure(by: path, on: page, box: box)
        guard !cut.removed.isEmpty else { return }
        if !cut.added.isEmpty {
            await change("document.ink.add", ["strokes": cut.added])
        }
        added.removeAll { cut.removed.contains($0) }
        canUndo = !added.isEmpty
        await change("document.ink.remove", ["ids": cut.removed])
    }

    /// The strokes on the page that the eraser's path passed over, within a space of their line.
    func erased(by path: [CGPoint], on page: Int, box: CGRect) -> [String] {
        erasure(by: path, on: page, box: box).removed
    }

    /// What the eraser's path does on a page: the strokes it touched, and what is left of them as strokes to keep, each in
    /// its stroke's tool, ink and size (InkSplit).
    func erasure(by path: [CGPoint], on page: Int, box: CGRect) -> (removed: [String], added: [[String: Any]]) {
        let reach = Double(box.width) / DocumentStroke.spacesAcross
        var removed: [String] = []
        var pieces: [[String: Any]] = []
        for stroke in strokes where stroke.page == page {
            guard let left = InkSplit.pieces(stroke.samples(in: box), radius: stroke.radius(in: box), path: path, reach: reach) else { continue }
            removed.append(stroke.id)
            pieces += left.compactMap { piece in
                DocumentStroke.request(
                    id: Self.newId(), tool: stroke.tool, colour: stroke.colour, size: stroke.size, page: page, box: box,
                    points: piece.map { ($0.point, $0.pressure) }
                )
            }
        }
        return (removed, pieces)
    }
}
