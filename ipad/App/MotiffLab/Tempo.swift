import SwiftUI

/// The playback tempo and what the piece's tempo is (version 0.5.4, #511 section 17), apart from `Player`: the pill
/// observes the player, whose position changes about 30 times a second while playing, so a tempo menu inside it was drawn
/// again with every report and could not be changed while playing. The tempo control observes this object alone.
@MainActor
final class TempoSetting: ObservableObject {
    /// The playback tempo, 25 % to 150 %, in 1 % steps; the engine changes its rate at once, the place and the loop stay.
    @Published var percent = 100 {
        didSet {
            let clamped = TempoReading.clamped(percent)
            if clamped != percent { percent = clamped }
            if percent != oldValue { changed?(percent) }
        }
    }

    /// The piece's tempo at the start and where it comes from, from music-core's playback score.
    @Published var reading: TempoReading?

    /// Sends the rate to the engine (set by the player).
    var changed: (@MainActor (Int) -> Void)?
}

/// music-core's `playback` "tempo" (within contract 12, v0.5.4): the tempo at the start as played and as the piece states
/// it, and the beat it is read in: a dotted quarter in 6/8 and the other compound meters, a quarter otherwise.
struct TempoReading: Equatable, Sendable {
    /// The slowest and fastest the playback goes, as a percentage of the piece's tempo (the resume points' range too).
    static let percentRange = 25...150
    /// The coarse steps, 50 % to 110 %.
    static let steps = [50, 60, 70, 80, 90, 100, 110]

    static func clamped(_ percent: Int) -> Int {
        min(percentRange.upperBound, max(percentRange.lowerBound, percent))
    }

    /// Quarter notes a minute at the start as played, exact (from the first tempo's microseconds a quarter).
    var perMinute: Double
    /// "notated", "inferred", "assumed" or "player".
    var source: String
    /// The piece's own tempo at the start, before the player's, in quarter notes rounded, and where it comes from.
    var piecePerMinute: Int
    var pieceSource: String
    /// The tempo the player set for the piece in quarter notes a minute, or nil.
    var player: Int?
    /// "quarter" or "dottedQuarter".
    var beat = "quarter"
    /// The tempo at the start as played and the piece's own, in beats a minute, as music-core rounds them.
    var playedBeats: Int?
    var pieceBeats: Int?

    static func read(_ object: [String: Any]) -> TempoReading? {
        guard let tempo = object["tempo"] as? [String: Any], let source = tempo["source"] as? String,
              let piece = tempo["piece"] as? [String: Any], let pieceSource = piece["source"] as? String,
              let piecePerMinute = (piece["perMinute"] as? NSNumber)?.intValue else { return nil }
        let first = (object["tempos"] as? [[String: Any]])?.first
        let exact = ((first?["microsecondsPerQuarter"] as? NSNumber)?.doubleValue).map { 60_000_000 / $0 }
        guard let perMinute = exact ?? (tempo["perMinute"] as? NSNumber)?.doubleValue else { return nil }
        return TempoReading(perMinute: perMinute, source: source, piecePerMinute: piecePerMinute, pieceSource: pieceSource,
                            player: (tempo["player"] as? NSNumber)?.intValue, beat: tempo["beat"] as? String ?? "quarter",
                            playedBeats: (tempo["beatsPerMinute"] as? NSNumber)?.intValue,
                            pieceBeats: (piece["beatsPerMinute"] as? NSNumber)?.intValue)
    }

    var dotted: Bool { beat == "dottedQuarter" }

    /// The beat's note as a metronome mark writes it: "♩", or "♩." for a dotted quarter.
    var note: String { dotted ? "♩." : "♩" }

    /// Beats a minute at the start as played, exact: a dotted quarter lasts three halves of a quarter.
    var beatsPerMinute: Double { dotted ? perMinute * 2 / 3 : perMinute }

    /// The tempo at the start as played, in whole beats a minute.
    var startBeats: Int { playedBeats ?? Int(beatsPerMinute.rounded()) }

    /// The piece's own tempo at the start, in whole beats a minute.
    var pieceStartBeats: Int { pieceBeats ?? (dotted ? Int((Double(piecePerMinute) * 2 / 3).rounded()) : piecePerMinute) }

