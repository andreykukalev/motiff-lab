import SwiftUI

/// «Аккорды и гаммы» (v0.5, the web's ChordsScalesChoice): a chord or a scale from a root, chosen apart from how it sounds,
/// the position, the spacing and the way a chord is played, or the way and the octaves of a scale. Kept while the sheet is
/// open; nothing of it is saved.
struct SoundingChoice: Equatable {
    var scale = false
    var inversion = 0
    /// "close" or "open".
    var spacing = "close"
    /// A chord's way: "together", "up", "down", "upAndDown", "broken".
    var pattern = "upAndDown"
    /// A scale's way: "up", "down", "upAndDown".
    var scalePattern = "upAndDown"
    var octaves = 1
    var loop = false

    /// The `reference.sounding` request for the chord or the scale chosen.
    func request(root: ReferenceRoot, type: String?, form: String?, language: String, naming: String) -> [String: Any] {
        var fields: [String: Any] = ["step": root.step, "alter": root.alter, "language": language, "naming": naming]
        if scale, let form {
            fields["form"] = form
            fields["pattern"] = scalePattern
            fields["octaves"] = octaves
        } else {
            fields["type"] = type ?? "major"
            fields["inversion"] = inversion
            fields["spacing"] = spacing
            fields["pattern"] = pattern
        }
        return fields
    }
}

/// A chord or a scale as it sounds, as music-core answers `reference.sounding` on piece 0: its notation, the keys a keyboard
/// marks with their names, the choices offered and the sound itself.
struct ReferenceSounding {
    var mei: String
    var keys: [Int]
    var names: [String]
    var low: Int
    var high: Int
    var score: PlaybackScore?
    /// A chord's position as it sounds (at most its last), its name and figures and the bass named.
    var inversion: Int?
    var title: String?
    var figure: String?
    var bass: String?
    var spacings: [String]
    var patterns: [String]

    init?(answer: String) {
        guard let object = facadeObject(answer), object["error"] == nil, let mei = object["mei"] as? String else { return nil }
        self.mei = mei
        let marked = object["keys"] as? [[String: Any]] ?? []
        keys = marked.compactMap { $0["pitch"] as? Int }
        names = marked.compactMap { $0["name"] as? String }
        low = object["keyboardLow"] as? Int ?? 48
        high = object["keyboardHigh"] as? Int ?? 83
        score = PlaybackScore.read(answer)
        inversion = object["inversion"] as? Int
        title = object["title"] as? String
        figure = object["figure"] as? String
        bass = object["bass"] as? String
        spacings = object["spacings"] as? [String] ?? []
        patterns = object["patterns"] as? [String] ?? []
    }

    /// The keys for the small keyboard.
    var sound: ReferenceCard.Sound {
        ReferenceCard.Sound(id: "sounding", title: "", notes: names, pitches: keys, low: low, high: high)
    }
}

extension ReferenceController {
    /// Chords and scales open on C major the first time they are shown, the chord and the scale from one root.
    func loadSounding(language: String, naming: String) async {
        if chords == nil {
            await loadChord(nil, type: nil, language: language, naming: naming)
        }
        if scales == nil {
            await loadScale(chords?.chord?.root, form: nil, language: language, naming: naming)
        }
        await sound(language: language, naming: naming)
    }

    /// A root for both the chord and the scale.
    func pickRoot(_ root: ReferenceRoot, language: String, naming: String) async {
        await loadChord(root, type: nil, language: language, naming: naming)
        await loadScale(root, form: nil, language: language, naming: naming)
        await sound(language: language, naming: naming)
    }

    /// A chord ("type") or a scale ("form") from the root chosen.
    func pick(type: String? = nil, form: String? = nil, language: String, naming: String) async {
        if let type {
            await loadChord(nil, type: type, language: language, naming: naming)
        }
        if let form {
            await loadScale(nil, form: form, language: language, naming: naming)
        }
        await sound(language: language, naming: naming)
    }

    /// A change of how it sounds, heard at once while the music plays, else waiting for Play.
    func setSounding(_ change: (inout SoundingChoice) -> Void, language: String, naming: String) async {
        change(&sounding)
        await sound(language: language, naming: naming)
    }

    /// Play or Pause.
    func playSounding() async {
        if playing == "chords" {
            pause()
        } else {
            await listen("chords", sounded?.score, loop: sounding.loop)
        }
    }

    /// The loop on or off, heard at once while the music plays.
    func loopSounding() async {
        sounding.loop.toggle()
        if playing == "chords" {
            await listen("chords", sounded?.score, loop: sounding.loop)
        }
    }

