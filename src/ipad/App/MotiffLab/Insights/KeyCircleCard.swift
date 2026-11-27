import SwiftUI

/// The circle in a fact's row (the web app's KeyCircleCard): the major keys outside and their relative minors inside, the
/// key in cobalt (dashed when inferred), the key that fits almost as well outlined, the chords as numbered dots; then its
/// lines, the chords and the notes outside the scale as chips that ring on the score, and Hear the scale.
struct KeyCircleCard: View {
    let circle: KeyCircle
    let fact: Fact
    @ObservedObject var insights: InsightsController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            CircleDrawing(circle: circle, chosen: insights.chosen?.custom)
                .frame(maxWidth: 288)
                .aspectRatio(1, contentMode: .fit)
                .frame(maxWidth: .infinity)
                .accessibilityElement()
                .accessibilityLabel(text.format("circle.aria", circle.name))
                .accessibilityAddTraits(.isImage)
            if let note = circle.runnerUpNote {
                caption(note)
            }
            ForEach(circle.lines, id: \.self) { caption($0) }
            if !circle.chords.isEmpty || circle.outside != nil {
                FlowLayout(spacing: CGFloat.ds("s-2")) {
                    ForEach(circle.chords, id: \.number) { chord in
                        chip("\(chord.number). \(chord.symbol)\(chord.degree.map { " (\($0))" } ?? "")", place: chord.place)
                    }
                    if let outside = circle.outside {
                        chip(outside.label, place: outside)
                            .accessibilityHint(text["circle.showHint"])
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel(text["circle.show"])
            }
            Button {
                insights.hearScale()
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: insights.scaleHeard ? "pause" : "play")
                    Text(text[insights.scaleHeard ? "circle.stopScale" : "circle.hearScale"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityAddTraits(insights.scaleHeard ? .isSelected : [])
        }
        .padding(CGFloat.ds("s-3"))
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["circle.title"])
    }

    private func chip(_ label: String, place: Fact.Place) -> some View {
        let on = insights.chosen?.custom == place
        return Button(label) { insights.chooseCirclePlace(place, of: fact) }
            .buttonStyle(DSChipStyle(on: on))
            .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
    }
}
