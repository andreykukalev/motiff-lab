import SwiftUI
import WebKit

/// A piece's score (0018 section 2, design/kit/screens/s2_score_page.png): the score is the whole screen, A4 pages in one
/// column in portrait and two to a row in landscape, with the pencil marks on them (0019 section 2.2). Verovio's SVG is in
/// one web view that runs the web app's score modules (ScoreController, Web/score.js). Nothing is docked: the bar floats
/// over the top with the way back, the title, the pages in view and the actions, and the pill floats at the bottom with
/// play, the tempo, the metronome and the count-in. Colours come from the design kit's tokens, Daylight or Night with
/// the iPad's appearance.
struct ScoreScreen: View {
    let piece: OpenPiece
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.dismiss) private var dismiss
    @StateObject private var player = Player()
    @StateObject private var score = ScoreController()
    @State private var more = false

    var body: some View {
        ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            .ignoresSafeArea(edges: .bottom)
            .background(Color.ds("desk").ignoresSafeArea())
            .overlay(alignment: .top) {
                VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                    ScoreBar(title: piece.title, pages: score.pages, text: text, player: player, score: score, more: $more) { dismiss() }
                    if score.penOn {
                        PenPalette(score: score, text: text)
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.top, CGFloat.ds("s-3"))
                .animation(.easeOut(duration: 0.16), value: score.penOn)
            }
            .overlay(alignment: .bottom) {
                Pill(player: player, text: text)
                    .padding(.bottom, CGFloat.ds("s-4"))
            }
            .toolbar(.hidden, for: .navigationBar)
            .task { await player.load(piece: piece.id) }
            .onDisappear {
                player.shutdown()
                save()
            }
            .onChange(of: scenePhase) { _, phase in
                // The project file is written whenever the app leaves the screen, so nothing waits for a Save button.
                if phase == .background { save() }
            }
    }

    private func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        Task { try? await ProjectFiles.save(piece: id, to: file) }
    }
}

/// The bar over the score (ds.css `.bar`): back to the library, the title and the pages in view, then Pen, Play and More.
/// The actions the iPad does not have yet (phrases, the piano roll, Insights) come with their own steps.
struct ScoreBar: View {
    let title: String
    let pages: ScoreController.PagesInView
    let text: UIText
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @Binding var more: Bool
    let back: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: back) {
                Icon(name: "back", size: CGFloat.ds("icon-l"))
                    .frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["nav.library"])
            Text(title)
                .font(DesignTokens.font(display: true, weight: 500, size: 17, relativeTo: .headline))
                .foregroundStyle(Color.ds("ink"))
                .lineLimit(1)
                .accessibilityAddTraits(.isHeader)
            if let pageText {
                Text(pageText)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .padding(.leading, CGFloat.ds("s-2"))
            }
            Spacer(minLength: CGFloat.ds("s-2"))
            Button {
                score.penOn.toggle()
            } label: {
                Icon(name: "pen", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: score.penOn))
            .accessibilityLabel(text["bar.pen"])
            .accessibilityAddTraits(score.penOn ? .isSelected : [])
            Button {
                player.toggle()
            } label: {
                Icon(name: player.state == .stopped ? "play" : "pause", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .disabled(!player.ready)
            .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])
            Button {
                more.toggle()
            } label: {
                Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: more))
            .accessibilityLabel(text["bar.more"])
            .popover(isPresented: $more) { MorePanel(text: text, problem: player.problem ?? score.problem) }
        }
        .padding(.leading, CGFloat.ds("s-2"))
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }

    /// "p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; nothing before the score is laid out.
    private var pageText: String? {
        guard pages.count > 0 else { return nil }
        return pages.first == pages.last
            ? text.format("bar.page", String(pages.first), String(pages.count))
            : text.format("bar.pages", String(pages.first), String(pages.last), String(pages.count))
    }
}

/// An action in the bar (ds.css `.bar .act`): the ink, and the accent on its tint while it is on.
struct BarActionStyle: ButtonStyle {
    var on = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(on ? Color.ds("accent") : Color.ds("ink"))
            .background(on || configuration.isPressed ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Rectangle())
    }
}

