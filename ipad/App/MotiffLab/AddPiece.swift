import SwiftUI

/// The last card on the shelf (ds.css `.card.add`): the ways in, and the demo piece.
struct AddCard: View {
    let text: UIText
    let add: () -> Void

    var body: some View {
        Button(action: add) {
            VStack(spacing: CGFloat.ds("s-3")) {
                Icon(name: "plus", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                    .frame(width: 56, height: 56)
                    .background(Color.ds("accent-tint"), in: Circle())
                Text(text["library.addAPiece"])
                    .dsType(.heading)
                    .foregroundStyle(Color.ds("ink"))
                Text(text["library.openFileHintHere"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .multilineTextAlignment(.center)
                Text(text["library.tryDemo"])
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            }
            .padding(CGFloat.ds("s-6"))
            .frame(maxWidth: .infinity, minHeight: 280)
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(
                RoundedRectangle(cornerRadius: CGFloat.ds("r-m"))
                    .strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
            )
        }
        .buttonStyle(.plain)
        .accessibilityLabel(text["library.addAPiece"])
    }
}

/// Add piece (0018 section 9): opening a file runs on the device and is free. Scanning a page and hearing a recording come
/// to the iPad in a later step; until then they are on the web app.
struct AddPieceSheet: View {
    let text: UIText
    let picked: (Result<URL, Error>) -> Void
    /// A recording heard by Motiff Lab's scan server (Pro), to open as a new piece.
    let heard: (HeardRecording) -> Void
    let demo: () -> Void
    @EnvironmentObject private var account: Account
    @State private var choosing = false
    @State private var progress: String?
    @State private var problem: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                HStack {
                    Text(text["library.addAPiece"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                    Spacer()
                    SheetCloseButton(text: text)
                }
                Button {
                    choosing = true
                } label: {
                    HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                        Icon(name: "file", size: CGFloat.ds("icon-l"))
                            .foregroundStyle(Color.ds("accent"))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(text["library.openFile"])
                                .dsType(.bodyStrong)
                                .foregroundStyle(Color.ds("ink"))
                            Text(text["library.openFileHintHere"])
                                .dsType(.caption)
                                .foregroundStyle(Color.ds("ink-2"))
                        }
                        Spacer(minLength: 0)
                    }
                    .padding(CGFloat.ds("s-4"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                }
                .buttonStyle(.plain)
                // On the button, not the sheet: the recording's chooser below has its own, and a second file importer on a
                // view that holds another is not shown.
                .fileImporter(isPresented: $choosing, allowedContentTypes: LibraryView.openable) { result in
                    picked(result)
                }
                if account.isAvailable {
                    fromRecording
                }
                Button(action: demo) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "play")
                        Text(text["library.demo"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            .padding(CGFloat.ds("s-6"))
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium, .large])
        .interactiveDismissDisabled(progress != nil)
    }

    /// From a recording (0014): the scan server hears the sound of an audio or video file, up to its first 7 minutes, and
    /// its notes become a new piece. Pro, as on the web; what it needs first is offered here.
    private var fromRecording: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                Icon(name: "mic", size: CGFloat.ds("icon-l"))
                    .foregroundStyle(Color.ds("accent"))
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text["library.fromRecording"])
                            .dsType(.bodyStrong)
                            .foregroundStyle(Color.ds("ink"))
                        ProTag()
                    }
                    Text(text["library.fromRecordingHint"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Spacer(minLength: 0)
            }
            RecordingChooser(
                text: text,
                line: text["library.fromRecordingLineDevice"],
                button: text["library.chooseRecording"],
                hint: text["transcribe.choose"],
                progress: progress,
                problem: problem
            ) { url in
                Task { await hear(url) }
            }
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    private func hear(_ url: URL) async {
        problem = nil
        defer { progress = nil }
        switch await RecordingHearing.hear(url, account: account, text: text, progress: { progress = $0 }) {
        case .success(let recording):
            heard(recording)
        case .failure(let failure):
            problem = failure.message
        }
    }
}

/// The close button at the top right of a sheet, which closes it.
struct SheetCloseButton: View {
    let text: UIText
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        Button {
            dismiss()
        } label: {
            Icon(name: "close")
                .foregroundStyle(Color.ds("ink-2"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
        }
        .accessibilityLabel(text["library.close"])
    }
}
