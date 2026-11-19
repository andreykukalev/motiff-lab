import SwiftUI

/// Piece DNA's harmony rows (chords, steps of the key, functions, closes), the rows' switches, and the card over the map:
/// the circle of fifths of a section or key stretch, or what a tapped close is.
extension DnaScreen {
    /// Bars joined while they keep the same chord, or the same function.
    private struct Segment {
        var onset: Double
        var end: Double
        var symbol: String
        var degree: String
        var function: String
        var clear: Bool
        var firstBar: Int
        var lastBar: Int
        var doubt: String?

        var middle: Double { (onset + end) / 2 }
    }

    private func segments(_ same: (DnaMap.Chord, DnaMap.Chord) -> Bool) -> [Segment] {
        var result: [Segment] = []
        var previous: DnaMap.Chord?
        for chord in map.harmony {
            guard let span = map.bar(chord.bar) else { continue }
            if let before = previous, same(before, chord), var last = result.last, last.lastBar == chord.bar - 1 {
                last.end = span.upperBound
                last.lastBar = chord.bar
                last.clear = last.clear && chord.clear
                last.doubt = last.doubt ?? chord.doubt
                result[result.count - 1] = last
            } else {
                result.append(Segment(onset: span.lowerBound, end: span.upperBound, symbol: chord.symbol, degree: chord.degree,
                                      function: chord.function, clear: chord.clear, firstBar: chord.bar, lastBar: chord.bar, doubt: chord.doubt))
            }
            previous = chord
        }
        return result
    }

    private var chordSegments: [Segment] { segments { $0.symbol == $1.symbol } }
    private var functionSegments: [Segment] { segments { $0.function == $1.function }.filter { $0.function != "none" } }

    /// The segments whose label fits, each centred on its bars: one is left out where it would touch the one before.
    private func labels(_ segments: [Segment], _ width: CGFloat, least: CGFloat, _ label: (Segment) -> String) -> [Segment] {
        var edge = -CGFloat.infinity
        var shown: [Segment] = []
        for segment in segments {
            let words = label(segment)
            guard !words.isEmpty else { continue }
            let room = max(CGFloat(words.count + 1) * 8, least)
            let centre = x(segment.middle, width)
            guard centre - room / 2 >= edge else { continue }
            edge = centre + room / 2
            shown.append(segment)
        }
        return shown
    }

    /// A harmony row's switch as it is kept on the device, with the catalogue's default until the settings are read.
    private func rowOn(_ id: String, _ fallback: Bool) -> Bool {
        let value = settings[id]
        return value.isEmpty ? fallback : value == "on"
    }

    private var showChords: Bool { rowOn("dna.chords", true) }
    private var showDegrees: Bool { rowOn("dna.degrees", false) }
    private var showFunctions: Bool { rowOn("dna.functions", true) }

    var harmonyHeight: CGFloat {
        var height: CGFloat = 24 + 8
        if showChords { height += 27 }
        if showDegrees { height += 21 }
        if showFunctions { height += 19 }
        return height
    }

    /// The symbol of the chord picked on the open circle, lit on the chords row.
    private var pickedSymbol: String? {
        guard let circle, let chosenChord else { return nil }
        return circle.circle.chords.first { $0.number == chosenChord }?.symbol
    }

