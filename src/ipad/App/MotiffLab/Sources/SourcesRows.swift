import SwiftUI

/// What the rows of the Sources sheet take from it (the rows moved out of `SourcesSheet.swift` in v0.10): the view shown
/// now, where recognising goes, and what a row's buttons do in the sheet. A row itself does nothing when tapped: the
/// views are picked in the switch in the bar (v0.10, Andrei's finding 25).
struct SourcesRow {
    let text: UIText
    /// The view shown now: its row is tinted.
    let current: String?
    /// Where an original's pages go to be recognised: the scan server chosen; nil when there is none.
    let address: URL?
    /// The scan server chosen is Motiff Lab's: recognising is Pro.
    let hosted: Bool
    /// Scan jobs are on (version 1.3): Motiff Lab's server keeps the PDF until this iPad has fetched what was read.
    var jobs = false
    /// A file to pick in Files for a row: the same file as a deleted original, or where a linked one lies.
    let pick: (Picking) -> Void
    /// ⋯ → «Удалить…» on an original: the warning before it goes.
    let delete: (SourceLayers.Original) -> Void
    /// ⋯ → «Удалить ноты…» on Notes.
    let deleteNotes: (SourceLayers.Notes) -> Void
    /// ⋯ → «Переименовать…» on a version; nil on the PDF, where no score holds the versions.
    var rename: ((SourceLayers.Notes) -> Void)?
    /// ⋯ → «Переименовать…» and «Удалить…» on a draft (v1.1).
    var renameDraft: (SourceLayers.Draft) -> Void = { _ in }
    var deleteDraft: (SourceLayers.Draft) -> Void = { _ in }

    func view(_ id: String, in layers: SourceLayers) -> SourceLayers.ViewEntry? { layers.views.first { $0.id == id } }

    func isCurrent(_ id: String, in layers: SourceLayers) -> Bool { id == (current ?? layers.working) }

    /// Recognising the original is Pro: Motiff Lab's scan server reads it, as it always hears a recording.
    func isPro(_ original: SourceLayers.Original) -> Bool { original.isRecording || hosted }

    /// What the sheet's one file importer picks (in `SourcesSheet.swift` until v1.6 i-13, when the sheet went to Workspace).
    enum Picking: Equatable {
        case pdf, recording, score
        /// The same file as a deleted original.
        case same(SourceLayers.Original)
        /// «Привязать оригинал»: the PDF where it lies in Files.
        case relink(String)
    }
}

/// An original and the Notes made from it, together in one card, the Notes under it, set in (Andrei, v0.8 finding 19).
struct SourcesGroupCard: View {
    @ObservedObject var sources: SourcesController
    @ObservedObject var originals: OriginalsController
    let group: SourceLayers.Group
    let row: SourcesRow
    /// The original whose recognise panel is open.
    @Binding var recognising: String?
    /// Why an original was not recognised, by its id.
    @Binding var problems: [String: String]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            SourcesOriginalRow(sources: sources, originals: originals, original: group.original, row: row, recognising: $recognising, problems: $problems)
            ForEach(group.notes) { notes in
                Divider().overlay(Color.ds("hairline"))
                SourcesNotesRow(sources: sources, notes: notes, underOriginal: true, row: row).padding(.leading, CGFloat.ds("s-6"))
            }
        }
        .padding(CGFloat.ds("s-1"))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .accessibilityElement(children: .contain)
    }
}

// MARK: - An original's row

/// An original's row: its pictogram, name, the eye and ⋯, its caption, where its file is, and «Распознать».
struct SourcesOriginalRow: View {
    @ObservedObject var sources: SourcesController
    @ObservedObject var originals: OriginalsController
    let original: SourceLayers.Original
    let row: SourcesRow
    @Binding var recognising: String?
    @Binding var problems: [String: String]

    private var text: UIText { row.text }

