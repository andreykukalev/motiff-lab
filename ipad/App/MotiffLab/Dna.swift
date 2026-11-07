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
    }

    struct Cadence: Sendable {
        var at: Double
        var full: Bool
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
        barStarts = (object["barStarts"] as? [NSNumber] ?? []).map(\.doubleValue)
        form = (lane("form")["sections"] as? [[String: Any]] ?? []).map { section in
            let (onset, end, first, last) = passage(section["passage"])
            return Section(id: section["id"] as? String ?? "", label: section["label"] as? String ?? "", onset: onset, end: end,
                           firstBar: first, lastBar: last, sharesWith: section["sharesWith"] as? [String] ?? [])
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
            Chord(bar: int($0["bar"]), symbol: $0["symbol"] as? String ?? "", clear: $0["clear"] as? Bool ?? true)
        }
        cadences = (lane("harmony")["cadences"] as? [[String: Any]] ?? []).map {
            Cadence(at: number($0["at"]), full: ($0["kind"] as? String) == "full")
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
/// piece opens the assistant's popover at the button, over the map, which stays as it was.
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

    /// The lane names' column, as the web app's `--name`.
    private let nameWidth: CGFloat = 140

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
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
            row(text["dna.form"], tag: map.form.isEmpty ? nil : text["dna.inferred"], height: 56) { width in
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
                            let returning = map.form.contains { section.sharesWith.contains($0.id) && $0.onset < section.onset }
                            Button {
                                tap(section.onset..<section.end)
                            } label: {
                                Text(section.label)
                                    .dsType(.heading)
                                    .frame(width: max(24, x(section.end, width) - left), height: 28)
                                    .background(returning ? Color.ds("surface") : Color.ds("accent-tint"), in: Rectangle())
                                    .overlay(Rectangle().strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: returning ? [3, 2] : [])))
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
            row(text["dna.key"]) { width in
                if map.keys.isEmpty {
                    empty(text["dna.keyEmpty"])
                } else {
                    ZStack(alignment: .leading) {
                        ForEach(Array(map.keys.enumerated()), id: \.offset) { _, key in
                            Text(keyName(key))
                                .dsType(.caption)
                                .lineLimit(1)
                                .padding(.horizontal, CGFloat.ds("s-2"))
                                .frame(width: max(1, x(key.end, width) - x(key.onset, width)), height: 22, alignment: .leading)
                                .background(key.minor ? Color.ds("plum-tint") : Color.ds("accent-tint"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
                                .offset(x: x(key.onset, width))
                        }
                    }
                }
            }
            row(text["dna.harmony"]) { width in
                if map.harmony.isEmpty {
                    empty(text["dna.harmonyEmpty"])
                } else {
                    ZStack(alignment: .topLeading) {
                        ForEach(Array(shownChords(width).enumerated()), id: \.offset) { _, chord in
                            Text(chord.symbol)
                                .dsType(.label)
                                .italic(!chord.clear)
                                .foregroundStyle(chord.clear ? Color.ds("ink") : Color.ds("ink-2"))
                                .fixedSize()
                                .offset(x: x(map.bar(chord.bar)?.lowerBound ?? map.start, width), y: 4)
                        }
                        ForEach(Array(map.cadences.enumerated()), id: \.offset) { _, cadence in
                            Circle()
                                .fill(cadence.full ? Color.ds("ink") : .clear)
                                .overlay(Circle().strokeBorder(Color.ds("ink"), lineWidth: 1.5))
                                .frame(width: 8, height: 8)
                                .offset(x: x(cadence.at, width) - 4, y: 28)
                        }
                    }
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(harmonySummary)
                }
            }
            row(text["dna.density"], height: 40) { width in
                bars(map.density, of: max(0.01, map.density.map(\.value).max() ?? 1), colour: Color.ds("accent-tint-strong"), width: width)
            }
            row(text["dna.newMaterial"]) { width in
                ZStack(alignment: .leading) {
                    ForEach(map.newMaterial, id: \.self) { number in
                        if let span = map.bar(number) {
                            Rectangle()
                                .fill(Color.ds("teal-tint"))
                                .overlay(alignment: .leading) { Rectangle().fill(Color.ds("teal")).frame(width: 2) }
                                .frame(width: max(2, x(span.upperBound, width) - x(span.lowerBound, width)), height: 16)
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
                row(text["dna.register"], height: 40) { width in
                    registerLane(width)
                }
                row(text["dna.dynamics"], height: 40) { width in
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

    /// A lane: its name in the left column and its track on the bar axis, `width` wide.
    private func row<Track: View>(_ name: String, tag: String? = nil, overline: Bool = true, height: CGFloat = 36, @ViewBuilder track: @escaping (CGFloat) -> Track) -> some View {
        HStack(alignment: .center, spacing: 0) {
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

    /// The chords where they change, each with room for its symbol.
    private func shownChords(_ width: CGFloat) -> [DnaMap.Chord] {
        var shown: [DnaMap.Chord] = []
        var previous: String?
        var free: CGFloat = -.infinity
        for chord in map.harmony {
            let left = x(map.bar(chord.bar)?.lowerBound ?? map.start, width)
            defer { previous = chord.symbol }
            guard chord.symbol != previous, left >= free else { continue }
            shown.append(chord)
            free = left + CGFloat(chord.symbol.count + 1) * 8
        }
        return shown
    }

    private var harmonySummary: String {
        var previous: String?
        var changes: [String] = []
        for chord in map.harmony where chord.symbol != previous {
            changes.append("\(text.format("dna.bar", String(chord.bar))) \(chord.symbol)")
            previous = chord.symbol
        }
        return "\(text["dna.harmony"]): \(changes.joined(separator: ", "))"
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