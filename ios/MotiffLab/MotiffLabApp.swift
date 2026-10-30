// Motiff Lab for iPad (decision record 0015): a native shell around the Motiff Lab web app. The web
// app does all the music; the shell adds what a browser tab cannot: an app on the home screen, sound
// with the silent switch on, a screen that stays on at the piano, the Apple Pencil's double tap and
// squeeze, and saving exported files to Files.

import SwiftUI

@main
struct MotiffLabApp: App {
    var body: some Scene {
        WindowGroup {
            WorkspaceView()
                .background(Color("Charcoal").ignoresSafeArea())
                .preferredColorScheme(.dark)
        }
    }
}

/// The web app, full screen within the safe area.
struct WorkspaceView: UIViewControllerRepresentable {
    func makeUIViewController(context: Context) -> WebViewController {
        WebViewController()
    }

    func updateUIViewController(_ controller: WebViewController, context: Context) {}
}
