import AVFoundation
import SwiftUI

/// The views switch in the bar (version 0.6; sources sheet handoff, board s5b): every view
/// of the piece music-core lists (`sources.layers`), the working notes first, then by file. Three views or fewer are the
/// segmented choice of PDF | Notes, with a hairline between files and the working notes' dot after the word; four or
/// more are a menu button with the shown view's pictogram, its word and a chevron, its popover grouped by file; one view
/// is no switch. It sits after the title, where PDF | Notes was, in music-core's order: a PDF before the Notes read from
/// it (#582 item 2).
struct SourcesViewSwitch: View {
    let layers: SourceLayers
    /// The view shown now: an original's, Notes' or a recording's id.
    let current: String?
    let text: UIText
    let pick: (SourceLayers.ViewEntry) -> Void
    let openSources: () -> Void
    @State private var menuOpen = false

    var body: some View {
        let views = layers.shownViews
        if views.count >= 4 {
            menuButton(views)
        } else if views.count >= 2 {
            segmented(views)
        }
    }

    private func isCurrent(_ view: SourceLayers.ViewEntry) -> Bool {
        view.id == (current ?? layers.working)
    }

    /// Three views or fewer: today's segmented choice, a hairline between files.
    private func segmented(_ views: [SourceLayers.ViewEntry]) -> some View {
        HStack(spacing: 0) {
            ForEach(Array(views.enumerated()), id: \.element.id) { index, view in
                if index > 0 && views[index - 1].group != view.group {
                    Rectangle().fill(Color.ds("hairline-strong")).frame(width: 1).padding(.vertical, CGFloat.ds("s-1"))
                }
                let on = isCurrent(view)
                Button {
                    if !on { pick(view) }
                } label: {
                    HStack(spacing: CGFloat.ds("s-1")) {
                        ZStack {
                            // The word in the chosen weight, unseen, holds the option's width either way.
                            Text(SourceLayers.word(view, text)).dsType(.label).fontWeight(.semibold).lineLimit(1).hidden().accessibilityHidden(true)
                            Text(SourceLayers.word(view, text))
                                .dsType(.label)
                                .fontWeight(on ? .semibold : .medium)
                                .foregroundStyle(on ? Color.ds("ink") : Color.ds("ink-2"))
                                .lineLimit(1)
                        }
                        if view.working { WorkingDot() }
                    }
                    .modifier(SegmentFill(on: on))
                }
                .buttonStyle(.plain)
                .accessibilityLabel(label(view))
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .modifier(SegmentedCapsule())
        .accessibilityLabel(text["pdf.views"])
    }

    /// Four views or more: the shown view's pictogram, word and dot, and a chevron, in the same capsule.
    private func menuButton(_ views: [SourceLayers.ViewEntry]) -> some View {
        let shown = views.first(where: isCurrent) ?? views[0]
        return Button {
            menuOpen.toggle()
        } label: {
            HStack(spacing: CGFloat.ds("s-1")) {
                Icon(name: SourceLayers.pictogram(shown), size: CGFloat.ds("icon-s"))
                Text(SourceLayers.word(shown, text)).dsType(.label).fontWeight(.semibold).lineLimit(1)
                if shown.working { WorkingDot() }
                Icon(name: "chevd", size: CGFloat.ds("icon-s"))
            }
            .foregroundStyle(Color.ds("ink"))
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(height: CGFloat.ds("h-compact"))
            .background(menuOpen ? Color.ds("accent-tint-strong") : Color.ds("surface"), in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline")))
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .fixedSize()
        .accessibilityLabel(text["pdf.views"])
        .accessibilityValue(label(shown))
        .popover(isPresented: $menuOpen) {
            SourcesViewMenu(layers: layers, views: views, current: shown.id, text: text) { view in
                menuOpen = false
                if view.id != shown.id { pick(view) }
            } openSources: {
                menuOpen = false
                openSources()
            }
            .presentationCompactAdaptation(.popover)
        }
    }

    private func label(_ view: SourceLayers.ViewEntry) -> String {
        let word = SourceLayers.word(view, text)
        return view.working ? word + ", " + text["sources.sheet.working"] : word
    }
}

/// The working notes' mark in the switch: a small dot in the ink after the word.
struct WorkingDot: View {
    var body: some View {
        Circle().fill(Color.ds("ink")).frame(width: 5, height: 5).accessibilityHidden(true)
    }
}

/// «Рабочие»: the working notes' tag, in the ink.
struct WorkingTag: View {
    let text: UIText

    var body: some View {
        Text(text["sources.sheet.working"])
            .dsType(.caption)
            .fontWeight(.semibold)
            .foregroundStyle(Color.ds("surface"))
            .padding(.horizontal, CGFloat.ds("s-2"))
            .background(Color.ds("ink"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
    }
}

/// The switch's popover (300 pt, on the second surface): a group per file in the switch's order, its caption the file's
/// icon and name, then its views, the working notes with «Рабочие» and their file and date, then the Notes of no file;
/// the shown view tinted with a check; at the foot «Источники», with how many views are hidden, which opens the sheet.
struct SourcesViewMenu: View {
    let layers: SourceLayers
    let views: [SourceLayers.ViewEntry]
    let current: String
    let text: UIText
    let pick: (SourceLayers.ViewEntry) -> Void
    let openSources: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(groups, id: \.self) { group in
                if let original = layers.original(group) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: SourceLayers.pictogram(kind: original.kind), size: CGFloat.ds("icon-s"))
                        Text(original.shown).dsType(.caption).lineLimit(1)
                    }
                    .foregroundStyle(Color.ds("ink-2"))
                    .padding(.horizontal, CGFloat.ds("s-3"))
                    .padding(.top, CGFloat.ds("s-2"))
                    .accessibilityAddTraits(.isHeader)
                }
                ForEach(views.filter { $0.group == group }) { view in item(view) }
                Divider().overlay(Color.ds("hairline"))
            }
            ForEach(views.filter { $0.group == nil }) { view in item(view) }
            if views.contains(where: { $0.group == nil }) { Divider().overlay(Color.ds("hairline")) }
            Button(action: openSources) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "layers")
                    Text(text["sources.title"]).dsType(.bodyStrong)
                    Spacer(minLength: CGFloat.ds("s-2"))
                    if layers.hiddenCount > 0 {
                        Text(text.format("sources.sheet.hidden", String(layers.hiddenCount))).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                    }
                }
                .foregroundStyle(Color.ds("ink"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(minHeight: CGFloat.ds("h-touch"))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
        .padding(.vertical, CGFloat.ds("s-1"))
        .frame(width: 300)
        .background(Color.ds("surface-2"))
    }

    /// The files in the switch's order: a PDF before the Notes read from it, the working ones among them (#582 item 2).
    private var groups: [String] {
        var seen: [String] = []
        for view in views {
            if let group = view.group, !seen.contains(group) { seen.append(group) }
        }
        return seen
    }

    private func item(_ view: SourceLayers.ViewEntry) -> some View {
        let on = view.id == current
        let notes = layers.notes(view.id)
        return Button { pick(view) } label: {
            HStack(alignment: .top, spacing: CGFloat.ds("s-3")) {
                Icon(name: SourceLayers.pictogram(view)).foregroundStyle(Color.ds("ink-2"))
                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(SourceLayers.word(view, text)).dsType(.bodyStrong).foregroundStyle(Color.ds("ink"))
                        if view.working { WorkingTag(text: text) }
                    }
                    if let line = line(view, notes), !line.isEmpty {
                        Text(line).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                if on {
                    Icon(name: "check", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("accent"))
                }
            }
            .padding(.horizontal, CGFloat.ds("s-3"))
            .padding(.vertical, CGFloat.ds("s-2"))
            .frame(minHeight: CGFloat.ds("h-touch"))
            .background(on ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .padding(.horizontal, CGFloat.ds("s-1"))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// The working notes: «{files} · {date}»; other Notes: «{engine} · {date}».
    private func line(_ view: SourceLayers.ViewEntry, _ notes: SourceLayers.Notes?) -> String? {
        guard let notes else { return nil }
        let date = notes.at.map { SourceLayers.shortDate($0, text) }
        if view.working {
            return [layers.title(of: notes, text), date].compactMap { $0 }.joined(separator: " · ")
        }
        return [notes.engine, date].compactMap { $0 }.joined(separator: " · ")
    }
}

/// The Sources button after the switch: the layers icon, as the bar's other actions, tinted while the sheet is open, and
/// «Источники» for VoiceOver and the pointer (#582 item 2: with its word it took the room the title needs on the score).
/// It stays when there is no switch.
struct SourcesButton: View {
    let open: Bool
    let text: UIText
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Icon(name: "layers", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: open))
        .fixedSize()
        .help(text["sources.title"])
        .accessibilityLabel(text["sources.title"])
        .accessibilityAddTraits(open ? .isSelected : [])
    }
}

/// What was just done in the Sources sheet, as the score's toast (MergeToastView): a light capsule with a quiet
/// «Отменить» and ✕.
struct SourcesToastView: View {
    @ObservedObject var sources: SourcesController
    let toast: SourcesToast
    let text: UIText

    var body: some View {
        UndoToast(words: toast.text, text: text, undo: toast.undo != nil ? { Task { await sources.undo() } } : nil) { sources.toast = nil }
            .task(id: toast.id) {
                // A toast without Undo goes by itself; one with Undo stays a while longer.
                try? await Task.sleep(for: .seconds(toast.undo == nil ? 4 : 10))
                if sources.toast?.id == toast.id { sources.toast = nil }
            }
    }
}

/// The «Исполнение» view on the iPad: the recording's sound kept on this iPad, heard as it is, from a capsule under the
/// bar while the score stays the working notes. Its slider moves through the recording, the time beside it (Andrei, v0.8
/// finding 20). ✕ goes back to the notes.
struct RecordingViewCapsule: View {
    let url: URL
    let title: String
    let text: UIText
    let close: () -> Void
    @StateObject private var listening = RecordingListening()

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                listening.toggle(url)
            } label: {
                Icon(name: listening.playing ? "pause" : "play", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: listening.playing))
            .accessibilityLabel(listening.playing ? text["sources.sheet.pause"] : text.format("sources.sheet.listen", title))
            Icon(name: "mic", size: CGFloat.ds("icon-s")).foregroundStyle(Color.ds("ink-2"))
            Text(title).dsType(.label).foregroundStyle(Color.ds("ink")).lineLimit(1)
            Slider(
                value: Binding(get: { Double(listening.elapsed) }, set: { listening.seek(to: Int($0.rounded())) }),
                in: 0...Double(max(1, listening.length))
            ) { editing in
                listening.holding = editing
            }
            .tint(Color.ds("accent"))
            // Five fingertips wide: room to aim at a minute of a long recording.
            .frame(width: CGFloat.ds("h-touch") * 5)
            .disabled(listening.length == 0)
            .accessibilityLabel(text["sources.sheet.seek"])
            .accessibilityValue(text.format("sources.sheet.seekValue", SourceLayers.duration(listening.elapsed), SourceLayers.duration(listening.length)))
            Text(SourceLayers.duration(listening.elapsed) + " / " + SourceLayers.duration(listening.length))
                .dsType(.caption)
                .monospacedDigit()
                .foregroundStyle(Color.ds("ink-2"))
            Button(action: close) {
                Icon(name: "close", size: CGFloat.ds("icon-s")).frame(width: 28, height: 28)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["bar.close"])
        }
        .controlShape(.capsule)
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
        .onAppear { listening.load(url) }
        .onDisappear { listening.stop() }
    }
}

