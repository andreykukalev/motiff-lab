import Foundation

/// What was just done in the Sources sheet, with Undo where it can be undone (sources sheet, 0.6).
struct SourcesToast: Identifiable, Equatable {
    enum Undo: Equatable {
        /// The project file as it was just before: written back, and the piece opened again from it.
        case restore(String)
        /// The last step of this draft undone (`drafts.undo`, v1.1): a приём, a hand or a span taken or left, a seam's offer
        /// taken or the seam left, or, while the piece is open, a change of its places (a new base, a variant put or moved).
        case draftStep(String)
    }

    let id = UUID()
    var text: String
    var undo: Undo?
}

/// What passes from one screen of a piece to the next when the piece is opened again (version 0.6): the toast of what
/// was done, with its Undo, or a view of the switch to show, by the project file. Sources itself opens where it is asked
/// for, the PDF included (#582 item 4).
@MainActor
enum SourcesHandover {
    private static var toasts: [URL: SourcesToast] = [:]
    private static var views: [URL: String] = [:]

    static func give(_ toast: SourcesToast, to file: URL) { toasts[file.standardizedFileURL] = toast }

    static func toast(for file: URL?) -> SourcesToast? {
        guard let file else { return nil }
        return toasts.removeValue(forKey: file.standardizedFileURL)
    }

    static func show(_ view: String, on file: URL) { views[file.standardizedFileURL] = view }

    static func view(for file: URL?) -> String? {
        guard let file else { return nil }
        return views.removeValue(forKey: file.standardizedFileURL)
    }

    /// The switch of all views as the screen handing over had it (v0.6.4 spec section 13): the next screen of the same
    /// file draws it from its first frame, instead of PDF | Notes or no switch until it has read its own. Read as often as
    /// the screen is made, and dropped once that screen has read the sources itself.
    private static var layers: [URL: SourceLayers] = [:]

    static func give(_ shown: SourceLayers, to file: URL?) {
        guard let file, shown.working != nil else { return }
        layers[file.standardizedFileURL] = shown
    }

    static func layers(for file: URL?) -> SourceLayers? {
        file.flatMap { layers[$0.standardizedFileURL] }
    }

    static func drop(_ file: URL?) {
        if let file { layers[file.standardizedFileURL] = nil }
    }

    /// The recording heard over the views as the screen hands over (v0.10, finding 24), by the project file of the next
    /// screen: it opens there again and its sound goes on. Read once.
    private static var performances: [URL: String] = [:]

    static func give(performance id: String, to file: URL) { performances[file.standardizedFileURL] = id }

    static func performance(for file: URL?) -> String? {
        guard let file else { return nil }
        return performances.removeValue(forKey: file.standardizedFileURL)
    }
}
