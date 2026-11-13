import SwiftUI

/// «Исследователь» in the Reference's sheet (v0.5, the web's ExplorerTab, mockup C3): a key, its chords to tap into a
/// progression of up to eight bars, each bar's position or its removal a tap on the bar; the progression in notes and on the
/// keyboard, in a texture over the bass, close or open, played once or round at a tempo, a change heard at once. Everything
/// is music-core's (`reference.explorer`, HarmonyExplorer); nothing is written into the piece.
struct ReferenceExplorerView: View {
    @ObservedObject var reference: ReferenceController
    let text: UIText
    let naming: String
    let wide: Bool

    var body: some View {
        if let answer = reference.progression {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-5")) {
                labelled(text["reference.key"]) { keyMenu(answer) }
                labelled(text["reference.chordsOfKey"]) { chordTiles(answer) }
                progression(answer)
                sound(answer)
            }
        }
    }

    private var choice: ExplorerChoice { reference.explorer }

    private func change(_ apply: @escaping (inout ExplorerChoice) -> Void) {
        Task { await reference.setExplorer(apply, language: text.language, naming: naming) }
    }

    /// A row with its label on the left when the sheet is wide, above it when narrow.
    @ViewBuilder private func labelled<Content: View>(_ label: String, @ViewBuilder content: () -> Content) -> some View {
        if wide {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-4")) {
                Text(label).dsType(.body).foregroundStyle(Color.ds("ink-2")).frame(width: CGFloat.ds("s-12") * 3, alignment: .leading)
                content().frame(maxWidth: .infinity, alignment: .leading)
            }
        } else {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(label).overline()
                content()
            }
        }
    }

    /// The thirty keys, each starting the progression again from I–IV–V–I.
    private func keyMenu(_ answer: ExplorerAnswer) -> some View {
        Menu {
            ForEach(answer.keys) { key in
                Button(key.name) {
                    change { $0.start(fifths: key.fifths, minor: key.minor) }
                }
            }
        } label: {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(answer.key.name).dsType(.bodyStrong)
                Icon(name: "chevd")
            }
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-touch"))
            .background(Color.ds("surface-2"), in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline-strong")))
            .contentShape(Capsule())
        }
        .accessibilityLabel(text["reference.key"])
        .accessibilityValue(answer.key.name)
    }

    /// The key's chords: a tap adds one at the end while there is room.
    private func chordTiles(_ answer: ExplorerAnswer) -> some View {
        let full = (choice.steps?.count ?? 0) >= answer.maxBars
        return FlowLayout(spacing: CGFloat.ds("s-2")) {
            ForEach(answer.chords) { chord in
                Button {
                    change { $0.add(chord.id, maxBars: answer.maxBars) }
                } label: {
                    ReferenceChordTile(numeral: chord.numeral, figure: nil, symbol: chord.symbol, on: false)
                        .frame(minWidth: CGFloat.ds("s-12") * 1.5)
                }
                .buttonStyle(.plain)
                .disabled(full)
                .accessibilityLabel(text.format("reference.addChord", chord.numeral, chord.symbol))
            }
        }
    }

    /// The bars, the chosen one's positions over them, the notation and the chosen bar's keys.
    @ViewBuilder private func progression(_ answer: ExplorerAnswer) -> some View {
        if let selected = choice.selected, answer.bars.indices.contains(selected) {
            turns(answer, bar: selected)
        }
        LazyVGrid(columns: [GridItem(.adaptive(minimum: CGFloat.ds("s-12") * 2), spacing: CGFloat.ds("s-2"))], spacing: CGFloat.ds("s-2")) {
            ForEach(answer.bars.indices, id: \.self) { index in
                bar(answer.bars[index], index: index)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["reference.progression"])
        if answer.bars.isEmpty {
            Text(text["reference.progressionEmpty"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        } else {
            ReferenceNotation(mei: answer.mei, stretch: true,
                              label: text.format("reference.notationAria", answer.bars.map(\.symbol).joined(separator: ", ")))
        }
        if let selected = choice.selected, let sound = answer.sound(of: selected) {
            MiniKeyboard(sound: sound)
                .frame(maxWidth: CGFloat.ds("s-12") * 11, alignment: .leading)
                .accessibilityLabel(text.format("reference.keys", sound.title))
        }
    }

    private func bar(_ bar: ExplorerAnswer.Bar, index: Int) -> some View {
        let on = choice.selected == index
        let numeral = bar.figure.map { "\(bar.numeral) \($0)" } ?? bar.numeral
        return Button {
            reference.selectBar(index)
        } label: {
            ReferenceChordTile(numeral: bar.numeral, figure: bar.figure, symbol: bar.symbol, on: on)
                .frame(maxWidth: .infinity)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text.format("reference.barAria", String(index + 1), numeral, bar.symbol))
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// Above the bars, for the bar chosen: each position of its chord by its name or figures, and Remove.
    private func turns(_ answer: ExplorerAnswer, bar: Int) -> some View {
        let shown = answer.bars[bar]
        let options = (answer.chord(shown.chord)?.positions ?? []).map { position in
            (position.inversion, ReferencePositionName.of(inversion: position.inversion, title: position.title, figure: position.figure, text: text))
        }
        return FlowLayout(spacing: CGFloat.ds("s-3")) {
            Text(text.format("reference.barTurns", String(bar + 1)))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .frame(minHeight: CGFloat.ds("h-touch"))
            SegmentedChoice(options: options, chosen: shown.inversion, label: text["reference.inversion"]) { inversion in
                change { $0.turn(bar: bar, inversion: inversion) }
            }
            .frame(height: CGFloat.ds("h-touch"))
            Button {
                change { $0.remove(bar: bar) }
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "trash")
                    Text(text["reference.removeBar"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
        }
        .padding(.horizontal, CGFloat.ds("s-3"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text.format("reference.barTurns", String(bar + 1)))
    }

    /// The texture and the spacing, then Play, the loop and the tempo.
    private func sound(_ answer: ExplorerAnswer) -> some View {
        let textures = answer.textures.map { ($0, text["reference.texture.\(ExplorerChoice.key($0))"]) }
        let spacings = answer.spacings.map { ($0, text["reference.spacing.\($0)"]) }
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            FlowLayout(spacing: CGFloat.ds("s-6")) {
                ReferenceChoiceRow(label: text["reference.texture"], options: textures, chosen: choice.texture) { value in
                    change { $0.texture = value }
                }
                ReferenceChoiceRow(label: text["reference.spacing"], options: spacings, chosen: choice.spacing) { value in
                    change { $0.spacing = value }
                }
            }
            ReferenceTransport(text: text, playing: reference.playing == "explorer", loop: choice.loop, enabled: !answer.bars.isEmpty) {
                Task { await reference.playExplorer() }
            } toggleLoop: {
                Task { await reference.loopExplorer() }
            } extra: {
                Button {
                    change { $0.nextTempo(answer.tempos) }
                } label: {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "tempo")
                        Text(text.format("reference.tempo", String(choice.tempo)))
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                .accessibilityLabel(text.format("reference.tempoAria", String(choice.tempo)))
            }
        }
    }
}

/// A chord as a tile: its numeral with the figures of its position, and its symbol; the accent while it is chosen.
struct ReferenceChordTile: View {
    let numeral: String
    let figure: String?
    let symbol: String
    let on: Bool

    var body: some View {
        VStack(spacing: CGFloat.ds("s-1")) {
            HStack(alignment: .top, spacing: 0) {
                Text(numeral).dsType(.heading)
                if let figure {
                    Text(figure).dsType(.caption)
                }
            }
            Text(symbol).dsType(.caption)
        }
        .foregroundStyle(on ? Color.ds("accent") : Color.ds("ink"))
        .padding(.horizontal, CGFloat.ds("s-3"))
        .padding(.vertical, CGFloat.ds("s-2"))
        .frame(minHeight: CGFloat.ds("h-touch"))
        .background(on ? Color.ds("accent-tint") : Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("accent") : Color.ds("hairline"), lineWidth: on ? 2 : 1))
        .contentShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
    }
}
