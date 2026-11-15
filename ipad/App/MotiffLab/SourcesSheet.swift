import SwiftUI
import UniformTypeIdentifiers

/// The Sources sheet (version 0.6, behind `experimental.sources`; decision record 0030 and the sources sheet handoff), in
/// the side sheet where Other versions was: «Оригиналы» and «Ноты», each row its pictogram, its name never cut, the eye
/// «Показывать в переключателе», «Открыть» and ⋯, a line under the name and a state line. An original is added without
/// recognising it («+ Добавить оригинал»: a PDF, photos, a recording, MIDI or MusicXML), recognised («Распознать»,
/// «Распознать ещё раз») into new Notes beside the ones there are, and deleted with or without its notes, which the toast
/// undoes; a deleted original stays as a row that takes the same file back. Other Notes become the working ones from ⋯.
/// A version's «Открыть» shows its page here, as Other versions did (0022).
struct SourcesSheet: View {
    @ObservedObject var sources: SourcesController
    @ObservedObject var score: ScoreController
    @ObservedObject var originals: OriginalsController
    let text: UIText
    /// The view shown now: its row is tinted and has no «Открыть».
    let current: String?
    let close: () -> Void
    /// "‹ More" when opened from More; nil from elsewhere.
    var back: (label: String, action: () -> Void)?
    /// «Открыть» on a row: the screen shows that view.
    let open: (SourceLayers.ViewEntry) -> Void
    @EnvironmentObject private var account: Account
    @ObservedObject private var scan = ScanServer.shared

    /// What the one file importer picks.
    enum Picking: Equatable {
        case pdf, recording, score
        /// The same file as a deleted original.
        case same(SourceLayers.Original)
        /// «Привязать оригинал»: the PDF where it lies in Files.
        case relink(String)
    }

    @State private var picking: Picking?
    /// Files is open for `picking`, kept until the importer answers.
    @State private var importing = false
    @State private var camera = false
    /// The original whose recognise panel is open.
    @State private var recognising: String?
    @State private var problems: [String: String] = [:]
    @State private var deleting: SourceLayers.Original?
    @State private var deletingNotes: SourceLayers.Notes?
    @State private var versionName = ""

    private var layers: SourceLayers { sources.layers }

    /// The version whose page shows, once music-core has it shown.
    private var versionPage: VersionEntry? {
        guard let id = score.versionPage, let shown = score.versions.shown, shown.id == id else { return nil }
        return shown
    }

