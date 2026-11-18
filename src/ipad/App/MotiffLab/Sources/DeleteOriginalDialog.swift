import SwiftUI

/// The warning before an original is deleted (0030, section 4; the web's `.apply-dialog`): the Notes made from it and
/// the marks on its pages, then «Удалить только оригинал» first, «Удалить вместе с нотами», «Отмена».
struct DeleteOriginalDialog: View {
    let layers: SourceLayers
    let original: SourceLayers.Original
    let text: UIText
    let delete: (_ withNotes: Bool) -> Void
    let cancel: () -> Void

    var body: some View {
        let made = original.notes.compactMap { layers.notes($0) }
        // The working notes cannot go while they are working (0030, section 4).
        let withNotesAllowed = !made.isEmpty && !made.contains(where: \.isWorking)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text.format("sources.sheet.deleteTitle", original.shown))
                .dsType(.heading)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityAddTraits(.isHeader)
            if !made.isEmpty {
                Text(text["sources.sheet.deleteLead"]).dsType(.body).foregroundStyle(Color.ds("ink"))
                ForEach(made) { notes in
                    HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                        Icon(name: SourceLayers.pictogram(op: notes.op)).foregroundStyle(Color.ds("ink-2"))
                        Text(layers.title(of: notes, text)).dsType(.body).foregroundStyle(Color.ds("ink"))
                        Text(SourceLayers.caption(of: notes, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                }
            }
            if original.pageMarks > 0 {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "pen").foregroundStyle(Color.ds("ink-2"))
                    Text(text.count("sources.sheet.pageMarks", original.pageMarks)).dsType(.body).foregroundStyle(Color.ds("ink"))
                }
            }
            Button { delete(false) } label: {
                Text(text[made.isEmpty ? "sources.sheet.deleteButton" : "sources.sheet.deleteOnly"])
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(FilledDangerStyle())
            .padding(.top, CGFloat.ds("s-2"))
            if withNotesAllowed {
                Button { delete(true) } label: {
                    Text(text["sources.sheet.deleteWith"]).frame(maxWidth: .infinity)
                }
                .buttonStyle(DangerOutlineStyle())
            }
            Button(action: cancel) {
                Text(text["common.cancel"]).frame(maxWidth: .infinity)
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
        }
        .padding(CGFloat.ds("s-6"))
        .frame(maxWidth: 440)
        .background(Color.ds("surface"))
    }
}

/// The first way to delete: filled in the danger colour.
struct FilledDangerStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .dsType(.label)
            .fontWeight(.semibold)
            .foregroundStyle(Color.ds("on-accent"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-control"))
            .background(Color.ds("danger").opacity(configuration.isPressed ? 0.85 : 1), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .contentShape(Rectangle())
    }
}

/// The second way to delete: outlined in the danger colour.
struct DangerOutlineStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .dsType(.label)
            .fontWeight(.semibold)
            .foregroundStyle(Color.ds("danger"))
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-control"))
            .background(configuration.isPressed ? Color.ds("accent-tint") : Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("danger")))
            .contentShape(Rectangle())
    }
}