    /// The player's tempo for the piece in beats a minute, or nil.
    var playerBeats: Int? { player == nil ? nil : startBeats }

    /// Beats a minute heard at `percent`, rounded: the "♩ 83" beside "90 %".
    func heard(at percent: Int) -> Int {
        Int((beatsPerMinute * Double(percent) / 100).rounded())
    }

    /// The pill's beats: "♩ 83" from the notes or the player, "♩ ≈ 83" from a recording, "♩ ?" when not recognised.
    func heardText(at percent: Int) -> String {
        switch source {
        case "assumed": return "\(note) ?"
        case "inferred": return "\(note) ≈ \(heard(at: percent))"
        default: return "\(note) \(heard(at: percent))"
        }
    }

    /// The percentage that plays `beats` a minute, to the nearest whole one (not clamped).
    func percent(for beats: Int) -> Int {
        Int((Double(beats) * 100 / beatsPerMinute).rounded())
    }

    /// Quarter notes a minute of `beats` a minute, as music-core keeps them: a dotted quarter's 61 is 92 (half up).
    func quarterNotes(_ beats: Int) -> Int {
        dotted ? (3 * beats + 1) / 2 : beats
    }

    /// Whether the player can set `beats` a minute as the piece's tempo, as music-core takes it (20 to 300 quarter notes).
    func canSet(_ beats: Int) -> Bool {
        (1...300).contains(beats) && TempoReading.canSet(quarterNotes(beats))
    }

    /// Whether music-core takes `perMinute` quarter notes as the piece's tempo (20 to 300).
    static func canSet(_ perMinute: Int) -> Bool { (20...300).contains(perMinute) }

    /// The words of the beat counted: "quarter notes a minute" or "dotted quarters a minute".
    func beatsCaption(_ text: UIText) -> String {
        text[dotted ? "tempoControl.dottedQuarters" : "tempoControl.quarters"]
    }

    /// Where the piece's tempo comes from, as a tag: from the notes, from the recording, not recognised, set by you.
    func sourceTag(_ text: UIText) -> (String, Tag.Kind) {
        switch source {
        case "inferred": return (text["tempoControl.tagInferred"], .detected)
        case "assumed": return (text["tempoControl.tagAssumed"], .assumed)
        case "player": return (text["tempoControl.tagPlayer"], .player)
        default: return (text["tempoControl.tagNotated"], .plain)
        }
    }

    /// The piece's tempo as its source has it, so the printed tempo, an estimate and the player's are never mixed up.
    func sourceLine(_ text: UIText) -> String {
        let start = String(startBeats)
        switch source {
        case "inferred": return text.format("tempoControl.lineInferred", note, start)
        case "assumed": return text.format("tempoControl.lineAssumed", note, start)
        case "player" where pieceSource == "notated":
            return text.format("tempoControl.linePlayerOver", note, start, String(pieceStartBeats))
        case "player": return text.format("tempoControl.linePlayer", note, start)
        default: return text.format("tempoControl.lineNotated", note, start)
        }
    }
}

/// The pill's tempo (#511 section 17, the UX prototype's variant A that Andrei approved): "90 % · ♩ 83", and in its
/// popover the big numbers typed by a tap, − slider + by 1 % held to repeat, coarse steps, the piece's tempo with its
/// source and the tempo printed in the notes. It observes only the tempo, so it stays open and works while playing; it
/// closes with ✕ or a tap on the notes.
struct TempoControl: View, Equatable {
    @ObservedObject private var tempo: TempoSetting
    private let text: UIText
    /// The piece's tempo the player read in the printed notes, in quarter notes a minute, or nil to clear it.
    private let setPiece: (Int?) -> Void
    /// What the pill's drawing compares (`.equatable()`): the same tempo in the same language is not drawn again.
    private let identity: ObjectIdentifier
    private let language: String
    @State private var open = false

    init(tempo: TempoSetting, text: UIText, setPiece: @escaping (Int?) -> Void) {
        _tempo = ObservedObject(wrappedValue: tempo)
        self.text = text
        self.setPiece = setPiece
        identity = ObjectIdentifier(tempo)
        language = text.language
    }

    nonisolated static func == (lhs: TempoControl, rhs: TempoControl) -> Bool {
        lhs.identity == rhs.identity && lhs.language == rhs.language
    }

