import SwiftUI

/// A margin note (facade `marks`, 0018 section 5): the player's words about some bars.
struct MarginNote: Identifiable, Equatable, Sendable {
    var id: String
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int
    var text: String
    var createdAt: String
}

/// A highlight: a translucent band in an ink behind the staves of some bars.
struct HighlightMark: Identifiable, Equatable, Sendable {
    var id: String
    var onset: Double
    var end: Double
    var firstBar: Int
    var lastBar: Int
    /// "amber", "carmine", "forest" or "ochre".
    var colour: String
}

/// What the piece's marks hold beyond the ink's strokes, as music-core answers every mark call: the margin notes in score
/// order, the highlights, and the numbers of the bars that carry ink.
struct MarksShelf: Equatable, Sendable {
    var notes: [MarginNote] = []
    var highlights: [HighlightMark] = []
    var inkBars: [Int] = []
    var inkCount = 0

    init() {}

    init(object: [String: Any]) {
        func number(_ value: Any?) -> Double? { (value as? NSNumber)?.doubleValue }
        func place(_ entry: [String: Any]) -> (onset: Double, end: Double, first: Int, last: Int)? {
            let span = entry["span"] as? [String: Any]
            let bars = entry["bars"] as? [String: Any]
            guard let onset = number(span?["onset"]), let end = number(span?["end"]) else { return nil }
            return (onset, end, Int(number(bars?["first"]) ?? 0), Int(number(bars?["last"]) ?? 0))
        }
        notes = (object["notes"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let id = entry["id"] as? String, let at = place(entry) else { return nil }
            return MarginNote(id: id, onset: at.onset, end: at.end, firstBar: at.first, lastBar: at.last, text: entry["text"] as? String ?? "", createdAt: entry["createdAt"] as? String ?? "")
        }
        .sorted { $0.onset != $1.onset ? $0.onset < $1.onset : $0.createdAt < $1.createdAt }
        highlights = (object["highlights"] as? [[String: Any]] ?? []).compactMap { entry in
            guard let id = entry["id"] as? String, let at = place(entry) else { return nil }
            return HighlightMark(id: id, onset: at.onset, end: at.end, firstBar: at.first, lastBar: at.last, colour: entry["colour"] as? String ?? "amber")
        }
        let ink = object["ink"] as? [[String: Any]] ?? []
        inkCount = ink.count
        inkBars = Set(ink.compactMap { number($0["barNumber"]).map(Int.init) }).sorted()
    }

    /// The note's number beside its bar, from 1 in score order.
    func number(of note: MarginNote) -> Int {
        (notes.firstIndex { $0.id == note.id } ?? 0) + 1
    }

    /// The runs of consecutive bars that carry ink, as the Notes tab lists them: "Ink, bars 9–12".
    var inkRuns: [ClosedRange<Int>] {
        var runs: [ClosedRange<Int>] = []
        for bar in inkBars {
            if let last = runs.last, last.upperBound + 1 == bar {
                runs[runs.count - 1] = last.lowerBound...bar
            } else {
                runs.append(bar...bar)
            }
        }
        return runs
    }

    var isEmpty: Bool { notes.isEmpty && highlights.isEmpty && inkCount == 0 }

    /// "The leap needs a lighter thumb…": a note's first words.
    static func firstWords(_ text: String, _ length: Int = 48) -> String {
        text.count <= length ? text : String(text.prefix(length)).trimmingCharacters(in: .whitespaces) + "…"
    }

    /// The ink's name: "carmine", "forest green".
    static func inkName(_ ink: String, _ text: UIText) -> String {
        text["ink.\(ink)"]
    }
}

/// "bars 9–12", or "bar 9".
func barsText(_ first: Int, _ last: Int, _ text: UIText) -> String {
    first == last ? text.format("bars.one", String(first)) : text.format("bars.range", String(first), String(last))
}

/// The note card over the selection (the web app's `.note-card`): the player's words about the bars, Done, and Delete for
/// a note already written. Emptying the words and choosing Done deletes the note.
struct NoteCard: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var marksModel: MarksModel
    let text: UIText
    @FocusState private var focused: Bool