    /// Asks music-core for the chord or scale as chosen, and plays it again if it was playing.
    private func sound(language: String, naming: String) async {
        guard let root = (sounding.scale ? scales?.scale?.root : chords?.chord?.root) ?? chords?.chord?.root else { return }
        let request = ReferenceController.json(sounding.request(root: root, type: chords?.chord?.type, form: scales?.scale?.form,
                                                                language: language, naming: naming))
        let text = await MusicCore.run { MusicCore.call(piece: 0, "reference.sounding", request) }
        guard let next = ReferenceSounding(answer: text) else { return }
        sounded = next
        if let inversion = next.inversion {
            sounding.inversion = inversion
        }
        if playing == "chords" {
            await listen("chords", next.score, loop: sounding.loop)
        }
    }
}

/// «Аккорды и гаммы» in the Reference's sheet (the web's ChordsScalesTab, the iPad mockup d1): what sounds, a chord or a
/// scale from a root in groups, then the result, its name, degrees over notes, notation and keyboard, and under it how it
/// sounds, «Звучание». Side by side when the sheet is wide, one above the other when it is narrow. Everything is
/// music-core's; the view only lays it out.
struct ReferenceChordsScalesView: View {
    @ObservedObject var reference: ReferenceController
    let text: UIText
    let naming: String
    let wide: Bool

    var body: some View {
        if wide {
            HStack(alignment: .top, spacing: CGFloat.ds("s-8")) {
                material.frame(maxWidth: .infinity, alignment: .leading)
                result.frame(maxWidth: .infinity, alignment: .leading)
            }
        } else {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                material
                Divider().overlay(Color.ds("hairline"))
                result
            }
        }
    }

    private var choice: SoundingChoice { reference.sounding }

    private var material: some View {
        let root = choice.scale ? reference.scales?.scale?.root : reference.chords?.chord?.root
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            SegmentedChoice(options: [(false, text["reference.chordType"]), (true, text["reference.scaleForm"])],
                            chosen: choice.scale, label: text["reference.material"]) { scale in
                change { $0.scale = scale }
            }
            .frame(height: CGFloat.ds("h-control"))
            Text(text["reference.root"]).overline()
            RootChips(roots: reference.chords?.roots ?? [], chosen: root, label: text["reference.root"]) { picked in
                Task { await reference.pickRoot(picked, language: text.language, naming: naming) }
            }
            if let lookup = choice.scale ? reference.scales : reference.chords {
                groups(lookup, root: root)
            }
        }
    }

    /// The chords or scales offered, a group under its name.
    private func groups(_ lookup: ReferenceLookup, root: ReferenceRoot?) -> some View {
        let families = lookup.choices.map(\.family).reduce(into: [String]()) { if !$0.contains($1) { $0.append($1) } }
        let chosen = choice.scale ? lookup.scale?.form : lookup.chord?.type
        return ForEach(families, id: \.self) { family in
            Text(text["reference.family.\(family)"]).overline()
            FlowLayout(spacing: CGFloat.ds("s-2")) {
                ForEach(lookup.choices.filter { $0.family == family }) { item in
                    let on = chosen == item.id
                    let label = choice.scale ? item.short : "\(root?.letter ?? "")\(item.suffix)"
                    Button(label) {
                        Task {
                            if choice.scale {
                                await reference.pick(form: item.id, language: text.language, naming: naming)
                            } else {
                                await reference.pick(type: item.id, language: text.language, naming: naming)
                            }
                        }
                    }
                    .buttonStyle(SignatureChoiceStyle(on: on))
                    .accessibilityLabel(choice.scale ? item.name : "\(label), \(item.name)")
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
        }
    }

    @ViewBuilder private var result: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            if choice.scale, let scale = reference.scales?.scale {
                heading(scale.name.prefix(1).uppercased() + scale.name.dropFirst(), detail: nil, formula: scale.formula, notes: scale.notes)
            } else if !choice.scale, let chord = reference.chords?.chord {
                let position = [reference.sounded?.title, reference.sounded?.bass.map { text.format("reference.bass", $0) }].compactMap { $0 }
                heading(chord.symbol, detail: ([chord.name] + position).joined(separator: " · "), formula: chord.formula, notes: chord.notes)
            }
            if let sounded = reference.sounded {
                ReferenceNotation(mei: sounded.mei, label: text.format("reference.notationAria", sounded.names.joined(separator: " ")))
                MiniKeyboard(sound: sounded.sound)
                    .frame(maxWidth: CGFloat.ds("s-12") * 11, alignment: .leading)
                    .accessibilityLabel(text.format("reference.keys", sounded.names.joined(separator: " ")))
            }
            Divider().overlay(Color.ds("hairline"))
            Text(text["reference.sound"]).overline()
            ways
            ReferenceTransport(text: text, playing: reference.playing == "chords", loop: choice.loop, enabled: reference.sounded != nil) {
                Task { await reference.playSounding() }
            } toggleLoop: {
                Task { await reference.loopSounding() }
            }
        }
    }

    private func heading(_ title: String, detail: String?, formula: [String], notes: [String]) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(title).dsType(.title).foregroundStyle(Color.ds("ink")).accessibilityAddTraits(.isHeader)
            if let detail {
                Text(detail).dsType(.body).foregroundStyle(Color.ds("ink-2"))
            }
            FormulaTable(formula: formula, notes: notes, text: text)
        }
    }

    /// How it sounds: a chord's spacing, position and way, or a scale's way and octaves.
    @ViewBuilder private var ways: some View {
        if choice.scale {
            ReferenceChoiceRow(label: text["reference.pattern"], options: (reference.sounded?.patterns ?? []).map { ($0, text["reference.pattern.\(ExplorerChoice.key($0))"]) },
                               chosen: choice.scalePattern) { value in change { $0.scalePattern = value } }
            ReferenceChoiceRow(label: text["reference.octaves"], options: [("1", "1"), ("2", "2")], chosen: String(choice.octaves)) { value in
                change { $0.octaves = Int(value) ?? 1 }
            }
        } else {
            ReferenceChoiceRow(label: text["reference.spacing"], options: (reference.sounded?.spacings ?? []).map { ($0, text["reference.spacing.\($0)"]) },
                               chosen: choice.spacing) { value in change { $0.spacing = value } }
            ReferenceChoiceRow(label: text["reference.inversion"], options: positions, chosen: String(choice.inversion)) { value in
                change { $0.inversion = Int(value) ?? 0 }
            }
            ReferenceChoiceRow(label: text["reference.pattern"], options: (reference.sounded?.patterns ?? []).map { ($0, text["reference.pattern.\(ExplorerChoice.key($0))"]) },
                               chosen: choice.pattern) { value in change { $0.pattern = value } }
        }
    }

    /// Root position by its name, each inversion by its figures, or by its number where it has none.
    private var positions: [(String, String)] {
        (reference.chords?.chord?.positions ?? []).map { position in
            (String(position.inversion), ReferencePositionName.of(inversion: position.inversion, title: position.title, figure: position.figure, text: text))
        }
    }

    private func change(_ apply: @escaping (inout SoundingChoice) -> Void) {
        Task { await reference.setSounding(apply, language: text.language, naming: naming) }
    }
}

