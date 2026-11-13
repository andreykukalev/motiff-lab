import SwiftUI

/// The pen palette under the bar while the Pen is on (ds.css `.pen-palette`), on the score and on a PDF's pages, in two
/// groups (#495 UX-06): a row with the tools (one chosen) and what acts on the marks (Undo, what the surface adds, the
/// cross that puts the tools away), and under it the active tool's own settings, so a thickness or an ink reads as the
/// tool's and never as a tool. The first row keeps its size whatever the tool, so the tools stay under the finger. The
/// Pencil writes with the chosen tool; a double tap on it switches as the iPad's settings say.
struct PenPaletteView<Holder: PenHolder, Extra: View>: View {
    @ObservedObject private var holder: Holder
    private let text: UIText
    private let extra: (_ compact: Bool) -> Extra

    init(holder: Holder, text: UIText, @ViewBuilder extra: @escaping (_ compact: Bool) -> Extra) {
        _holder = ObservedObject(wrappedValue: holder)
        self.text = text
        self.extra = extra
    }

    var body: some View {
        VStack(alignment: .trailing, spacing: CGFloat.ds("s-1")) {
            // The whole row where it fits; in portrait its words give way to the icons alone.
            ViewThatFits(in: .horizontal) {
                row(compact: false)
                row(compact: true)
            }
            PenToolSettings(holder: holder, text: text)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.label"])
    }

    private func row(compact: Bool) -> some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            PenToolChoice(holder: holder, text: text, compact: compact)
            PaletteDivider()
            Button {
                holder.undo()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "undo", size: CGFloat.ds("icon-s"))
                    if !compact { Text(text["pencil.undo"]) }
                }
            }
            .buttonStyle(DSItemStyle())
            .accessibilityLabel(text["pencil.undo"])
            .disabled(!holder.canUndo)
            extra(compact)
            PenClose(holder: holder, text: text)
        }
        .modifier(PaletteSurface())
    }
}

extension PenPaletteView where Extra == EmptyView {
    init(holder: Holder, text: UIText) {
        self.init(holder: holder, text: text) { _ in EmptyView() }
    }
}

/// The score's pen palette: the tools, their settings, Undo, the layers of marks and the cross.
struct PenPalette: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        PenPaletteView(holder: score, text: text) { compact in
            PenLayersButton(score: score, text: text, compact: compact)
        }
    }
}

/// The pen's palette on a PDF (the score's PenPalette, 0023): the tools, their settings, Undo and the cross.
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
    let compact: Bool
    @State private var open = false

    var body: some View {
        Button {
            open.toggle()
        } label: {
            HStack(spacing: 6) {
                Icon(name: "layers", size: CGFloat.ds("icon-s"))
                if !compact { Text(text["layers.title"]) }
            }
        }
        .accessibilityLabel(text["layers.title"])
        .buttonStyle(DSItemStyle(on: open || !score.hiddenLayers.isDisjoint(with: ScoreController.layers)))
        .popover(isPresented: $open) {
            LayersList(score: score, text: text)
                .padding(CGFloat.ds("s-4"))
                .frame(width: 300, alignment: .leading)
                .presentationCompactAdaptation(.popover)
        }
    }
}
