import SwiftUI
import UIKit

/// Motiff Lab for the iPad (decision record 0019): SwiftUI over the one C# music-core. This is the foundation the
/// redesign's screens are built on (0018): open a piece and see its score on A4 pages.
@main
struct MotiffLabApp: App {
    var body: some Scene {
        WindowGroup {
            LibraryView()
                // The first web view starts its process in about 2.5 s, so it is started with the app (0019).
                .onAppear {
                    ScoreWebView.prewarm()
                    // The screen stays on while the app is open: a player reads the score with their hands on the keys (0019).
                    UIApplication.shared.isIdleTimerDisabled = true
                }
        }
    }
}
