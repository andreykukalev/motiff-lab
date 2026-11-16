import UIKit

/// Time asked of iPadOS to finish a save as the app goes to the background (core and iPad review v0.6.5, finding 3): a
/// plain task there may be suspended before the project file is written, and if iPadOS then ends the app, everything done
/// since the piece opened (ink, bookmarks, Check, ideas, the resume point) is lost. The time ends when the work does, or
/// when iPadOS says it is up.
@MainActor
final class BackgroundTime {
    private var id = UIBackgroundTaskIdentifier.invalid

    /// Runs `work` with background time asked for it.
    static func run(_ work: @escaping @MainActor () async -> Void) {
        let time = BackgroundTime()
        time.id = UIApplication.shared.beginBackgroundTask(withName: "motiff.save") {
            MainActor.assumeIsolated { time.end() }
        }
        Task { @MainActor in
            await work()
            time.end()
        }
    }

    private func end() {
        guard id != .invalid else { return }
        UIApplication.shared.endBackgroundTask(id)
        id = .invalid
    }
}
