import SwiftUI

/// The PDF's Contents (the web app's `.pdf-contents`): the bookmarked pages, to go to, name or remove.
struct PdfContents: View {
    @ObservedObject var pdf: PdfController
    let text: UIText
    /// Opens «Пьесы в этом файле».
    var parts: (() -> Void)?
    let go: (Int) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let parts { PiecePartsEntry(family: pdf.family, text: text, open: parts) }
            Text(text["pdf.bookmarks"]).overline().accessibilityAddTraits(.isHeader)
            if pdf.bookmarks.isEmpty {
                Text(text["pdf.noBookmarks"])
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
                    .fixedSize(horizontal: false, vertical: true)
            }
            ForEach(pdf.bookmarks) { bookmark in
                PdfBookmarkRow(bookmark: bookmark, text: text) {
                    go(bookmark.page)
                } rename: { name in
                    Task { await pdf.rename(bookmark, name) }
                } remove: {
                    Task { await pdf.remove(bookmark) }
                }
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 360, alignment: .leading)
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["bar.contents"])
    }
}

struct PdfBookmarkRow: View {
    let bookmark: PageBookmarkEntry
    let text: UIText
    let go: () -> Void
    let rename: (String) -> Void
    let remove: () -> Void
    @State private var name = ""

    var body: some View {
        let place = text.format("contents.page", String(bookmark.page))
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: go) {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "bookmark").foregroundStyle(Color.ds("ink-carmine"))
                    Text(place)
                        .dsType(.label)
                        .monospacedDigit()
                        .foregroundStyle(Color.ds("ink"))
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            .accessibilityLabel(text.format("pdf.goTo", bookmark.name ?? place))
            TextField(text["pdf.bookmarkName"], text: $name)
                .dsType(.body)
                .textFieldStyle(.roundedBorder)
                .submitLabel(.done)
                .onSubmit { rename(String(name.prefix(80))) }
                .accessibilityLabel(text.format("pdf.bookmarkNameFor", place))
            Button(action: remove) {
                Icon(name: "trash").frame(width: 32, height: 32)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text.format("pdf.removeBookmark", place))
            .accessibilityHint(text["pdf.removeBookmarkHint"])
        }
        .onAppear { name = bookmark.name ?? "" }
        .onDisappear { rename(String(name.prefix(80))) }
    }
}
