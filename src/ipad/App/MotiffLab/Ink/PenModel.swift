import SwiftUI

/// What the score's pen needs of the score it writes on (ADR 0031 section 3, S6): the page as it is now (a scenario test
/// puts a recording one in after the controller is made, so the pen never keeps the one there was), the piece open in
/// music-core, a change to the marks drawn from what music-core answers, and the Pen turned on or off, which the root
/// follows with who writes and scrolls, the roll and the page. ScoreController, the composition root, is it.
@MainActor
protocol PenScore: AnyObject {
    /// The score's page, the one way to its functions (`PageCommand`).
    var scorePage: ScorePage { get }
    /// The piece open in music-core; nil while none is.
    var pieceId: Int32? { get }
    /// A change to the marks through music-core; the answer is drawn unless `draws` is false, for the first of two calls
    /// that make one change, so the page never shows it half done.
    func changeMarks(_ piece: Int32, _ call: String, _ request: [String: Any], draws: Bool) async
    /// The Pen was turned on or off (`PenModel.penOn`).
    func penTurned(_ on: Bool)
}

/// The score's pen (ADR 0031 section 3, S6): the pen's state as last chosen on this device, the bar's Pen, the pencil
/// palette, the Pen's own history for Undo and Redo (BUG-05, #521 9), the eraser's points on their way to the page, the
/// ink queue and how the stroke being written looks. It keeps a stroke the pen finished, rubs out as the eraser moves and
/// saves a press of it as one step, through the score it is given (`PenScore`). Who writes, selects and scrolls
/// (InkInput, InputRules) and where a point lies on the page stay ScoreController's, which hands the points in in the
/// page's pixels. The views that show the pen observe this model; a PDF's pages have their own (PdfController).
@MainActor
final class PenModel: ObservableObject, PenHolder {
    /// The pen's one tool and each tool's settings (PenState, #495 UX-06), as last chosen on this device (PenMemory): the
    /// same after PDF | Notes and when the piece opens again.
    @Published var pen = PenMemory.saved() {
        didSet { if pen != oldValue { PenMemory.keep(pen) } }
    }
    /// The bar's Pen (0018 section 5): while it is on, the Pencil writes; on an iPad without a stylus one finger writes
    /// and two fingers scroll, so the score takes marks with or without a Pencil (InputRules).
    @Published var penOn = false {
        didSet { score?.penTurned(penOn) }
    }
    /// The pencil palette, opened by the Pencil's double tap or squeeze (#479 item 8); nil while it is closed.
    @Published var pencilPalette: PencilPaletteCall?
    /// Whether the Pen's Undo has a step.
    @Published private(set) var canUndo = false
    /// Whether the Pen's Redo has a step (#521 9).
    @Published private(set) var canRedo = false

    /// The score the pen writes on, set by the root once it is made.
    weak var score: PenScore?
    /// What the pen and the eraser did here, newest last, for Undo and Redo: a stroke kept, or a press of the eraser (BUG-05).
    private var steps = InkHistory()
    /// The page's ink changes, one after another: strokes kept, the eraser's moves and saves, Undo.
    private let inkQueue = InkQueue()
    /// The eraser's points not yet sent to the page, in the page's pixels, and whether a send is waiting in the queue.
    private var rubPoints: [[String: Double]] = []
    private var rubQueued = false

    /// Takes back the last step, after any change still being saved: a stroke goes, and a press of the eraser is put back
    /// whole, the pieces it left taken away and the marks it cut kept again (BUG-05).
    func undo() {
        inkQueue.then { [weak self] in
            guard let self, let score = self.score, let piece = score.pieceId, let last = self.steps.undo() else { return }
            self.inkStepsChanged()
            await last.undo(remove: "ink.remove", add: "ink.add") { call, request, draws in
                await score.changeMarks(piece, call, request, draws: draws)
            }
        }
    }

    /// Does the step undone last again (#521 9): a stroke comes back, a press of the eraser takes again what it took.
    func redo() {
        inkQueue.then { [weak self] in
            guard let self, let score = self.score, let piece = score.pieceId, let step = self.steps.redo() else { return }
            self.inkStepsChanged()
            await step.redo(remove: "ink.remove", add: "ink.add") { call, request, draws in
                await score.changeMarks(piece, call, request, draws: draws)
            }
        }
    }

    private func inkStepsChanged() {
        canUndo = steps.canUndo
        canRedo = steps.canRedo
    }

    // MARK: - Strokes

