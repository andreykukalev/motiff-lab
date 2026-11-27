import SwiftUI

// The items of the pills and the selection menu: an action as its icon alone, ‹ or ›, and the line between groups.

/// ‹ or › of a pill: the phrase's (`Pill`) or a draft's приём's (`DraftAssemblyPill`), its word for VoiceOver.
@MainActor func stepItem(_ icon: String, _ label: String, enabled: Bool = true, action: @escaping () -> Void) -> some View {
    Button(action: action) {
        Icon(name: icon).frame(width: 32, height: 32)
    }
    .buttonStyle(DSItemStyle())
    .disabled(!enabled)
    .accessibilityLabel(label)
}

/// The thin line between groups of a pill's or the selection menu's items.
struct ItemDivider: View {
    var body: some View {
        Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
    }
}

/// An action of the selection menu or the pill as its icon alone (#495 UX-11): the word for VoiceOver and the pointer's tip.
@MainActor func iconItem(_ icon: String, _ label: String, on: Bool, action: @escaping () -> Void) -> some View {
    Button(action: action) {
        Icon(name: icon)
    }
    .buttonStyle(DSIconItemStyle(on: on))
    .accessibilityLabel(label)
    .accessibilityAddTraits(on ? .isSelected : [])
    .help(label)
}
