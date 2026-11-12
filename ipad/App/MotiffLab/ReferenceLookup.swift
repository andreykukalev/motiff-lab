import SwiftUI

/// A root a chord or a scale of the Reference starts on (music-core's `KeyReference.Roots`): a letter ("step", 0 for C to 6
/// for B) with its accidental ("alter"), its letter name for symbols and its name in the note naming asked for.
struct ReferenceRoot: Equatable, Identifiable {
    var step: Int
    var alter: Int
    var letter: String
    var name: String
    var id: String { "\(step).\(alter)" }

    init(step: Int, alter: Int, letter: String = "", name: String = "") {
        self.step = step
        self.alter = alter
        self.letter = letter
        self.name = name
    }

    init?(_ value: Any?) {
        guard let root = value as? [String: Any], let step = root["step"] as? Int, let alter = root["alter"] as? Int else { return nil }
        self.init(step: step, alter: alter, letter: root["letter"] as? String ?? "", name: root["name"] as? String ?? "")
    }

    static func == (left: ReferenceRoot, right: ReferenceRoot) -> Bool {
        left.step == right.step && left.alter == right.alter
    }
}

/// Chords or Scales of the Reference as music-core answers them (`reference.chord`, `reference.scale` on piece 0): the
/// seventeen roots, what can be built on them, and the card of the one chosen. The app only lays them out.
struct ReferenceLookup {
    /// A chord or a scale to choose: its id ("minor7", "dorian"), for a chord the symbol after the root, and its name.
    struct Choice: Identifiable, Equatable {
        var id: String
        var suffix: String
        var name: String
    }

    /// A chord's own card (`ChordCard`): symbol, name, degrees over notes, then root position and each inversion.
    struct Chord: Equatable {
        struct Position: Equatable, Identifiable {
            var inversion: Int
            var title: String
            /// "6", "6/4", "6/5", "4/3", "2" for a triad or a seventh turned; nil otherwise.
            var figure: String?
            var notes: [String]
            var block: ReferenceCard.Sound
            var arpeggio: ReferenceCard.Sound
            var id: Int { inversion }
        }

        var root: ReferenceRoot
        var type: String
        var symbol: String
        var name: String
        var formula: [String]
        var notes: [String]
        var positions: [Position]
    }

    /// A scale's own card (`ScaleCard`): its name, degrees over notes, the way down when it differs, and its sound up and down.
    struct Scale: Equatable {
        var root: ReferenceRoot
        var form: String
        var name: String
        var formula: [String]
        var notes: [String]
        var descending: [String]
        var sound: ReferenceCard.Sound
    }

    var roots: [ReferenceRoot]
    /// The chords of `reference.chord` ("types") or the scales of `reference.scale` ("forms").
    var choices: [Choice]
    var chord: Chord?
    var scale: Scale?

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil else { return nil }
        roots = (object["roots"] as? [Any] ?? []).compactMap { ReferenceRoot($0) }
        choices = ((object["types"] ?? object["forms"]) as? [[String: Any]] ?? []).compactMap { choice in
            guard let id = choice["id"] as? String else { return nil }
            return Choice(id: id, suffix: choice["suffix"] as? String ?? "", name: choice["name"] as? String ?? "")
        }
        guard let card = object["card"] as? [String: Any], let root = ReferenceRoot(card["root"]) else { return }
        let formula = card["formula"] as? [String] ?? []
        let notes = card["notes"] as? [String] ?? []
        if let type = card["type"] as? String {
            let positions = (card["positions"] as? [[String: Any]] ?? []).compactMap(Self.position)
            chord = Chord(root: root, type: type, symbol: card["symbol"] as? String ?? "", name: card["name"] as? String ?? "",
                          formula: formula, notes: notes, positions: positions)
        } else if let form = card["form"] as? String, let raw = card["sound"] as? [String: Any],
                  let sound = ReferenceCard.sound(raw, title: card["name"] as? String, notes: notes) {
            scale = Scale(root: root, form: form, name: card["name"] as? String ?? "", formula: formula, notes: notes,
                          descending: card["descending"] as? [String] ?? [], sound: sound)
        }
    }

    private static func position(_ item: [String: Any]) -> Chord.Position? {
        let notes = item["notes"] as? [String] ?? []
        guard let inversion = item["inversion"] as? Int,
              let block = (item["block"] as? [String: Any]).flatMap({ ReferenceCard.sound($0, title: item["title"] as? String, notes: notes) }),
              let arpeggio = (item["arpeggio"] as? [String: Any]).flatMap({ ReferenceCard.sound($0, title: item["title"] as? String, notes: notes) })
        else { return nil }
        return Chord.Position(inversion: inversion, title: item["title"] as? String ?? "", figure: item["figure"] as? String,
                              notes: notes, block: block, arpeggio: arpeggio)
    }
}

/// The seventeen roots as chips (the web's RootChips): the letters and each black key spelled both ways.
struct RootChips: View {
    let roots: [ReferenceRoot]
    let chosen: ReferenceRoot?
    let label: String
    let choose: (ReferenceRoot) -> Void

