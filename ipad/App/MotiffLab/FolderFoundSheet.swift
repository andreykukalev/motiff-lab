import SwiftUI

/// «Что нашлось» and «Что изменилось в папке» (version 0.5, the approved screens import-ipad-2-found.png and
/// import-web-8-recheck.png, on the iPad): the files found, the counters that filter them, each new file's title and
/// composer suggested in grey with where the suggestion comes from (the player types over it), what the folders become,
/// how the PDFs are kept, and «Добавить N пьес». On a check, the library's files found again are said, and a file that may
/// be a piece whose file changed is offered, never linked by itself.
struct FolderFoundSheet: View {
    @ObservedObject var review: FolderReview
    let text: UIText
    /// Where the PDFs would go for «Распознать ноты: сразу для всех»; nil when the player may not send them now.
    var offer: RecognitionOffer?
    /// The import to start.
    let add: (ImportJob) -> Void
    /// Opens the piece found again, when the check came from it.
    let open: (URL) -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
                header
                counters
                status
            }
            .padding([.horizontal, .top], CGFloat.ds("s-6"))
            .padding(.bottom, CGFloat.ds("s-3"))
            ScrollView {
                LazyVStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    if let reopen = review.reopen {
                        FoundAgainLine(text: text, title: ProjectFiles.title(of: reopen.lastPathComponent)) { open(reopen) }
                    }
                    ForEach(review.proposals) { proposal in
                        SameProposalCard(proposal: proposal, text: text) {
                            Task { await review.accept(proposal) }
                        } decline: {
                            review.decline(proposal)
                        }
                    }
                    FoundTable(review: review, text: text)
                }
                .padding(.horizontal, CGFloat.ds("s-6"))
                .padding(.bottom, CGFloat.ds("s-4"))
            }
            footer
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.large])
        .interactiveDismissDisabled(review.listing)
        .task { await review.start() }
    }

    private var header: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                Text(review.folder.place).overline()
                Text(text[review.mode == .first ? "folderImport.foundTitle" : "folderImport.changedTitle"])
                    .dsType(.title)
                    .foregroundStyle(Color.ds("ink"))
                    .accessibilityAddTraits(.isHeader)
            }
            Spacer()
            SheetCloseButton(text: text)
        }
    }

    /// The counters, each a filter of the list: on a check, the new files and the files that may be a piece's changed one.
    private var counters: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: CGFloat.ds("s-2")) {
                ForEach(filters, id: \.self) { filter in
                    Button {
                        review.filter = filter
                    } label: {
                        Text(text[FoundFilter.key + filter.rawValue].replacingOccurrences(of: "{0}", with: String(count(filter))))
                    }
                    .buttonStyle(DSChipStyle(on: review.filter == filter))
                    .accessibilityAddTraits(review.filter == filter ? .isSelected : [])
                }
                if review.mode == .recheck && !review.proposals.isEmpty {
                    Text(text.format("folderImport.filter.changed", String(review.proposals.count)))
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                        .padding(.horizontal, CGFloat.ds("s-3"))
                }
            }
        }
    }

    private var filters: [FoundFilter] {
        review.mode == .first ? FoundFilter.allCases : [.new, .inLibrary]
    }

    private func count(_ filter: FoundFilter) -> Int {
        let counts = review.plan.counts
        switch filter {
        case .found: return counts.found
        case .new: return counts.new
        case .inLibrary: return counts.inLibrary
        case .duplicates: return counts.duplicates
        case .unsuitable: return counts.unsuitable
        }
    }

    /// Listing, reading «12 из 655», the library's files found again, or why the folder could not be read.
    @ViewBuilder
    private var status: some View {
        if let problem = review.problem {
            Text(text[problem]).dsType(.label).foregroundStyle(Color.ds("danger"))
        } else if review.listing {
            HStack(spacing: CGFloat.ds("s-2")) {
                ProgressView()
                Text(text["folderImport.listing"]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            }
        } else if review.read < review.files.count {
            HStack(spacing: CGFloat.ds("s-2")) {
                ProgressView()
                Text(text.format("folderImport.reading", String(review.read), String(review.files.count)))
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
            }
        }
        if review.found > 0 {
            Text(text.count("folderImport.foundAgain", review.found)).dsType(.label).foregroundStyle(Color.ds("success"))
        }
    }

    /// What the folders become, how the PDFs are kept, and «Добавить N пьес».
    private var footer: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            choice(text["folderImport.foldersBecome"], FolderMapping.allCases.map { ($0, text["folderImport.folders." + $0.rawValue]) }, review.folders) { review.folders = $0 }
            if let offer, offer.allowed {
                choice(text["folderImport.recognise"], [(false, text["folderImport.recogniseLater"]), (true, text["folderImport.recogniseNow"])], review.recognise) { review.recognise = $0 }
                if review.recognise {
                    Text(offer.hosted ? text.format("folderImport.recogniseHosted", String(offer.leftToday), String(DailyPages.limit)) : text.format("folderImport.recogniseOwn", offer.address.host ?? offer.address.absoluteString))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            if review.mode == .first {
                if review.linkable {
                    choice(text["folderImport.keep"], [(true, text["folderImport.keepLink"]), (false, text["folderImport.keepCopy"])], review.linked) { review.linked = $0 }
                } else {
                    Text(text["folderImport.linkImpossible"])
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("warning"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            HStack {
                if review.pagesToAdd > 0 {
                    Text(text.count("folderImport.pages", review.pagesToAdd)).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                Spacer()
                Button(text.count("folderImport.addPieces", review.toAdd.count)) {
                    add(review.job())
                }
                .buttonStyle(DSButtonStyle(kind: .primary))
                .disabled(review.toAdd.isEmpty || review.listing)
            }
        }
        .padding(CGFloat.ds("s-6"))
        .background(Color.ds("surface-2"))
        .overlay(alignment: .top) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }

    private func choice<Value: Hashable>(_ label: String, _ options: [(Value, String)], _ chosen: Value, _ choose: @escaping (Value) -> Void) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: CGFloat.ds("s-4")) {
                Text(label).dsType(.body).foregroundStyle(Color.ds("ink-2"))
                SegmentedChoice(options: options, chosen: chosen, label: label, choose: choose)
            }
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(label).dsType(.body).foregroundStyle(Color.ds("ink-2"))
                ScrollView(.horizontal, showsIndicators: false) {
                    SegmentedChoice(options: options, chosen: chosen, label: label, choose: choose)
                }
            }
        }
    }
}

