import SwiftUI

/// The bar over the score (ds.css `.bar`): back to the library, the title, which opens the Contents the way a reader opens
/// a book at its contents, and the pages in view, then Phrases, Pen, the piano roll, Insights, Play and More, in the web
/// app's order. It shows what it is handed and calls the actions it is handed; where it floats is the screen's
/// (`ScoreScreenContent.bars`).
struct ScoreBar: View {
    let title: String
    let pages: ScoreController.PagesInView
    let text: UIText
    /// Handed on to the modes, Practice, the layers list and More.
    @ObservedObject var score: ScoreController
    /// Extra information's button shows whether it is on.
    @ObservedObject var marksModel: MarksModel
    /// Handed on to Practice, which is off in Expand.
    let focusModel: FocusModel
    /// Whether the piece has other versions: the layers list offers their marks (MarksModel.pieceExtra).
    let hasVersions: Bool
    /// Which panels are open (PanelState), and the screen's way to change them.
    let panels: PanelState
    let change: (PanelState.Intent) -> Void
    /// PDF | Notes (0023): goes to the PDF the notes were read from; nil for notes with none.
    let pdf: (() -> Void)?
    /// Piece DNA from More.
    let openDna: () -> Void
    /// The Reference's circle of fifths; nil where there is none.
    var openReference: (() -> Void)?
    /// What went wrong, if anything did, which More says (the player's, else the score's).
    let problem: String?
    /// More's question about the whole piece: the assistant's panel opens on it (#582 item 17.1).
    let askAboutPiece: () -> Void
    /// The piece's views for the switch of all views (version 0.6, 0030), in PDF | Notes' place with
    /// Sources after it.
    var sourceLayers: SourceLayers? = nil
    var currentView: String? = nil
    var pickView: ((SourceLayers.ViewEntry) -> Void)? = nil
    /// The read-only tag of a view shown over the piece; nil for the piece itself.
    var viewing: String? = nil
    /// The view's note: how many bars the app wrote where the source's record did not fit (`SourcesModel.Engraved.note`),
    /// the first thing in the bar to give way (TopBar).
    var viewNote: String? = nil
    /// The bar's width, the screen's less its margins (TopBar).
    var width: CGFloat = 0
    let back: () -> Void
    /// The list of extra information under the ⓘ button's chevron.
    @State private var extraOpen = false

    /// The bar is laid out as the PDF's (TopBar, #479 item 13, #582 item 2): Back, the Contents, the title and the switch
    /// in the same places in both views, then the pages in view, and each view's own actions at the right.
    var body: some View {
        TopBar(width: width, pages: pageText, note: viewNote) {
            BarLeading(title: title, contentsOpen: panels.binding(.contents, change: change), text: text, back: back)
            if let sourceLayers, let pickView {
                SourcesViewSwitch(layers: sourceLayers, current: currentView, text: text, pick: pickView) { change(.open(.versions)) }
                SourcesButton(open: panels[.versions], text: text) { change(.toggle(.versions)) }
            } else if let pdf {
                PdfNotesSwitch(onPdf: false, text: text) { pdf() }
            }
            if let viewing { Tag(text: viewing, kind: .assumed) }
        } actions: {
            actions(compact: false)
        } compact: {
            actions(compact: true)
        }
    }

    /// In groups without captions, as the approved mockup v0.5.2 (#479 item 21): what the score shows (Phrases, the extra
    /// information), then how the score is used (the modes), then the Circle and
    /// Insights, then Play and More. Compact where the bar is short (#582 item 2): the chosen mode by its icon alone.
    @ViewBuilder private func actions(compact: Bool) -> some View {
        viewButtons
        PaletteDivider()
        // The modes (version 0.5): one group for the score and the roll, the Pen in it.
        ModeGroup(score: score, penModel: score.penModel, text: text, compact: compact)
        PaletteDivider()
        if let openReference {
            // The Reference: the Circle, the circle with a sharp, opens the circle of fifths.
            Button(action: openReference) {
                Icon(name: "keysig", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["reference.open"])
            .accessibilityHint(text["reference.openHint"])
        }
        Button {
            change(.toggle(.insights))
        } label: {
            Icon(name: "insight", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: panels[.insights]))
        .accessibilityLabel(text["bar.insights"])
        .accessibilityAddTraits(panels[.insights] ? .isSelected : [])
        PracticeButton(score: score, focusModel: focusModel, text: text)
        PaletteDivider()
        playAndMore
    }

    /// What the score shows: the extra information and its list, with the phrases' marks first; the piano roll is the
    /// pill's alone (Andrei's 0.8 test, finding 7, answer 1).
    @ViewBuilder private var viewButtons: some View {
        // Extra information (testing item 33): one press shows or hides what the app adds over the notes; the chevron
        // lists each kind and the player's own marks.
        HStack(spacing: 0) {
            let on = !marksModel.hiddenLayers.contains("extra")
            Button {
                score.toggleExtra()
            } label: {
                Icon(name: "info", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: on))
            .accessibilityLabel(text["extra.toggle"])
            .accessibilityHint(text["extra.hint"])
            .accessibilityAddTraits(on ? .isSelected : [])
            Button {
                extraOpen.toggle()
            } label: {
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).frame(width: 20, height: 40)
            }
            .buttonStyle(BarActionStyle(on: extraOpen))
            .accessibilityLabel(text["extra.list"])
            .popover(isPresented: $extraOpen) {
                LayersList(score: score, marksModel: marksModel, checkModel: score.checkModel, text: text, hasVersions: hasVersions)
                    .padding(CGFloat.ds("s-4"))
                    .frame(width: 300, alignment: .leading)
                    .presentationCompactAdaptation(.popover)
            }
        }
    }

    /// More: the pill, always shown, plays (the playback model, v0.5.2, #479 item 22).
    @ViewBuilder private var playAndMore: some View {
        Button {
            change(.toggle(.more))
        } label: {
            Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: panels[.more]))
        .accessibilityLabel(text["bar.more"])
        .popover(isPresented: panels.binding(.more, change: change)) {
            MorePanel(score: score, checkModel: score.checkModel, hasVersions: hasVersions, text: text, problem: problem, openCheck: {
                change(.openFromMore(.check))
            }, openVersions: viewing == nil ? openSources : nil, openSettings: {
                change(.openFromMore(.settings))
            }, openDna: {
                change(.close(.more))
                openDna()
            }, askAboutPiece: {
                change(.close(.more))
                askAboutPiece()
            })
        }
    }

    /// Sources from More.
    private func openSources() {
        change(.openFromMore(.versions))
    }

    /// "p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; nothing before the score is laid out.
    private var pageText: String? {
        guard pages.count > 0 else { return nil }
        return pages.first == pages.last
            ? text.format("bar.page", String(pages.first), String(pages.count))
            : text.format("bar.pages", String(pages.first), String(pages.last), String(pages.count))
    }
}