    var body: some View {
        Button {
            open.toggle()
        } label: {
            HStack(spacing: 6) {
                Icon(name: "tempo")
                Text(label).monospacedDigit()
            }
        }
        .buttonStyle(DSItemStyle(on: open || tempo.percent != 100))
        .accessibilityLabel(text.format("pill.tempoLabel", String(tempo.percent)))
        .popover(isPresented: $open) {
            TempoPanel(tempo: tempo, text: text, setPiece: setPiece) { open = false }
                .padding(CGFloat.ds("s-4"))
                .frame(width: 420, alignment: .leading)
                .presentationCompactAdaptation(.popover)
        }
    }

    private var label: String {
        guard let reading = tempo.reading else { return "\(tempo.percent)%" }
        return text.format("tempoControl.reading", String(tempo.percent), reading.heardText(at: tempo.percent))
    }
}

/// The popover's content, top to bottom as the prototype has it.
private struct TempoPanel: View {
    private enum Typing { case percent, beats }

    @ObservedObject var tempo: TempoSetting
    let text: UIText
    let setPiece: (Int?) -> Void
    let close: () -> Void
    @State private var typing: Typing?
    @State private var typed = ""
    @State private var printing = false
    @State private var printed = ""
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack {
                Text(text["tempoControl.title"]).dsType(.heading).foregroundStyle(Color.ds("ink"))
                Spacer()
                Button(action: close) { Icon(name: "close") }
                    .buttonStyle(DSIconItemStyle())
                    .accessibilityLabel(text["tempoControl.close"])
            }
            if let typing { entry(typing) } else { bigNumbers }
            fine
            coarse
            if let reading = tempo.reading {
                Divider().overlay(Color.ds("hairline"))
                if printing { printedForm(reading) } else { pieceRow(reading) }
            }
        }
    }

    /// The percentage and the beats a minute, each typed by a tap.
    private var bigNumbers: some View {
        HStack(spacing: 0) {
            bigNumber(Text(verbatim: String(tempo.percent)) + Text(verbatim: "%").font(DSType.title.token.font).foregroundStyle(Color.ds("ink-2")),
                      caption: text["tempoControl.percentCaption"], label: text["tempoControl.typePercent"]) { start(.percent) }
            if let reading = tempo.reading {
                Rectangle().fill(Color.ds("hairline")).frame(width: 1).padding(.vertical, CGFloat.ds("s-2"))
                bigNumber(Text(verbatim: "\(reading.note) \(reading.heard(at: tempo.percent))"),
                          caption: reading.source == "assumed"
                              ? text.format("tempoControl.ofTaken", reading.note, String(reading.startBeats))
                              : reading.beatsCaption(text),
                          label: text["tempoControl.typeBeat"]) { start(.beats) }
            }
        }
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    private func bigNumber(_ value: Text, caption: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                value.font(DesignTokens.font(weight: 600, size: DSType.display.token.size, relativeTo: .largeTitle))
                    .monospacedDigit().foregroundStyle(Color.ds("ink"))
                Text(caption).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, CGFloat.ds("s-3"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }

    /// A percentage or beats a minute typed: the beats as the percentage that plays them, kept within 25 to 150.
    private func entry(_ kind: Typing) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            if kind == .beats, let reading = tempo.reading { Text("\(reading.note) =").foregroundStyle(Color.ds("ink-2")) }
            TextField("", text: $typed)
                .keyboardType(.numberPad)
                .font(DSType.title.token.font)
                .monospacedDigit()
                .focused($focused)
                .onSubmit(enter)
                .padding(.horizontal, CGFloat.ds("s-2"))
                .frame(height: CGFloat.ds("h-control"))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                .accessibilityLabel(kind == .percent ? text["tempoControl.percent"] : tempo.reading?.beatsCaption(text) ?? "")
            Text(kind == .percent ? "%" : tempo.reading?.beatsCaption(text) ?? "").dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Button(text["tempoControl.done"], action: enter).buttonStyle(DSButtonStyle(kind: .primary, compact: true))
        }
        .onAppear { focused = true }
    }

    /// − slider + in 1 % steps; a held − or + steps again and again.
    private var fine: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            stepButton("−", by: -1, label: text["tempoControl.slower"],
                       disabled: tempo.percent <= TempoReading.percentRange.lowerBound)
            Slider(value: Binding(get: { Double(tempo.percent) }, set: { tempo.percent = Int($0.rounded()) }),
                   in: Double(TempoReading.percentRange.lowerBound)...Double(TempoReading.percentRange.upperBound), step: 1)
                .tint(Color.ds("accent"))
                .accessibilityLabel(text["tempoControl.percent"])
            stepButton("+", by: 1, label: text["tempoControl.faster"],
                       disabled: tempo.percent >= TempoReading.percentRange.upperBound)
        }
    }

    private func stepButton(_ sign: String, by step: Int, label: String, disabled: Bool) -> some View {
        Button { tempo.percent += step } label: {
            Text(sign)
                .font(DSType.title.token.font)
                .foregroundStyle(Color.ds("ink"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .background(Color.ds("surface-2"), in: Circle())
                .overlay(Circle().strokeBorder(Color.ds("hairline-strong")))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .buttonRepeatBehavior(.enabled)
        .disabled(disabled)
        .opacity(disabled ? 0.38 : 1)
        .accessibilityLabel(label)
    }

    private var coarse: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            ForEach(TempoReading.steps, id: \.self) { step in
                Button(String(step)) { tempo.percent = step }
                    .buttonStyle(DSChipStyle(on: tempo.percent == step))
                    .accessibilityAddTraits(tempo.percent == step ? .isSelected : [])
            }
            Text("%").dsType(.caption).foregroundStyle(Color.ds("ink-3")).accessibilityHidden(true)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["tempoControl.steps"])
    }

    /// «Темп пьесы»: where the piece's tempo comes from, and the way to give the printed one.
    private func pieceRow(_ reading: TempoReading) -> some View {
        let (tag, kind) = reading.sourceTag(text)
        let unknown = reading.source == "assumed"
        return HStack(spacing: CGFloat.ds("s-2")) {
            Text(text["tempoControl.pieceTempo"]).overline()
            Tag(text: tag, kind: kind)
            Text(reading.sourceLine(text)).dsType(.label).monospacedDigit().foregroundStyle(Color.ds("ink"))
            Spacer(minLength: 0)
            Button(text[unknown ? "tempoControl.printedUnknown" : "tempoControl.printed"]) {
                printed = reading.playerBeats.map(String.init) ?? ""
                typing = nil
                printing = true
            }
            .buttonStyle(DSButtonStyle(kind: unknown ? .secondary : .quiet, compact: true))
        }
    }

    /// The tempo printed in the notes: kept with the piece and written over the first bar; the percentage stays.
    private func printedForm(_ reading: TempoReading) -> some View {
        let value = Int(printed.trimmingCharacters(in: .whitespaces))
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["tempoControl.printedTitle"]).overline()
            HStack(spacing: CGFloat.ds("s-2")) {
                Text("\(reading.note) =").foregroundStyle(Color.ds("ink-2"))
                TextField(String(reading.pieceStartBeats), text: $printed)
                    .keyboardType(.numberPad)
                    .font(DSType.title.token.font)
                    .monospacedDigit()
                    .focused($focused)
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(height: CGFloat.ds("h-control"))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                    .accessibilityLabel(reading.beatsCaption(text))
                Button(text["tempoControl.save"]) {
                    if let value, reading.canSet(value) {
                        printing = false
                        setPiece(reading.quarterNotes(value))
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                .disabled(value.map { !reading.canSet($0) || $0 == reading.playerBeats } ?? true)
                Button(text["tempoControl.cancel"]) { printing = false }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                if reading.player != nil {
                    Button(text["tempoControl.clear"]) {
                        printing = false
                        setPiece(nil)
                    }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
            Text(text.format("tempoControl.printedHint", String(tempo.percent))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        .onAppear { focused = true }
    }

    private func start(_ kind: Typing) {
        printing = false
        typed = kind == .percent ? String(tempo.percent) : tempo.reading.map { String($0.heard(at: tempo.percent)) } ?? ""
        typing = kind
    }

    private func enter() {
        defer { typing = nil }
        guard let value = Int(typed.trimmingCharacters(in: .whitespaces)), value > 0 else { return }
        if typing == .beats, let reading = tempo.reading {
            tempo.percent = reading.percent(for: value)
        } else {
            tempo.percent = value
        }
    }
}