    private var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }
    private var hosted: Bool { scanAddress != nil && scanAddress == ScanServer.hosted(account) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    if let page = versionPage {
                        VersionPage(score: score, text: text, version: page, name: $versionName)
                    } else {
                        list
                    }
                }
                .padding(.leading, CGFloat.ds("s-5"))
                .padding(.trailing, CGFloat.ds("s-4"))
                .padding(.vertical, CGFloat.ds("s-3"))
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .background(Color.ds("surface"))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["sources.title"])
        .fileImporter(isPresented: $importing, allowedContentTypes: types) { result in
            let picked = picking
            picking = nil
            guard case .success(let url) = result, let picked else { return }
            Task { await take(url, for: picked) }
        }
        .fullScreenCover(isPresented: $camera) {
            DocumentCamera { images in
                camera = false
                Task { await sources.addPhotos(images) }
            }
            .ignoresSafeArea()
        }
        .sheet(item: $deleting) { original in
            DeleteOriginalDialog(layers: layers, original: original, text: text) { withNotes in
                deleting = nil
                Task { await sources.remove(original, withNotes: withNotes) }
            } cancel: {
                deleting = nil
            }
            .fittedSheet()
        }
        .confirmationDialog(
            deletingNotes.map { text.format("sources.sheet.deleteNotesTitle", layers.title(of: $0, text)) } ?? "",
            isPresented: Binding(get: { deletingNotes != nil }, set: { if !$0 { deletingNotes = nil } }),
            titleVisibility: .visible,
            presenting: deletingNotes
        ) { notes in
            Button(text["sources.sheet.deleteNotesButton"], role: .destructive) { Task { await sources.remove(notes) } }
            Button(text["common.cancel"], role: .cancel) {}
        } message: { _ in
            Text(text["sources.sheet.deleteNotesLine"])
        }
        .confirmationDialog(
            text["originals.linkOrCopy"],
            isPresented: Binding(get: { originals.choice != nil }, set: { if !$0 { originals.choice = nil } }),
            titleVisibility: .visible,
            presenting: originals.choice
        ) { picked in
            if picked.bookmark != nil {
                Button(text["originals.keepWhere"]) { Task { await originals.keepLink(picked); await sources.refresh() } }
            }
            Button(text["originals.copyToIpad"]) { Task { await originals.keepCopy(picked); await sources.refresh() } }
            Button(text["common.cancel"], role: .cancel) {}
        } message: { _ in
            Text(text["originals.linkOrCopyHint"])
        }
        .onAppear {
            versionName = score.versions.shown?.name ?? ""
            Task {
                await sources.refresh()
                await originals.verify()
            }
        }
        .onChange(of: score.versions.shown?.name) { _, shown in versionName = shown ?? "" }
    }

    // MARK: - Header

    private var header: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            if let page = versionPage {
                BackChip(label: text["sources.title"], text: text) { score.closeVersionPage() }
                    .padding(.leading, -CGFloat.ds("s-2"))
                Text(text[page.source == "score" ? "sources.roleNotes" : "sources.rolePlaying"]).overline()
            } else if let back {
                BackChip(label: back.label, text: text, action: back.action)
                    .padding(.leading, -CGFloat.ds("s-2"))
            }
            HStack(alignment: .center, spacing: CGFloat.ds("s-2")) {
                Text(versionPage?.name ?? text["sources.title"])
                    .dsType(versionPage == nil ? .title : .heading)
                    .foregroundStyle(Color.ds("ink"))
                    .accessibilityAddTraits(.isHeader)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if versionPage == nil { addMenu }
                Button(action: close) {
                    Icon(name: "close").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["performance.close"])
            }
        }
        .padding(.leading, CGFloat.ds("s-5"))
        .padding(.trailing, CGFloat.ds("s-4"))
        .padding(.top, CGFloat.ds("s-4"))
    }

    /// «+ Добавить оригинал»: a PDF, photos of pages, a recording, or a MIDI or MusicXML file, read at once on the iPad.
    private var addMenu: some View {
        Menu {
            Button { pick(.pdf) } label: {
                Label { Text(text["pdf.viewPdfShort"]); Text(text["sources.sheet.addPdfHint"]) } icon: { Image("ic-pages") }
            }
            if DocumentCamera.isAvailable {
                Button { camera = true } label: {
                    Label { Text(text["sources.sheet.photos"]); Text(text["sources.sheet.addPhotosHint"]) } icon: { Image("ic-camera") }
                }
            }
            Button { pick(.recording) } label: {
                Label { Text(text["sources.sheet.recording"]); Text(text["sources.sheet.addRecordingHint"]) } icon: { Image("ic-mic") }
            }
            Button { pick(.score) } label: {
                Label { Text(text["sources.sheet.addScore"]); Text(text["sources.sheet.addScoreHint"]) } icon: { Image("ic-fromFile") }
            }
        } label: {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: "plus", size: CGFloat.ds("icon-s"))
                Text(text["sources.sheet.addOriginal"]).lineLimit(1)
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .fixedSize()
    }

    // MARK: - The list

    @ViewBuilder private var list: some View {
        if let status = sources.status {
            caption(status, colour: "danger").padding(.bottom, CGFloat.ds("s-2"))
        }
        sectionHead(text.format("sources.sheet.originalsCount", String(layers.liveCount)))
            .padding(.top, CGFloat.ds("s-2"))
        ForEach(layers.listedOriginals) { original in
            originalRow(original)
            Divider().overlay(Color.ds("hairline"))
        }
        sectionHead(text.format("sources.sheet.notesCount", String(layers.notes.count)))
            .padding(.top, CGFloat.ds("s-5"))
        ForEach(layers.listedNotes) { notes in
            notesRow(notes)
            Divider().overlay(Color.ds("hairline"))
        }
        if let status = score.versionStatus {
            caption(status, colour: "ink-2").padding(.top, CGFloat.ds("s-3"))
        }
    }

    private func view(_ id: String) -> SourceLayers.ViewEntry? { layers.views.first { $0.id == id } }

    private func isCurrent(_ id: String) -> Bool { id == (current ?? layers.working) }

    // MARK: - An original's row

    private func originalRow(_ original: SourceLayers.Original) -> some View {
        let entry = originals.originals.first { $0.id == original.id }
        let badge = entry.map { originals.badge($0) }
        let shownView = original.isRemoved ? nil : view(original.id)
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            rowHead(
                icon: SourceLayers.pictogram(kind: original.kind), iconLabel: nil, title: original.shown, removed: original.isRemoved,
                eye: shownView.map { (id: $0.id, hidden: $0.hidden) }, opens: shownView.flatMap { isCurrent($0.id) ? nil : $0 },
                menu: original.isRemoved ? nil : AnyView(originalMenu(original))
            )
            Text(SourceLayers.caption(of: original, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            if original.isRemoved {
                caption(text["sources.sheet.gone"], colour: "ink-3")
                Button(text["sources.sheet.addSame"]) { pick(.same(original)) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            } else if badge == .changed {
                caption(text["originals.changed"], colour: "warning")
                relink(original)
            } else if !original.isHere, let badge {
                OriginalPlaceLabel(label: badge.text(text), badge: badge)
                if entry?.isDocument == true { relink(original) }
            } else if !original.isHere {
                OriginalPlaceLabel(label: text["originals.nowhere"], badge: .nowhere)
            }
            if canRecognise(original) && original.notes.isEmpty && recognising != original.id {
                Button { recognising = original.id } label: {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text["sources.sheet.recognise"])
                        if hosted { ProTag() }
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if recognising == original.id {
                recognisePanel(original)
            }
            if let problem = problems[original.id] {
                caption(problem, colour: "danger")
            }
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-2"))
        .background(rowBackground(current: shownView.map { isCurrent($0.id) } ?? false, fresh: false))
    }

    /// The pages can go to the scan server: a PDF or photos whose bytes are kept in the piece here.
    private func canRecognise(_ original: SourceLayers.Original) -> Bool {
        original.isPages && original.isHere && original.kept == "file" && layers.working != nil
    }

    private func relink(_ original: SourceLayers.Original) -> some View {
        Button(text["originals.relink"]) { pick(.relink(original.id)) }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(originals.loading != nil)
            .accessibilityHint(text["originals.relinkHintIpad"])
    }

    /// ⋯ on an original: «Распознать ещё раз» (Pro), a divider, «Удалить…».
    @ViewBuilder private func originalMenu(_ original: SourceLayers.Original) -> some View {
        if canRecognise(original) && !original.notes.isEmpty {
            Button { recognising = original.id } label: {
                Label(hosted ? text["sources.sheet.recogniseAgain"] + " · Pro" : text["sources.sheet.recogniseAgain"], image: "ic-recheck")
            }
            Divider()
        }
        Button(role: .destructive) { deleting = original } label: {
            Label(text["sources.sheet.delete"], image: "ic-trash")
        }
    }

    // MARK: - Notes' row

    private func notesRow(_ notes: SourceLayers.Notes) -> some View {
        let shownView = view(notes.id)
        let current = isCurrent(notes.id)
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            rowHead(
                icon: SourceLayers.pictogram(op: notes.op), iconLabel: SourceLayers.operationName(notes.op, text),
                title: layers.title(of: notes, text), removed: false,
                eye: notes.isWorking ? nil : shownView.map { (id: $0.id, hidden: $0.hidden) },
                opens: current ? nil : shownView,
                menu: notes.isWorking ? nil : AnyView(notesMenu(notes))
            )
            HStack(spacing: CGFloat.ds("s-2")) {
                if notes.isWorking { WorkingTag(text: text) }
                Text(SourceLayers.caption(of: notes, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            switch notes.state ?? "" {
            case "fileChanged": caption(text["originals.changed"], colour: "warning")
            case "removed": caption(text["sources.sheet.goneShort"], colour: "ink-2")
            default: EmptyView()
            }
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-2"))
        .background(rowBackground(current: current, fresh: sources.fresh == notes.id))
    }

    /// ⋯ on Notes: «Сделать рабочими», a divider, «Удалить ноты…».
    @ViewBuilder private func notesMenu(_ notes: SourceLayers.Notes) -> some View {
        if notes.canMakeWorking {
            Button { Task { await sources.makeWorking(notes) } } label: {
                Label(text["sources.sheet.makeWorking"], image: "ic-check")
            }
            Divider()
        }
        Button(role: .destructive) { deletingNotes = notes } label: {
            Label(text["sources.sheet.deleteNotes"], image: "ic-trash")
        }
    }

    // MARK: - Parts of a row

    /// The title line: the pictogram, the name in Body-strong on as many lines as it takes, then the eye, «Открыть» and ⋯.
    private func rowHead(icon: String, iconLabel: String?, title: String, removed: Bool, eye: (id: String, hidden: Bool)?, opens: SourceLayers.ViewEntry?, menu: AnyView?) -> some View {
        HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
            Image("ic-\(icon)")
                .renderingMode(.template)
                .resizable()
                .scaledToFit()
                .frame(width: CGFloat.ds("icon-m"), height: CGFloat.ds("icon-m"))
                .foregroundStyle(Color.ds(removed ? "ink-3" : "ink-2"))
                .padding(.top, 2)
                .accessibilityLabel(iconLabel ?? "")
                .accessibilityHidden(iconLabel == nil)
            Text(title)
                .dsType(.bodyStrong)
                .foregroundStyle(Color.ds(removed ? "ink-3" : "ink"))
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let eye {
                Button { Task { await sources.setHidden(eye.id, !eye.hidden) } } label: {
                    Icon(name: eye.hidden ? "eyeoff" : "eye")
                        .foregroundStyle(Color.ds(eye.hidden ? "ink-3" : "ink-2"))
                        .frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text["sources.sheet.showInSwitch"])
                .accessibilityAddTraits(eye.hidden ? [] : .isSelected)
            }
            if let opens {
                Button(text["home.open"]) { open(opens) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if let menu {
                Menu { menu } label: {
                    Icon(name: "more").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(text.format("sources.sheet.more", title))
            }
        }
    }

    private func rowBackground(current: Bool, fresh: Bool) -> some View {
        RoundedRectangle(cornerRadius: CGFloat.ds("r-s"))
            .fill(current ? Color.ds("accent-tint") : fresh ? Color.ds("surface-2") : Color.clear)
            .overlay {
                if fresh && !current { RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")) }
            }
    }

    /// «Распознать ноты» under the row: where the pages go, then Send (the player's yes) and Cancel; recognised again,
    /// the notes there are and their corrections stay beside.
    private func recognisePanel(_ original: SourceLayers.Original) -> some View {
        let pages = max(1, original.pages ?? 1)
        let earlier = original.notes.compactMap { layers.notes($0) }.last
        let busy = sources.reading == original.id
        return VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-2")) {
                Text(text[earlier == nil ? "sources.sheet.readHeading" : "sources.sheet.recogniseAgain"]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                if hosted { ProTag() }
            }
            if let address = scanAddress {
                if hosted && account.player == nil {
                    caption(text["scan.signInPrompt"], colour: "ink-2")
                } else if hosted && !account.allows(.scan) {
                    caption(text["scan.proPrompt"], colour: "ink-2")
                } else {
                    caption(hosted
                        ? SourceLayers.counted("sources.sheet.readHosted", pages, text: text)
                        : SourceLayers.counted("sources.sheet.readOwn", pages, address.absoluteString, text: text), colour: "ink-2")
                    if let earlier {
                        caption(text.format("sources.sheet.readKeeps", earlier.at.map { SourceLayers.shortDate($0, text) } ?? "", text.count("sources.sheet.edits", earlier.edits)), colour: "ink-2")
                    }
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Button(busy ? text["import.scanBusy"] : SourceLayers.counted("sources.sheet.send", pages, text: text)) {
                            Task {
                                problems[original.id] = nil
                                if let problem = await sources.recognise(original, address: address, account: account) {
                                    problems[original.id] = problem
                                } else {
                                    recognising = nil
                                }
                            }
                        }
                        .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                        .disabled(busy)
                        Button(text["common.cancel"]) { recognising = nil }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                            .disabled(busy)
                    }
                }
            } else {
                caption(text["pdf.readNone"], colour: "ink-2")
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
    }

    /// «Оригиналы · 4», «Ноты · 3»: the overline in the second ink, as the web's sheet has it.
    private func sectionHead(_ words: String) -> some View {
        Text(words)
            .dsType(.overline)
            .textCase(.uppercase)
            .tracking(0.66)
            .foregroundStyle(Color.ds("ink-2"))
            .accessibilityAddTraits(.isHeader)
    }

    private func caption(_ words: String, colour: String) -> some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds(colour))
            .fixedSize(horizontal: false, vertical: true)
    }

    // MARK: - Picking files

    private func pick(_ what: Picking) {
        picking = what
        importing = true
    }

    private var types: [UTType] {
        switch picking {
        case .recording: RecordingHearing.recordings
        case .score: VersionsDrawer.versionFiles
        case .same(let original): original.isRecording ? RecordingHearing.recordings : [.pdf]
        default: [.pdf]
        }
    }

    /// A file picked in Files: read once, inside its security scope, and never changed.
    private func take(_ url: URL, for picked: Picking) async {
        switch picked {
        case .recording:
            await sources.addRecording(url)
        case .relink(let id):
            if let entry = originals.originals.first(where: { $0.id == id }) {
                await originals.picked(url, for: entry)
                await sources.refresh()
            }
        case .same(let original) where original.isRecording:
            await sources.addRecording(url)
        case .pdf, .score, .same:
            let lent = url.startAccessingSecurityScopedResource()
            let data = try? Data(contentsOf: url)
            if lent { url.stopAccessingSecurityScopedResource() }
            guard let data else { return }
            if picked == .score {
                await score.addVersion(fileName: url.lastPathComponent, data: data)
                await sources.refresh()
            } else {
                let kind: String
                if case .same(let original) = picked { kind = original.kind } else { kind = "pdf" }
                await sources.addPages(data, fileName: url.lastPathComponent, kind: kind)
            }
        }
    }
}

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
