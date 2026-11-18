import SwiftUI

/// A key's card in the Reference (the web app's KeyCardView): its name; the relative, parallel and enharmonic keys, each a
/// tap to their own card; the key signature on a treble staff and in words; the scales and the three main chords with
/// their notes, each played by a tap and shown on the small keyboard below, its own card then opening under this one.
struct ReferenceCardView: View {
    let card: ReferenceCard
    let shown: ReferenceCard.Sound?
    let text: UIText
    let pick: (ReferenceCard.Related) -> Void
    let hear: (ReferenceCard.Sound) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(card.name.prefix(1).uppercased() + card.name.dropFirst())
                .dsType(.heading)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            FlowLayout(spacing: CGFloat.ds("s-3")) {
                related("reference.relative", card.relative)
                related("reference.parallel", card.parallel)
                if let enharmonic = card.enharmonic {
                    related("reference.enharmonic", enharmonic)
                }
            }
            HStack(spacing: CGFloat.ds("s-3")) {
                SignatureStaff(steps: card.trebleSteps, sharps: card.fifths > 0)
                    .accessibilityLabel(text.format("reference.staffAria", card.signatureLine))
                Text(card.signatureLine)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityHidden(true)
            }
            Text(text["reference.scales"]).overline()
            ForEach(card.scales) { scale in
                soundRow(scale, label: text.format("reference.play", "\(scale.title): \(scale.notes.joined(separator: " "))")) {
                    Icon(name: "play")
                    Text(scale.title.prefix(1).uppercased() + scale.title.dropFirst()).dsType(.bodyStrong)
                }
            }
            Text(text["reference.chords"]).overline()
            ForEach(card.chords) { chord in
                let spoken = "\(chord.numeral ?? ""), \(chord.functionName ?? ""), \(chord.title): \(chord.notes.joined(separator: " "))"
                soundRow(chord, label: text.format("reference.play", spoken)) {
                    Text(chord.numeral ?? "")
                        .font(DesignTokens.font(display: true, weight: 600, size: 17))
                        .frame(minWidth: CGFloat.ds("s-8"), alignment: .leading)
                    Text(chord.title).dsType(.bodyStrong)
                }
                .overlay(alignment: .leading) {
                    Rectangle()
                        .fill(Color.ds(CircleDrawing.functionColour(chord.function ?? "none")))
                        .frame(width: CGFloat.ds("s-1"))
                        .accessibilityHidden(true)
                }
            }
            if let shown {
                MiniKeyboard(sound: shown)
                    .frame(maxWidth: 320, alignment: .leading)
                    .accessibilityLabel(text.format("reference.keys", shown.notes.joined(separator: " ")))
            }
        }
    }

    private func related(_ label: String, _ key: ReferenceCard.Related) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Text(text[label]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Button(key.name) { pick(key) }
                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                .foregroundStyle(Color.ds("accent"))
                .accessibilityLabel("\(text[label]): \(key.name)")
        }
    }

    /// A scale or chord as one button: what it is, its notes, and for a chord its function; pressed while it is shown.
    private func soundRow<Lead: View>(_ sound: ReferenceCard.Sound, label: String, @ViewBuilder lead: () -> Lead) -> some View {
        let on = shown?.id == sound.id
        return Button {
            hear(sound)
        } label: {
            HStack(spacing: CGFloat.ds("s-3")) {
                lead()
                Text(sound.notes.joined(separator: " ")).dsType(.body).foregroundStyle(Color.ds("ink-2"))
                Spacer(minLength: CGFloat.ds("s-2"))
                if let function = sound.functionName {
                    Text(function).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(minHeight: CGFloat.ds("h-touch"))
            .background(on ? Color.ds("accent-tint") : Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(on ? Color.ds("accent") : Color.ds("hairline")))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A key signature on a treble staff: five lines, the clef, and each sharp or flat on its line or space (music-core's
/// treble steps, diatonic steps above the bottom line). Drawn in units of 4 a step, 48 high.
struct SignatureStaff: View {
    let steps: [Int]
    let sharps: Bool

    var body: some View {
        let width = 34.0 + 9.0 * Double(max(1, steps.count))
        Canvas { context, size in
            let unit = Double(size.height) / 48
            let ink = Color.ds("ink")
            for line in 0..<5 {
                let y: Double = (40 - Double(line) * 8) * unit
                var path = Path()
                path.move(to: CGPoint(x: 0, y: y))
                path.addLine(to: CGPoint(x: Double(size.width), y: y))
                context.stroke(path, with: .color(ink), lineWidth: CGFloat(0.8 * unit))
            }
            context.draw(Text("𝄞").font(.system(size: CGFloat(34 * unit))).foregroundStyle(ink), at: CGPoint(x: 12 * unit, y: 26 * unit))
            for (index, step) in steps.enumerated() {
                let at = CGPoint(x: (30 + 9 * Double(index)) * unit, y: (40 - 4 * Double(step)) * unit)
                context.draw(Text(sharps ? "♯" : "♭").font(.system(size: CGFloat(13 * unit))).foregroundStyle(ink), at: at)
            }
        }
        .frame(width: width * 1.25, height: 60)
        .accessibilityElement()
        .accessibilityAddTraits(.isImage)
    }
}

/// A small keyboard: the white and black keys from the C below a scale or chord to the B above it, its notes in the accent.
struct MiniKeyboard: View {
    let sound: ReferenceCard.Sound

    private static let black: Set<Int> = [1, 3, 6, 8, 10]

    var body: some View {
        let whites = (sound.low...max(sound.low, sound.high)).filter { !Self.black.contains($0 % 12) }
        Canvas { context, size in
            let width = size.width / CGFloat(max(1, whites.count))
            for (index, pitch) in whites.enumerated() {
                let key = Path(CGRect(x: CGFloat(index) * width, y: 0, width: width, height: size.height))
                context.fill(key, with: .color(Color.ds(sound.marked.contains(pitch) ? "accent" : "paper")))
                context.stroke(key, with: .color(Color.ds("ink-2")), lineWidth: 0.6)
            }
            for index in whites.indices.dropFirst() where whites[index] - whites[index - 1] == 2 {
                let pitch = whites[index] - 1
                let key = Path(CGRect(x: CGFloat(index) * width - width * 0.32, y: 0, width: width * 0.64, height: size.height * 0.62))
                context.fill(key, with: .color(Color.ds(sound.marked.contains(pitch) ? "accent" : "ink")))
            }
        }
        .aspectRatio(CGFloat(max(1, whites.count)) * 14 / 52, contentMode: .fit)
        .accessibilityElement()
        .accessibilityAddTraits(.isImage)
    }
}