/// A position's name as the web's ListeningTab gives it: root position by its name, an inversion by its figures, or
/// «Обр. 2» where it has none.
enum ReferencePositionName {
    static func of(inversion: Int, title: String, figure: String?, text: UIText) -> String {
        if inversion == 0 {
            return title.prefix(1).uppercased() + title.dropFirst()
        }
        return figure ?? text.format("reference.inversionNumber", String(inversion))
    }
}

/// A labelled row of choices (the web's SegChoice): the label on the left, the segmented choice beside it.
struct ReferenceChoiceRow: View {
    let label: String
    let options: [(String, String)]
    let chosen: String
    let choose: (String) -> Void

    var body: some View {
        FlowLayout(spacing: CGFloat.ds("s-3")) {
            Text(label)
                .dsType(.body)
                .foregroundStyle(Color.ds("ink-2"))
                .frame(minWidth: CGFloat.ds("s-12") * 3, minHeight: CGFloat.ds("h-touch"), alignment: .leading)
            SegmentedChoice(options: options, chosen: chosen, label: label, choose: choose)
                .frame(height: CGFloat.ds("h-touch"))
        }
    }
}

/// Play or Pause, and the loop: the Reference's music, heard once or round and round.
struct ReferenceTransport<Extra: View>: View {
    let text: UIText
    let playing: Bool
    let loop: Bool
    var enabled = true
    let toggle: () -> Void
    let toggleLoop: () -> Void
    @ViewBuilder var extra: () -> Extra

    var body: some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Button(action: toggle) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: playing ? "pause" : "playf")
                    Text(text[playing ? "reference.pause" : "reference.playIt"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .primary))
            .disabled(!enabled)
            Button(action: toggleLoop) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "loop")
                    Text(text["reference.loop"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .secondary))
            .background(loop ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .accessibilityAddTraits(loop ? .isSelected : [])
            extra()
        }
    }
}

extension ReferenceTransport where Extra == EmptyView {
    init(text: UIText, playing: Bool, loop: Bool, enabled: Bool = true, toggle: @escaping () -> Void, toggleLoop: @escaping () -> Void) {
        self.init(text: text, playing: playing, loop: loop, enabled: enabled, toggle: toggle, toggleLoop: toggleLoop) { EmptyView() }
    }
}