    func harmonyRows(_ width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            if showChords { chordRow(width) }
            if showDegrees { degreeRow(width) }
            if showFunctions { functionRow(width) }
            cadenceRow(width)
        }
    }

    /// The chords, each opening the circle on its bar with it picked.
    private func chordRow(_ width: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(labels(chordSegments, width, least: 28) { $0.symbol }.enumerated()), id: \.offset) { _, segment in
                let picked = pickedSymbol == segment.symbol
                Button {
                    openCircle(at: segment.onset, symbol: segment.symbol)
                } label: {
                    Text(segment.symbol)
                        .dsType(.caption)
                        .fontWeight(.semibold)
                        .foregroundStyle(Color.ds(segment.clear ? "ink" : "ink-2"))
                        .lineLimit(1)
                        .fixedSize()
                        .padding(.horizontal, 3)
                        .frame(minWidth: 24, minHeight: 24)
                        .background(picked ? Color.ds("accent-tint") : Color.clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .position(x: x(segment.middle, width), y: 12)
                .accessibilityLabel(chordName(segment))
                .accessibilityHint(text["dna.keyHint"])
            }
        }
        .frame(width: width, height: 24, alignment: .topLeading)
    }

    /// The chords' steps of the key.
    private func degreeRow(_ width: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(labels(chordSegments, width, least: 24) { $0.degree }.enumerated()), id: \.offset) { _, segment in
                Text(segment.degree)
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .fixedSize()
                    .position(x: x(segment.middle, width), y: 9)
            }
        }
        .frame(width: width, height: 18, alignment: .topLeading)
        .accessibilityHidden(true)
    }

    /// The functions in their own colours, a band per stretch.
    private func functionRow(_ width: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(functionSegments.enumerated()), id: \.offset) { _, segment in
                let left = x(segment.onset, width)
                let span = max(1, x(segment.end, width) - left - 1)
                let colour = CircleDrawing.functionColour(segment.function)
                ZStack(alignment: .bottom) {
                    Rectangle().fill(Color.ds("\(colour)-tint"))
                    Rectangle().fill(Color.ds(colour)).frame(height: 3)
                    if span >= 12 {
                        Text(functionLetter(segment.function))
                            .dsType(.caption)
                            .fontWeight(.semibold)
                            .foregroundStyle(Color.ds("ink"))
                            .frame(maxHeight: .infinity)
                    }
                }
                .frame(width: span, height: 16)
                .offset(x: left)
            }
        }
        .frame(width: width, height: 16, alignment: .topLeading)
        .accessibilityHidden(true)
    }

    /// The closes, each saying what it is when tapped.
    private func cadenceRow(_ width: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(Array(shownCadences(width).enumerated()), id: \.offset) { _, cadence in
                let words = text[cadence.full ? "dna.cadFull" : "dna.cadHalf"]
                let on = explained != nil && explainedRange == map.bar(cadence.bar)
                Button {
                    explain(cadence)
                } label: {
                    Text(words)
                        .dsType(.caption)
                        .foregroundStyle(Color.ds(on ? "accent" : "ink-2"))
                        .lineLimit(1)
                        .fixedSize()
                        .padding(.horizontal, CGFloat.ds("s-2"))
                        .frame(height: 18)
                        .background(Color.ds("surface-2"), in: Capsule())
                        .overlay(Capsule().strokeBorder(Color.ds(on ? "accent" : "hairline-strong"),
                                                        style: StrokeStyle(lineWidth: 1, dash: cadence.full ? [] : [3, 2])))
                        .frame(minWidth: 24, minHeight: 24)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .position(x: x(cadence.at, width), y: 12)
                .accessibilityLabel(cadence.explanation.isEmpty ? words : cadence.explanation)
            }
        }
        .frame(width: width, height: 24, alignment: .topLeading)
    }

    /// The closes whose pill fits beside the one before.
    private func shownCadences(_ width: CGFloat) -> [DnaMap.Cadence] {
        var edge = -CGFloat.infinity
        var shown: [DnaMap.Cadence] = []
        for cadence in map.cadences {
            let room = CGFloat(text[cadence.full ? "dna.cadFull" : "dna.cadHalf"].count + 4) * 8
            let centre = x(cadence.at, width)
            guard centre - room / 2 >= edge else { continue }
            edge = centre + room / 2
            shown.append(cadence)
        }
        return shown
    }

    private func functionLetter(_ function: String) -> String {
        switch function {
        case "tonic": text["dna.fnT"]
        case "subdominant": text["dna.fnS"]
        case "dominant": text["dna.fnD"]
        default: ""
        }
    }

    /// "Bars 1–2 Am, i, tonic".
    private func chordName(_ segment: Segment) -> String {
        var name = "\(bars(segment.firstBar, segment.lastBar)) \(segment.symbol)"
        if !segment.degree.isEmpty { name += ", \(segment.degree)" }
        if segment.function != "none" { name += ", \(text["dna.fnName.\(segment.function)"])" }
        if let doubt = segment.doubt { name += ". \(doubt)" }
        return name
    }

    /// The harmony lane's own controls: the rows to show, kept on the device. The circle is the key lane's alone (Andrei,
    /// v0.8 finding 12: the three rows are the harmony's buttons).
    var harmonyControls: some View {
        FlowRow(spacing: 4) {
            rowToggle("dna.chords", "dna.rowChords", fallback: true)
            rowToggle("dna.degrees", "dna.rowDegrees", fallback: false)
            rowToggle("dna.functions", "dna.rowFunctions", fallback: true)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["dna.rows"])
    }

    private func rowToggle(_ id: String, _ name: String, fallback: Bool) -> some View {
        let on = rowOn(id, fallback)
        return Button {
            settings.set(id, on ? "off" : "on")
        } label: {
            Text(text[name])
        }
        .buttonStyle(DSChipStyle(on: on))
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    var circleButton: some View {
        Button {
            toggleCircle()
        } label: {
            Text("◎ " + text["dna.circle"])
        }
        .buttonStyle(DSChipStyle(on: circle != nil))
        .accessibilityLabel(text["dna.keyHint"])
        .accessibilityAddTraits(circle != nil ? .isSelected : [])
    }

    private func toggleCircle() {
        if circle != nil {
            circle = nil
            chosenChord = nil
        } else {
            openCircle(at: heard?.lowerBound ?? map.start, symbol: nil)
        }
    }

    /// The circle of the section or key stretch a tick falls in, with a chord picked by its symbol.
    func openCircle(at tick: Double, symbol: String?) {
        guard let found = map.circles.last(where: { $0.onset <= tick }) ?? map.circles.first else { return }
        show(found, symbol: symbol)
    }

    func show(_ found: DnaMap.SectionCircle, symbol: String?) {
        explained = nil
        explainedRange = nil
        circle = found
        chosenChord = symbol.flatMap { symbol in found.circle.chords.first { $0.symbol == symbol }?.number }
    }

    private func explain(_ cadence: DnaMap.Cadence) {
        circle = nil
        chosenChord = nil
        explained = cadence.explanation.isEmpty ? text[cadence.full ? "dna.cadFull" : "dna.cadHalf"] : cadence.explanation
        explainedRange = map.bar(cadence.bar)
    }

    @ViewBuilder var pop: some View {
        if let circle {
            circleCard(circle)
        } else if let explained {
            explanationCard(explained)
        }
    }

    private func circleCard(_ shown: DnaMap.SectionCircle) -> some View {
        let picked = shown.circle.chords.first { $0.number == chosenChord }
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                Text(text.format(shown.id.hasPrefix("key_") ? "dna.circleOfKey" : "dna.circleOfSection", shown.label))
                    .dsType(.heading)
                    .accessibilityAddTraits(.isHeader)
                Spacer(minLength: 0)
                closeButton {
                    circle = nil
                    chosenChord = nil
                }
            }
            if map.circles.count > 1 {
                FlowRow(spacing: CGFloat.ds("s-1")) {
                    ForEach(map.circles) { other in
                        Button {
                            show(other, symbol: nil)
                        } label: {
                            Text(other.label)
                        }
                        .buttonStyle(DSChipStyle(on: other.id == shown.id))
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["dna.circleSections"])
            }
            CircleDrawing(circle: shown.circle, chosen: picked?.place, functions: true)
                .frame(width: 236, height: 236)
                .frame(maxWidth: .infinity)
                .accessibilityElement()
                .accessibilityLabel(text.format("circle.aria", shown.circle.name))
                .accessibilityAddTraits(.isImage)
            ForEach(shown.circle.lines, id: \.self) { line in
                Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
            if !shown.circle.chords.isEmpty {
                FlowRow(spacing: CGFloat.ds("s-1")) {
                    ForEach(shown.circle.chords, id: \.number) { chord in
                        Button {
                            chosenChord = chosenChord == chord.number ? nil : chord.number
                        } label: {
                            Text("\(chord.number). \(chord.symbol)\(chord.degree.map { " (\($0))" } ?? "")")
                        }
                        .buttonStyle(DSChipStyle(on: chord.number == chosenChord))
                    }
                }
            }
            legend
            if let picked, !picked.role.isEmpty {
                Text(picked.role)
                    .dsType(.label)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if let from = shown.fromName {
                Text(text.format("dna.modulation", shown.label, from, shown.circle.name))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            FlowRow(spacing: CGFloat.ds("s-2")) {
                if let range = shown.circle.scaleRange {
                    Button {
                        hearScale(shown.circle, over: range)
                    } label: {
                        HStack(spacing: 6) {
                            Icon(name: player.hearingAlone ? "pause" : "play")
                            Text(text[player.hearingAlone ? "circle.stopScale" : "circle.hearScale"])
                        }
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
                Button(text["dna.inScore"]) {
                    leave(picked.map { $0.place.onset..<$0.place.end } ?? shown.onset..<shown.end, insights: false)
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
        .modifier(PopSurface())
        .accessibilityElement(children: .contain)
    }

    private func explanationCard(_ words: String) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-2")) {
                Text(words)
                    .dsType(.body)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
                closeButton {
                    explained = nil
                    explainedRange = nil
                }
            }
            if let range = explainedRange {
                FlowRow(spacing: CGFloat.ds("s-2")) {
                    hearButton(range)
                    Button(text["dna.inScore"]) { leave(range, insights: false) }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
        }
        .modifier(PopSurface())
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["dna.explainTitle"])
    }

    /// The circle's zones in words: tonic, subdominant, dominant and the far keys.
    private var legend: some View {
        FlowRow(spacing: CGFloat.ds("s-2")) {
            ForEach(["tonic", "subdominant", "dominant"], id: \.self) { function in
                let colour = CircleDrawing.functionColour(function)
                HStack(spacing: 4) {
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Color.ds("\(colour)-tint"))
                        .overlay(RoundedRectangle(cornerRadius: 2).strokeBorder(Color.ds(colour)))
                        .frame(width: 10, height: 10)
                    Text(text["dna.zone" + function.prefix(1).uppercased() + String(function.dropFirst())])
                }
            }
            HStack(spacing: 4) {
                RoundedRectangle(cornerRadius: 2)
                    .fill(Color.ds("surface"))
                    .overlay(RoundedRectangle(cornerRadius: 2).strokeBorder(Color.ds("hairline-strong")))
                    .frame(width: 10, height: 10)
                Text(text["dna.zoneFar"])
            }
        }
        .dsType(.caption)
        .foregroundStyle(Color.ds("ink-2"))
        .accessibilityHidden(true)
    }

    private func closeButton(_ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: "close").frame(width: 32, height: 32)
        }
        .buttonStyle(BarActionStyle())
        .accessibilityLabel(text["dna.closeCircle"])
    }

    /// The scale of the circle's key, alone; pressed again, it stops.
    private func hearScale(_ shown: KeyCircle, over range: Range<Double>) {
        if player.hearingAlone {
            player.stopAlone()
            return
        }
        stop()
        player.hearAlone(shown.scale, over: range)
    }
}

/// The card over the map, at its corner: the circle of a section or what a close is.
private struct PopSurface: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(CGFloat.ds("s-4"))
            .frame(width: 360, alignment: .leading)
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            .dsShadow("shadow-menu")
    }
}
