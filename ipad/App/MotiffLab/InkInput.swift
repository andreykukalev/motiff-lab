import UIKit
import UIKit.UIGestureRecognizerSubclass

/// The pen on the score and on a PDF's pages (#479 items 7 and 18). The stroke is caught here rather than by PencilKit,
/// and drawn while it is written with the very outline the page then keeps (InkOutline, the page's `outlinePath`), its
/// pressure and its pencil grain, so it looks the same before and after the Pencil lifts. Who writes and who scrolls is
/// InputRules': the gesture takes only the pointers that write, the scroll view's pan only those that scroll, and a finger
/// on a selection handle is left to the handle (the Pencil writes there in the Pen, #511 4.1). The eraser draws nothing here: its points go to the host as they come,
/// which takes the marks under it at once and saves the press when it lifts (BUG-05).
@MainActor
final class InkInput: NSObject, UIGestureRecognizerDelegate {
    let gesture = InkGesture()
    let live = LiveInkView()
    /// The Pencil's double tap and squeeze over the same view (#479 item 8).
    let pencil = PencilGestures()
    private weak var host: UIView?
    private weak var pan: UIPanGestureRecognizer?
    private(set) var rules = InputRules(stylus: false, tool: .none)
    /// The look of the stroke now beginning at a point, in the host's coordinates; nil writes nothing there.
    var style: (CGPoint) -> LiveInkView.Style? = { _ in nil }
    /// A stroke written, its points in the host's coordinates: the host keeps it and then calls `live.dropOldest()`.
    var ended: ([InkSample]) -> Void = { _ in }
    /// Whether the stroke now beginning rubs out instead of writing: the eraser (BUG-05). Such a stroke draws nothing over
    /// the page; its points go to `rubbed` as they come, so the host takes the marks under it at once.
    var rubs: () -> Bool = { false }
    /// The eraser's newest points in the host's coordinates, as they come, the reported ones only (never the predicted:
    /// the eraser takes only where it has been); `lifted` is true once, last, when it lifts or the touch is let go.
    var rubbed: (_ points: [CGPoint], _ lifted: Bool) -> Void = { _, _ in }
    /// The selection handles' centres in the host's coordinates: a finger there moves the handle and never writes; the
    /// Pencil writes there in the Pen (#511 4.1).
    var handles: () -> [CGPoint] = { [] }
    /// The playheads' handles' centres in the host's coordinates (#495 UX-08): a finger there drags the playhead and never
    /// writes; the Pencil writes there in the Pen, as the design has it (playback-model-v0.5.2, «Жест»).
    var heads: () -> [CGPoint] = { [] }
    /// A stroke began (true) or ended (false), by the Pencil or a finger: the host keeps the music still under it (#521 10).
    var penDown: (Bool) -> Void = { _ in }
    /// While the page is still (phrase focus), nothing writes on it.
    var still = false {
        didSet { apply(rules) }
    }
    private var writing: LiveInkView.Style?
    /// The eraser is down, and how many of its points have gone to `rubbed`.
    private var rubbing = false
    private var rubbedCount = 0
    /// A stroke is being written, and whether the scroll view's pan and pinch were let go for it (and how they were).
    private var down = false
    private var heldStill: (pan: Bool, pinch: Bool)?

    /// Whether this iPad is used with a stylus: the iPad's setting «Only draw with Apple Pencil», read when asked, so a
    /// change in Settings counts from the next tool chosen or the next time the piece is shown.
    static var stylusPreferred: Bool { UIPencilInteraction.prefersPencilOnlyDrawing }

