import SwiftUI

/// The library's marks for «Распознать ноты» for several pieces (version 0.5, experiment «Импорт папки»): the pieces picked
/// while choosing (nil when not choosing), a tap that picks or unpicks a piece, and what is happening to each piece's notes.
struct LibraryMarks {
    var chosen: Set<URL>?
    var toggle: (LibraryEntry) -> Void = { _ in }
    var status: (URL) -> RecognitionItem? = { _ in nil }
    /// While choosing (version 0.5.1): the player typed a piece's "title" or "composer" on its line; empty clears it.
    var rename: (LibraryEntry, String, String) -> Void = { _, _, _ in }

    /// «Выбрать все»: every piece shown that can be picked, or none of them when all already are.
    static func all(_ chosen: Set<URL>, shown: [LibraryEntry]) -> Set<URL> {
        let pickable = Set(shown.filter(\.withoutNotes).map(\.url))
        return pickable.isSubset(of: chosen) ? chosen.subtracting(pickable) : chosen.union(pickable)
    }
}

private struct LibraryMarksKey: EnvironmentKey {
    static let defaultValue = LibraryMarks()
}

extension EnvironmentValues {
    var libraryMarks: LibraryMarks {
        get { self[LibraryMarksKey.self] }
        set { self[LibraryMarksKey.self] = newValue }
    }
}

/// A piece's recognition on its line (import-ipad-5-status): «в очереди», «распознаётся», «есть ноты», «не вышло: …». While
/// it is read, a small bar of its pages and the page being read, «распознаётся · стр. 2 из 3», as on the web (v0.5.6, #533
/// section 1).
@MainActor
struct RecognitionTag: View {
    let item: RecognitionItem
    let text: UIText
    @ObservedObject private var queue = RecognitionQueue.shared

    init(item: RecognitionItem, text: UIText) {
        self.item = item
        self.text = text
    }

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            switch item.state {
            case .queued: Circle().strokeBorder(Color.ds("ink-3")).frame(width: 8, height: 8)
            case .reading:
                if let progress = reading {
                    ProgressSegments(segments: progress.states.map { ProgressSegments.Segment(state: $0) }, denseAbove: 8)
                        .frame(width: 40)
                } else {
                    ProgressView().controlSize(.mini)
                }
            case .done: Icon(name: "check", size: CGFloat.ds("icon-s"))
            case .failed: EmptyView()
            }
            Text(words).lineLimit(1)
        }
        .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
        .foregroundStyle(colour)
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(minHeight: 22)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("hairline")))
    }

    /// How far this piece is, while the batch reads it.
    private var reading: ScanProgress? {
        queue.progress.flatMap { $0.file == item.file ? $0.progress : nil }
    }

    private var words: String {
        switch item.state {
        case .failed: text.format("recognition.failedWhy", item.reason ?? "")
        case .reading where reading?.current != nil:
            text.format("recognition.readingPage", String(reading?.current ?? 1), String(reading?.pages ?? 1))
        default: text["recognition.state." + item.state.rawValue]
        }
    }

    private var colour: Color {
        switch item.state {
        case .queued, .reading: .ds("ink-2")
        case .done: .ds("success")
        case .failed: .ds("danger")
        }
    }
}

/// Over the library: the batch's banner («Распознаю ноты: готово 2 из 8 › ещё около 10 минут», Пауза), and while the
/// player chooses, «Выбрано 8 пьес · 41 страница» with «Снять выбор» and «Распознать ноты».
struct RecognitionTop: View {
    @ObservedObject var queue: RecognitionQueue
    let text: UIText
    @Binding var chosen: Set<URL>?
    let entries: [LibraryEntry]
    /// The pieces the list shows now, which «Выбрать все» picks.
    var shown: [LibraryEntry] = []
    let recognise: () -> Void

