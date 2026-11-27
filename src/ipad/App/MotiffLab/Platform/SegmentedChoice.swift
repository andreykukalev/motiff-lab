import SwiftUI

/// A choice of two or three (ds.css `.seg`): the chosen one on the accent's strong tint. Each option keeps the width of
/// its word in the chosen weight, so the control does not change size when the choice moves (#479 item 11).
struct SegmentedChoice<Value: Hashable>: View {
    let options: [(Value, String)]
    let chosen: Value
    let label: String
    let choose: (Value) -> Void

    var body: some View {
        HStack(spacing: 0) {
            ForEach(options.indices, id: \.self) { index in
                let value = options[index].0
                let name = options[index].1
                let on = value == chosen
                Button {
                    choose(value)
                } label: {
                    // The word in the chosen weight holds the option's width either way.
                    SteadyWord(text: name, on: on)
                        .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                        .modifier(SegmentFill(on: on))
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .modifier(SegmentedCapsule())
        .accessibilityLabel(label)
    }
}

/// An option of a segmented choice (ds.css `.seg button`): the chosen one on the accent's strong tint.
struct SegmentFill: ViewModifier {
    let on: Bool

    func body(content: Content) -> some View {
        content
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(maxHeight: .infinity)
            .background(on ? Color.ds("accent-tint-strong") : .clear, in: Capsule())
            .contentShape(Capsule())
    }
}

/// The capsule a segmented choice sits in (ds.css `.seg`), at the width of its options.
struct SegmentedCapsule: ViewModifier {
    func body(content: Content) -> some View {
        content
            .padding(2)
            .frame(height: CGFloat.ds("h-compact"))
            .background(Color.ds("surface"), in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline")))
            .fixedSize()
            .accessibilityElement(children: .contain)
    }
}