    func attach(to host: UIView, pan: UIPanGestureRecognizer?) {
        live.frame = host.bounds
        live.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        host.addSubview(live)
        gesture.addTarget(self, action: #selector(drew(_:)))
        gesture.delegate = self
        host.addGestureRecognizer(gesture)
        let interaction = UIPencilInteraction()
        interaction.delegate = pencil
        host.addInteraction(interaction)
        self.host = host
        self.pan = pan
        apply(rules)
    }

    /// Takes the rules: the pen's gesture for the pointers that write, the pan for those that scroll.
    func apply(_ rules: InputRules) {
        self.rules = rules
        let writers = InputRules.Pointer.allCases.filter { rules.writes($0) }
        gesture.allowedTouchTypes = writers.flatMap(Self.touchTypes)
        gesture.isEnabled = !writers.isEmpty && !still
        guard let pan else { return }
        let scrollers = InputRules.Pointer.allCases.filter { rules.scrolls($0) }
        pan.allowedTouchTypes = scrollers.flatMap(Self.touchTypes) + [NSNumber(value: UITouch.TouchType.indirectPointer.rawValue)]
        pan.minimumNumberOfTouches = rules.scrollFingers
    }

    private static func touchTypes(_ pointer: InputRules.Pointer) -> [NSNumber] {
        [NSNumber(value: (pointer == .finger ? UITouch.TouchType.direct : .pencil).rawValue)]
    }

    @objc private func drew(_ recognizer: InkGesture) {
        if recognizer.state == .began { hold(true, pencil: recognizer.byPencil) }
        defer {
            if recognizer.state != .began && recognizer.state != .changed { hold(false, pencil: recognizer.byPencil) }
        }
        switch recognizer.state {
        case .began, .changed:
            if writing == nil, !rubbing, let first = recognizer.samples.first {
                if rubs() {
                    rubbing = true
                    rubbedCount = 0
                } else {
                    writing = style(first.point)
                }
            }
            if rubbing {
                rub(recognizer, lifted: false)
                return
            }
            guard let writing else { return }
            live.show(recognizer.samples + recognizer.predicted, style: writing)
        case .ended:
            if rubbing {
                rub(recognizer, lifted: true)
                return
            }
            guard let writing else { return }
            self.writing = nil
            live.show(recognizer.samples, style: writing)
            live.finish()
            ended(recognizer.samples)
        default:
            // A touch let go (a second finger came) keeps what the eraser took: the page already shows it so.
            if rubbing { rub(recognizer, lifted: true) }
            writing = nil
            live.clear()
        }
    }

    /// A gesture belongs to one tool (#521 10): while the Pencil writes or rubs out, the music under it stays still. The
    /// scroll view's pan and pinch let go of a palm or a finger beside the Pencil and take none until it lifts, and a scroll
    /// still gliding stops, so a stroke never moves the score (the cause of the scroll while drawing: the pan was allowed
    /// beside the pen's gesture, and a resting palm is a finger to it). A finger writing (an iPad without a stylus) keeps
    /// the rule of before: a second finger lets its stroke go and two fingers scroll. The host hears every stroke begin and
    /// end, so playback does not move the page under the pen either.
    private func hold(_ on: Bool, pencil: Bool) {
        guard on != down else { return }
        down = on
        if on, pencil, let pan {
            let scroll = pan.view as? UIScrollView
            if let scroll, scroll.isDecelerating { scroll.setContentOffset(scroll.contentOffset, animated: false) }
            let pinch = scroll?.pinchGestureRecognizer
            heldStill = (pan.isEnabled, pinch?.isEnabled ?? false)
            pan.isEnabled = false
            pinch?.isEnabled = false
        } else if !on, let held = heldStill {
            heldStill = nil
            pan?.isEnabled = held.pan
            (pan?.view as? UIScrollView)?.pinchGestureRecognizer?.isEnabled = held.pinch
        }
        penDown(on)
    }

    /// Hands the eraser's points not handed yet to `rubbed`, and, when it lifts, ends the press.
    private func rub(_ recognizer: InkGesture, lifted: Bool) {
        let points = recognizer.samples.dropFirst(rubbedCount).map(\.point)
        rubbedCount = recognizer.samples.count
        if lifted {
            rubbing = false
            rubbedCount = 0
        }
        if !points.isEmpty || lifted { rubbed(points, lifted) }
    }

    // MARK: - UIGestureRecognizerDelegate

    /// A finger on a selection handle or a playhead's handle (its 44 pt target) moves the handle; the Pencil that writes
    /// writes there too (#511 4.1, InputRules.inkTakes), so a stroke never moves an edge of the selection.
    func gestureRecognizer(_ recognizer: UIGestureRecognizer, shouldReceive touch: UITouch) -> Bool {
        guard recognizer === gesture, let host else { return true }
        let at = touch.location(in: host)
        let near = { (spot: CGPoint) in hypot(spot.x - at.x, spot.y - at.y) <= InputRules.handleReach }
        let pointer: InputRules.Pointer = touch.type == .pencil ? .stylus : .finger
        return rules.inkTakes(pointer, nearHandle: handles().contains(where: near) || heads().contains(where: near))
    }

    /// Two fingers scroll and pinch beside a finger writing, which then lets its stroke go.
    func gestureRecognizer(_ recognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        other === pan || other is UIPinchGestureRecognizer
    }
}

/// Changes to the ink one after another, each once the one before has finished (BUG-05): the eraser's moves, the save
/// when it lifts, a stroke kept and Undo, so a move never meets a press half saved and Undo takes back the step just saved.
@MainActor
final class InkQueue {
    private var last: Task<Void, Never>?

