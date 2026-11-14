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
            // Out of range: setting the clamped value runs this again, which tells the engine once.
            if clamped != percent {
                percent = clamped
                return
            }
            if percent != told {
                told = percent
                changed?(percent)
            }
        }
    }

    /// The percent the engine was last told, so a value clamped back to it is not sent again.
    private var told = 100

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
    /// "notated", "inferred", "player", "default" (nothing states one: 80, version 0.5.5) or "assumed" (a recording's clock).
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

    /// Whether the piece's tempo is known (version 0.5.5): only a recording's own clock, before its bars are worked out,
    /// is not; a piece without any tempo plays at its default of 80 ("default"), which is known and marked.
    var known: Bool { source != "assumed" }

    /// The piece tempo (#521 section 12), the one source of truth the score's mark shows: "♩ = 92" from the notes, the
    /// player or the default, "♩ ≈ 92" from a recording, "♩ ?" for a recording's own clock. Playback pace never changes it.
    var pieceText: String {
        switch source {
        case "assumed": return "\(note) ?"
        case "inferred": return "\(note) ≈ \(startBeats)"
        default: return "\(note) = \(startBeats)"
        }
    }

    /// Beats a minute heard at `percent`, rounded: the effective speed, the piece tempo × the pace (120 at 80 % is 96).
    func heard(at percent: Int) -> Int {
        Int((beatsPerMinute * Double(percent) / 100).rounded())
    }

    /// The effective speed as the pace section says it: "♩ 96", "♩ ≈ 96" from a recording, nil for a recording's own clock.
    func heardText(at percent: Int) -> String? {
        switch source {
        case "assumed": return nil
        case "inferred": return "\(note) ≈ \(heard(at: percent))"
        default: return "\(note) \(heard(at: percent))"
        }
    }

    /// The pill: the piece tempo, and the pace beside it when it is not 100 % ("♩ = 92 · 80%").
    func pillText(at percent: Int, _ text: UIText) -> String {
        percent == 100 ? pieceText : text.format("tempoControl.reading", pieceText, String(percent))
    }

    /// What the piece tempo is, under its number: the music's own tempo, a default taken because nothing states one (an
    /// assumption, said so), worked out from a recording, or not known before a recording's bars.
    func pieceCaption(_ text: UIText) -> String {
        switch source {
        case "default": return text.format("tempoControl.pieceDefault", note, String(startBeats))
        case "inferred": return text["tempoControl.pieceInferred"]
        case "assumed": return text["tempoControl.pieceUnknown"]
        default: return text["tempoControl.pieceCaption"]
        }
    }

    /// The tempo the piece goes back to when the player's is cleared, in beats a minute: its own from the notes, a
    /// recording or the default; nil when the player set none.
    var ownBeats: Int? { player == nil ? nil : pieceStartBeats }

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
}

/// The pill's tempo (#511 section 17; version 0.5.5, #521 section 12): the piece tempo, with the playback pace beside it
/// when it is not 100 % ("♩ = 92 · 80%"), and in its popover two separate sections: the piece tempo, typed by a tap on its
/// number, which the score's mark shows; and the playback pace, a percentage with its own − slider + (by 1 %, held to
/// repeat) and coarse steps, which changes only the speed heard (tempo × pace). It observes only the tempo, so it stays
/// open and works while playing; it closes with ✕ or a tap on the notes.
struct TempoControl: View, Equatable {
    @ObservedObject private var tempo: TempoSetting
    private let text: UIText
    /// The piece tempo the player typed, in quarter notes a minute, or nil to clear theirs.
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
        .accessibilityLabel(text.format("pill.tempoLabel", tempo.reading?.pieceText ?? "", String(tempo.percent)))
        .popover(isPresented: $open) {
            TempoPanel(tempo: tempo, text: text, setPiece: setPiece) { open = false }
                .padding(CGFloat.ds("s-4"))
                .frame(width: 420, alignment: .leading)
                .presentationCompactAdaptation(.popover)
        }
    }

    private var label: String {
        tempo.reading?.pillText(at: tempo.percent, text) ?? "\(tempo.percent)%"
    }
}

/// The popover's content: the piece tempo, then the playback pace, each a section of its own.
private struct TempoPanel: View {
    private enum Typing { case piece, pace }

