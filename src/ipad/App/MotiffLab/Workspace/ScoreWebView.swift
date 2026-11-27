import SwiftUI
import WebKit

/// The score's page: the controller's one web view (ScoreController, Web/score.html), under everything over the score.
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
        // Out of SwiftUI's update (#675): showing a new piece opens the selection's, the sources' and the focus's models,
        // whose published values may not change while SwiftUI updates the view; the next turn of the main queue shows it.
        let score = self.score, piece = self.piece, dark = self.dark
        DispatchQueue.main.async { score.show(piece, dark: dark) }
        // The stylus menu opens where the Pencil is over any part of the screen, the roll's too (#521 7).
        score.watchStylus(in: view.window)
    }
}