    /// A stroke the pen finished, its points in the page's pixels with their pressure, kept as a mark in the queue's turn
    /// with the tool it was written with; `kept` runs after it, in the same turn (the root drops the stroke drawn live).
    func keep(_ points: [[String: Double]], in piece: Int32, then kept: @escaping @MainActor () -> Void) {
        let pen = pen
        inkQueue.then { [weak self] in
            guard let self else { return }
            switch pen.stroke {
            case .pen(let colour):
                await self.add(piece, points, tool: "pen", colour: colour.rawValue, width: pen.pencil.width)
            case .highlighter:
                await self.add(piece, points, tool: "highlighter", colour: "amber", width: pen.marker.width)
            case .eraser:
                // The eraser rubs out as it moves (`rub`); a stroke written before it was chosen is not kept.
                break
            }
            kept()
        }
    }

    /// The eraser moved (InkInput, BUG-05), its points in the page's pixels: the page takes the marks under it at once
    /// (`motiff.rub`), cutting them where it passes (#479 item 7), with nothing drawn for the eraser itself; when it lifts,
    /// the press is saved as one step in `piece`. Points that come while the page is still busy go together in the next
    /// send, so a fast eraser never queues up.
    func rub(_ points: [[String: Double]], lifted: Bool, in piece: Int32?) {
        rubPoints += points
        if !rubQueued, !rubPoints.isEmpty {
            rubQueued = true
            inkQueue.then { [weak self] in await self?.sendRub() }
        }
        if lifted {
            inkQueue.then { [weak self] in await self?.endRub(piece) }
        }
    }

    private func sendRub() async {
        rubQueued = false
        let points = rubPoints
        rubPoints = []
        guard !points.isEmpty, let score else { return }
        await score.scorePage.send(.rub, ["points": points])
    }

    /// The eraser lifted: what the press did is saved as one step (`ink.add` for the pieces left, `ink.remove` for the
    /// marks it cut, drawn once both are done), and Undo puts it back whole.
    private func endRub(_ piece: Int32?) async {
        guard let score,
              let cut = try? await score.scorePage.call(.rubEnd) as? [String: Any],
              let removed = cut["removed"] as? [[String: Any]], !removed.isEmpty, let piece
        else { return }
        let pieces = cut["added"] as? [[String: Any]] ?? []
        if !pieces.isEmpty {
            await score.changeMarks(piece, "ink.add", ["strokes": pieces], draws: false)
        }
        await score.changeMarks(piece, "ink.remove", ["ids": removed.compactMap { $0["id"] as? String }], draws: true)
        steps.record(InkStep(added: pieces.compactMap { $0["id"] as? String }, removed: removed, strokes: pieces))
        inkStepsChanged()
    }

    /// The mark `motiff.anchor` makes goes to `ink.add` as it is, its `margin` (v0.4) with it when the page sets one.
    private func add(_ piece: Int32, _ points: [[String: Double]], tool: String, colour: String, width: Int) async {
        guard let score else { return }
        let request: [String: Any] = ["points": points, "tool": tool, "colour": colour, "width": width]
        guard let mark = try? await score.scorePage.call(.anchor, request) as? [String: Any],
              let id = mark["id"] as? String
        else { return }
        steps.record(InkStep(added: [id], strokes: [mark]))
        inkStepsChanged()
        await score.changeMarks(piece, "ink.add", ["strokes": [mark]], draws: true)
    }

    /// How the stroke beginning now looks while it is written: as the page will draw it at this zoom (InkOutline), the
    /// pencil in its ink with the page's grain from the page's top left corner (`origin`, in the web view's coordinates),
    /// the highlighter opaque on a layer as translucent as the page's. `staffSpace` is the page's, in its pixels. The
    /// eraser draws nothing: what it takes goes from the page as it moves (BUG-05).
    func liveStyle(zoom: CGFloat, staffSpace: CGFloat, dark: Bool, origin: CGPoint) -> LiveInkView.Style? {
        let theme = UITraitCollection(userInterfaceStyle: dark ? .dark : .light)
        let space = Double(staffSpace * zoom)
        switch tool {
        case .pen(let colour):
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("ink-\(colour.rawValue)").resolvedColor(with: theme),
                radius: PenSizes.pen[pen.pencil.width] * space / 2, followsPressure: true,
                grain: (origin: origin, tile: InkPaint.tile * zoom)
            )
        case .highlighter:
            return LiveInkView.Style(
                colour: DesignTokens.dynamicColor("amber").resolvedColor(with: theme),
                radius: PenSizes.highlighter[pen.marker.width] * space / 2, followsPressure: false, alpha: PenSizes.highlighterAlpha
            )
        case .eraser:
            return nil
        }
    }
}
