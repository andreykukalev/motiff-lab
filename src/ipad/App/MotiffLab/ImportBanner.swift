import SwiftUI

/// The import over the library (version 0.5, the approved screen import-ipad-3-adding.png): «Добавляю 214 из 628 ›
/// Google Drive › Ноты» with Пауза and Остановить and the bar of what is done; paused by the player, by the app (no
/// connection, no access to the folder) or left unfinished when the app was closed, with Продолжить; and when it ends,
/// how many pieces were added and how many were not, with what happened to each.
struct ImportBanner: View {
    @ObservedObject var runner: ImportRunner
    let text: UIText
    @State private var details = false

    var body: some View {
        if let job = runner.job {
            VStack(spacing: 0) {
                HStack(spacing: CGFloat.ds("s-4")) {
                    Button {
                        details = true
                    } label: {
                        HStack(spacing: CGFloat.ds("s-2")) {
                            Text(headline(job)).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                            Icon(name: "fwd", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-2"))
                            Text(job.place).dsType(.label).foregroundStyle(Color.ds("ink-2")).lineLimit(1)
                        }
                        .frame(minHeight: CGFloat.ds("h-touch"))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityHint(text["folderImport.details"])
                    Spacer(minLength: CGFloat.ds("s-2"))
                    actions(job)
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.vertical, CGFloat.ds("s-1"))
                if !job.finished {
                    ProgressView(value: Double(job.done), total: Double(max(job.items.count, 1)))
                        .tint(Color.ds("ink-2"))
                        .accessibilityLabel(headline(job))
                }
            }
            .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
            .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
            .dsShadow("shadow-menu")
            .sheet(isPresented: $details) { ImportDetailsSheet(job: job, text: text) }
        }
    }

    /// What the import is doing, in one line.
    private func headline(_ job: ImportJob) -> String {
        if job.finished {
            let added = text.count("folderImport.added", job.added)
            return job.failed > 0 ? added + " · " + text.count("folderImport.notAdded", job.failed) : added
        }
        if let stopped = job.stopped { return text["folderImport.paused." + stopped] }
        if job.paused { return text.format("folderImport.pausedAt", String(job.done), String(job.items.count)) }
        if runner.interrupted { return text.format("folderImport.unfinished", String(job.done), String(job.items.count)) }
        return text.format("folderImport.adding", String(min(job.done + 1, job.items.count)), String(job.items.count))
    }

    @ViewBuilder
    private func actions(_ job: ImportJob) -> some View {
        if job.finished {
            Button(text["folderImport.done"]) { runner.stop() }
                .buttonStyle(DSButtonStyle(kind: .quiet))
        } else {
            if job.paused || runner.interrupted {
                Button {
                    runner.resume()
                } label: {
                    HStack(spacing: CGFloat.ds("s-1")) {
                        Icon(name: "play", size: CGFloat.ds("icon-s"))
                        Text(text["folderImport.resume"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            } else {
                Button {
                    runner.pause()
                } label: {
                    HStack(spacing: CGFloat.ds("s-1")) {
                        Icon(name: "pause", size: CGFloat.ds("icon-s"))
                        Text(text["folderImport.pause"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
            Button(text["folderImport.stop"]) { runner.stop() }
                .buttonStyle(DSButtonStyle(kind: .quiet))
        }
    }
}

/// «Подробнее»: each file of the import and what came of it, those not added with why.
struct ImportDetailsSheet: View {
    let job: ImportJob
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    Text(job.place).overline()
                    Text(text["folderImport.detailsTitle"])
                        .dsType(.title)
                        .foregroundStyle(Color.ds("ink"))
                        .accessibilityAddTraits(.isHeader)
                }
                Spacer()
                SheetCloseButton(text: text)
            }
            Text([text.count("folderImport.added", job.added), job.failed > 0 ? text.count("folderImport.notAdded", job.failed) : nil].compactMap { $0 }.joined(separator: " · "))
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(ordered) { item in
                        HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-3")) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.title ?? item.name).dsType(.body).foregroundStyle(Color.ds("ink"))
                                if let reason = item.reason {
                                    Text(reason.hasPrefix("folderImport.") ? text[reason] : reason)
                                        .dsType(.caption)
                                        .foregroundStyle(item.state == .failed ? Color.ds("danger") : Color.ds("ink-2"))
                                }
                            }
                            Spacer(minLength: 0)
                            Text(text["folderImport.state." + item.state.rawValue]).dsType(.label).foregroundStyle(Color.ds("ink-2"))
                        }
                        .padding(.vertical, CGFloat.ds("s-2"))
                        .overlay(alignment: .bottom) { Rectangle().fill(Color.ds("hairline")).frame(height: 1) }
                    }
                }
            }
        }
        .padding(CGFloat.ds("s-6"))
        .background(Color.ds("surface").ignoresSafeArea())
        .presentationDetents([.medium, .large])
    }

    /// Those not added first, then the rest in the order of the folder.
    private var ordered: [ImportItem] {
        job.items.filter { $0.state == .failed } + job.items.filter { $0.state != .failed }
    }
}
