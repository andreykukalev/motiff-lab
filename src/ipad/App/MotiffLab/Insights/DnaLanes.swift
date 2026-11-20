import SwiftUI

/// Piece DNA's lanes on the bar axis: the form with its arcs, the bar numbers, the key, the harmony's place, the density,
/// new material, register and dynamics, and a lane per idea with its marks.
extension DnaScreen {
    var lanes: some View {
        VStack(alignment: .leading, spacing: 0) {
            row(text["dna.form"], tag: map.form.isEmpty ? nil : text["dna.inferred"], height: 56, why: text["dna.why.form"]) { width in
                if map.form.isEmpty {
                    empty(text["dna.formEmpty"])
                } else {
                    ZStack(alignment: .topLeading) {
                        ForEach(Array(arcs.enumerated()), id: \.offset) { _, arc in
                            Arc()
                                .stroke(Color.ds("accent").opacity(0.6), lineWidth: 1.5)
                                .frame(width: max(1, x(middle(arc.1), width) - x(middle(arc.0), width)), height: 14)
                                .offset(x: x(middle(arc.0), width), y: 2)
                        }
                        ForEach(map.form) { section in
                            let left = x(section.onset, width)
                            let changed = section.label.count > 1
                            Button {
                                if circle != nil, let other = map.circles.first(where: { $0.id == section.id }) { show(other, symbol: nil) }
                                tap(section.onset..<section.end)
                            } label: {
                                Text(section.label)
                                    .dsType(.heading)
                                    .frame(width: max(24, x(section.end, width) - left), height: 28)
                                    .background(Color.ds(sectionColour(section)), in: Rectangle())
                                    .overlay(Rectangle().strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: changed ? [3, 2] : [])))
                            }
                            .buttonStyle(.plain)
                            .offset(x: left, y: 22)
                            .accessibilityLabel("\(section.label), \(bars(section.firstBar, section.lastBar))")
                        }
                    }
                }
            }
            row("", height: 20) { width in
                ZStack(alignment: .topLeading) {
                    ForEach(axisBars, id: \.self) { number in
                        if let span = map.bar(number) {
                            Text(String(number)).dsType(.caption).foregroundStyle(Color.ds("ink-2")).offset(x: x(span.lowerBound, width))
                        }
                    }
                }
                .accessibilityHidden(true)
            }
            row(text["dna.key"], why: text["dna.why.key"], accessory: map.circles.isEmpty ? nil : AnyView(circleButton)) { width in
                if map.keys.isEmpty {
                    empty(text["dna.keyEmpty"])
                } else {
                    ZStack(alignment: .leading) {
                        ForEach(Array(map.keys.enumerated()), id: \.offset) { _, key in
                            Button {
                                openCircle(at: key.onset, symbol: nil)
                            } label: {
                                Text(keyName(key))
                                    .dsType(.caption)
                                    .foregroundStyle(Color.ds("ink"))
                                    .lineLimit(1)
                                    .padding(.horizontal, CGFloat.ds("s-2"))
                                    .frame(width: max(1, x(key.end, width) - x(key.onset, width)), height: 22)
                                    .background(key.minor ? Color.ds("plum-tint") : Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                            }
                            .buttonStyle(.plain)
                            .offset(x: x(key.onset, width))
                            .accessibilityHint(text["dna.keyHint"])
                        }
                    }
                }
            }
            row(text["dna.harmony"], height: harmonyHeight, why: text["dna.why.harmony"], accessory: AnyView(harmonyControls)) { width in
                if map.harmony.isEmpty {
                    empty(text["dna.harmonyEmpty"])
                } else {
                    harmonyRows(width)
                        .accessibilityElement(children: .contain)
                }
            }
            row(text["dna.density"], height: 40, why: text["dna.why.density"]) { width in
                bars(map.density, of: max(0.01, map.density.map(\.value).max() ?? 1), colour: Color.ds("accent-tint-strong"), width: width)
            }
            row(text["dna.newMaterial"], why: text["dna.why.newMaterial"]) { width in
                ZStack(alignment: .leading) {
                    ForEach(map.firstBar...max(map.firstBar, map.lastBar), id: \.self) { number in
                        if let span = map.bar(number) {
                            Rectangle()
                                .fill(Color.ds(map.newMaterial.contains(number) ? "ink-2" : "hairline-strong"))
                                .frame(width: max(1, x(span.upperBound, width) - x(span.lowerBound, width) - 1), height: 12)
                                .offset(x: x(span.lowerBound, width))
                        }
                    }
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(text.format("dna.newMaterialSummary", map.newMaterial.map(String.init).joined(separator: ", ")))
            }
            HStack {
                Spacer()
                Button {
                    moreLanes.toggle()
                } label: {
                    HStack(spacing: 4) {
                        Text(moreLanes ? "▾" : "▸")
                        Text(text["dna.moreLanes"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            .padding(.top, CGFloat.ds("s-1"))
            if moreLanes {
                row(text["dna.register"], height: 40, why: text["dna.why.register"]) { width in
                    registerLane(width)
                }
                row(text["dna.dynamics"], height: 40, why: text["dna.why.dynamics"]) { width in
                    if map.dynamics.isEmpty {
                        empty(text["dna.dynamicsEmpty"])
                    } else {
                        bars(map.dynamics, of: 127, colour: Color.ds("teal-tint"), width: width)
                    }
                }
            }
            HStack(spacing: CGFloat.ds("s-1")) {
                Text(text["dna.ideasTitle"]).overline()
                // The ideas by what they are in the music (#582 item 15) are readings of the phrases and sections.
                if !map.ideas.isEmpty { laneTag(text["dna.inferred"]) }
            }
            .padding(.top, CGFloat.ds("s-4"))
            .padding(.bottom, CGFloat.ds("s-1"))
            if map.ideas.isEmpty {
                empty(text[map.ideasStatus == "ready" ? "dna.ideasNone" : "dna.ideasEmpty"])
                    .padding(.vertical, CGFloat.ds("s-2"))
            }
            ForEach(map.ideas) { idea in
                row(idea.title, overline: false) { width in
                    ZStack(alignment: .leading) {
                        ForEach(Array(marks(idea, width).enumerated()), id: \.offset) { _, entry in
                            let mark = entry.0
                            let tappable = entry.1
                            let on = heard == mark.onset..<mark.end
                            Button {
                                tap(mark.onset..<mark.end)
                            } label: {
                                MarkShape(kind: mark.kind)
                                    .frame(width: 24, height: 24)
                                    .background(on ? Color.ds("accent-tint") : .clear, in: Circle())
                                    .contentShape(Circle())
                            }
                            .buttonStyle(.plain)
                            .disabled(!tappable)
                            .offset(x: x(mark.onset, width) - 12)
                            .accessibilityLabel(markName(idea, mark))
                            .accessibilityHidden(!tappable)
                        }
                    }
                }
            }
            if !map.ideas.isEmpty {
                HStack(spacing: 6) {
                    Spacer()
                    MarkShape(kind: "exact").frame(width: 10, height: 10)
                    Text(text["dna.legendExact"])
                    Text("·")
                    MarkShape(kind: "transposed").frame(width: 10, height: 10)
                    Text(text["dna.legendTransposed"])
                }
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .padding(.top, CGFloat.ds("s-2"))
                .accessibilityHidden(true)
            }
        }
        .overlay(alignment: .topLeading) {
            // Where the music is, across the lanes, dragged by its handle on top (Andrei, v0.8 finding 15).
            if map.end > map.start {
                DnaPlayhead(map: map, player: player, text: text, nameWidth: nameWidth, taken: playheadTaken)
            }
        }
    }

    /// A lane: its name in the left column, with what it shows (`why`) and its own controls under it, and its track on
    /// the bar axis, `width` wide.
    private func row<Track: View>(_ name: String, tag: String? = nil, overline: Bool = true, height: CGFloat = 36, why: String? = nil,
                                  accessory: AnyView? = nil, @ViewBuilder track: @escaping (CGFloat) -> Track) -> some View {
        HStack(alignment: why == nil ? .center : .top, spacing: 0) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: CGFloat.ds("s-1")) {
                    if overline {
                        Text(name).overline()
                    } else {
                        Text(name).dsType(.label).fontWeight(.semibold).lineLimit(2)
                    }
                    if let tag { laneTag(tag) }
                }
                if let accessory { accessory }
                if let why {
                    Text(why)
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.vertical, why == nil ? 0 : CGFloat.ds("s-2"))
            .padding(.trailing, CGFloat.ds("s-3"))
            .frame(width: nameWidth, alignment: .leading)
            GeometryReader { proxy in
                track(proxy.size.width)
                    .frame(width: proxy.size.width, height: proxy.size.height, alignment: .leading)
            }
            .frame(height: height)
        }
        .padding(.vertical, 2)
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }

    /// A lane's note on what it is, such as "inferred".
    private func laneTag(_ words: String) -> some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds("ink-2"))
            .padding(.horizontal, 4)
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("hairline-strong")))
    }

    private func empty(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
    }

    /// A tick as a distance along a track `width` wide.
    func x(_ tick: Double, _ width: CGFloat) -> CGFloat {
        CGFloat(map.fraction(of: tick)) * width
    }

    private func middle(_ section: FormSection) -> Double { (section.onset + section.end) / 2 }

    /// Arcs from each section that brings material back to the one before it with the same letter.
    private var arcs: [(FormSection, FormSection)] {
        map.form.compactMap { section in
            map.form.last { $0.onset < section.onset && section.sharesWith.contains($0.id) }.map { ($0, section) }
        }
    }

    /// The bar numbers under the form: the first bar, then about ten more.
    private var axisBars: [Int] {
        let count = map.lastBar - map.firstBar + 1
        let step = max(1, Int((Double(count) / 10).rounded(.up)))
        return Array(stride(from: map.firstBar, through: map.lastBar, by: step))
    }

    private func bars(_ levels: [DnaMap.Level], of most: Double, colour: Color, width: CGFloat) -> some View {
        ZStack(alignment: .bottomLeading) {
            ForEach(Array(levels.enumerated()), id: \.offset) { _, level in
                if let span = map.bar(level.bar) {
                    Rectangle()
                        .fill(colour)
                        .frame(width: max(1, x(span.upperBound, width) - x(span.lowerBound, width) - 1), height: 2 + CGFloat(min(level.value / most, 1)) * 30)
                        .offset(x: x(span.lowerBound, width))
                }
            }
        }
        .frame(maxHeight: .infinity, alignment: .bottom)
        .accessibilityHidden(true)
    }

    private func registerLane(_ width: CGFloat) -> some View {
        let low = map.register.map(\.low).min() ?? 0
        let high = max(low + 1, map.register.map(\.high).max() ?? 1)
        let range = Double(high - low)
        return ZStack(alignment: .bottomLeading) {
            ForEach(Array(map.register.enumerated()), id: \.offset) { _, span in
                if let bar = map.bar(span.bar) {
                    Rectangle()
                        .fill(Color.ds("plum-tint"))
                        .frame(width: max(1, x(bar.upperBound, width) - x(bar.lowerBound, width) - 1), height: 2 + CGFloat(Double(span.high - span.low) / range) * 30)
                        .offset(x: x(bar.lowerBound, width), y: -CGFloat(Double(span.low - low) / range) * 30)
                }
            }
        }
        .frame(maxHeight: .infinity, alignment: .bottom)
        .accessibilityHidden(true)
    }

    /// The idea's marks, each saying whether it is far enough from the one before it for a finger.
    private func marks(_ idea: DnaMap.Idea, _ width: CGFloat) -> [(DnaMap.Mark, Bool)] {
        var last: CGFloat = -.infinity
        return idea.marks.map { mark in
            let left = x(mark.onset, width)
            let tappable = left - last >= 24
            if tappable { last = left }
            return (mark, tappable)
        }
    }

    func bars(_ first: Int, _ last: Int) -> String {
        first == last ? text.format("dna.bar", String(first)) : text.format("dna.barsRange", String(first), String(last))
    }

    private func markName(_ idea: DnaMap.Idea, _ mark: DnaMap.Mark) -> String {
        let key: String
        switch mark.kind {
        case "original": key = idea.fromSelection ? "dna.mark.original" : "dna.mark.first"
        case "exact": key = "dna.mark.exact"
        case "transposed": key = "dna.mark.transposed"
        case "varied": key = "dna.mark.varied"
        default: key = "dna.mark.inverted"
        }
        return text.format(key, bars(mark.firstBar, mark.lastBar))
    }

    /// A section's colour by its letter, in the order the letters first come (A′ keeps A's colour, with a dashed edge).
    private func sectionColour(_ section: FormSection) -> String {
        func letter(_ label: String) -> String {
            var label = label
            while let last = label.last, "′″‴".contains(last) { label.removeLast() }
            return label
        }
        var letters: [String] = []
        for other in map.form where !letters.contains(letter(other.label)) { letters.append(letter(other.label)) }
        let tints = ["accent-tint", "amber-tint", "teal-tint", "plum-tint"]
        return tints[(letters.firstIndex(of: letter(section.label)) ?? 0) % tints.count]
    }
}

/// An arc over the form, from one section's middle to another's.
private struct Arc: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.maxY))
        path.addQuadCurve(to: CGPoint(x: rect.maxX, y: rect.maxY), control: CGPoint(x: rect.midX, y: rect.minY - rect.height * 0.6))
        return path
    }
}

/// A mark on an idea's lane (0018 section 11): filled for the idea and an exact repeat, hollow for a transposed or varied
/// one, a triangle for an inverted one.
struct MarkShape: View {
    let kind: String

    var body: some View {
        switch kind {
        case "transposed", "varied":
            Circle().strokeBorder(Color.ds("accent"), lineWidth: 1.5).frame(width: 10, height: 10)
        case "inverted":
            Triangle().fill(Color.ds("accent")).frame(width: 12, height: 10)
        default:
            Circle().fill(Color.ds("accent")).frame(width: 10, height: 10)
        }
    }

    private struct Triangle: Shape {
        func path(in rect: CGRect) -> Path {
            var path = Path()
            path.move(to: CGPoint(x: rect.minX, y: rect.minY))
            path.addLine(to: CGPoint(x: rect.maxX, y: rect.minY))
            path.addLine(to: CGPoint(x: rect.midX, y: rect.maxY))
            path.closeSubpath()
            return path
        }
    }
}