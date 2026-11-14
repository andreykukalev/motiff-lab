import SwiftUI

/// The pen palette under the bar while the Pen is on (ds.css `.pen-palette`), on the score and on a PDF's pages, redrawn as a
/// whole for version 0.5.6 (#533 9; the web app's Score/PenPalette.razor is the same): one card in two rows. The first stays
/// put whatever the tool: the three tools, each its own kit icon, one chosen; Undo and Redo; what the surface adds (the
/// score's layers) and the cross that puts the tools away. Under a hairline the second holds the chosen tool's settings,
/// grouped: colour, then thickness (PenToolSettings). The card keeps one size, so nothing moves under the finger. The
/// Pencil writes with the chosen tool; a double tap on it switches as the iPad's settings say.
struct PenPaletteView<Holder: PenHolder, Extra: View>: View {
    @ObservedObject private var holder: Holder
    private let text: UIText
    private let extra: () -> Extra

    init(holder: Holder, text: UIText, @ViewBuilder extra: @escaping () -> Extra) {
        _holder = ObservedObject(wrappedValue: holder)
        self.text = text
        self.extra = extra
    }

    var body: some View {
        let card = RoundedRectangle(cornerRadius: CGFloat.ds("r-l"))
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            HStack(spacing: CGFloat.ds("s-1")) {
                PenToolChoice(holder: holder, text: text)
                Spacer(minLength: CGFloat.ds("s-2"))
                PenHistory(holder: holder, text: text)
                PaletteDivider()
                extra()
                PenClose(holder: holder, text: text)
            }
            .frame(maxWidth: .infinity)
            Rectangle()
                .fill(Color.ds("hairline"))
                .frame(maxWidth: .infinity, minHeight: 1, maxHeight: 1)
            PenToolSettings(holder: holder, text: text)
        }
        // As wide as its widest row, each row across all of it.
        .fixedSize()
        .padding(.horizontal, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface-2"), in: card)
        .overlay(card.strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.label"])
    }
}

extension PenPaletteView where Extra == EmptyView {
    init(holder: Holder, text: UIText) {
        self.init(holder: holder, text: text) { EmptyView() }
    }
}

/// The score's pen palette: the tools, their settings, Undo and Redo, the layers of marks and the cross.
struct PenPalette: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        PenPaletteView(holder: score, text: text) {
            PenLayersButton(score: score, text: text)
        }
    }
}

/// The pen's palette on a PDF (the score's PenPalette, 0023): the tools, their settings, Undo and Redo and the cross.
struct PdfPenPalette: View {
    @ObservedObject var pdf: PdfController
    let text: UIText

    var body: some View {
        PenPaletteView(holder: pdf, text: text)
    }
}

/// The layers of marks, shown or hidden by kind, from the score's pen palette (0018 section 5).
private struct PenLayersButton: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    @State private var open = false

    var body: some View {
        Button {
            open.toggle()
        } label: {
            Icon(name: "layers", size: CGFloat.ds("icon-m"))
        }
        .accessibilityLabel(text["layers.title"])
        .help(text["layers.title"])
        .buttonStyle(DSIconItemStyle(on: open || !score.hiddenLayers.isDisjoint(with: ScoreController.layers)))
        .popover(isPresented: $open) {
            LayersList(score: score, text: text)
                .padding(CGFloat.ds("s-4"))
                .frame(width: 300, alignment: .leading)
                .presentationCompactAdaptation(.popover)
        }
    }
}
