import SwiftUI
import WebKit

/// A piece's score on A4 pages, one under another (0017, 0018 section 2): Verovio's SVG in one web view, as in the web
/// app, so the same score modules can draw the overlays later (0019). The page colours come from the design kit's
/// tokens (design/kit/tokens.css, copied into the app), in Daylight or Night stand with the iPad's appearance.
struct ScoreScreen: View {
    let piece: OpenPiece
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        ScoreWebView(pages: piece.pages, dark: colorScheme == .dark)
            .ignoresSafeArea(edges: .bottom)
            .navigationTitle(piece.title)
            .navigationBarTitleDisplayMode(.inline)
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