    var body: some View {
        let entry = originals.originals.first { $0.id == original.id }
        let badge = entry.map { originals.badge($0) }
        let shownView = original.isRemoved ? nil : row.view(original.id, in: sources.layers)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            SourcesRowHead(
                sources: sources, row: row,
                icon: SourceLayers.pictogram(kind: original.kind), iconLabel: nil, title: original.shown, removed: original.isRemoved,
                eye: shownView.map { (id: $0.id, hidden: $0.hidden) },
                menu: original.isRemoved ? nil : AnyView(menu)
            )
            Text(SourceLayers.caption(of: original, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            if original.isRemoved {
                SourcesCaption(words: text["sources.sheet.gone"], colour: "ink-3")
                Button(text["sources.sheet.addSame"]) { row.pick(.same(original)) }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            } else if badge == .changed {
                SourcesCaption(words: text["originals.changed"], colour: "warning")
                relink
            } else if !original.isHere, let badge {
                OriginalPlaceLabel(label: badge.text(text), badge: badge)
                if entry?.isDocument == true { relink }
            } else if !original.isHere {
                OriginalPlaceLabel(label: text["originals.nowhere"], badge: .nowhere)
            }
            if sources.canRecognise(original) && original.notes.isEmpty && recognising != original.id {
                SourcesRecogniseButton(text: text, pro: row.isPro(original)) { recognising = original.id }
            }
            if recognising == original.id {
                SourcesRecognisePanel(sources: sources, original: original, row: row, recognising: $recognising, problems: $problems)
            }
            if let problem = problems[original.id] {
                SourcesCaption(words: problem, colour: "danger")
            }
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-2"))
        .background(SourcesRowBackground(current: shownView.map { row.isCurrent($0.id, in: sources.layers) } ?? false, fresh: false))
    }

    private var relink: some View {
        Button(text["originals.relink"]) { row.pick(.relink(original.id)) }
            .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            .disabled(originals.loading != nil)
            .accessibilityHint(text["originals.relinkHintIpad"])
    }

    /// ⋯ on an original: «Распознать» (Pro), a divider, «Удалить…». Recognised again, the word is the same: the row shows
    /// its notes already (v0.10, Andrei's finding 27).
    @ViewBuilder private var menu: some View {
        if sources.canRecognise(original) && !original.notes.isEmpty {
            Button { recognising = original.id } label: {
                Label(row.isPro(original) ? text["sources.sheet.recognise"] + " · Pro" : text["sources.sheet.recognise"], image: "ic-recheck")
            }
            Divider()
        }
        Button(role: .destructive) { row.delete(original) } label: {
            Label(text["sources.sheet.delete"], image: "ic-trash")
        }
    }
}

// MARK: - Notes' row

/// A row of Notes: under their original in its card, or among the Notes of no original; the working ones with their tag.
struct SourcesNotesRow: View {
    @ObservedObject var sources: SourcesController
    let notes: SourceLayers.Notes
    var underOriginal = false
    let row: SourcesRow

    private var text: UIText { row.text }

    var body: some View {
        let layers = sources.layers
        let shownView = row.view(notes.id, in: layers)
        let current = row.isCurrent(notes.id, in: layers)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            SourcesRowHead(
                sources: sources, row: row,
                icon: SourceLayers.pictogram(op: notes.op), iconLabel: SourceLayers.operationName(notes.op, text),
                title: layers.title(of: notes, text, underOriginal: underOriginal), removed: false,
                eye: notes.isWorking ? nil : shownView.map { (id: $0.id, hidden: $0.hidden) },
                menu: notes.isWorking ? nil : AnyView(menu)
            )
            HStack(spacing: CGFloat.ds("s-2")) {
                if notes.isWorking { WorkingTag(text: text) }
                Text(SourceLayers.caption(of: notes, text)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
            switch notes.state ?? "" {
            case "fileChanged": SourcesCaption(words: text["originals.changed"], colour: "warning")
            case "removed": SourcesCaption(words: text["sources.sheet.goneShort"], colour: "ink-2")
            default: EmptyView()
            }
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-2"))
        .background(SourcesRowBackground(current: current, fresh: sources.fresh == notes.id))
    }

    /// ⋯ on Notes: «Сделать рабочими» or, on a version, «Переименовать…», a divider, «Удалить ноты…».
    @ViewBuilder private var menu: some View {
        if notes.canMakeWorking {
            Button { Task { await sources.makeWorking(notes) } } label: {
                Label(text["sources.sheet.makeWorking"], image: "ic-check")
            }
            Divider()
        } else if notes.role == "version", let rename = row.rename {
            Button { rename(notes) } label: {
                Label(text["sources.sheet.rename"], image: "ic-pencil")
            }
            Divider()
        }
        Button(role: .destructive) { row.deleteNotes(notes) } label: {
            Label(text["sources.sheet.deleteNotes"], image: "ic-trash")
        }
    }
}

// MARK: - A draft's row

/// A draft in the group «Черновики» (v1.1, ADR 0034; spec «Экран (iPad)»): its pictogram, its name, the eye (its tab in
/// the switch, as any view) and ⋯ with «Переименовать…» and «Удалить…», then music-core's caption and how many places it
/// has. Its view is picked in the switch, as every view is; the row opens none.
struct SourcesDraftRow: View {
    @ObservedObject var sources: SourcesController
    let draft: SourceLayers.Draft
    let row: SourcesRow

    private var text: UIText { row.text }

    var body: some View {
        let shownView = row.view(draft.id, in: sources.layers)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            SourcesRowHead(
                sources: sources, row: row, icon: SourceLayers.pictogram(op: "draft"), iconLabel: nil, title: draft.name, removed: false,
                eye: shownView.map { (id: $0.id, hidden: $0.hidden) }, menu: AnyView(menu)
            )
            Text(Self.caption(draft, text))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .padding(.horizontal, CGFloat.ds("s-2"))
    }

    /// The line under the draft's name: where its fragments came from, its places and, once there are приёмы, how many of
    /// them are taken, as music-core counts them: «из нот с PDF и записи · 2 места · взято 3 приёма из 4».
    static func caption(_ draft: SourceLayers.Draft, _ text: UIText) -> String {
        var parts = [draft.caption, text.count("drafts.places", draft.places)]
        if draft.techniques > 0 { parts.append(SourceLayers.counted("drafts.tally", draft.taken, String(draft.techniques), text: text)) }
        return parts.joined(separator: " · ")
    }

    @ViewBuilder private var menu: some View {
        Button { row.renameDraft(draft) } label: {
            Label(text["sources.sheet.rename"], image: "ic-pencil")
        }
        Divider()
        Button(role: .destructive) { row.deleteDraft(draft) } label: {
            Label(text["sources.sheet.delete"], image: "ic-trash")
        }
    }
}

// MARK: - Parts of a row

/// The title line: the pictogram, the name in Body-strong on as many lines as it takes, then the eye and ⋯.
struct SourcesRowHead: View {
    let sources: SourcesController
    let row: SourcesRow
    let icon: String
    let iconLabel: String?
    let title: String
    let removed: Bool
    let eye: (id: String, hidden: Bool)?
    let menu: AnyView?

    var body: some View {
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
                .accessibilityLabel(row.text["sources.sheet.showInSwitch"])
                .accessibilityAddTraits(eye.hidden ? [] : .isSelected)
            }
            if let menu {
                Menu { menu } label: {
                    Icon(name: "more").frame(width: 32, height: 32)
                }
                .buttonStyle(BarActionStyle())
                .accessibilityLabel(row.text.format("sources.sheet.more", title))
            }
        }
    }
}

/// A row's background: tinted for the view shown now, and once outlined for Notes just recognised.
struct SourcesRowBackground: View {
    let current: Bool
    let fresh: Bool

    var body: some View {
        RoundedRectangle(cornerRadius: CGFloat.ds("r-s"))
            .fill(current ? Color.ds("accent-tint") : fresh ? Color.ds("surface-2") : Color.clear)
            .overlay {
                if fresh && !current { RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")) }
            }
    }
}

/// A caption of the Sources sheet in one of its inks, on as many lines as it takes.
struct SourcesCaption: View {
    let words: String
    let colour: String

    var body: some View {
        Text(words)
            .dsType(.caption)
            .foregroundStyle(Color.ds(colour))
            .fixedSize(horizontal: false, vertical: true)
    }
}
