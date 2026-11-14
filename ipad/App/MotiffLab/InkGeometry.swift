import CoreGraphics
import Foundation

/// A point the pen passed through, with its pressure from 0 to 1: 0.5 for a finger, which has none.
struct InkSample: Equatable, Sendable {
    var point: CGPoint
    var pressure: Double

    init(_ point: CGPoint, pressure: Double = 0.5) {
        self.point = point
        self.pressure = pressure
    }

    /// The pressure a touch's force gives, as the strokes have always been kept: an ordinary press of the Pencil (force
    /// about 1) is 0.5, a finger (no force) 0.5 too.
    static func pressure(force: CGFloat, pencil: Bool) -> Double {
        pencil && force > 0 ? min(1, max(0, Double(force) / 2)) : 0.5
    }
}

/// The outline of a stroke, filled (#479 item 7): exactly the shape the score page draws for a kept mark
/// (Scripts/score/pencil-marks.ts `outlinePath`), so the stroke being written and the stroke kept are one shape and the
/// line no longer thins when the Pencil lifts. Each point's half-thickness follows its pressure for the pencil, eased
/// with its neighbours'; the line's two edges are eased with quadratic curves through their midpoints and joined by
/// round ends.
enum InkOutline {
    /// The segments each round end is drawn with.
    static let capSegments = 12

    /// A point's half-thickness: pressure 0.5 draws the stroke's own size, a light touch thinner, a firm one thicker.
    static func radius(_ radius: Double, pressure: Double, followsPressure: Bool) -> Double {
        followsPressure ? radius * (0.5 + min(1, max(0, pressure))) : radius
    }

    /// Each point's half-thickness, eased with its neighbours' so a pressure change does not show as a step.
    static func radii(_ samples: [InkSample], radius: Double, followsPressure: Bool) -> [Double] {
        let raw = samples.map { Self.radius(radius, pressure: $0.pressure, followsPressure: followsPressure) }
        return raw.indices.map { index in
            (raw[max(0, index - 1)] + 2 * raw[index] + raw[min(raw.count - 1, index + 1)]) / 4
        }
    }

    /// The stroke's outline as a path to fill; empty without points, a dot for one point.
    static func path(_ samples: [InkSample], radius: Double, followsPressure: Bool) -> CGPath {
        let path = CGMutablePath()
        guard let first = samples.first else { return path }
        let radii = radii(samples, radius: radius, followsPressure: followsPressure)
        if samples.allSatisfy({ $0.point == first.point }) {
            let r = CGFloat(radii[0])
            path.addEllipse(in: CGRect(x: first.point.x - r, y: first.point.y - r, width: 2 * r, height: 2 * r))
            return path
        }
        let (left, right, directions) = edges(samples, radii: radii)
        path.move(to: left[0])
        smooth(left, into: path)
        cap(path, at: samples[samples.count - 1].point, radius: radii[radii.count - 1], from: directions[directions.count - 1] + .pi / 2)
        smooth(Array(right.reversed()), into: path)
        cap(path, at: first.point, radius: radii[0], from: directions[0] - .pi / 2)
        path.closeSubpath()
        return path
    }

    /// The two edges, each point's half-thickness from the centre line across the way it runs there, and that way.
    private static func edges(_ samples: [InkSample], radii: [Double]) -> (left: [CGPoint], right: [CGPoint], directions: [Double]) {
        var left: [CGPoint] = []
        var right: [CGPoint] = []
        var directions: [Double] = []
        for index in samples.indices {
            let before = samples[max(0, index - 1)].point
            let after = samples[min(samples.count - 1, index + 1)].point
            var dx = Double(after.x - before.x)
            var dy = Double(after.y - before.y)
            let length = hypot(dx, dy)
            if length == 0 {
                dx = 1
                dy = 0
            } else {
                dx /= length
                dy /= length
            }
            let r = radii[index]
            let point = samples[index].point
            left.append(CGPoint(x: Double(point.x) - dy * r, y: Double(point.y) + dx * r))
            right.append(CGPoint(x: Double(point.x) + dy * r, y: Double(point.y) - dx * r))
            directions.append(atan2(dy, dx))
        }
        return (left, right, directions)
    }

    /// A line through the points after the first, eased with quadratic curves through their midpoints, as the page's.
    private static func smooth(_ points: [CGPoint], into path: CGMutablePath) {
        guard points.count > 1 else { return }
        for index in 1..<(points.count - 1) {
            let mid = CGPoint(x: (points[index].x + points[index + 1].x) / 2, y: (points[index].y + points[index + 1].y) / 2)
            path.addQuadCurve(to: mid, control: points[index])
        }
        path.addLine(to: points[points.count - 1])
    }

    /// A round end: half a circle about the end point, from the edge at angle `start` round the outside of the stroke.
    private static func cap(_ path: CGMutablePath, at centre: CGPoint, radius: Double, from start: Double) {
        for step in 1...capSegments {
            let angle = start - Double.pi * Double(step) / Double(capSegments)
            path.addLine(to: CGPoint(x: Double(centre.x) + radius * cos(angle), y: Double(centre.y) + radius * sin(angle)))
        }
    }
}

/// One step Undo takes back: the strokes a stroke of the pen or a press of the eraser added, by id, and the strokes the
/// eraser took, whole, as music-core keeps them, to put back. A press of the eraser is one step, however much it took
/// (BUG-05). Where the step keeps the strokes it added whole too, Redo does it again (#521 9, the score).
struct InkStep {
    var added: [String]
    var removed: [[String: Any]] = []
    /// The strokes it added, whole, for Redo; empty where only Undo is kept (a PDF's pages).
    var strokes: [[String: Any]] = []

