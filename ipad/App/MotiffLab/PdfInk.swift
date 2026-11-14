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
        DocumentStroke(id: id, tool: tool, colour: colour, size: size, page: page, box: box, points: points)?.fields
    }

    /// The stroke as music-core takes it (`document.ink.add`) and answers it.
    var fields: [String: Any] {
        var fields: [String: Any] = [
            "id": id, "tool": tool, "colour": colour, "size": size, "page": page,
            "u": u, "v": v, "points": points, "pressure": pressure,
        ]
        if let margin { fields["margin"] = margin }
        return fields
    }

    /// A new stroke's id, as the strokes written here have.
    static func newId() -> String {
        "pk_" + UUID().uuidString.prefix(12).lowercased()
    }

    /// The margin a stroke begun at `u` across its page is in: "left" before the page's left edge, "right" past its right.
    static func margin(u: Double) -> String? {
        u < 0 ? "left" : u > 1 ? "right" : nil
    }
}

extension DocumentStroke {
    /// A stroke of the points the pen gave, in the page's own coordinates, on the page they start on; nil without points.
    init?(id: String, tool: String, colour: String, size: Double, page: Int, box: CGRect, points: [(CGPoint, Double)]) {
        guard let first = points.first else { return nil }
        let space = box.width / Self.spacesAcross
        var values: [Double] = []
        for (point, pressure) in points.dropFirst() {
            values += [(point.x - first.0.x) / space, (first.0.y - point.y) / space, pressure]
        }
        if values.isEmpty { values = [0, 0, first.1] }
        let u = Double((first.0.x - box.minX) / box.width)
        self.init(
            id: id, tool: tool, colour: colour, size: size, page: page, u: u, v: Double((box.maxY - first.0.y) / space),
            points: values, pressure: first.1, margin: Self.margin(u: u)
        )
    }

    /// What is left of a stroke: the points, in the page's own coordinates, in the stroke's tool, ink and size.
    init?(id: String, like stroke: DocumentStroke, box: CGRect, samples: [InkSample]) {
        self.init(id: id, tool: stroke.tool, colour: stroke.colour, size: stroke.size, page: stroke.page, box: box, points: samples.map { ($0.point, $0.pressure) })
    }
}

/// The eraser on a PDF's page while it is pressed (BUG-05): each move cuts the strokes it passes over at once, from where
/// the last move left off (InkSplit), and the page shows what it has done (`shown`); when it lifts, the strokes it cut go
/// and the pieces are kept, as one step. A piece cut again is replaced in turn; only a kept stroke is in `removed`.
struct PageEraser {
    /// The page it is on, from 1, the page's box, and how far it reaches from its path, in the page's points.
    let page: Int
    let box: CGRect
    let reach: Double
    /// The kept strokes it cut, whole, to take away when it lifts and to put back on Undo.
    private(set) var removed: [DocumentStroke] = []
    /// What is left of them, not kept yet.
    private(set) var pieces: [DocumentStroke] = []
    /// Where it was at the last move, in the page's coordinates.
    private var last: CGPoint?

    init(page: Int, box: CGRect, reach: Double) {
        self.page = page
        self.box = box
        self.reach = reach
    }

    /// The strokes as they look now: those given without the ones it cut (or pieces already kept), and the pieces.
    func shown(_ strokes: [DocumentStroke]) -> [DocumentStroke] {
        guard !removed.isEmpty || !pieces.isEmpty else { return strokes }
        let gone = Set(removed.map(\.id) + pieces.map(\.id))
        return strokes.filter { !gone.contains($0.id) } + pieces
    }

