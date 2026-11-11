import SwiftUI

/// The map of the whole piece as music-core answers `dna` (0018 section 11; testing item 22): the form's sections lettered
/// by what they share, the key lane, a chord per bar with the closes, the density, new material, register and dynamics,
/// and the ideas with every place each is heard. Every lane says how far it is filled.
struct DnaMap: Sendable {
    struct Section: Sendable, Identifiable, Equatable {
        var id: String
        var label: String
        var onset: Double
        var end: Double
        var firstBar: Int
        var lastBar: Int
        var sharesWith: [String]
        /// What a tap on the section selects: the whole section, or its first bars when it runs longer than a selection holds.
        var selectableOnset: Double
        var selectableEnd: Double
    }

    struct Mark: Sendable, Equatable {
        /// "original", "exact", "transposed", "varied" or "inverted".
        var kind: String
        var onset: Double
        var end: Double
        var firstBar: Int
        var lastBar: Int
    }

    struct Idea: Sendable, Identifiable {
        var id: String
        var title: String
        var summary: String
        var fromSelection: Bool
        var onset: Double
        var end: Double
        var marks: [Mark]
    }

    struct Key: Sendable {
        var label: String
        var minor: Bool
        var inferred: Bool
        var onset: Double
        var end: Double
    }

    struct Chord: Sendable {
        var bar: Int
        var symbol: String
        var clear: Bool
        /// Its step of the bar's key as a Roman numeral: "i", "V7" (testing item 30).
        var degree = ""
        /// "tonic", "subdominant", "dominant" or "none".
        var function = "none"
        /// Why it is uncertain when it rests on recognised notes still to check (#352); nil otherwise.
        var doubt: String?
    }

    struct Cadence: Sendable {
        var at: Double
        var full: Bool
        var bar = 0
        /// What the close is, in words, said when it is tapped.
        var explanation = ""
    }

    /// The circle of fifths of one section of the form, or of one key stretch (testing item 30).
    struct SectionCircle: Sendable, Identifiable {
        var id: String
        var label: String
        var onset: Double
        var end: Double
        /// The key of the stretch before, when this one is in another.
        var fromName: String?
        var circle: KeyCircle
    }

    struct Level: Sendable {
        var bar: Int
        var value: Double
    }

    struct Span: Sendable {
        var bar: Int
        var low: Int
        var high: Int
    }

    var firstBar = 1
    var lastBar = 1
    /// The map in one line, read before any lane.
    var summary = ""
    var circles: [SectionCircle] = []
    /// Where each bar starts, in ticks, from the first bar to one past the last.
    var barStarts: [Double] = []
    var form: [Section] = []
    var ideasStatus = "notYet"
    var ideas: [Idea] = []
    var keys: [Key] = []
    var harmony: [Chord] = []
    var cadences: [Cadence] = []
    var density: [Level] = []
    var newMaterial: [Int] = []
    var register: [Span] = []
    var dynamics: [Level] = []

    var start: Double { barStarts.first ?? 0 }
    var end: Double { barStarts.last ?? 0 }

    /// Where a bar starts and ends, in ticks.
    func bar(_ number: Int) -> Range<Double>? {
        let index = number - firstBar
        guard index >= 0, index + 1 < barStarts.count else { return nil }
        return barStarts[index]..<barStarts[index + 1]
    }

    init() {}

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        func number(_ value: Any?) -> Double { (value as? NSNumber)?.doubleValue ?? 0 }
        func int(_ value: Any?) -> Int { (value as? NSNumber)?.intValue ?? 0 }
        func passage(_ value: Any?) -> (Double, Double, Int, Int) {
            let passage = value as? [String: Any] ?? [:]
            return (number(passage["onset"]), number(passage["end"]), int(passage["firstBar"]), int(passage["lastBar"]))
        }
        func lane(_ name: String) -> [String: Any] { object[name] as? [String: Any] ?? [:] }
        func levels(_ name: String) -> [Level] {
            (lane(name)["bars"] as? [[String: Any]] ?? []).map { Level(bar: int($0["bar"]), value: number($0["value"])) }
        }

