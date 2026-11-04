import SwiftUI

/// Motiff Lab for the iPad (decision record 0019): SwiftUI over the one C# music-core. This is the foundation the
/// redesign's screens are built on (0018): open a piece and see its score on A4 pages.
@main
struct MotiffLabApp: App {
    var body: some Scene {
        WindowGroup {
            LibraryView()
                // The first web view starts its process in about 2.5 s, so it is started with the app (0019).
                .onAppear { ScoreWebView.prewarm() }
        }
    }
}
