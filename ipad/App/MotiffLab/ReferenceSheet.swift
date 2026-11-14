import SwiftUI

/// The Reference's sheet, «Справочник» (an experiment, Settings · Experimental; the web app's HandbookSheet, v0.5): its
/// sections as tabs, Тональности, Аккорды и гаммы and Исследователь. Keys first, as the player most often calls the circle
/// up: the key signatures, as he most often looks a key up by them (Andrei, 2026-11-11 23:44 UTC), then the circle of
/// fifths, the chosen key's card and the own card of its chord or scale, side by side as far as the sheet's width allows,
/// so the sheet's room holds music rather than empty paper (#479 item 6). A signature marks the major and its relative
/// minor and opens the major's card; a tap on the circle opens that key's card; a scale or chord of the card plays on the
/// app's piano and opens its own card. Over a piece, the key it starts in is outlined on the circle, dotted when it is
/// assumed, and its card opens first, and the explorer starts in it. Opened from the library and from the score's bar alike.
struct ReferenceSheet: View {
    let text: UIText
    @ObservedObject var settings: DeviceSettings
    @StateObject private var reference: ReferenceController
    /// Opened over a piece (the score or a PDF's pages), where the piece's key may be not known.
    private let inPiece: Bool
    @Environment(\.dismiss) private var dismiss

    /// The content's width from which the sheet lays its parts side by side in three columns, and in two.
    static let threeColumns: CGFloat = 900
    static let twoColumns: CGFloat = 600

    /// `piece`: the open piece, whose key the circle marks; 0 from the library.
    init(text: UIText, settings: DeviceSettings, piece: Int32 = 0) {
        self.text = text
        _settings = ObservedObject(wrappedValue: settings)
        _reference = StateObject(wrappedValue: ReferenceController(piece: piece))
        inPiece = piece != 0
    }

    var body: some View {
        GeometryReader { size in
            let width = size.size.width - 2 * CGFloat.ds("s-6")
            VStack(alignment: .leading, spacing: 0) {
                header
                Divider().overlay(Color.ds("hairline"))
                ScrollView {
                    content(width: width)
                        .padding(.horizontal, CGFloat.ds("s-6"))
                        .padding(.vertical, CGFloat.ds("s-5"))
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        .background(Color.ds("surface"))
        .modifier(ReferenceSheetSizing())
        .task { await load(nil, minor: false) }
        .onDisappear { reference.stop() }
    }

    @ViewBuilder private func content(width: CGFloat) -> some View {
        switch reference.mode {
        case "chords":
            ReferenceChordsScalesView(reference: reference, text: text, naming: naming, wide: width >= Self.threeColumns)
        case "explorer":
            ReferenceExplorerView(reference: reference, text: text, naming: naming, wide: width >= Self.twoColumns)
        default:
            keys(width: width)
        }
    }

    /// The title and the tabs on one row when they fit, the tabs under the title when not.
    private var header: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: CGFloat.ds("s-5")) {
                title
                tabs
                Spacer(minLength: 0)
                close
            }
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                HStack {
                    title
                    Spacer(minLength: 0)
                    close
                }
                tabs
            }
        }
        .padding(.horizontal, CGFloat.ds("s-6"))
        .padding(.vertical, CGFloat.ds("s-3"))
    }

    private var title: some View {
        Text(text["reference.handbook"])
            .dsType(.title)
            .foregroundStyle(Color.ds("ink"))
            .lineLimit(1)
            .accessibilityAddTraits(.isHeader)
    }

    private var tabs: some View {
        SegmentedChoice(options: ["keys", "chords", "explorer"].map { ($0, text["reference.tab.\($0)"]) },
                        chosen: reference.mode, label: text["reference.tabs"]) { mode in
            reference.mode = mode
            Task { await loadTab(mode) }
        }
        .frame(height: CGFloat.ds("h-touch"))
    }

    private var close: some View {
        Button {
            dismiss()
        } label: {
            Icon(name: "close").frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        }
        .buttonStyle(BarActionStyle())
        .accessibilityLabel(text["reference.closeHandbook"])
    }

    private var naming: String { settings["notes.naming"] }

    /// Тональности: the signatures across the top; under them the circle, the key's card and its chord's or scale's own card
    /// in three columns when the sheet is wide, the circle and the key's card side by side with the own card under them
    /// when it is middling, all one above the other when it is narrow.
    @ViewBuilder private func keys(width: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
            lookup
            if let answer = reference.answer {
                if width >= Self.threeColumns {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-6")) {
                        circleColumn(answer).frame(width: width * 0.32)
                        keyCard(answer).frame(maxWidth: .infinity, alignment: .leading)
                        if reference.detail != nil {
                            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) { detail }
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                } else if width >= Self.twoColumns {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-6")) {
                        circleColumn(answer).frame(width: width * 0.42)
                        keyCard(answer).frame(maxWidth: .infinity, alignment: .leading)
                    }
                    detailBelow
                } else {
                    circleColumn(answer).frame(maxWidth: CGFloat.ds("s-12") * 8).frame(maxWidth: .infinity)
                    keyCard(answer)
                    detailBelow
                }
            }
        }
    }

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

    /// The circle, and under it the piece's key with a way to its card, or that it is not known (the circle then shows C
    /// major, #533 7).
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
            } else if ReferenceOpening.saysKeyUnknown(inPiece: inPiece, pieceKey: answer.pieceKey) {
                Text(text["reference.pieceKeyUnknown"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
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

    /// The key's card, or a word on how to choose one.
    @ViewBuilder private func keyCard(_ answer: ReferenceAnswer) -> some View {
        if let card = answer.card {
            ReferenceCardView(card: card, shown: reference.shown, text: text) { related in
                Task { await load(related.fifths, minor: related.minor) }
            } hear: { sound in
                Task { await reference.hear(sound, language: text.language, naming: naming) }
            }
        } else {
            Text(text["reference.choose"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
        }
    }

    /// The own card under the key's card, past a rule.
    @ViewBuilder private var detailBelow: some View {
        if reference.detail != nil {
            Divider().overlay(Color.ds("hairline"))
            detail
        }
    }

    /// The own card of the chord or scale last opened from the key's card.
    @ViewBuilder private var detail: some View {
        if reference.detail == "chord", let chord = reference.chords?.chord {
            ChordCardPanel(chord: chord, shown: reference.chordShown, text: text) { sound in
                Task { await reference.hearChord(sound) }
            }
        } else if reference.detail == "scale", let scale = reference.scales?.scale {
            ScaleCardPanel(scale: scale, text: text) {
                Task { await reference.hearScale() }
            }
        }
    }

    @MainActor private func load(_ fifths: Int?, minor: Bool) async {
        await reference.load(fifths: fifths, minor: minor, language: text.language, naming: naming)
    }

    /// Аккорды и гаммы asks music-core each time it is shown, as a key card may have chosen its chord or scale since; the
    /// explorer the first time.
    @MainActor private func loadTab(_ mode: String) async {
        if mode == "chords" {
            await reference.loadSounding(language: text.language, naming: naming)
        } else if mode == "explorer", reference.progression == nil {
            await reference.loadExplorer(language: text.language, naming: naming)
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