        firstBar = int(object["firstBar"])
        lastBar = int(object["lastBar"])
        summary = object["summary"] as? String ?? ""
        circles = (object["circles"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let drawn = entry["circle"] as? [String: Any], let circle = KeyCircle(circle: drawn) else { return nil }
            let (onset, end, _, _) = passage(entry["passage"])
            return SectionCircle(id: entry["id"] as? String ?? "", label: entry["label"] as? String ?? "", onset: onset, end: end,
                          fromName: entry["fromName"] as? String, circle: circle)
        }
        barStarts = (object["barStarts"] as? [NSNumber] ?? []).map(\.doubleValue)
        form = (lane("form")["sections"] as? [[String: Any]] ?? []).map { section in
            let (onset, end, first, last) = passage(section["passage"])
            let selectable = section["selectable"] as? [String: Any]
            return Section(id: section["id"] as? String ?? "", label: section["label"] as? String ?? "", onset: onset, end: end,
                           firstBar: first, lastBar: last, sharesWith: section["sharesWith"] as? [String] ?? [],
                           selectableOnset: selectable.map { number($0["onset"]) } ?? onset,
                           selectableEnd: selectable.map { number($0["end"]) } ?? end)
        }
        ideasStatus = lane("ideas")["status"] as? String ?? "notYet"
        ideas = (lane("ideas")["items"] as? [[String: Any]] ?? []).map { idea in
            let (onset, end, _, _) = passage(idea["passage"])
            let marks = (idea["occurrences"] as? [[String: Any]] ?? []).map { mark in
                let (onset, end, first, last) = passage(mark["passage"])
                return Mark(kind: mark["kind"] as? String ?? "exact", onset: onset, end: end, firstBar: first, lastBar: last)
            }
            return Idea(id: idea["id"] as? String ?? "", title: idea["title"] as? String ?? "", summary: idea["summary"] as? String ?? "",
                        fromSelection: idea["fromSelection"] as? Bool ?? false, onset: onset, end: end, marks: marks)
        }
        keys = (lane("keys")["spans"] as? [[String: Any]] ?? []).map { key in
            let (onset, end, _, _) = passage(key["passage"])
            let mode = (key["key"] as? [String: Any])?["mode"] as? String
            return Key(label: key["label"] as? String ?? "", minor: mode == "minor",
                       inferred: (key["modeInferred"] as? Bool ?? false) || (key["keyInferred"] as? Bool ?? false), onset: onset, end: end)
        }
        harmony = (lane("harmony")["bars"] as? [[String: Any]] ?? []).map {
            Chord(bar: int($0["bar"]), symbol: $0["symbol"] as? String ?? "", clear: $0["clear"] as? Bool ?? true,
                  degree: $0["degree"] as? String ?? "", function: $0["function"] as? String ?? "none", doubt: $0["doubt"] as? String)
        }
        cadences = (lane("harmony")["cadences"] as? [[String: Any]] ?? []).map {
            Cadence(at: number($0["at"]), full: ($0["kind"] as? String) == "full", bar: int($0["bar"]),
                    explanation: $0["explanation"] as? String ?? "")
        }
        density = levels("density")
        newMaterial = (lane("newMaterial")["bars"] as? [NSNumber] ?? []).map(\.intValue)
        register = (lane("register")["bars"] as? [[String: Any]] ?? []).map {
            Span(bar: int($0["bar"]), low: int($0["low"]), high: int($0["high"]))
        }
        dynamics = levels("dynamics")
    }
}

/// Piece DNA (0018 section 11; testing item 22; the web app's PieceDnaMap): the third place, a full-screen map of the
/// whole piece read top to bottom: the form with arcs between sections that share material, the key, the harmony with the
/// closes as dots, the density, new material, register and dynamics folded, then a lane per idea with a mark at every
/// place it is heard, and a card per idea. A mark plays its passage; a second tap opens the score there. Ask about the
/// piece opens the assistant's popover at the button, over the map, which stays as it was. The map explains itself
/// (testing item 30): a line on what it shows, a line under each lane's name, the form coloured by letter, the harmony in
/// rows that turn on and off (chords, steps of the key, functions in their own colours), the closes in words that say what
/// they are when tapped, and the circle of fifths of a section from the key and harmony lanes.
struct DnaScreen: View {
    let map: DnaMap
    let title: String
    @ObservedObject var player: Player
    @ObservedObject var conversation: AskConversation
    @ObservedObject var account: Account
    let text: UIText
    /// ‹ More when the map was opened from More; nil for the score.
    let back: (label: String, action: () -> Void)?
    let close: () -> Void
    /// Leaves for the score with the passage selected, and Insights open on it when asked.
    let showInScore: (Range<Double>, Bool) -> Void
    /// Add Structure bookmarks: answers what was added, in words.
    let addBookmarks: () async -> String?
    /// Open in Insights from the assistant's popover.
    let askInInsights: () -> Void

