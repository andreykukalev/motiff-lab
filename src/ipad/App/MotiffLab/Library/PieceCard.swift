import SwiftUI

/// A session on the Sessions tab (ds.css `.card`, lib-3): the piece's page, title, composer and what kind of piece it is,
/// the ideas kept and bars to check, and where work on it stopped, with Resume. A tap on the card resumes it too; a long
/// press offers its properties and Delete, which asks once more.
struct PieceCard: View {
    let entry: LibraryEntry
    let summary: PieceSummary?
    let text: UIText
    let open: () -> Void
    let edit: () -> Void
    let delete: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-4")) {
                Button(action: open) {
                    RoundedRectangle(cornerRadius: 3)
                        .fill(Color.ds("paper"))
                        .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(Color.ds("hairline")))
                        .frame(width: 84, height: 118)
                        .shadow(color: Color.ds("hairline"), radius: 1, y: 1)
                }
                .buttonStyle(.plain)
                .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                    Text(entry.title)
                        .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    Text(meta)
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                    SourceIcons(sources: entry.sources, text: text)
                    if let away = entry.originalAway {
                        OriginalPlaceLabel(label: away.text(text), badge: away.badge)
                    }
                    if let summary, summary.ideas > 0 || summary.checkBars > 0 {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            if summary.ideas > 0 { Tag(text: text.count("library.ideas", summary.ideas), kind: .saved) }
                            if summary.checkBars > 0 { Tag(text: text.count("library.check", summary.checkBars), kind: .warn) }
                        }
                    }
                }
                Spacer(minLength: 0)
            }
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            if let resumeName {
                Text(text["library.recentWork"]).overline()
                Text(resumeName)
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink"))
                Text(text.format("home.lastOpened", entry.modified.formatted(.relative(presentation: .named).locale(Locale(identifier: text.language)))))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                Button(action: open) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.resume"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary))
                .accessibilityLabel(text.format("library.resumeLabel", entry.title))
            } else {
                // A piece not opened yet has no work to come back to: one Open, as on the web.
                Button(text["home.open"], action: open)
                    .buttonStyle(DSButtonStyle(kind: .secondary))
                    .accessibilityLabel(text.format("library.openLabel", entry.title))
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .contentShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .onTapGesture(perform: open)
        .modifier(PieceActions(text: text, edit: edit, delete: delete))
    }

    /// The composer, and what kind of piece: scanned, from a recording or a PDF, or a file the player opened when no
    /// composer is named.
    private var meta: String {
        guard let summary else { return entry.composer ?? " " }
        let kind = Self.kind(summary, text)
        return [entry.composer, kind ?? (entry.composer == nil ? text["library.openFile"] : nil)].compactMap { $0 }.joined(separator: " · ")
    }

    /// What kind of piece, when it is not a file the player opened: scanned, from a recording or a PDF.
    static func kind(_ summary: PieceSummary?, _ text: UIText) -> String? {
        guard let summary else { return nil }
        if !summary.hasNotes { return text["library.pdf"] }
        return switch summary.origin {
        case "scanned": text["library.scanned"]
        case "transcribed": text["library.recorded"]
        default: nil
        }
    }

    /// Where work on the piece stopped; nil for a piece not opened yet.
    private var resumeName: String? {
        guard let summary else { return nil }
        if !summary.hasNotes { return text["library.pdfAsIs"] }
        guard let latest = summary.latest else { return nil }
        return latest.name ?? text["library.page1"]
    }
}