    var body: some View {
        if let draft = marksModel.noteDraft {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                Text(text.format("margin.about", barsText(draft.firstBar, draft.lastBar, text))).overline()
                TextField(text["margin.placeholder"], text: Binding(get: { marksModel.noteDraft?.text ?? "" }, set: { marksModel.noteDraft?.text = $0 }), axis: .vertical)
                    .lineLimit(3...6)
                    .focused($focused)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .padding(.vertical, CGFloat.ds("s-2"))
                    .frame(minHeight: 72, alignment: .topLeading)
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                HStack(spacing: CGFloat.ds("s-2")) {
                    if let id = draft.id {
                        Button {
                            score.deleteNote(id)
                        } label: {
                            HStack(spacing: 6) {
                                Icon(name: "trash", size: CGFloat.ds("icon-s"))
                                Text(text["bookmark.delete"])
                            }
                        }
                        .buttonStyle(DSButtonStyle(kind: .danger, compact: true))
                    }
                    Spacer(minLength: 0)
                    Button(text["bookmark.cancel"]) { marksModel.noteDraft = nil }
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    Button(text["bookmark.done"]) { score.saveNote() }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                }
            }
            .onAppear { focused = true }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(draft.id == nil ? text["margin.new"] : text.format("margin.about", barsText(draft.firstBar, draft.lastBar, text)))
        }
    }
}