/// The list of «Что нашлось»: a heading of columns, then a line per file under the filter chosen.
struct FoundTable: View {
    @ObservedObject var review: FolderReview
    let text: UIText
    @Environment(\.horizontalSizeClass) private var size

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if size != .compact {
                HStack(spacing: CGFloat.ds("s-4")) {
                    Color.clear.frame(width: CGFloat.ds("h-touch"), height: 1)
                    Text(text["library.fieldTitle"]).overline().frame(maxWidth: .infinity, alignment: .leading).layoutPriority(1)
                    Text(text["library.fieldComposer"]).overline().frame(maxWidth: .infinity, alignment: .leading)
                    Text(text["folderImport.colFolder"]).overline().frame(maxWidth: .infinity, alignment: .leading)
                    Text(text["folderImport.colPages"]).overline().frame(width: CGFloat.ds("h-touch"), alignment: .trailing)
                }
                .padding(.vertical, CGFloat.ds("s-2"))
                .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
            }
            if review.shown.contains(where: \.isNew) {
                selectAll
            }
            ForEach(review.shown) { file in
                FoundLine(file: file, review: review, text: text, compact: size == .compact)
            }
            if !review.listing && review.shown.isEmpty {
                Text(text["folderImport.nothingHere"])
                    .dsType(.body)
                    .foregroundStyle(Color.ds("ink-2"))
                    .padding(.vertical, CGFloat.ds("s-4"))
            }
        }
    }
}

