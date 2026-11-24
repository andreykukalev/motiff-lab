import SwiftUI
import UniformTypeIdentifiers

/// The Sources sheet (version 0.6; decision record 0030 and the sources sheet handoff), in
/// the side sheet where Other versions was: «Оригиналы», each in one card with the Notes made from it under it (v0.9,
/// Andrei's v0.8 finding 19), then «Ноты без оригинала»; each row its pictogram, its name never cut, the eye
/// «Показывать в переключателе» and ⋯, a line under the name and a state line; a row opens no view, the switch in the bar
/// does (v0.10, finding 25). An original is added without recognising it («+ Добавить», «+ Добавить ещё»: a PDF, photos,
/// a recording, MIDI or MusicXML), recognised («Распознать», again from ⋯) into new Notes beside the ones there are, and
/// deleted with or without its notes, which the toast undoes; a deleted original stays as a row that takes the same file
/// back. Other Notes become the working ones from ⋯, and a version is renamed there. A version picked in the switch opens
/// the sheet (v1.1: its page, the bar-by-bar merge, left the iPad, ADR 0034 section 8). Last, «Черновики» (v1.1, ADR
/// 0034): each draft with its eye and ⋯ (rename, delete), and «Новый черновик» on the score. The rows are in
/// `SourcesRows.swift`, «Распознать» in `SourcesRecognise.swift` and the dialogs in `SourcesDialogs.swift` (v0.10); this
/// file is the sheet, its header and add menu, and picking files.
struct SourcesSheet: View {
    @ObservedObject var sources: SourcesController
    /// The score's controller, for the versions; nil on the PDF, where the sheet opens over the pages (#582 item 4).
    let score: ScoreController?
    @ObservedObject var originals: OriginalsController
    let text: UIText
    /// The view shown now: its row is tinted.
    let current: String?
    let close: () -> Void
    /// "‹ More" when opened from More; nil from elsewhere.
    var back: (label: String, action: () -> Void)?
    /// What the versions last said, from the score; nil on the PDF.
    var versionStatus: String? = nil
    /// «Новый черновик» (v1.1): from the score, which holds the selection; nil on the PDF.
    var newDraft: (() -> Void)?
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
    /// Why an original was not recognised, by its id, under its row.
    @State private var problems: [String: String] = [:]
    @State private var deleting: SourceLayers.Original?
    @State private var deletingNotes: SourceLayers.Notes?
    @State private var renaming: SourceLayers.Notes?
    @State private var renamingDraft: SourceLayers.Draft?
    @State private var deletingDraft: SourceLayers.Draft?

    private var layers: SourceLayers { sources.layers }

