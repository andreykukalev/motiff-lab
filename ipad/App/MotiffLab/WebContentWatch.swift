import WebKit

/// Loads a web view's page again when iPadOS ends its WebContent process (core and iPad review v0.6.5, finding 5): under
/// memory pressure, with a large score's SVG beside a PDF, the process may go; the view then stays blank and every call
/// into it fails quietly. The page loads again, says it is ready, and its controller draws it again from what it holds.
/// The web view keeps its navigation delegate weakly, so the controller keeps this.
final class WebContentWatch: NSObject, WKNavigationDelegate {
    private let ended: @MainActor () -> Void

    init(ended: @escaping @MainActor () -> Void) {
        self.ended = ended
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        MainActor.assumeIsolated {
            ended()
            webView.reload()
        }
    }
}
