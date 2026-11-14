import MSAL
import SwiftUI
import UIKit

/// Motiff Lab for the iPad (decision record 0019): SwiftUI over the one C# music-core. This is the foundation the
/// redesign's screens are built on (0018): open a piece and see its score on A4 pages.
@main
struct MotiffLabApp: App {
    @StateObject private var account = Account()
    /// The player's settings on this iPad (0018 section 10): the score and the roll follow them, and so does the appearance.
    @StateObject private var settings = DeviceSettings.shared
    @Environment(\.scenePhase) private var scenePhase

    init() {
        // The design kit's faces, before the first screen draws (0018 section 13).
        DesignTokens.registerFonts()
    }

    var body: some Scene {
        WindowGroup {
            LibraryView()
                .environmentObject(account)
                .environmentObject(settings)
                .preferredColorScheme(settings.colorScheme)
                .task { await settings.load() }
                // The player signed in last time, without asking anything (0014).
                .task { await account.restore() }
                // Back in the foreground: a plan or profile the API (scaled to zero) did not give yet is asked again.
                .onChange(of: scenePhase) { _, phase in
                    if phase == .active { Task { await account.refreshIfMissing() } }
                }
                // Sign-in comes back on msauth.<bundle id>://auth.
                .onOpenURL { url in _ = MSALPublicClientApplication.handleMSALResponse(url, sourceApplication: nil) }
                // The first web view starts its process in about 2.5 s, so it is started with the app (0019).
                .onAppear {
                    ScoreWebView.prewarm()
                    // The screen stays on while the app is open: a player reads the score with their hands on the keys (0019).
                    UIApplication.shared.isIdleTimerDisabled = true
                }
        }
    }
}