    func then(_ work: @escaping @MainActor () async -> Void) {
        let before = last
        last = Task { @MainActor in
            await before?.value
            await work()
        }
    }
}

/// One stroke of the pen as it is written: the touch it began with, every point the iPad reports for it (the Pencil's
/// coalesced touches, up to 240 a second) with its pressure, and the points the iPad predicts, drawn ahead to cut the
/// lag. A second touch lets a finger's stroke go: two fingers scroll. Beside the Pencil writing, a palm or a finger is not
/// heeded: the stroke goes on (#521 10).
final class InkGesture: UIGestureRecognizer {
    private(set) var samples: [InkSample] = []
    private(set) var predicted: [InkSample] = []
    private var tracked: UITouch?

    /// Whether the stroke is the Pencil's.
    var byPencil: Bool { tracked?.type == .pencil }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        super.touchesBegan(touches, with: event)
        if let tracked, tracked.type == .pencil {
            for touch in touches where touch !== tracked {
                ignore(touch, for: event)
            }
            return
        }
        guard tracked == nil, touches.count == 1, let touch = touches.first else {
            state = state == .began || state == .changed ? .cancelled : .failed
            return
        }
        tracked = touch
        take(touch, event)
        state = .began
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent) {
        super.touchesMoved(touches, with: event)
        guard let tracked, touches.contains(tracked) else { return }
        take(tracked, event)
        state = .changed
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent) {
        super.touchesEnded(touches, with: event)
        guard let tracked, touches.contains(tracked) else { return }
        take(tracked, event)
        predicted = []
        state = .ended
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent) {
        super.touchesCancelled(touches, with: event)
        state = state == .began || state == .changed ? .cancelled : .failed
    }

    override func reset() {
        super.reset()
        samples = []
        predicted = []
        tracked = nil
    }

    private func take(_ touch: UITouch, _ event: UIEvent) {
        let pencil = touch.type == .pencil
        let sample = { (each: UITouch) in InkSample(each.location(in: self.view), pressure: InkSample.pressure(force: each.force, pencil: pencil)) }
        samples += (event.coalescedTouches(for: touch) ?? [touch]).map(sample)
        predicted = (event.predictedTouches(for: touch) ?? []).map(sample)
    }
}

/// The stroke being written, over the page, and the strokes just written until the page has drawn them as kept. Each is
/// InkOutline's shape in its ink, the pencil's with the grain the page gives its pencil (Web/grain.png, tiled from the
/// page's top left corner), the highlighter's opaque on a layer as translucent as the page's highlighter layer.
final class LiveInkView: UIView {
    struct Style {
        var colour: UIColor
        /// The stroke's half-thickness at pressure 0.5, in this view's points.
        var radius: Double
        var followsPressure: Bool
        /// The layer's alpha: the highlighter's, else 1.
        var alpha: CGFloat = 1
        /// Where the page's grain tiles start and how big a tile is, in this view's points; nil for no grain.
        var grain: (origin: CGPoint, tile: CGFloat)?
    }

