import UIKit
import UIKit.UIGestureRecognizerSubclass

/// The pen on the score and on a PDF's pages (#479 items 7 and 18). The stroke is caught here rather than by PencilKit,
/// and drawn while it is written with the very outline the page then keeps (InkOutline, the page's `outlinePath`), its
/// pressure and its pencil grain, so it looks the same before and after the Pencil lifts. Who writes and who scrolls is
/// InputRules': the gesture takes only the pointers that write, the scroll view's pan only those that scroll, and a touch
/// on a selection handle is left to the handle.
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
    /// The selection handles' centres in the host's coordinates: a touch there moves the handle and never writes.
    var handles: () -> [CGPoint] = { [] }
    /// While the page is still (phrase focus), nothing writes on it.
    var still = false {
        didSet { apply(rules) }
    }
    private var writing: LiveInkView.Style?

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
        switch recognizer.state {
        case .began, .changed:
            if writing == nil, let first = recognizer.samples.first {
                writing = style(first.point)
            }
            guard let writing else { return }
            live.show(recognizer.samples + recognizer.predicted, style: writing)
        case .ended:
            guard let writing else { return }
            self.writing = nil
            live.show(recognizer.samples, style: writing)
            live.finish()
            ended(recognizer.samples)
        default:
            writing = nil
            live.clear()
        }
    }

    // MARK: - UIGestureRecognizerDelegate

    /// A touch on a selection handle (its 44 pt target) moves the handle: the pen does not take it.
    func gestureRecognizer(_ recognizer: UIGestureRecognizer, shouldReceive touch: UITouch) -> Bool {
        guard recognizer === gesture, let host else { return true }
        let at = touch.location(in: host)
        return !handles().contains { hypot($0.x - at.x, $0.y - at.y) <= InputRules.handleReach }
    }

    /// Two fingers scroll and pinch beside a finger writing, which then lets its stroke go.
    func gestureRecognizer(_ recognizer: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool {
        other === pan || other is UIPinchGestureRecognizer
    }
}

/// One stroke of the pen as it is written: the touch it began with, every point the iPad reports for it (the Pencil's
/// coalesced touches, up to 240 a second) with its pressure, and the points the iPad predicts, drawn ahead to cut the
/// lag. A second touch lets the stroke go: two fingers scroll.
final class InkGesture: UIGestureRecognizer {
    private(set) var samples: [InkSample] = []
    private(set) var predicted: [InkSample] = []
    private var tracked: UITouch?

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        super.touchesBegan(touches, with: event)
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