/// The sound of a recording kept on this iPad, heard as it is (AVAudioPlayer).
@MainActor
final class RecordingListening: ObservableObject {
    @Published private(set) var playing = false
    /// Where it is and how long it is, in milliseconds.
    @Published private(set) var elapsed = 0
    @Published private(set) var length = 0
    /// The player's finger is on the slider: the place it shows is the finger's, not the sound's.
    var holding = false
    private var player: AVAudioPlayer?
    private var timer: Timer?

    func load(_ url: URL) {
        guard player == nil else { return }
        player = try? AVAudioPlayer(contentsOf: url)
        player?.prepareToPlay()
        length = Int(((player?.duration ?? 0) * 1000).rounded())
    }

    func toggle(_ url: URL) {
        load(url)
        guard let player else { return }
        if player.isPlaying {
            player.pause()
            playing = false
            timer?.invalidate()
        } else {
            // Heard with the ring switch on silent too, as the score's own playback is (Player).
            try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .default)
            try? AVAudioSession.sharedInstance().setActive(true)
            player.play()
            playing = true
            timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
                Task { @MainActor in self?.tick() }
            }
        }
    }

    func stop() {
        player?.stop()
        playing = false
        timer?.invalidate()
        timer = nil
    }

    /// Moves the sound to a place, in milliseconds, kept within the recording; playing, it goes on from there.
    func seek(to milliseconds: Int) {
        guard let player else { return }
        let place = min(max(0, milliseconds), length)
        player.currentTime = Double(place) / 1000
        elapsed = place
    }

    private func tick() {
        guard let player, !holding else { return }
        elapsed = Int((player.currentTime * 1000).rounded())
        if !player.isPlaying {
            playing = false
            timer?.invalidate()
        }
    }
}
