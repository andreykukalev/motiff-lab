import SwiftUI

/// The inks a highlight is laid in (facade `highlight.add` colour; the names are the project file's): the amber
/// highlighter's first, then the pencil inks. The one list of them, for the marks' shelf (Marks) and the pen's colour bars
/// (Ink, finding 31).
enum HighlightInk: String, CaseIterable, Equatable, Sendable {
    case amber, carmine, forest, ochre

    /// The kit's colour of an ink by its name: the amber highlighter's, or the ink's token ("ink-graphite" too).
    static func color(_ name: String) -> Color {
        name == "amber" ? .ds("amber") : .ds("ink-\(name)")
    }

    var color: Color { Self.color(rawValue) }
}