    var body: some View {
        if let batch = queue.batch {
            banner(batch)
        }
        if let chosen {
            let picked = entries.filter { chosen.contains($0.url) }
            let pickable = shown.filter(\.withoutNotes)
            HStack(spacing: CGFloat.ds("s-4")) {
                if !pickable.isEmpty {
                    let all = pickable.allSatisfy { chosen.contains($0.url) }
                    Button {
                        self.chosen = LibraryMarks.all(chosen, shown: shown)
                    } label: {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            CheckBox(on: all)
                            Text(text["recognition.selectAll"]).dsType(.body).foregroundStyle(Color.ds("ink"))
                        }
                        .frame(minHeight: CGFloat.ds("h-touch"))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(all ? .isSelected : [])
                }
                Text(picked.isEmpty ? text["recognition.chooseHint"] : text.format("recognition.chosenLine", text.count("recognition.pieces", picked.count)) + " · " + text.count("recognition.pages", picked.reduce(0) { $0 + $1.pages }))
                    .dsType(.bodyStrong)
                    .foregroundStyle(Color.ds("ink"))
                Spacer(minLength: CGFloat.ds("s-2"))
                Button(text["recognition.clear"]) { self.chosen = nil }
                    .buttonStyle(DSButtonStyle(kind: .quiet))
                Button(text["recognition.recognise"], action: recognise)
                    .buttonStyle(DSButtonStyle(kind: .primary))
                    .disabled(picked.isEmpty)
            }
        }
    }