    var body: some View {
        FlowLayout(spacing: CGFloat.ds("s-2")) {
            ForEach(roots) { root in
                let on = chosen == root
                Button(root.name) { choose(root) }
                    .buttonStyle(SignatureChoiceStyle(on: on))
                    .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}

/// A chord's or a scale's degrees over its notes, one column a tone (the web's FormulaTable).
struct FormulaTable: View {
    let formula: [String]
    let notes: [String]
    let text: UIText

    var body: some View {
        Grid(alignment: .center, horizontalSpacing: CGFloat.ds("s-3"), verticalSpacing: CGFloat.ds("s-1")) {
            GridRow {
                Text(text["reference.formula"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")).gridColumnAlignment(.leading)
                ForEach(formula.indices, id: \.self) { index in
                    Text(formula[index]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                }
            }
            GridRow {
                Text(text["reference.notes"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                ForEach(notes.indices, id: \.self) { index in
                    Text(notes[index]).dsType(.body).foregroundStyle(Color.ds("ink"))
                }
            }
        }
        .accessibilityElement(children: .combine)
    }
}

/// A chord's own card (the web's ChordCardView): its symbol and name, degrees over notes, then root position and each
/// inversion, each played struck at once or broken and shown on the small keyboard, one stretch of keys for them all.
struct ChordCardPanel: View {
    let chord: ReferenceLookup.Chord
    let shown: ReferenceCard.Sound?
    let text: UIText
    let hear: (ReferenceCard.Sound) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                Text(chord.symbol).dsType(.heading).foregroundStyle(Color.ds("ink"))
                Text(chord.name).dsType(.body).foregroundStyle(Color.ds("ink-2"))
            }
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(.isHeader)
            FormulaTable(formula: chord.formula, notes: chord.notes, text: text)
            Text(text["reference.positions"]).overline()
            ForEach(chord.positions) { position in
                row(position)
            }
            if let shown {
                MiniKeyboard(sound: shown)
                    .frame(maxWidth: 320, alignment: .leading)
                    .accessibilityLabel(text.format("reference.keys", shown.notes.joined(separator: " ")))
            }
        }
    }

    private func row(_ position: ReferenceLookup.Chord.Position) -> some View {
        let spoken = "\(chord.symbol), \(position.title): \(position.notes.joined(separator: " "))"
        return FlowLayout(spacing: CGFloat.ds("s-2")) {
            Text(position.title.prefix(1).uppercased() + position.title.dropFirst()).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            if let figure = position.figure {
                Text(figure).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            Text(position.notes.joined(separator: " ")).dsType(.body).foregroundStyle(Color.ds("ink-2"))
            play(position.block, label: "reference.block", spoken: spoken)
            play(position.arpeggio, label: "reference.arpeggio", spoken: spoken)
        }
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .frame(maxWidth: .infinity, minHeight: CGFloat.ds("h-touch"), alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
    }

    private func play(_ sound: ReferenceCard.Sound, label: String, spoken: String) -> some View {
        let on = shown?.id == sound.id
        return Button {
            hear(sound)
        } label: {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: "play")
                Text(text[label])
            }
        }
        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        .background(on ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .frame(minHeight: CGFloat.ds("h-touch"))
        .accessibilityLabel(text.format("reference.play", "\(spoken), \(text[label])"))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A scale's own card (the web's ScaleCardView): its name, degrees over notes, the way down when it differs, and the scale
/// up and down to hear, its way up on the small keyboard.
struct ScaleCardPanel: View {
    let scale: ReferenceLookup.Scale
    let text: UIText
    let hear: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(scale.name.prefix(1).uppercased() + scale.name.dropFirst())
                .dsType(.heading)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            FormulaTable(formula: scale.formula, notes: scale.notes, text: text)
            if !scale.descending.isEmpty {
                Text(text.format("reference.descending", scale.descending.joined(separator: " ")))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            Button {
                hear()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "play")
                    Text(text["reference.upAndDown"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .frame(minHeight: CGFloat.ds("h-touch"))
            .accessibilityLabel(text.format("reference.play", "\(scale.name), \(text["reference.upAndDown"])"))
            MiniKeyboard(sound: scale.sound)
                .frame(maxWidth: 320, alignment: .leading)
                .accessibilityLabel(text.format("reference.keys", scale.notes.joined(separator: " ")))
        }
    }
}

/// Chords or Scales in the Reference's sheet: a root, then what to build on it (a chord's chips show its symbol on that
/// root), then the card. Everything is music-core's; the view only lays it out.
struct ReferenceLookupView: View {
    let lookup: ReferenceLookup
    /// "chords" or "scales".
    let kind: String
    let text: UIText
    @ObservedObject var reference: ReferenceController
    let choose: (ReferenceRoot?, String?) -> Void

    var body: some View {
        let root = lookup.chord?.root ?? lookup.scale?.root
        let chosen = lookup.chord?.type ?? lookup.scale?.form
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["reference.root"]).overline()
            RootChips(roots: lookup.roots, chosen: root, label: text["reference.root"]) { choose($0, chosen) }
            Text(text[kind == "chords" ? "reference.chordType" : "reference.scaleForm"]).overline()
            FlowLayout(spacing: CGFloat.ds("s-2")) {
                ForEach(lookup.choices) { choice in
                    let on = chosen == choice.id
                    Button(kind == "chords" ? "\(root?.letter ?? "")\(choice.suffix)" : choice.name) { choose(root, choice.id) }
                        .buttonStyle(SignatureChoiceStyle(on: on))
                        .accessibilityLabel(kind == "chords" ? "\(root?.letter ?? "")\(choice.suffix), \(choice.name)" : choice.name)
                        .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
            if let chord = lookup.chord {
                ChordCardPanel(chord: chord, shown: reference.chordShown, text: text) { sound in
                    Task { await reference.hearChord(sound) }
                }
                .padding(.top, CGFloat.ds("s-3"))
            } else if let scale = lookup.scale {
                ScaleCardPanel(scale: scale, text: text) {
                    Task { await reference.hearScale() }
                }
                .padding(.top, CGFloat.ds("s-3"))
            }
        }
    }
}