    private var scanAddress: URL? { scan.address(hosted: ScanServer.hosted(account)) }
    private var hosted: Bool { scanAddress != nil && scanAddress == ScanServer.hosted(account) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            ScrollView {
                VStack(alignment: .leading, spacing: 0) {
                    list
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
        .modifier(SourcesDialogs(
            sources: sources, originals: originals, text: text, deleting: $deleting, deletingNotes: $deletingNotes, renaming: $renaming,
            rename: { notes, name in Task { await score?.renameVersion(notes.id, to: name); await sources.refresh() } }
        ))
        .modifier(DraftDialogs(sources: sources, text: text, renaming: $renamingDraft, deleting: $deletingDraft))
        .onAppear {
            Task {
                await sources.refresh()
                await originals.verify()
            }
        }
    }

    // MARK: - Header

    private var header: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            if let back {
                BackChip(label: back.label, text: text, action: back.action)
                    .padding(.leading, -CGFloat.ds("s-2"))
            }
            HStack(alignment: .center, spacing: CGFloat.ds("s-2")) {
                Text(text["sources.title"])
                    .dsType(.title)
                    .foregroundStyle(Color.ds("ink"))
                    .accessibilityAddTraits(.isHeader)
                    .frame(maxWidth: .infinity, alignment: .leading)
                addMenu
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

    /// «+ Добавить», or «+ Добавить ещё» once the piece has an original (#582 item 5: it said «Добавить оригинал»): another
    /// source of the same piece, never a new piece. Its word for the number of originals there are.
    static func addWord(liveOriginals: Int) -> String {
        liveOriginals > 0 ? "sources.sheet.addMore" : "sources.sheet.add"
    }

    /// The add menu: a PDF, photos of pages, a recording (to recognise later), or a MIDI or MusicXML file, all kept in this
    /// piece.
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
            // Notes from a file become a version of the working notes, which only the score holds.
            if score != nil {
                Button { pick(.score) } label: {
                    Label { Text(text["sources.sheet.addScore"]); Text(text["sources.sheet.addScoreHint"]) } icon: { Image("ic-fromFile") }
                }
            }
        } label: {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: "plus", size: CGFloat.ds("icon-s"))
                Text(text[SourcesSheet.addWord(liveOriginals: layers.liveCount)]).lineLimit(1)
            }
        }
        .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
        .fixedSize()
    }

    // MARK: - The list

    @ViewBuilder private var list: some View {
        if let status = sources.status {
            SourcesCaption(words: status, colour: "danger").padding(.bottom, CGFloat.ds("s-2"))
        }
        sectionHead(text.format("sources.sheet.originalsCount", String(layers.liveCount)))
            .padding(.top, CGFloat.ds("s-2"))
            .padding(.bottom, CGFloat.ds("s-2"))
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            ForEach(layers.groups) { group in
                SourcesGroupCard(sources: sources, originals: originals, group: group, row: row, recognising: $recognising, problems: $problems)
            }
        }
        if !layers.looseNotes.isEmpty {
            sectionHead(text.format("sources.sheet.looseNotesCount", String(layers.looseNotes.count)))
                .padding(.top, CGFloat.ds("s-5"))
            ForEach(layers.looseNotes) { notes in
                SourcesNotesRow(sources: sources, notes: notes, row: row)
                Divider().overlay(Color.ds("hairline"))
            }
        }
        drafts
        if let status = versionStatus {
            SourcesCaption(words: status, colour: "ink-2").padding(.top, CGFloat.ds("s-3"))
        }
    }

    /// «Черновики · N» (v1.1, ADR 0034): the piece's drafts, then «Новый черновик» where the score is.
    @ViewBuilder private var drafts: some View {
        if !layers.drafts.isEmpty || newDraft != nil {
            sectionHead(text.format("drafts.group", String(layers.drafts.count)))
                .padding(.top, CGFloat.ds("s-5"))
            ForEach(layers.drafts) { draft in
                SourcesDraftRow(sources: sources, draft: draft, row: row)
                Divider().overlay(Color.ds("hairline"))
            }
            if let newDraft {
                Button(action: newDraft) {
                    HStack(spacing: CGFloat.ds("s-1")) {
                        Icon(name: "plus", size: CGFloat.ds("icon-s"))
                        Text(text["drafts.new"]).lineLimit(1)
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .fixedSize()
                .padding(.top, CGFloat.ds("s-2"))
            }
        }
    }

    /// What the rows take from the sheet: the view shown now, where recognising goes, and their buttons' actions here.
    private var row: SourcesRow {
        SourcesRow(
            text: text, current: current, address: scanAddress, hosted: hosted, jobs: ScanServer.jobs, pick: pick,
            delete: { deleting = $0 }, deleteNotes: { deletingNotes = $0 },
            rename: score == nil ? nil : { (notes: SourceLayers.Notes) in renaming = notes },
            renameDraft: { renamingDraft = $0 }, deleteDraft: { deletingDraft = $0 }
        )
    }

    /// «Оригиналы · 4», «Ноты без оригинала · 1»: the overline in the second ink, as the web's sheet has it.
    private func sectionHead(_ words: String) -> some View {
        Text(words)
            .dsType(.overline)
            .textCase(.uppercase)
            .tracking(0.66)
            .foregroundStyle(Color.ds("ink-2"))
            .accessibilityAddTraits(.isHeader)
    }

    // MARK: - Picking files

    private func pick(_ what: Picking) {
        picking = what
        importing = true
    }

    private var types: [UTType] {
        switch picking {
        case .recording: RecordingHearing.recordings
        case .score: VersionFiles.types
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
                await score?.addVersion(fileName: url.lastPathComponent, data: data)
                await sources.refresh()
            } else {
                let kind: String
                if case .same(let original) = picked { kind = original.kind } else { kind = "pdf" }
                await sources.addPages(data, fileName: url.lastPathComponent, kind: kind)
            }
        }
    }
}
