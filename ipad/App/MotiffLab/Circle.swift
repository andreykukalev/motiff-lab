import SwiftUI

/// The circle of fifths for the selection, as music-core states it (facade `circle`, `KeyCircle`): the twelve places, the
/// key with how sure the reading is and the key that fits almost as well, the passage's chords on the sectors of their
/// roots, the notes outside the scale, the scale to hear and the lines that say what it shows. The app only places it.
struct KeyCircle: Equatable, Sendable {
    struct Sector: Equatable, Sendable {
        var position: Int
        var major: String
        var minor: String
        var signature: String
    }

    struct Chord: Equatable, Sendable {
        var number: Int
        var symbol: String
        var degree: String?
        var position: Int
        var minor: Bool
        var place: Fact.Place
    }

    var name: String
    /// "detected", "inferred" or "uncertain".
    var certainty: String
    var position: Int
    var minor: Bool
    var runnerUpPosition: Int?
    var runnerUpMinor: Bool
    var runnerUpNote: String?
    var sectors: [Sector]
    var neighbourPositions: [Int]
    var chords: [Chord]
    var outside: Fact.Place?
    var scale: [PlaybackNote]
    var lines: [String]

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil, let circle = object["circle"] as? [String: Any],
              let name = circle["name"] as? String, let position = circle["position"] as? Int else { return nil }
        func place(_ value: Any?) -> Fact.Place? {
            guard let place = value as? [String: Any], let onset = (place["onset"] as? NSNumber)?.doubleValue,
                  let end = (place["end"] as? NSNumber)?.doubleValue else { return nil }
            return Fact.Place(label: place["label"] as? String ?? "", onset: onset, end: end, noteIds: place["noteIds"] as? [String] ?? [])
        }
        func isMinor(_ key: Any?) -> Bool { (key as? [String: Any])?["mode"] as? String == "minor" }
        self.name = name
        certainty = circle["certainty"] as? String ?? "inferred"
        self.position = position
        minor = isMinor(circle["key"])
        runnerUpPosition = circle["runnerUpPosition"] as? Int
        runnerUpMinor = isMinor(circle["runnerUp"])
        runnerUpNote = circle["runnerUpNote"] as? String
        sectors = (circle["sectors"] as? [[String: Any]] ?? []).compactMap { sector in
            guard let position = sector["position"] as? Int else { return nil }
            return Sector(position: position, major: sector["major"] as? String ?? "", minor: sector["minor"] as? String ?? "", signature: sector["signature"] as? String ?? "")
        }
        neighbourPositions = (circle["neighbours"] as? [[String: Any]] ?? []).compactMap { $0["position"] as? Int }
        chords = (circle["chords"] as? [[String: Any]] ?? []).compactMap { chord in
            guard let number = chord["number"] as? Int, let position = chord["position"] as? Int, let place = place(chord["place"]) else { return nil }
            return Chord(number: number, symbol: chord["symbol"] as? String ?? "", degree: chord["degree"] as? String, position: position,
                         minor: chord["ring"] as? String == "minor", place: place)
        }
        outside = place(circle["outside"])
        scale = (circle["scaleNotes"] as? [[String: Any]] ?? []).compactMap { note in
            guard let pitch = note["pitch"] as? Int, let onset = (note["onset"] as? NSNumber)?.doubleValue,
                  let end = (note["end"] as? NSNumber)?.doubleValue else { return nil }
            return PlaybackNote(onset: onset, duration: end - onset, pitch: pitch, velocity: 80)
        }
        lines = circle["lines"] as? [String] ?? []
    }

    /// Where the scale sounds, in ticks.
    var scaleRange: Range<Double>? {
        guard let first = scale.first, let last = scale.last else { return nil }
        return first.onset..<(last.onset + last.duration)
    }
}

/// The circle in a fact's row (the web app's KeyCircleCard): the major keys outside and their relative minors inside, the
/// key in cobalt (dashed when inferred), the key that fits almost as well outlined, the chords as numbered dots; then its
/// lines, the chords and the notes outside the scale as chips that ring on the score, and Hear the scale.
struct KeyCircleCard: View {
    let circle: KeyCircle
    let fact: Fact
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            CircleDrawing(circle: circle, chosen: insights.chosen?.custom)
                .frame(maxWidth: 288)
                .aspectRatio(1, contentMode: .fit)
                .frame(maxWidth: .infinity)
                .accessibilityElement()
                .accessibilityLabel(text.format("circle.aria", circle.name))
                .accessibilityAddTraits(.isImage)
            if let note = circle.runnerUpNote {
                caption(note)
            }
            ForEach(circle.lines, id: \.self) { caption($0) }
            if !circle.chords.isEmpty || circle.outside != nil {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(circle.chords, id: \.number) { chord in
                        chip("\(chord.number). \(chord.symbol)\(chord.degree.map { " (\($0))" } ?? "")", place: chord.place)
                    }
                    if let outside = circle.outside {
                        chip(outside.label, place: outside)
                            .accessibilityHint(text["circle.showHint"])
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["circle.show"])
            }
            Button {
                insights.hearScale()
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: insights.scaleHeard ? "pause" : "play")
                    Text(text[insights.scaleHeard ? "circle.stopScale" : "circle.hearScale"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityAddTraits(insights.scaleHeard ? .isSelected : [])
        }
        .padding(CGFloat.ds("s-3"))
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["circle.title"])
    }

    private func chip(_ label: String, place: Fact.Place) -> some View {
        let on = insights.chosen?.custom == place
        return Button(label) { insights.chooseCirclePlace(place, of: fact) }
            .buttonStyle(DSChipStyle(on: on))
            .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
    }
}

