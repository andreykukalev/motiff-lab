import SwiftUI

/// The Reference's sheet (an experiment, Settings · Experimental; the web app's ReferenceSheet): Keys, Chords and Scales.
/// Keys first, as the player most often calls the circle up: the key signatures, as he most often looks a key up by them
/// (Andrei, 2026-11-11 23:44 UTC), then the circle of fifths and the chosen key's card. A signature marks the major and its
/// relative minor and opens the major's card; a tap on the circle opens that key's card; a scale or chord of the card plays
/// on the app's piano, shows on a small keyboard and opens its own card underneath. Over a piece, the key it starts in is
/// outlined on the circle, dotted when it is assumed, and its card opens first. Chords and Scales look one up by its root.
/// Opened from the library and from the score's bar alike.
struct ReferenceSheet: View {
    let text: UIText
    @ObservedObject var settings: DeviceSettings
    @StateObject private var reference: ReferenceController
    @Environment(\.dismiss) private var dismiss

    /// `piece`: the open piece, whose key the circle marks; 0 from the library.
    init(text: UIText, settings: DeviceSettings, piece: Int32 = 0) {
        self.text = text
        _settings = ObservedObject(wrappedValue: settings)
        _reference = StateObject(wrappedValue: ReferenceController(piece: piece))
    }