    @ObservedObject var tempo: TempoSetting
    let text: UIText
    let setPiece: (Int?) -> Void
    let close: () -> Void
    @State private var typing: Typing?
    @State private var typed = ""
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
            if let reading = tempo.reading {
                pieceSection(reading)
                Divider().overlay(Color.ds("hairline"))
            }
            paceSection
        }
    }

    /// «Темп пьесы»: the musical tempo, the score's mark; a tap on the number types a new one.
    private func pieceSection(_ reading: TempoReading) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["tempoControl.pieceTempo"]).overline()
            if typing == .piece {
                pieceEntry(reading)
            } else {
                bigNumber(Text(verbatim: reading.pieceText), caption: reading.pieceCaption(text),
                          label: text["tempoControl.typePiece"]) { start(.piece) }
            }
        }
    }

    /// «Скорость игры»: the percentage of the piece tempo heard, with its slider and steps; the piece tempo stays.
    private var paceSection: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["tempoControl.pace"]).overline()
            if typing == .pace {
                paceEntry
            } else {
                bigNumber(Text(verbatim: String(tempo.percent)) + Text(verbatim: "%").font(DSType.title.token.font).foregroundStyle(Color.ds("ink-2")),
                          caption: tempo.reading?.heardText(at: tempo.percent).map { text.format("tempoControl.paceHeard", $0) }
                              ?? text["tempoControl.percentCaption"],
                          label: text["tempoControl.typePercent"]) { start(.pace) }
            }
            fine
            coarse
        }
    }

    private func bigNumber(_ value: Text, caption: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                value.font(DesignTokens.font(weight: 600, size: DSType.display.token.size, relativeTo: .largeTitle))
                    .monospacedDigit().foregroundStyle(Color.ds("ink"))
                Text(caption).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, CGFloat.ds("s-3"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
        .accessibilityValue(caption)
    }

    private func field(_ label: String) -> some View {
        TextField("", text: $typed)
            .keyboardType(.numberPad)
            .font(DSType.title.token.font)
            .monospacedDigit()
            .focused($focused)
            .onSubmit(enter)
            .padding(.horizontal, CGFloat.ds("s-2"))
            .frame(height: CGFloat.ds("h-control"))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
            .accessibilityLabel(label)
            .onAppear { focused = true }
    }

    /// The piece tempo typed in beats a minute ("♩ = 92"; dotted quarters in 6/8), kept with the piece and marked on the
    /// score; the pace stays, so the speed heard follows it. The player's own can be cleared back to the piece's.
    private func pieceEntry(_ reading: TempoReading) -> some View {
        let value = Int(typed.trimmingCharacters(in: .whitespaces))
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text("\(reading.note) =").foregroundStyle(Color.ds("ink-2"))
                field(reading.beatsCaption(text))
                Button(text["tempoControl.done"], action: enter)
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(value.map { !reading.canSet($0) } ?? true)
                Button(text["tempoControl.cancel"]) { typing = nil }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            if let own = reading.ownBeats {
                Button(text.format("tempoControl.resetTo", reading.note, String(own))) {
                    typing = nil
                    setPiece(nil)
                }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
        }
    }

    private var paceEntry: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            field(text["tempoControl.percent"])
            Text(verbatim: "%").dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Button(text["tempoControl.done"], action: enter).buttonStyle(DSButtonStyle(kind: .primary, compact: true))
        }
    }

    /// − slider + in 1 % steps; a held − or + steps again and again. Only the pace.
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

    private func start(_ kind: Typing) {
        typed = kind == .pace ? String(tempo.percent) : tempo.reading.map { String($0.startBeats) } ?? ""
        typing = kind
    }

    /// The pace typed is kept within 25 to 150 %; the piece tempo typed is set when music-core takes it (20 to 300
    /// quarter notes a minute) and differs from the one in force.
    private func enter() {
        let kind = typing
        guard let value = Int(typed.trimmingCharacters(in: .whitespaces)), value > 0 else { return }
        switch kind {
        case .piece:
            guard let reading = tempo.reading, reading.canSet(value) else { return }
            typing = nil
            if value != reading.startBeats { setPiece(reading.quarterNotes(value)) }
        case .pace:
            typing = nil
            tempo.percent = value
        case nil:
            break
        }
    }
}
