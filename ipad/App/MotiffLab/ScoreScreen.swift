import SwiftUI
import WebKit

/// A piece's score on A4 pages, one under another (0017, 0018 section 2): Verovio's SVG in one web view, as in the web
/// app, so the same score modules can draw the overlays later (0019). The page colours come from the design kit's
/// tokens (design/kit/tokens.css, copied into the app), in Daylight or Night stand with the iPad's appearance.
struct ScoreScreen: View {
    let piece: OpenPiece
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @StateObject private var player = Player()

    var body: some View {
        ScoreWebView(pages: piece.pages, dark: colorScheme == .dark)
            .ignoresSafeArea(edges: .bottom)
            .safeAreaInset(edge: .bottom) { TransportBar(player: player, text: text) }
            .navigationTitle(piece.title)
            .navigationBarTitleDisplayMode(.inline)
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

struct ScoreWebView: UIViewRepresentable {
    let pages: [String]
    let dark: Bool

    /// The web view made at launch, so the first score does not wait for WebKit's process (0019).
    @MainActor private static var warm: WKWebView?

    @MainActor static func prewarm() {
        guard warm == nil else { return }
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.loadHTMLString("<!doctype html><title></title>", baseURL: nil)
        warm = view
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIView(context: Context) -> WKWebView {
        let view = Self.warm ?? WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        Self.warm = nil
        view.isOpaque = false
        view.backgroundColor = .clear
        return view
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        let coordinator = context.coordinator
        // SwiftUI calls this on every change; load the pages only when they or the theme are new.
        guard coordinator.loaded != pages || coordinator.dark != dark else { return }
        coordinator.loaded = pages
        coordinator.dark = dark
        let body = pages.map { "<div class=\"page\">\($0)</div>" }.joined()
        let html = """
        <!doctype html><html\(dark ? " data-theme=\"dark\"" : "")><head>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <link rel="stylesheet" href="tokens.css">
        <style>
          body { margin: 0; padding: var(--s-4); background: var(--desk); }
          .page { background: var(--paper); color: var(--notation); margin: 0 auto var(--s-4);
                  box-shadow: var(--shadow-paper); }
          .page svg { display: block; width: 100%; height: auto; }
        </style></head><body>\(body)</body></html>
        """
        view.loadHTMLString(html, baseURL: Bundle.main.resourceURL)
    }

    final class Coordinator {
        var loaded: [String] = []
        var dark = false
    }
}