    /// The eraser moved on to these points, in the page's coordinates: whether it cut anything.
    mutating func move(to points: [CGPoint], over strokes: [DocumentStroke]) -> Bool {
        guard let end = points.last else { return false }
        let path = (last.map { [$0] } ?? []) + points
        last = end
        var cut = false
        for stroke in shown(strokes) where stroke.page == page {
            guard let left = InkSplit.pieces(stroke.samples(in: box), radius: stroke.radius(in: box), path: path, reach: reach) else { continue }
            cut = true
            if let index = pieces.firstIndex(where: { $0.id == stroke.id }) {
                pieces.remove(at: index)
            } else {
                removed.append(stroke)
            }
            pieces += left.compactMap { DocumentStroke(id: DocumentStroke.newId(), like: stroke, box: box, samples: $0) }
        }
        return cut
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
        ink.rubs = { [weak self] in self?.tool == .eraser }
        ink.rubbed = { [weak self] points, lifted in self?.rubbed(points, lifted: lifted) }
        ink.pencil.perform = { [weak self] action, point in self?.pencilGesture(action, at: point) }
        applyInput()
    }

    /// Takes back the last step, after any change still being saved: a stroke goes, and a press of the eraser is put back
    /// whole (BUG-05).
    func undo() {
        inkQueue.then { [weak self] in
            guard let self, let last = self.steps.undo() else { return }
            self.inkStepsChanged()
            await last.undo(remove: "document.ink.remove", add: "document.ink.add") { call, request, draws in
                await self.change(call, request, draws: draws)
            }
        }
    }

    /// Does the step undone last again (#533 9), as on the score: a stroke comes back, a press of the eraser takes again
    /// what it took.
    func redo() {
        inkQueue.then { [weak self] in
            guard let self, let step = self.steps.redo() else { return }
            self.inkStepsChanged()
            await step.redo(remove: "document.ink.remove", add: "document.ink.add") { call, request, draws in
                await self.change(call, request, draws: draws)
            }
        }
    }

    private func inkStepsChanged() {
        canUndo = steps.canUndo
        canRedo = steps.canRedo
    }