/// Extra information and the player's marks (0018 section 5; testing item 33): first the phrases' marks, shown by default
/// and kept on this device, with «Вернуть найденные границы» (the phrases spec, v1.2; a warning first while the player has
/// boundaries of their own); then what else the app adds over the notes, off by default, each kind
/// with its own switch; then the layers of the player's own marks, each hidden for a clean page or all at once; those
/// kept with the piece.
struct LayersList: View {
    /// Shows and hides the layers, and whether Check has flags.
    @ObservedObject var score: ScoreController
    @ObservedObject var marksModel: MarksModel
    let text: UIText
    /// Whether the piece has other versions, a plain value from the screen that observes them (Marks may not read Sources).
    let hasVersions: Bool
    /// The player's own boundaries the reset would replace, while its warning asks; nil while it does not.
    @State private var resetting: Int?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["extra.title"]).overline()
            // The phrases' marks (the phrases spec, v1.2): shown by default, whatever the switch below; kept on this device.
            Toggle(text["extra.phrases"], isOn: Binding(get: { !marksModel.phraseMarksHidden }, set: { score.showPhraseMarks($0) }))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .tint(Color.ds("accent"))
            // Every boundary as the app found it («Player's boundaries» 2): at once without the player's own, else after the warning.
            if let phrases = score.selectionModel.phrases, !phrases.units.isEmpty {
                Button(text["phrases.reset"]) {
                    if phrases.own > 0 { resetting = phrases.own } else { score.resetPhrases() }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                .padding(.leading, CGFloat.ds("s-3"))
            }
            let extraOn = !marksModel.hiddenLayers.contains("extra")
            Toggle(text["extra.show"], isOn: Binding(get: { extraOn }, set: { score.showLayers(["extra"], $0) }))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink"))
                .tint(Color.ds("accent"))
            let extra = marksModel.pieceExtra(hasVersions: hasVersions, hasFlags: score.check.hasFlags)
            if extra.isEmpty {
                Text(text["extra.none"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            ForEach(extra, id: \.self) { item in
                Toggle(marksModel.layerName(item), isOn: Binding(get: { !marksModel.hiddenLayers.contains(item) }, set: { score.showLayers([item], $0) }))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .tint(Color.ds("accent"))
                    .padding(.leading, CGFloat.ds("s-3"))
                    .disabled(!extraOn)
            }
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Text(text["extra.yours"]).overline()
            ForEach(marksModel.pieceLayers, id: \.self) { layer in
                Toggle(marksModel.layerName(layer), isOn: Binding(get: { !marksModel.hiddenLayers.contains(layer) }, set: { score.showLayers([layer], $0) }))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink"))
                    .tint(Color.ds("accent"))
            }
            let layers = marksModel.pieceLayers
            let allHidden = layers.allSatisfy { marksModel.hiddenLayers.contains($0) }
            Button(allHidden ? text["layers.showAll"] : text["layers.hideAll"]) {
                score.showLayers(layers, allHidden)
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        }
        .alert(text.format("phrases.resetWarning", String(resetting ?? 0)), isPresented: Binding(get: { resetting != nil }, set: { if !$0 { resetting = nil } })) {
            Button(text["phrases.resetConfirm"], role: .destructive) { score.resetPhrases() }
            Button(text["common.cancel"], role: .cancel) {}
        }
    }
}

/// The Contents' notes and marks: the margin notes with their numbers, the highlights filtered by colour and the runs of
/// bars with ink; a tap goes there and selects the bars, and a note opens to read or change.
struct NotesList: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var marksModel: MarksModel
    let text: UIText
    @State private var filter: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            let marks = marksModel.marks
            if marks.isEmpty {
                Text(text["contents.notesEmpty"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            if !marks.notes.isEmpty {
                heading(text["contents.marginNotes"], marks.notes.count, ink: "amber")
                ForEach(marks.notes) { note in
                    entry(numbered: marks.number(of: note), title: MarksShelf.firstWords(note.text, 80), detail: barsText(note.firstBar, note.lastBar, text)) {
                        score.openNote(note.id)
                    }
                }
            }
            if !marks.highlights.isEmpty {
                heading(text["contents.highlights"], marks.highlights.count, ink: "amber")
                HStack(spacing: CGFloat.ds("s-1")) {
                    ForEach(HighlightInk.allCases.map(\.rawValue).filter { ink in marks.highlights.contains { $0.colour == ink } }, id: \.self) { ink in
                        let on = filter == ink
                        Button {
                            filter = on ? nil : ink
                        } label: {
                            Circle().fill(HighlightInk.color(ink)).frame(width: 18, height: 18)
                                .frame(width: 32, height: 32)
                                .background(on ? Color.ds("accent-tint") : .clear, in: Circle())
                                .overlay(Circle().strokeBorder(on ? Color.ds("accent") : .clear))
                                .contentShape(Circle())
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(MarksShelf.inkName(ink, text))
                        .accessibilityAddTraits(on ? .isSelected : [])
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["contents.highlightFilter"])
                ForEach(marks.highlights.filter { filter == nil || $0.colour == filter }.sorted { $0.onset < $1.onset }) { highlight in
                    entry(ink: highlight.colour, title: text.format("contents.highlightEntry", MarksShelf.inkName(highlight.colour, text)), detail: barsText(highlight.firstBar, highlight.lastBar, text)) {
                        Task { await score.select(highlight.onset, highlight.end) }
                    }
                }
            }
            if !marks.inkRuns.isEmpty {
                heading(text["contents.ink"], marks.inkCount, ink: "graphite")
                ForEach(marks.inkRuns, id: \.lowerBound) { run in
                    entry(title: text.format("contents.inkEntry", barsText(run.lowerBound, run.upperBound, text)), detail: nil) {
                        score.selectBars(run.lowerBound, run.upperBound, extend: false)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func heading(_ name: String, _ count: Int, ink: String) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Circle().fill(HighlightInk.color(ink)).frame(width: 10, height: 10)
            Text(text.format("contents.category", name, String(count)))
                .dsType(.overline)
                .textCase(.uppercase)
                .tracking(0.66)
                .foregroundStyle(Color.ds("ink-2"))
        }
        .frame(height: 32)
        .padding(.top, CGFloat.ds("s-2"))
        .accessibilityAddTraits(.isHeader)
    }

    private func entry(numbered: Int? = nil, ink: String? = nil, title: String, detail: String?, go: @escaping () -> Void) -> some View {
        Button(action: go) {
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-1")) {
                    if let numbered {
                        Text(String(numbered))
                            .font(DesignTokens.font(weight: 600, size: 11))
                            .foregroundStyle(Color.ds("ink"))
                            .frame(width: 18, height: 18)
                            .background(Color.ds("amber"), in: UnevenRoundedRectangle(topLeadingRadius: 9, bottomLeadingRadius: 2, bottomTrailingRadius: 9, topTrailingRadius: 9))
                            .accessibilityHidden(true)
                    }
                    if let ink {
                        Circle().fill(HighlightInk.color(ink)).frame(width: 10, height: 10)
                    }
                    Text(title)
                        .dsType(.body)
                        .foregroundStyle(Color.ds("ink"))
                        .multilineTextAlignment(.leading)
                }
                if let detail {
                    Text(detail)
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
            }
            .padding(.horizontal, CGFloat.ds("s-3"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .frame(maxWidth: .infinity, minHeight: CGFloat.ds("h-touch"), alignment: .leading)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