/// The pill (ds.css `.pill`): play, the tempo, the metronome and the count-in, floating at the bottom of the score.
struct Pill: View {
    @ObservedObject var player: Player
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                player.toggle()
            } label: {
                Icon(name: player.state == .stopped ? "playf" : "pause")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])
            Menu {
                Picker(text["transport.tempo"], selection: $player.tempoPercent) {
                    ForEach(Player.tempoChoices, id: \.self) { Text("\($0)%").tag($0) }
                }
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "tempo")
                    Text("\(player.tempoPercent)%").monospacedDigit()
                }
            }
            .menuStyle(.button)
            .buttonStyle(DSItemStyle(on: player.tempoPercent != 100))
            .accessibilityLabel(text.format("pill.tempoLabel", String(player.tempoPercent)))
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                player.metronome.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "metronome")
                    Text(text["transport.metronome"])
                }
            }
            .buttonStyle(DSItemStyle(on: player.metronome))
            .accessibilityAddTraits(player.metronome ? .isSelected : [])
            Button {
                player.countIn.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "count")
                    Text(text["transport.countIn"])
                }
            }
            .buttonStyle(DSItemStyle(on: player.countIn))
            .accessibilityAddTraits(player.countIn ? .isSelected : [])
        }
        .disabled(!player.ready)
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }
}

/// The pen palette under the bar while the Pen is on (ds.css `.pen-palette`): the four inks, the highlighter, the eraser
/// and Undo. The Pencil writes with the chosen one; a double tap on it switches as the iPad's settings say.
struct PenPalette: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                let chosen = score.tool == .pen(ink)
                Button {
                    score.tool = .pen(ink)
                } label: {
                    Circle()
                        .fill(Color(uiColor: ink.color))
                        .frame(width: 20, height: 20)
                        .padding(2)
                        .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: chosen ? 2 : 1))
                        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text[ink.labelKey])
                .accessibilityAddTraits(chosen ? .isSelected : [])
            }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                score.tool = .highlighter
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "highlight", size: CGFloat.ds("icon-s"))
                    Text(text["pencil.highlighter"])
                }
            }
            .buttonStyle(DSItemStyle(on: score.tool == .highlighter))
            .accessibilityAddTraits(score.tool == .highlighter ? .isSelected : [])
            Button(text["pencil.eraser"]) {
                score.tool = .eraser
            }
            .buttonStyle(DSItemStyle(on: score.tool == .eraser))
            .accessibilityAddTraits(score.tool == .eraser ? .isSelected : [])
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                score.undo()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "undo", size: CGFloat.ds("icon-s"))
                    Text(text["pencil.undo"])
                }
            }
            .buttonStyle(DSItemStyle())
            .disabled(!score.canUndo)
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface-2"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.label"])
    }
}

/// More, from the bar: for now what the app owes the piano and the engraving (CC BY 3.0 asks for the piano's credit
/// wherever it plays), and what went wrong, if anything did. The layout, the PDF and the other tools come with later steps.
struct MorePanel: View {
    let text: UIText
    let problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
            }
            Text(credit)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 340, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private var credit: AttributedString {
        let markdown = "\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"]) \(text["credit.engraving"]) [Verovio](https://www.verovio.org) 6.3.0, [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html), [\(text["credit.source"])](https://github.com/rism-digital/verovio/tree/version-6.3.0)."
        return (try? AttributedString(markdown: markdown)) ?? AttributedString(markdown)
    }
}

struct ScoreWebView: UIViewRepresentable {
    @ObservedObject var score: ScoreController
    let piece: OpenPiece
    let dark: Bool

    /// A web view made at launch, so the first score does not wait for WebKit's process to start (0019).
    @MainActor private static var warm: WKWebView?

    @MainActor static func prewarm() {
        guard warm == nil else { return }
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.loadHTMLString("<!doctype html><title></title>", baseURL: nil)
        warm = view
    }

    func makeUIView(context: Context) -> WKWebView {
        score.webView
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        score.show(piece, dark: dark)
    }
}