    var body: some View {
        GeometryReader { size in
            VStack(alignment: .leading, spacing: 0) {
                header
                ScrollView {
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                        SegmentedChoice(options: ["keys", "chords", "scales"].map { ($0, text["reference.mode.\($0)"]) },
                                        chosen: reference.mode, label: text["reference.mode"]) { mode in
                            reference.mode = mode
                            Task { await loadLookup(mode) }
                        }
                        content(wide: size.size.width > 760)
                    }
                    .padding(.horizontal, CGFloat.ds("s-6"))
                    .padding(.bottom, CGFloat.ds("s-6"))
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        .background(Color.ds("surface"))
        .modifier(ReferenceSheetSizing())
        .task { await load(nil, minor: false) }
        .onDisappear { reference.stop() }
    }

    @ViewBuilder private func content(wide: Bool) -> some View {
        switch reference.mode {
        case "chords":
            if let chords = reference.chords {
                ReferenceLookupView(lookup: chords, kind: "chords", text: text, reference: reference) { root, type in
                    Task { await reference.loadChord(root, type: type, language: text.language, naming: naming) }
                }
            }
        case "scales":
            if let scales = reference.scales {
                ReferenceLookupView(lookup: scales, kind: "scales", text: text, reference: reference) { root, form in
                    Task { await reference.loadScale(root, form: form, language: text.language, naming: naming) }
                }
            }
        default:
            lookup
            if let answer = reference.answer {
                if wide {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-6")) {
                        circleColumn(answer).frame(width: 340)
                        cards(answer)
                    }
                } else {
                    circleColumn(answer).frame(maxWidth: 360).frame(maxWidth: .infinity)
                    cards(answer)
                }
            }
        }
    }

    private var header: some View {
        HStack {
            Text(text[reference.mode == "keys" ? "reference.title" : "reference.mode.\(reference.mode)"])
                .dsType(.title)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Spacer()
            Button {
                dismiss()
            } label: {
                Icon(name: "close").frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["reference.close"])
        }
        .padding(.horizontal, CGFloat.ds("s-6"))
        .padding(.vertical, CGFloat.ds("s-3"))
    }

    private var naming: String { settings["notes.naming"] }

    /// None and the sharps on one row, the flats on the next.
    private var lookup: some View {
        let choices = reference.answer?.signatures ?? []
        let chosen = reference.answer?.card?.fifths
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(text["reference.bySignature"]).overline()
            let rows = [choices.filter { $0.fifths >= 0 }, choices.filter { $0.fifths < 0 }]
            ForEach(rows.indices, id: \.self) { index in
                let row = rows[index]
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(row) { choice in
                        let on = chosen == choice.fifths
                        Button(choice.label) {
                            Task { await load(choice.fifths, minor: false) }
                        }
                        .buttonStyle(SignatureChoiceStyle(on: on))
                        .accessibilityLabel(choice.spoken)
                        .accessibilityAddTraits(on ? .isSelected : [])
                    }
                }
            }
        }
    }

    /// The circle, and under it the piece's key with a way to its card.
    private func circleColumn(_ answer: ReferenceAnswer) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            circle(answer)
            if let key = answer.pieceKey {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    RoundedRectangle(cornerRadius: CGFloat.ds("r-xs"))
                        .strokeBorder(Color.ds("ink"), style: StrokeStyle(lineWidth: 1.5, dash: key.inferred ? [3, 2] : []))
                        .frame(width: CGFloat.ds("s-4"), height: CGFloat.ds("s-3"))
                        .accessibilityHidden(true)
                    Text(text.format(key.inferred ? "reference.pieceKeyInferred" : "reference.pieceKey", key.name))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                    Button(text["reference.showPieceKey"]) {
                        Task { await load(key.fifths, minor: key.minor) }
                    }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
            }
        }
    }

    /// The circle; a tap on a key opens its card, and VoiceOver's swipes up and down step round it.
    private func circle(_ answer: ReferenceAnswer) -> some View {
        let piece = answer.pieceKey.map { CircleDrawing.PieceMark(position: $0.position, minor: $0.minor, inferred: $0.inferred) }
        return GeometryReader { box in
            CircleDrawing(circle: answer.circle, chosen: nil, piece: piece)
                .contentShape(Rectangle())
                .onTapGesture { location in
                    guard let place = Self.place(at: location, in: box.size), let sector = answer.sectors.first(where: { $0.position == place.position }) else { return }
                    Task { await load(sector.fifths, minor: place.minor) }
                }
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityElement()
        .accessibilityLabel(text["reference.circleAria"])
        .accessibilityValue(answer.card?.name ?? "")
        .accessibilityAdjustableAction { direction in
            let card = answer.card
            let position = card.map { (($0.fifths % 12) + 12) % 12 } ?? 11
            let next = direction == .increment ? (position + 1) % 12 : (position + 11) % 12
            guard let sector = answer.sectors.first(where: { $0.position == next }) else { return }
            Task { await load(sector.fifths, minor: card?.minor ?? false) }
        }
    }

    /// The key's card, and under it the own card of the chord or scale last tapped on it.
    @ViewBuilder private func cards(_ answer: ReferenceAnswer) -> some View {
        if let card = answer.card {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                ReferenceCardView(card: card, shown: reference.shown, text: text) { related in
                    Task { await load(related.fifths, minor: related.minor) }
                } hear: { sound in
                    Task { await reference.hear(sound, language: text.language, naming: naming) }
                }
                detail
            }
        } else {
            Text(text["reference.choose"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
        }
    }

    @ViewBuilder private var detail: some View {
        if reference.detail == "chord", let chord = reference.chords?.chord {
            Divider().overlay(Color.ds("hairline"))
            ChordCardPanel(chord: chord, shown: reference.chordShown, text: text) { sound in
                Task { await reference.hearChord(sound) }
            }
        } else if reference.detail == "scale", let scale = reference.scales?.scale {
            Divider().overlay(Color.ds("hairline"))
            ScaleCardPanel(scale: scale, text: text) {
                Task { await reference.hearScale() }
            }
        }
    }

    @MainActor private func load(_ fifths: Int?, minor: Bool) async {
        await reference.load(fifths: fifths, minor: minor, language: text.language, naming: naming)
    }

    /// Chords and Scales start on C major the first time they are shown.
    @MainActor private func loadLookup(_ mode: String) async {
        if mode == "chords", reference.chords == nil {
            await reference.loadChord(nil, type: nil, language: text.language, naming: naming)
        } else if mode == "scales", reference.scales == nil {
            await reference.loadScale(nil, form: nil, language: text.language, naming: naming)
        }
    }

    /// The place and ring under a tap, in CircleDrawing's units (236 across, the minors between 36 and 66 from the centre,
    /// the majors between 66 and 100); nil outside the rings.
    static func place(at point: CGPoint, in size: CGSize) -> (position: Int, minor: Bool)? {
        guard size.width > 0 else { return nil }
        let unit = size.width / 236
        let dx = Double((point.x - size.width / 2) / unit), dy = Double((point.y - size.height / 2) / unit)
        let radius = (dx * dx + dy * dy).squareRoot()
        guard radius >= 36, radius <= 100 else { return nil }
        var degrees = atan2(dx, -dy) * 180 / .pi
        if degrees < 0 { degrees += 360 }
        return (Int((degrees + 15) / 30) % 12, radius < 66)
    }
}

/// A key signature, a root or a chord to look up (ds.css `.chip`, a touch target high): the accent filled while it is chosen.
struct SignatureChoiceStyle: ButtonStyle {
    var on = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .dsType(.bodyStrong)
            .foregroundStyle(on ? Color.ds("on-accent") : Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-2"))
            .frame(minWidth: CGFloat.ds("h-touch"), minHeight: CGFloat.ds("h-touch"))
            .background(on ? Color.ds("accent") : configuration.isPressed ? Color.ds("accent-tint") : Color.ds("surface-2"), in: Capsule())
            .overlay(Capsule().strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong")))
            .contentShape(Capsule())
    }
}

/// The sheet the size of a page on iPadOS 18 and later, as Settings is; the system's form sheet before.
private struct ReferenceSheetSizing: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 18.0, *) {
            content.presentationSizing(.page)
        } else {
            content
        }
    }
}