    @State private var heard: Range<Double>?
    @State private var playing = false
    @State private var moreLanes = false
    @State private var asking = false
    @State private var status: String?
    @ObservedObject private var settings = DeviceSettings.shared
    /// The circle of fifths open over the map, and the chord picked on it by number.
    @State private var circle: DnaMap.SectionCircle?
    @State private var chosenChord: Int?
    /// What a tapped close says, and its bar.
    @State private var explained: String?
    @State private var explainedRange: Range<Double>?

    /// The lane names' column, as the web app's `--name`.
    private let nameWidth: CGFloat = 176

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                if !map.summary.isEmpty {
                    Text(map.summary)
                        .dsType(.title)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: 640, alignment: .leading)
                }
                lanes
                if !map.ideas.isEmpty {
                    cards
                }
                actions
            }
            .padding(.horizontal, CGFloat.ds("s-4"))
            .padding(.top, CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
            .padding(.bottom, CGFloat.ds("s-8"))
        }
        .overlay(alignment: .top) { bar }
        .overlay(alignment: .bottomTrailing) {
            pop.padding(CGFloat.ds("s-4"))
        }
        .background(Color.ds("desk").ignoresSafeArea())
        .onDisappear { stop() }
    }

    private var bar: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                stop()
                if let back { back.action() } else { close() }
            } label: {
                HStack(spacing: 4) {
                    Icon(name: "back", size: CGFloat.ds("icon-l"))
                    if let back { Text(back.label).dsType(.label) }
                }
                .frame(minWidth: 40, minHeight: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(back.map { text.format("nav.backTo", $0.label) } ?? text["dna.back"])
            Text(title).dsType(.title).lineLimit(1)
            Text(text["dna.title"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            Spacer()
            Text(counts).dsType(.label).foregroundStyle(Color.ds("ink-2")).lineLimit(1)
        }
        .padding(.leading, CGFloat.ds("s-2"))
        .padding(.trailing, CGFloat.ds("s-4"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .padding(.horizontal, CGFloat.ds("s-4"))
        .padding(.top, CGFloat.ds("s-3"))
    }

    /// "106 bars · 4 ideas · A minor".
    private var counts: String {
        var parts = [text.count("dna.bars", map.lastBar - map.firstBar + 1)]
        if !map.ideas.isEmpty { parts.append(text.count("dna.ideas", map.ideas.count)) }
        if let key = map.keys.first { parts.append(keyName(key)) }
        return parts.joined(separator: " · ")
    }

    private func keyName(_ key: DnaMap.Key) -> String {
        key.inferred ? text.format("dna.keyAssumed", key.label) : key.label
    }

    // MARK: - The lanes

    private var lanes: some View {
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
            Text(text["dna.ideasTitle"])
                .overline()
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
            // Where the music is while the map plays, across the lanes.
            GeometryReader { proxy in
                if playing, player.state != .stopped, map.end > map.start, player.tick >= map.start, player.tick <= map.end {
                    let width = proxy.size.width - nameWidth
                    Rectangle()
                        .fill(Color.ds("accent"))
                        .frame(width: 2)
                        .offset(x: nameWidth + x(player.tick, width))
                }
            }
            .allowsHitTesting(false)
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
                    if let tag {
                        Text(tag)
                            .dsType(.caption)
                            .foregroundStyle(Color.ds("ink-2"))
                            .padding(.horizontal, 4)
                            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("hairline-strong")))
                    }
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

    private func empty(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
    }

    /// A tick as a distance along a track `width` wide.
    private func x(_ tick: Double, _ width: CGFloat) -> CGFloat {
        let length = map.end - map.start
        guard length > 0 else { return 0 }
        return CGFloat(min(max((tick - map.start) / length, 0), 1)) * width
    }

    private func middle(_ section: DnaMap.Section) -> Double { (section.onset + section.end) / 2 }

    /// Arcs from each section that brings material back to the one before it with the same letter.
    private var arcs: [(DnaMap.Section, DnaMap.Section)] {
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

    private func bars(_ first: Int, _ last: Int) -> String {
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

    // MARK: - Harmony rows, closes and the circle (testing item 30)

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

    private var harmonyHeight: CGFloat {
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

    private func harmonyRows(_ width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            if showChords {
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
            if showDegrees {
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
            if showFunctions {
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

    /// The harmony lane's own controls: the circle, and the rows to show, kept on the device.
    private var harmonyControls: some View {
        VStack(alignment: .leading, spacing: 4) {
            if !map.circles.isEmpty { circleButton }
            FlowRow(spacing: 4) {
                rowToggle("dna.chords", "dna.rowChords", fallback: true)
                rowToggle("dna.degrees", "dna.rowDegrees", fallback: false)
                rowToggle("dna.functions", "dna.rowFunctions", fallback: true)
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(text["dna.rows"])
        }
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

    private var circleButton: some View {
        Button {
            toggleCircle()
        } label: {
            Text("◎ " + text["dna.circle"])
        }
        .buttonStyle(DSChipStyle(on: circle != nil))
        .accessibilityLabel(text["dna.keyHint"])
        .accessibilityAddTraits(circle != nil ? .isSelected : [])
    }

    /// A section's colour by its letter, in the order the letters first come (A′ keeps A's colour, with a dashed edge).
    private func sectionColour(_ section: DnaMap.Section) -> String {
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

    private func toggleCircle() {
        if circle != nil {
            circle = nil
            chosenChord = nil
        } else {
            openCircle(at: heard?.lowerBound ?? map.start, symbol: nil)
        }
    }

    /// The circle of the section or key stretch a tick falls in, with a chord picked by its symbol.
    private func openCircle(at tick: Double, symbol: String?) {
        guard let found = map.circles.last(where: { $0.onset <= tick }) ?? map.circles.first else { return }
        show(found, symbol: symbol)
    }

    private func show(_ found: DnaMap.SectionCircle, symbol: String?) {
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

    @ViewBuilder private var pop: some View {
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
                    Button {
                        hear(range)
                    } label: {
                        HStack(spacing: 6) {
                            Icon(name: "play")
                            Text(text["dna.hear"])
                        }
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
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

    // MARK: - Cards and actions

    private var cards: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 260), spacing: CGFloat.ds("s-4"))], alignment: .leading, spacing: CGFloat.ds("s-4")) {
            ForEach(map.ideas) { idea in
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(idea.title).dsType(.heading)
                    Text(idea.summary).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                    FlowRow(spacing: CGFloat.ds("s-2")) {
                        Button {
                            hear(idea.onset..<idea.end)
                        } label: {
                            HStack(spacing: 6) {
                                Icon(name: "play")
                                Text(text["dna.hear"])
                            }
                        }
                        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                        Button(text["dna.inScore"]) { leave(idea.onset..<idea.end, insights: false) }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        Button(text["dna.inInsights"]) { leave(idea.onset..<idea.end, insights: true) }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    }
                }
                .padding(CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                .accessibilityElement(children: .contain)
            }
        }
    }

    private var actions: some View {
        FlowRow(spacing: CGFloat.ds("s-3")) {
            Button {
                playMap()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: playing && player.state != .stopped ? "pause" : "play")
                    Text(playing && player.state != .stopped ? text["dna.stop"] : text["dna.playMap"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .primary))
            if !map.form.isEmpty {
                Button {
                    Task { status = await addBookmarks() }
                } label: {
                    HStack(spacing: 6) {
                        Icon(name: "bookmark")
                        Text(text["dna.addBookmarks"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
            }
            Button {
                asking = true
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "insight")
                    Text(text["ask.aboutPiece"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            .popover(isPresented: $asking) {
                AskPopover(
                    conversation: conversation, account: account, text: text,
                    context: AskContext(selection: nil, wholePiece: true, title: title,
                                        map: text.format("ask.knowsMap", text.count("dna.ideas", map.ideas.count)), form: map.form),
                    openInInsights: {
                        asking = false
                        stop()
                        askInInsights()
                    },
                    close: { asking = false }
                )
            }
            if let status {
                Text(status).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
    }

    // MARK: - Playing

    /// The first tap plays the passage; a second tap on the same one opens the score there.
    private func tap(_ range: Range<Double>) {
        if heard == range {
            leave(range, insights: false)
            return
        }
        hear(range)
    }

    private func hear(_ range: Range<Double>) {
        heard = range
        playing = true
        player.setLoop(range)
    }

    /// Plays the piece from its start with the map following; pressed again, it stops.
    private func playMap() {
        if playing && player.state != .stopped {
            stop()
            return
        }
        player.setLoop(nil)
        playing = true
        player.play(from: map.start)
    }

    private func stop() {
        guard playing else { return }
        playing = false
        player.setLoop(nil)
        if player.state != .stopped { player.toggle() }
    }

    private func leave(_ range: Range<Double>, insights: Bool) {
        stop()
        showInScore(range, insights)
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