    /// The banner moves only as pages come back (v0.5.6, #533 section 1): a segment a piece, the one being read filled by its
    /// pages read; the piece and page being read, and about how long is left once a page is timed.
    private func banner(_ batch: RecognitionBatch) -> some View {
        let current = queue.running ? queue.progress : nil
        return VStack(spacing: 0) {
            HStack(spacing: CGFloat.ds("s-4")) {
                Text(headline(batch)).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                if !batch.finished && batch.stopped == nil, let reading = Self.reading(batch, current, minutes: queue.minutesLeft, text) {
                    Text(reading)
                        .monospacedDigit()
                        .dsType(.label)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                Spacer(minLength: CGFloat.ds("s-2"))
                actions(batch)
            }
            .padding(.horizontal, CGFloat.ds("s-4"))
            .frame(minHeight: CGFloat.ds("h-touch"))
            if !batch.finished {
                ProgressSegments(segments: Self.segments(batch, current), height: 3)
            }
        }
        .accessibilityElement(children: .contain)
        .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
        .dsShadow("shadow-menu")
    }

    private func headline(_ batch: RecognitionBatch) -> String {
        if batch.finished {
            let read = text.format("recognition.finished", String(batch.read), String(batch.items.count))
            return batch.failed > 0 ? read + " · " + text.count("folderImport.notAdded", batch.failed) : read
        }
        if let stopped = batch.stopped { return text["recognition.stopped." + stopped] }
        if batch.paused { return text.format("recognition.pausedAt", String(batch.done), String(batch.items.count)) }
        // The web app's words win where both name a key, and its «recognition.reading» is the line's one word.
        return text.format("recognition.running", String(batch.done), String(batch.items.count))
    }

    /// «Пьеса 3 из 8 · страница 2 из 5 · ещё около 4 мин.», as the web's banner: the piece and page being read, then the time
    /// left once a page is timed; nil when there is neither.
    static func reading(_ batch: RecognitionBatch, _ current: (file: String, progress: ScanProgress)?, minutes: Int?, _ text: UIText) -> String? {
        var parts: [String] = []
        if let current, let index = batch.items.firstIndex(where: { $0.file == current.file }), let page = current.progress.current {
            parts.append(text.format("recognition.pieceOf", String(index + 1), String(batch.items.count)))
            parts.append(text.format("recognition.pageOf", String(page), String(current.progress.pages)))
        }
        if let minutes { parts.append(text.format("recognition.left", String(minutes))) }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    /// A segment a piece: read (its notes or its reason back), being read with the share of its pages read, or waiting.
    static func segments(_ batch: RecognitionBatch, _ current: (file: String, progress: ScanProgress)?) -> [ProgressSegments.Segment] {
        batch.items.map { item in
            switch item.state {
            case .done, .failed: ProgressSegments.Segment(state: .done)
            case .reading:
                ProgressSegments.Segment(state: .current, filled: current.flatMap { $0.file == item.file ? Double($0.progress.done) / Double($0.progress.pages) : nil } ?? 0)
            case .queued: ProgressSegments.Segment(state: .waiting)
            }
        }
    }

    @ViewBuilder
    private func actions(_ batch: RecognitionBatch) -> some View {
        if batch.finished {
            Button(text["folderImport.done"]) { queue.stop() }.buttonStyle(DSButtonStyle(kind: .quiet))
        } else if batch.paused || queue.interrupted {
            Button(text["folderImport.resume"]) { queue.resume() }.buttonStyle(DSButtonStyle(kind: .quiet))
            Button(text["folderImport.stop"]) { queue.stop() }.buttonStyle(DSButtonStyle(kind: .quiet))
        } else {
            Button {
                queue.pause()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "pause", size: CGFloat.ds("icon-s"))
                    Text(text["folderImport.pause"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
        }
    }
}

/// Where the PDFs would go: the scan server the player chose, whether it is Motiff Lab's (Pro, with a daily limit), and
/// whether the player may send to it now.
struct RecognitionOffer: Equatable {
    let address: URL
    let hosted: Bool
    let signedIn: Bool
    let pro: Bool
    /// Pages read today on Motiff Lab's server.
    let usedToday: Int

    var allowed: Bool { !hosted || (signedIn && pro) }
    var leftToday: Int { max(0, DailyPages.limit - usedToday) }

    @MainActor
    static func of(_ account: Account, defaults: UserDefaults = .standard) -> RecognitionOffer? {
        let hostedAddress = ScanServer.hosted(account)
        guard let address = ScanServer.shared.address(hosted: hostedAddress) else { return nil }
        let hosted = address == hostedAddress
        let used = hosted ? account.player.map { DailyPages.used(player: $0, defaults: defaults) } ?? 0 : 0
        return RecognitionOffer(address: address, hosted: hosted, signedIn: account.player != nil, pro: account.allows(.scan), usedToday: used)
    }
}

/// One consent for the batch (import-ipad-4-consent): how many pieces and pages, where the PDFs go (the server set in
/// Settings), how long it takes, what is left of today's limit on Motiff Lab's server, and what becomes of the notes. As on
/// one PDF, pressing «Распознать ноты» is the player's yes (issue #479 item 17).
struct RecogniseConsentSheet: View {
    let pieces: Int
    let pages: Int
    let offer: RecognitionOffer?
    let text: UIText
    let settings: () -> Void
    let send: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                Text(text["recognition.recognise"]).dsType(.title).foregroundStyle(Color.ds("ink")).accessibilityAddTraits(.isHeader)
                if offer?.hosted == true { ProTag() }
                Spacer()
                SheetCloseButton(text: text)
            }
            Text(text.count("recognition.pieces", pieces) + ", " + text.count("recognition.pages", pages))
                .font(DesignTokens.font(display: true, weight: 500, size: 24, relativeTo: .title2))
                .foregroundStyle(Color.ds("ink"))
            if let offer {
                if offer.allowed { facts(offer) } else { blocked(offer) }
            } else {
                line(text["recognition.where"], text["import.scanSetUp"])
                Button(text["import.scanSetUpLink"]) { dismiss(); settings() }.buttonStyle(DSButtonStyle(kind: .secondary))
            }
            HStack(spacing: CGFloat.ds("s-3")) {
                Spacer()
                Button(text["recognition.cancel"]) { dismiss() }.buttonStyle(DSButtonStyle(kind: .quiet))
                Button(text["recognition.recognise"]) {
                    dismiss()
                    send()
                }
                .buttonStyle(DSButtonStyle(kind: .primary))
                .disabled(offer?.allowed != true)
            }
        }
        .padding(CGFloat.ds("s-6"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium, .large])
    }

    @ViewBuilder
    private func facts(_ offer: RecognitionOffer) -> some View {
        let local = ScanServer.isLocal(offer.address)
        line(text["recognition.where"], offer.hosted ? text["recognition.whereHosted"] : text.format(local ? "recognition.whereHere" : "recognition.whereOwn", offer.address.host ?? offer.address.absoluteString))
        line(text["recognition.howLong"], text.format("recognition.howLongLine", String(DailyPages.minutes(pages))))
        if offer.hosted {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                line(text["recognition.today"], text.format("recognition.todayLine", String(min(DailyPages.limit, offer.usedToday + pages)), String(DailyPages.limit)))
                ProgressView(value: Double(min(DailyPages.limit, offer.usedToday + pages)), total: Double(DailyPages.limit))
                    .tint(Color.ds("ink-2"))
                if pages > offer.leftToday {
                    Text(text.format("recognition.overLimit", String(offer.leftToday)))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("warning"))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        line(text["recognition.what"], text["recognition.whatLine"])
    }

    private func blocked(_ offer: RecognitionOffer) -> some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            Text(offer.signedIn ? text["scan.proPrompt"] : text["scan.signInPrompt"])
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .fixedSize(horizontal: false, vertical: true)
            if !offer.signedIn {
                Button(text["settings.signIn"]) { dismiss(); settings() }.buttonStyle(DSButtonStyle(kind: .secondary))
            }
        }
    }