extension FoundTable {
    /// «Выбрать все» (version 0.5.1): ticks or unticks every new file the filter shows.
    var selectAll: some View {
        let on = review.allShownTicked
        return Button {
            review.tickAllShown()
        } label: {
            HStack(spacing: CGFloat.ds("s-4")) {
                CheckBox(on: on).frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                Text(text["folderImport.selectAll"]).dsType(.body).foregroundStyle(Color.ds("ink"))
                Spacer(minLength: 0)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }
}

/// One found file: ticked to add (a new one), its title and composer as suggested, in grey until the player types over
/// them, with where each comes from, its folder and pages, and a badge when it is not new.
struct FoundLine: View {
    let file: PlannedFile
    @ObservedObject var review: FolderReview
    let text: UIText
    let compact: Bool

    var body: some View {
        HStack(alignment: .top, spacing: CGFloat.ds("s-4")) {
            tick
            if compact {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    titleColumn
                    composerColumn
                    Text(details).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                titleColumn.frame(maxWidth: .infinity, alignment: .leading).layoutPriority(1)
                composerColumn.frame(maxWidth: .infinity, alignment: .leading)
                Text(folder).dsType(.body).foregroundStyle(Color.ds("ink-2")).lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
                Text(pages.map { String($0) } ?? "").dsType(.body).foregroundStyle(Color.ds("ink-2")).frame(width: CGFloat.ds("h-touch"), alignment: .trailing)
            }
        }
        .padding(.vertical, CGFloat.ds("s-3"))
        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
    }

    private var name: String { file.path.split(separator: "/").last.map(String.init) ?? file.path }
    private var folder: String {
        guard let slash = file.path.lastIndex(of: "/") else { return "" }
        return String(file.path[..<slash])
    }
    private var pages: Int? { review.facts[file.path]?.pages }
    private var details: String {
        [folder.isEmpty ? nil : folder, pages.map { text.count("folderImport.pages", $0) }].compactMap { $0 }.joined(separator: " · ")
    }

    @ViewBuilder
    private var tick: some View {
        if file.isNew {
            let on = !review.unchecked.contains(file.path)
            Button {
                if on { review.unchecked.insert(file.path) } else { review.unchecked.remove(file.path) }
            } label: {
                CheckBox(on: on).frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
            }
            .buttonStyle(.plain)
            .accessibilityLabel(text.format("folderImport.addThis", file.title?.value ?? name))
            .accessibilityAddTraits(on ? .isSelected : [])
        } else {
            Color.clear.frame(width: CGFloat.ds("h-touch"), height: 1)
        }
    }

    private var titleColumn: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                if file.isNew {
                    SuggestedField(value: titleBinding, suggested: file.title?.value ?? name, label: text["library.fieldTitle"], serif: true)
                } else {
                    Text(file.title?.value ?? name)
                        .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                        .foregroundStyle(Color.ds("ink-2"))
                        .lineLimit(2)
                }
                FoundBadges(file: file, text: text)
            }
            if let line = caption(file.title, edited: review.edits[file.path]?.title != nil) {
                Text(line).dsType(.caption).captionStyle(file)
            }
        }
    }

    @ViewBuilder
    private var composerColumn: some View {
        if file.isNew {
            VStack(alignment: .leading, spacing: 2) {
                SuggestedField(value: composerBinding, suggested: file.composer?.value ?? "", label: text["library.fieldComposer"], serif: false)
                if let line = caption(file.composer, edited: review.edits[file.path]?.composer != nil) {
                    Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                }
            }
        }
    }

    /// Where a suggestion comes from, «из имени файла»; on a file that is not new, why («тот же файл, что в …»).
    private func caption(_ suggested: Suggested?, edited: Bool) -> String? {
        if !file.isNew { return FoundBadges.reason(file, text) }
        if edited { return text["folderImport.from.player"] }
        return suggested.map { text["folderImport.from." + $0.from] }
    }

    private var titleBinding: Binding<String?> {
        Binding { review.edits[file.path]?.title } set: { review.edits[file.path, default: SuggestionEdit()].title = $0 }
    }

    private var composerBinding: Binding<String?> {
        Binding { review.edits[file.path]?.composer } set: { review.edits[file.path, default: SuggestionEdit()].composer = $0 }
    }
}

private extension View {
    /// A file that does not suit is said in the danger colour; every other reason in the second ink.
    func captionStyle(_ file: PlannedFile) -> some View {
        foregroundStyle(file.isUnsuitable ? Color.ds("danger") : Color.ds("ink-2"))
    }
}

/// A suggestion the player may type over: the suggested text in grey until they do; an empty field means none.
struct SuggestedField: View {
    @Binding var value: String?
    let suggested: String
    let label: String
    let serif: Bool

    var body: some View {
        TextField(label, text: Binding { value ?? suggested } set: { value = $0 == suggested ? nil : $0 })
            .font(serif ? DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3) : DesignTokens.font(weight: 400, size: 15, relativeTo: .body))
            .foregroundStyle(value == nil ? Color.ds("ink-2") : Color.ds("ink"))
            .textInputAutocapitalization(.never)
            .autocorrectionDisabled()
            .submitLabel(.done)
            .accessibilityLabel(label)
    }
}