/// The drawing itself, in the web card's units: a square 236 across, the circle's centre in the middle.
private struct CircleDrawing: View {
    let circle: KeyCircle
    let chosen: Fact.Place?

    private static let outer = 100.0, middle = 66.0, inner = 36.0, step = 30.0

    var body: some View {
        GeometryReader { size in
            let unit = size.size.width / 236
            let centre = CGPoint(x: size.size.width / 2, y: size.size.height / 2)
            ZStack {
                ForEach(circle.sectors, id: \.position) { sector in
                    ring(sector.position, minor: false, unit: unit, centre: centre)
                    ring(sector.position, minor: true, unit: unit, centre: centre)
                    label(sector.major, at: point(sector.position, 84, unit, centre), size: 11 * unit, weight: 600, colour: "ink")
                    label(sector.minor, at: point(sector.position, 57, unit, centre), size: 8.5 * unit, weight: 400, colour: "ink")
                    label(sector.signature, at: point(sector.position, 28, unit, centre), size: 7 * unit, weight: 400, colour: "ink-2")
                }
                ForEach(chordAngles()) { entry in
                    let on = chosen == entry.chord.place
                    let radius = entry.chord.minor ? 43.0 : 108.0
                    let at = point(degrees: entry.angle, radius, unit, centre)
                    Circle()
                        .fill(Color.ds(on ? "accent" : "ink"))
                        .frame(width: 12 * unit, height: 12 * unit)
                        .position(at)
                    label(String(entry.chord.number), at: at, size: 8 * unit, weight: 600, colour: on ? "on-accent" : "paper")
                }
            }
        }
    }

    @ViewBuilder private func ring(_ position: Int, minor: Bool, unit: CGFloat, centre: CGPoint) -> some View {
        let shape = SectorShape(position: position, inner: (minor ? Self.inner : Self.middle) * unit, outer: (minor ? Self.middle : Self.outer) * unit, centre: centre)
        let isKey = position == circle.position && minor == circle.minor
        let isRunnerUp = position == circle.runnerUpPosition && minor == circle.runnerUpMinor
        let near = circle.neighbourPositions.contains(position)
        shape.fill(Color.ds(isKey ? "accent-tint-strong" : near ? "surface" : "surface-2"))
        shape.stroke(
            Color.ds(isKey ? "accent" : isRunnerUp ? "ink-2" : "hairline-strong"),
            style: StrokeStyle(
                lineWidth: (isKey ? 1.6 : isRunnerUp ? 1.2 : 0.6) * unit,
                dash: isKey && circle.certainty != "detected" ? [4 * unit, 3 * unit] : isRunnerUp ? [2 * unit, 2 * unit] : []))
    }

    private func label(_ words: String, at: CGPoint, size: CGFloat, weight: Int, colour: String) -> some View {
        Text(words)
            .font(DesignTokens.font(weight: weight, size: max(size, 6)))
            .foregroundStyle(Color.ds(colour))
            .fixedSize()
            .position(at)
            .accessibilityHidden(true)
    }

    /// A chord where it is drawn: the angle of its dot.
    private struct Placed: Identifiable {
        var chord: KeyCircle.Chord
        var angle: Double
        var id: Int { chord.number }
    }

    /// The chords on their sectors; several on one sector sit side by side within it.
    private func chordAngles() -> [Placed] {
        var result: [Placed] = []
        var groups: [String: [KeyCircle.Chord]] = [:]
        var order: [String] = []
        for chord in circle.chords {
            let key = "\(chord.position)-\(chord.minor)"
            if groups[key] == nil { order.append(key) }
            groups[key, default: []].append(chord)
        }
        for key in order {
            let chords = groups[key] ?? []
            for (index, chord) in chords.enumerated() {
                let spread = chords.count == 1 ? 0 : (Double(index) - Double(chords.count - 1) / 2) * (Self.step * 0.8 / Double(chords.count))
                result.append(Placed(chord: chord, angle: Double(chord.position) * Self.step + spread))
            }
        }
        return result
    }

    private func point(_ position: Int, _ radius: Double, _ unit: CGFloat, _ centre: CGPoint) -> CGPoint {
        point(degrees: Double(position) * Self.step, radius, unit, centre)
    }

    private func point(degrees: Double, _ radius: Double, _ unit: CGFloat, _ centre: CGPoint) -> CGPoint {
        let angle: Double = degrees * .pi / 180
        let length = radius * Double(unit)
        let x: Double = length * sin(angle), y: Double = length * cos(angle)
        return CGPoint(x: centre.x + CGFloat(x), y: centre.y - CGFloat(y))
    }
}

/// One place of a ring: from 15° before its centre to 15° after, between two radii, 0° at the top, clockwise.
private struct SectorShape: Shape {
    let position: Int
    let inner: CGFloat
    let outer: CGFloat
    let centre: CGPoint

    func path(in rect: CGRect) -> Path {
        let middle = Double(position) * 30
        let angles: [Double] = stride(from: middle - 15, through: middle + 15, by: 2.5).map { $0 * .pi / 180 }
        func at(_ angle: Double, _ radius: CGFloat) -> CGPoint {
            let length = Double(radius)
            let x: Double = length * sin(angle), y: Double = length * cos(angle)
            return CGPoint(x: centre.x + CGFloat(x), y: centre.y - CGFloat(y))
        }
        var path = Path()
        path.move(to: at(angles[0], outer))
        for angle in angles.dropFirst() { path.addLine(to: at(angle, outer)) }
        for angle in angles.reversed() { path.addLine(to: at(angle, inner)) }
        path.closeSubpath()
        return path
    }
}