    /// Takes the step back through `change(call, request, draws)`: the strokes it added go, then the strokes it took come
    /// back, the answer drawn only at the last call, so a press of the eraser comes back whole at once.
    @MainActor
    func undo(remove: String, add: String, through change: (String, [String: Any], Bool) async -> Void) async {
        if !added.isEmpty { await change(remove, ["ids": added], removed.isEmpty) }
        if !removed.isEmpty { await change(add, ["strokes": removed], true) }
    }

    /// Does the step again through `change(call, request, draws)`: the strokes it took go again, then the strokes it added
    /// come back, the answer drawn only at the last call.
    @MainActor
    func redo(remove: String, add: String, through change: (String, [String: Any], Bool) async -> Void) async {
        let taken = removed.compactMap { $0["id"] as? String }
        if !taken.isEmpty { await change(remove, ["ids": taken], strokes.isEmpty) }
        if !strokes.isEmpty { await change(add, ["strokes": strokes], true) }
    }
}

/// The Pen's own history on the score (#521 9): its steps for Undo and the steps undone for Redo, apart from the
/// selection's. A new step drops what Redo had; switching tools keeps both.
struct InkHistory {
    private(set) var done: [InkStep] = []
    private(set) var undone: [InkStep] = []

    var canUndo: Bool { !done.isEmpty }
    var canRedo: Bool { !undone.isEmpty }

    mutating func record(_ step: InkStep) {
        done.append(step)
        undone.removeAll()
    }

    /// The last step, to take back; it waits for Redo.
    mutating func undo() -> InkStep? {
        guard let step = done.popLast() else { return nil }
        undone.append(step)
        return step
    }

    /// The step undone last, to do again.
    mutating func redo() -> InkStep? {
        guard let step = undone.popLast() else { return nil }
        done.append(step)
        return step
    }
}

/// What is left of a stroke after the eraser passed along a path (#479 item 7): the runs of its centre line farther from
/// the path than the eraser's reach and the stroke's own half-thickness, as a real eraser leaves a line in pieces. The
/// line is first walked in short steps, so a cut can fall between two of its points. The same as the score page's
/// `eraseAlong` (Scripts/score/pencil-marks.ts), for a PDF's pages.
enum InkSplit {
    /// The pieces left, each two points or more; nil when the eraser missed the stroke, empty when it took all of it.
    static func pieces(_ samples: [InkSample], radius: Double, path: [CGPoint], reach: Double) -> [[InkSample]]? {
        guard !samples.isEmpty, !path.isEmpty else { return nil }
        let limit = reach + radius
        guard near(bounds(samples.map(\.point)), bounds(path), within: limit) else { return nil }
        let walked = densified(samples, step: max(0.5, limit / 2))
        let gone = walked.map { distance(from: $0.point, to: path) <= limit }
        guard gone.contains(true) else { return nil }
        var pieces: [[InkSample]] = []
        var run: [InkSample] = []
        for (sample, erased) in zip(walked, gone) {
            if erased {
                if run.count > 1 { pieces.append(run) }
                run = []
            } else {
                run.append(sample)
            }
        }
        if run.count > 1 { pieces.append(run) }
        return pieces
    }

    /// The box round the points (a point's box has no size): a quick no before every point is measured.
    static func bounds(_ points: [CGPoint]) -> CGRect {
        guard let first = points.first else { return .null }
        var box = CGRect(origin: first, size: .zero)
        for point in points.dropFirst() {
            box = box.union(CGRect(origin: point, size: .zero))
        }
        return box
    }

    /// Whether two boxes come within `gap` of each other.
    static func near(_ a: CGRect, _ b: CGRect, within gap: Double) -> Bool {
        let reach = CGFloat(gap)
        return a.minX - reach <= b.maxX && b.minX - reach <= a.maxX && a.minY - reach <= b.maxY && b.minY - reach <= a.maxY
    }

    /// The stroke's points with more between any two further apart than `step`, their pressure taken along the line.
    static func densified(_ samples: [InkSample], step: Double) -> [InkSample] {
        guard var previous = samples.first else { return [] }
        var walked = [previous]
        for sample in samples.dropFirst() {
            let length = hypot(Double(sample.point.x - previous.point.x), Double(sample.point.y - previous.point.y))
            let parts = Int((length / step).rounded(.up))
            if parts > 1 {
                for part in 1..<parts {
                    let t = Double(part) / Double(parts)
                    let point = CGPoint(
                        x: Double(previous.point.x) + t * Double(sample.point.x - previous.point.x),
                        y: Double(previous.point.y) + t * Double(sample.point.y - previous.point.y)
                    )
                    walked.append(InkSample(point, pressure: previous.pressure + t * (sample.pressure - previous.pressure)))
                }
            }
            walked.append(sample)
            previous = sample
        }
        return walked
    }

    /// How far a point is from a line through the points, or from the one point.
    static func distance(from point: CGPoint, to path: [CGPoint]) -> Double {
        guard path.count > 1 else {
            return path.first.map { hypot(Double($0.x - point.x), Double($0.y - point.y)) } ?? .infinity
        }
        var nearest = Double.infinity
        for index in 1..<path.count {
            nearest = min(nearest, segmentDistance(point, path[index - 1], path[index]))
        }
        return nearest
    }

    private static func segmentDistance(_ point: CGPoint, _ a: CGPoint, _ b: CGPoint) -> Double {
        let dx = Double(b.x - a.x)
        let dy = Double(b.y - a.y)
        let lengthSquared = dx * dx + dy * dy
        let t = lengthSquared == 0 ? 0 : max(0, min(1, (Double(point.x - a.x) * dx + Double(point.y - a.y) * dy) / lengthSquared))
        return hypot(Double(a.x) + t * dx - Double(point.x), Double(a.y) + t * dy - Double(point.y))
    }
}
