import SwiftUI

/// A page of a scanned PDF no engine read, at its place in the notes (v1.5 i-7; the spec «Распознавание сканов четырьмя
/// движками в фоне», «Если страница засбоила»: «в пьесе на месте этой страницы стоит пометка»): the bar beside its place,
/// the first of the notes' page it comes before, or the last bar for a page at the end. Which PDF pages the notes have and
/// lack is kept with the notes since project file 18 (`check`'s "unreadPages" and "readPages"), and by this iPad's
/// recognition while it reads them (`RecognitionItem`); where the notes' pages start is music-core's (`check`'s
/// "pageStarts"). Nothing here decides what is read or sent.
struct UnreadPage: Equatable, Identifiable {
    /// The page of the PDF, from 1.
    let page: Int
    let bar: Int
    let tick: Double
    /// Its place is after `bar`, the notes' last, rather than before it.
    let after: Bool
    var id: Int { page }

    /// The pages `unread` of a PDF whose notes have its pages `read`, placed by the notes' pages as they start; none when
    /// those are not the notes read (other working notes, or as many pages as the notes have not).
    @MainActor
    static func of(unread: [Int], read: [Int], starts: [CheckPageStart]) -> [UnreadPage] {
        guard let last = starts.last, starts.count == read.count else { return [] }
        return unread.sorted().map { page in
            let place = ScanPageAgain.place(of: page, read: read)
            // E-scan-2: a page after every page the notes have is marked at their last bar.
            guard starts.indices.contains(place - 1) else { return UnreadPage(page: page, bar: last.lastBar, tick: last.lastTick, after: true) }
            return UnreadPage(page: page, bar: starts[place - 1].bar, tick: starts[place - 1].tick, after: false)
        }
    }

    /// «Стр. 3 не распознана · её место перед тактом 9».
    func line(_ text: UIText) -> String {
        text.format(after ? "recognition.unreadAfter" : "recognition.unreadBefore", String(page), String(bar))
    }

    /// The Check flags with each of `pages` marked, the amber flag a bar to check has (spec part 6: «тем же знаком»): its
    /// own at the bar beside its place, or its words added to the label of the flag that bar has already, so two never lie
    /// on one another. A tap opens Check, which offers «Повторить» (`UnreadPagesCheck`).
    static func marked(_ flags: [[String: Any]], _ pages: [UnreadPage], _ text: UIText) -> [[String: Any]] {
        // E-scan-1: one flag on a bar, its label telling both.
        CheckModel.adding(pages.map { (bar: $0.bar, tick: $0.tick, label: $0.line(text)) }, to: flags)
    }
}

extension ScoreController {
    /// This piece's recognition on this iPad while it left pages of its PDF unread, or reads them again.
    var unreadRecognition: RecognitionItem? {
        guard let file = piece?.file, let item = RecognitionQueue.shared.status(of: file), item.unread != nil else { return nil }
        return item
    }

    /// The working notes' unread pages at their place: this iPad's recognition's while it has them (none while they are read
    /// again), else those kept with the notes (project file 18), so another iPad or a relaunch shows them too.
    var unreadPages: [UnreadPage] {
        if let item = unreadRecognition {
            guard item.state == .done, let unread = item.unread, let read = item.read else { return [] }
            return UnreadPage.of(unread: unread, read: read, starts: checkModel.check.pageStarts)
        }
        return UnreadPage.of(unread: checkModel.check.unreadPages, read: checkModel.check.readPages, starts: checkModel.check.pageStarts)
    }

    /// Check's part about these pages (`UnreadPagesCheck`), which the Check drawer shows without naming this folder's types.
    func unreadPagesCheck(_ text: UIText) -> some View {
        UnreadPagesCheck(score: self, checkModel: checkModel, text: text)
    }
}

/// Check's part about the PDF's pages no engine read (v1.5 i-7): each at its place in the notes with «Показать», and the
/// recognition's «Повторить» for them (`UnreadPagesRetry`, as on the piece's line in the library) while this iPad's
/// recognition has them; pages kept only with the notes (project file 18) are shown without it.
struct UnreadPagesCheck: View {
    @ObservedObject var score: ScoreController
    /// Where the notes' pages start (`check`'s "pageStarts"), which places the pages, and the pages kept with the notes.
    @ObservedObject var checkModel: CheckModel
    let text: UIText
    @ObservedObject private var queue = RecognitionQueue.shared

    var body: some View {
        let item = score.unreadRecognition
        let kept = item?.unread ?? checkModel.check.unreadPages
        if !kept.isEmpty {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                Text(text["recognition.unreadHeading"]).overline()
                if item == nil || item?.state == .done {
                    let placed = score.unreadPages
                    if placed.isEmpty {
                        Text(RecognitionTop.unreadLine(kept, text)).dsType(.caption).foregroundStyle(Color.ds("warning"))
                    }
                    ForEach(placed) { page in
                        HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                            Text(page.line(text)).dsType(.caption).foregroundStyle(Color.ds("warning")).fixedSize(horizontal: false, vertical: true)
                            Spacer(minLength: 0)
                            Button(text["scanCheck.show"]) { score.selectBars(page.bar, page.bar, extend: false) }
                                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                        }
                    }
                }
                if let item {
                    UnreadPagesRetry(item: item, text: text) { Task { await score.drawChecks() } }
                }
            }
            // The queue moves on its own (read again, in, or not): the flags on the score follow it.
            .onChange(of: queue.revision) { _, _ in Task { await score.drawChecks() } }
        }
    }
}