    private struct Drawn {
        var path: CGPath
        var style: Style
    }

    private var current: Drawn?
    private var pending: [Drawn] = []
    /// The part of the view the stroke's newest points covered at the last draw, drawn again with the next ones.
    private var tail = CGRect.null

    override init(frame: CGRect) {
        super.init(frame: frame)
        isUserInteractionEnabled = false
        isOpaque = false
        backgroundColor = .clear
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
    }

    /// The stroke being written, drawn again where its newest points are (only there the outline changes).
    func show(_ samples: [InkSample], style: Style) {
        guard !samples.isEmpty else { return }
        let path = InkOutline.path(samples, radius: style.radius, followsPressure: style.followsPressure)
        let starting = current == nil || samples.count <= Self.tailPoints
        current = Drawn(path: path, style: style)
        let reach = CGFloat(style.radius * 1.5) + 2
        let newest = samples.suffix(Self.tailPoints)
            .reduce(CGRect.null) { $0.union(CGRect(origin: $1.point, size: .zero)) }
            .insetBy(dx: -reach, dy: -reach)
        setNeedsDisplay(starting ? path.boundingBoxOfPath.insetBy(dx: -reach, dy: -reach) : newest.union(tail))
        tail = newest
    }

    /// The newest points whose part of the outline changes as the next come: eased thickness, curve and round end.
    private static let tailPoints = 6

    /// The stroke is written: it stays until the page draws it as kept.
    func finish() {
        if let current { pending.append(current) }
        current = nil
        tail = .null
    }

    /// The oldest stroke written is on the page now (or was not kept): it goes from here.
    func dropOldest() {
        guard !pending.isEmpty else { return }
        let gone = pending.removeFirst()
        setNeedsDisplay(gone.path.boundingBoxOfPath.insetBy(dx: -2, dy: -2))
    }

    /// The stroke being written goes, unkept (a second finger came, or the touch was cancelled).
    func clear() {
        if let current { setNeedsDisplay(current.path.boundingBoxOfPath.insetBy(dx: -2, dy: -2)) }
        current = nil
        tail = .null
    }

    override func draw(_ rect: CGRect) {
        guard let context = UIGraphicsGetCurrentContext() else { return }
        for drawn in pending + [current].compactMap({ $0 }) {
            InkPaint.fill([drawn.path], colour: drawn.style.colour.cgColor, alpha: drawn.style.alpha, grain: drawn.style.grain, in: context)
        }
    }
}

/// How strokes' outlines are painted, live and on a PDF's page alike: in their ink, opaque together in one layer, through
/// the pencil's grain where it has one, the layer laid at its alpha (one flat band for the highlighter's strokes).
enum InkPaint {
    /// The grain of the pencil, as the score page tiles it over its pencil marks (Web/grain.png: white whose alpha is the
    /// grain, the same top to bottom and bottom to top, so a page drawn upside down shows the same grain).
    static let grain: CGImage? = Bundle.main.url(forResource: "grain", withExtension: "png", subdirectory: "Web")
        .flatMap { UIImage(contentsOfFile: $0.path)?.cgImage }

    /// A tile of grain, in the page's own units: the score page's 48 pixels, and as much of a PDF's page.
    static let tile: CGFloat = 48

    static func fill(_ paths: [CGPath], colour: CGColor, alpha: CGFloat, grain: (origin: CGPoint, tile: CGFloat)?, in context: CGContext) {
        context.saveGState()
        context.setAlpha(alpha)
        context.beginTransparencyLayer(auxiliaryInfo: nil)
        context.setFillColor(colour)
        // Each outline is filled on its own: two strokes' outlines in one path could cancel where they overlap.
        for path in paths {
            context.addPath(path)
            context.fillPath()
        }
        if let grain, let image = Self.grain {
            // The ink stays where the grain is opaque: the speckles of paper a pencil leaves.
            context.setBlendMode(.destinationIn)
            context.draw(image, in: CGRect(origin: grain.origin, size: CGSize(width: grain.tile, height: grain.tile)), byTiling: true)
        }
        context.endTransparencyLayer()
        context.restoreGState()
    }
}