/// The badges of a found file that is not simply new: «повтор», «уже в библиотеке», «не подходит», «возможно, та же
/// пьеса», «несколько пьес?».
struct FoundBadges: View {
    let file: PlannedFile
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            switch file.status {
            case "duplicate": Tag(text: text["folderImport.badge.duplicate"], kind: .assumed)
            case "inLibrary": Tag(text: text["folderImport.badge.inLibrary"], kind: .assumed)
            case "unsupported", "tooBig", "unreadable":
                Text(text["folderImport.badge.unsuitable"])
                    .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
                    .foregroundStyle(Color.ds("danger"))
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(minHeight: 22)
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("hairline-strong")))
            default: EmptyView()
            }
            if file.possiblySame { Tag(text: text["folderImport.badge.possiblySame"], kind: .warn) }
            if file.collection { Tag(text: text["folderImport.badge.collection"], kind: .assumed) }
        }
    }

    /// Why a file is not added: the same file elsewhere in the folder, already in the library, or why it does not suit.
    static func reason(_ file: PlannedFile, _ text: UIText) -> String? {
        switch file.status {
        case "duplicate":
            guard let same = file.sameAs else { return nil }
            let place = same.lastIndex(of: "/").map { String(same[..<$0]) } ?? same
            return text.format("folderImport.sameFileAs", place)
        case "unsupported": return text["folderImport.why.unsupported"]
        case "tooBig": return text["folderImport.why.tooBig"]
        case "unreadable": return text["folderImport.why.unreadable"]
        default: return nil
        }
    }
}

/// «Похоже, это та же пьеса» (import-web-8-recheck): the piece in the library and the file in the folder now, side by
/// side, and the player's answer; the piece's notes, marks and changes stay either way.
struct SameProposalCard: View {
    let proposal: SameProposal
    let text: UIText
    let accept: () -> Void
    let decline: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["folderImport.sameTitle"]).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: CGFloat.ds("s-6")) { sides }
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) { sides }
            }
            HStack(spacing: CGFloat.ds("s-3")) {
                Button(text["folderImport.sameYes"], action: accept).buttonStyle(DSButtonStyle(kind: .secondary))
                Button(text["folderImport.sameNo"], action: decline).buttonStyle(DSButtonStyle(kind: .quiet))
            }
            Text(text["folderImport.sameKeeps"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        .padding(CGFloat.ds("s-4"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline-strong")))
    }

    @ViewBuilder
    private var sides: some View {
        side(text["folderImport.inLibrary"], proposal.title, [proposal.composer, proposal.pages.map { text.count("folderImport.pages", $0) }, proposal.bytes.map(Self.size)])
        side(text["folderImport.inFolderNow"], proposal.candidate.name, [proposal.candidate.folder.isEmpty ? nil : proposal.candidate.folder, Self.size(proposal.candidate.bytes)])
    }

    private func side(_ heading: String, _ title: String, _ facts: [String?]) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Text(heading).overline()
            Text(title)
                .font(DesignTokens.font(display: true, weight: 500, size: 19, relativeTo: .title3))
                .foregroundStyle(Color.ds("ink"))
            Text(facts.compactMap { $0 }.joined(separator: " · ")).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    static func size(_ bytes: Int64) -> String {
        ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
    }
}

/// On a check from a piece whose file was not found: it was found again, and opens.
struct FoundAgainLine: View {
    let text: UIText
    let title: String
    let open: () -> Void

    var body: some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Icon(name: "check").foregroundStyle(Color.ds("success"))
            Text(text.format("folderImport.foundPiece", title)).dsType(.body).foregroundStyle(Color.ds("ink"))
            Spacer(minLength: 0)
            Button(text["folderImport.openPiece"], action: open).buttonStyle(DSButtonStyle(kind: .primary, compact: true))
        }
        .padding(CGFloat.ds("s-3"))
        .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
    }
}

/// A tick box in the kit's colours: filled with the accent and a tick while on, a hairline square while off.
struct CheckBox: View {
    let on: Bool

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: CGFloat.ds("r-xs"))
                .fill(on ? Color.ds("accent") : Color.ds("surface"))
            RoundedRectangle(cornerRadius: CGFloat.ds("r-xs"))
                .strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: 1.5)
            if on { Icon(name: "check", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("on-accent")) }
        }
        .frame(width: CGFloat.ds("icon-l"), height: CGFloat.ds("icon-l"))
        .accessibilityHidden(true)
    }
}