    /// The strokes as the pages show them: as music-core answered, with what the eraser did and has not saved yet.
    var shownStrokes: [DocumentStroke] {
        let settled = closing.reduce(strokes) { $1.shown($0) }
        return rubbing?.shown(settled) ?? settled
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
    /// page's. The eraser draws nothing: what it takes goes from the page as it moves (BUG-05).
    private func liveStyle(at point: CGPoint) -> LiveInkView.Style? {
        guard let view, let on = page(at: point) else { return nil }
        let scale = Double(view.scaleFactor)
        let light = UITraitCollection(userInterfaceStyle: .light)
        switch tool {
        case .pen(let colour):
            let origin = view.convert(CGPoint(x: on.box.minX, y: on.box.maxY), from: on.page)
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("ink-\(colour.rawValue)").resolvedColor(with: light),
                radius: max(0.25, PenSizes.pen[pen.pencil.width] * on.space / 2) * scale, followsPressure: true,
                grain: (origin: origin, tile: InkPaint.tile * CGFloat(scale))
            )
        case .highlighter:
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("amber").resolvedColor(with: light),
                radius: PenSizes.highlighter[pen.marker.width] * on.space / 2 * scale, followsPressure: false,
                alpha: PenSizes.highlighterAlpha
            )
        case .eraser:
            return nil
        }
    }

    /// A stroke the pen finished, its points in the PDF view's: on the page it starts on, then kept as a stroke. The stroke
    /// written stays over the page until the page draws it as kept.
    private func strokeEnded(_ samples: [InkSample]) {
        guard let view, let document, let first = samples.first, let on = page(at: first.point) else {
            ink.live.dropOldest()
            return
        }
        let onPage = samples.map { InkSample(view.convert($0.point, to: on.page), pressure: $0.pressure) }
        let number = document.index(for: on.page) + 1
        let pen = pen
        let box = on.box
        inkQueue.then { [weak self] in
            guard let self else { return }
            switch pen.stroke {
            case .pen(let colour):
                await self.keep(onPage, tool: "pen", colour: colour.rawValue, size: PenSizes.pen[pen.pencil.width], page: number, box: box)
            case .highlighter:
                await self.keep(onPage, tool: "highlighter", colour: "amber", size: PenSizes.highlighter[pen.marker.width], page: number, box: box)
            case .eraser:
                // The eraser rubs out as it moves (`rubbed`); a stroke written before it was chosen is not kept.
                break
            }
            self.ink.live.dropOldest()
        }
    }

    private func keep(_ samples: [InkSample], tool: String, colour: String, size: Double, page: Int, box: CGRect) async {
        let id = DocumentStroke.newId()
        let points = samples.map { ($0.point, $0.pressure) }
        guard let request = DocumentStroke.request(id: id, tool: tool, colour: colour, size: size, page: page, box: box, points: points) else { return }
        steps.record(InkStep(added: [id], strokes: [request]))
        inkStepsChanged()
        await change("document.ink.add", ["strokes": [request]])
    }

    /// The eraser moved (InkInput, BUG-05), its points in the PDF view's: on the page it came down on, the strokes under it
    /// go at once, cut where it passes (#479 item 7), and only that page is drawn again; nothing is drawn for the eraser
    /// itself. When it lifts, the press is saved as one step.
    private func rubbed(_ points: [CGPoint], lifted: Bool) {
        if rubbing == nil, let document, let first = points.first, let on = page(at: first) {
            rubbing = PageEraser(page: document.index(for: on.page) + 1, box: on.box, reach: on.space)
        }
        if var eraser = rubbing, let view, let page = document?.page(at: eraser.page - 1) {
            let cut = eraser.move(to: points.map { view.convert($0, to: page) }, over: closing.reduce(strokes) { $1.shown($0) })
            rubbing = eraser
            if cut { markInk(on: page, number: eraser.page) }
        }
        if lifted { endRub() }
    }

    /// The eraser lifted: the strokes it cut go and the pieces left are kept, the pages drawn once both are saved; until
    /// then the pages show the press as it left them. Undo puts it back whole.
    private func endRub() {
        guard let eraser = rubbing else { return }
        rubbing = nil
        guard !eraser.removed.isEmpty else { return }
        closing.append(eraser)
        inkQueue.then { [weak self] in
            guard let self else { return }
            if !eraser.pieces.isEmpty {
                await self.change("document.ink.add", ["strokes": eraser.pieces.map(\.fields)], draws: false)
            }
            await self.change("document.ink.remove", ["ids": eraser.removed.map(\.id)])
            if !self.closing.isEmpty { self.closing.removeFirst() }
            self.steps.record(InkStep(added: eraser.pieces.map(\.id), removed: eraser.removed.map(\.fields), strokes: eraser.pieces.map(\.fields)))
            self.inkStepsChanged()
            self.markPages()
        }
    }

    /// One page's strokes drawn again, as the eraser moves over it.
    private func markInk(on page: PDFPage, number: Int) {
        for annotation in page.annotations where annotation is InkMark {
            page.removeAnnotation(annotation)
        }
        for mark in InkMark.marks(shownStrokes.filter { $0.page == number }, on: page) {
            page.addAnnotation(mark)
        }
        view?.annotationsChanged(on: page)
    }

    /// The strokes on the page that the eraser's path passed over, within a space of their line.
    func erased(by path: [CGPoint], on page: Int, box: CGRect) -> [String] {
        erasure(by: path, on: page, box: box).removed
    }

    /// What the eraser's whole path does on a page, as one press would leave it (PageEraser): the strokes it touched, and
    /// what is left of them as strokes to keep, each in its stroke's tool, ink and size.
    func erasure(by path: [CGPoint], on page: Int, box: CGRect) -> (removed: [String], added: [[String: Any]]) {
        var eraser = PageEraser(page: page, box: box, reach: Double(box.width) / DocumentStroke.spacesAcross)
        _ = eraser.move(to: path, over: strokes)
        return (eraser.removed.map(\.id), eraser.pieces.map(\.fields))
    }
}