    private func line(_ label: String, _ value: String) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-4")) {
                Text(label).dsType(.body).foregroundStyle(Color.ds("ink-2")).frame(width: 160, alignment: .leading)
                Text(value).dsType(.body).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(label).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                Text(value).dsType(.body).foregroundStyle(Color.ds("ink")).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

/// The batch's sheet and its start over the library: the consent, then the queue; at launch, a batch left unfinished.
@MainActor
struct RecognitionLayer: ViewModifier {
    @Binding var chosen: Set<URL>?
    @Binding var consenting: Bool
    @ObservedObject var account: Account
    let entries: [LibraryEntry]
    let on: Bool
    let text: UIText
    let settings: () -> Void
    let reload: () async -> Void
    @ObservedObject var queue = RecognitionQueue.shared

    /// The PDF-only pieces picked.
    private var picked: [LibraryEntry] {
        entries.filter { chosen?.contains($0.url) == true && $0.withoutNotes }
    }

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: $consenting) {
                RecogniseConsentSheet(pieces: picked.count, pages: picked.reduce(0) { $0 + $1.pages }, offer: RecognitionOffer.of(account), text: text, settings: settings) {
                    guard let offer = RecognitionOffer.of(account), offer.allowed else { return }
                    let items = picked.map { RecognitionItem(file: $0.url.lastPathComponent, title: $0.title, pages: max(1, $0.pages)) }
                    RecognitionQueue.shared.start(items, address: offer.address, hosted: offer.hosted)
                    chosen = nil
                }
            }
            .onChange(of: queue.revision) { _, _ in Task { await reload() } }
            .task(id: on) {
                guard on else { return }
                RecognitionQueue.shared.account = account
                RecognitionQueue.shared.restore()
                ImportRunner.shared.added = { url in
                    guard let offer = RecognitionOffer.of(account), offer.allowed else { return }
                    Task { await RecognitionQueue.shared.append(url, address: offer.address, hosted: offer.hosted) }
                }
            }
    }
}

extension View {
    @MainActor
    func recognition(chosen: Binding<Set<URL>?>, consenting: Binding<Bool>, account: Account, entries: [LibraryEntry], on: Bool, text: UIText, settings: @escaping () -> Void, reload: @escaping () async -> Void) -> some View {
        modifier(RecognitionLayer(chosen: chosen, consenting: consenting, account: account, entries: entries, on: on, text: text, settings: settings, reload: reload))
    }
}
