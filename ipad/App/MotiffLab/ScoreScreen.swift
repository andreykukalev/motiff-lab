import SwiftUI
import WebKit

/// A piece's score on A4 pages, one under another (0017, 0018 section 2), with the pencil marks on it (0019 section
/// 2.2): Verovio's SVG in one web view that runs the web app's score modules (ScoreController, Web/score.js). The page
/// colours come from the design kit's tokens, in Daylight or Night stand with the iPad's appearance.
struct ScoreScreen: View {
    let piece: OpenPiece
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var player = Player()
    @StateObject private var score = ScoreController()

    var body: some View {
        ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            .ignoresSafeArea(edges: .bottom)
            .safeAreaInset(edge: .bottom) { TransportBar(player: player, text: text) }
            .navigationTitle(piece.title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItemGroup(placement: .topBarTrailing) { PencilTools(score: score, text: text) }
            }
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

/// The Pencil's tools, as in the web app (pencil.*): the four inks, the highlighter, the eraser, and Undo. The Pencil
/// writes with the chosen one; a double tap on it switches as the iPad's settings say.
struct PencilTools: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: 14) {
            ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                Button {
                    score.tool = .pen(ink)
                } label: {
                    Image(systemName: score.tool == .pen(ink) ? "largecircle.fill.circle" : "circle.fill")
                        .foregroundStyle(Color(uiColor: ink.color))
                }
                .accessibilityLabel(text[ink.labelKey])
                .accessibilityAddTraits(score.tool == .pen(ink) ? .isSelected : [])
            }
            tool(.highlighter, symbol: "highlighter", label: "pencil.highlighter")
            tool(.eraser, symbol: "eraser", label: "pencil.eraser")
            Button {
                score.undo()
            } label: {
                Image(systemName: "arrow.uturn.backward")
            }
            .disabled(!score.canUndo)
            .accessibilityLabel(text["pencil.undo"])
        }
    }

    private func tool(_ tool: ScoreController.Tool, symbol: String, label: String) -> some View {
        Button {
            score.tool = tool
        } label: {
            Image(systemName: symbol)
                .padding(4)
                .background(score.tool == tool ? Color.accentColor.opacity(0.2) : .clear, in: Circle())
        }
        .accessibilityLabel(text[label])
        .accessibilityAddTraits(score.tool == tool ? .isSelected : [])
    }
}

/// Play and pause, back to the start, the tempo and the metronome, with the piano's credit (tools/piano: CC BY 3.0
/// asks for it wherever the piano plays). The words are the web app's (transport.*, credit.*).
struct TransportBar: View {
    @ObservedObject var player: Player
    let text: UIText

    var body: some View {
        VStack(spacing: 6) {
            HStack(spacing: 20) {
                Button {
                    player.rewind()
                } label: {
                    Image(systemName: "backward.end.fill")
                }
                .accessibilityLabel(text["transport.stop"])

                Button {
                    player.toggle()
                } label: {
                    Image(systemName: player.state == .stopped ? "play.fill" : "pause.fill")
                        .font(.title2)
                        .frame(width: 44, height: 44)
                }
                .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])

                Menu {
                    Picker(text["transport.tempo"], selection: $player.tempoPercent) {
                        ForEach(Player.tempoChoices, id: \.self) { Text("\($0)%").tag($0) }
                    }
                } label: {
                    Label("\(player.tempoPercent)%", systemImage: "metronome")
                }
                .accessibilityLabel(text["transport.tempo"])

                Toggle(isOn: $player.metronome) {
                    Image(systemName: "metronome.fill")
                }
                .toggleStyle(.button)
                .accessibilityLabel(text["transport.metronome"])
            }
            .disabled(!player.ready)

            if let problem = player.problem {
                Text(problem).font(.footnote).foregroundStyle(.red)
            }
            Text("\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"])")
                .font(.caption2)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(.horizontal)
        .padding(.vertical, 8)
        .frame(maxWidth: .infinity)
        .background(.bar)
    }
}
