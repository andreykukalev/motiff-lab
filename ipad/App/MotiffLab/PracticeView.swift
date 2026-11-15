import SwiftUI
import WebKit

/// The workspace's two visibilities (#582 item 10) over practice view (#533 11), the web app's rule
/// (Scripts/layout/practice-view.ts) on the iPad. Two states, each changed without the other:
/// - the selection controls (`selectionHidden`): the selection menu and the hints of the selection (cleared · Undo, how to
///   hold); the selection's band stays;
/// - the playback controls (`on`, practice view as it was): the pill, the hint back to the playing, the blue playhead of the
///   selection and the roll's loop strip; the black playhead of the piece stays, still dragged, and a tap on the page shows
///   Play/Pause and «Выйти из практики» (the playback controls back) for a few seconds, as a video player does.
/// Since v0.6.4 (spec 9) the bar's button only opens their two switches: no press hides or shows both, and nothing is
/// worked out from the two (a "Show on the score" over them unchecked itself whenever either was hidden). The selection
/// menu's Hide hides the selection controls only, practice view's Leave shows the playback controls only. Nothing else
/// changes: playback, the loop, the selection and the position go on, so showing them again shows them as they are. Kept on
/// the device (`saved`, `keep`), so they stay as the player set them when the Notes screen is made again (PDF ↔ Notes,
/// another piece); not in the project file, not a step of the selection's history.
struct PracticeView: Equatable {
    /// The playback controls shown (the normal workspace), hidden (practice view), or hidden with practice view's own
    /// controls shown for a while.
    enum Shown: String {
        case off, on, reveal
    }

    /// One group shown or hidden on its own (#582 item 10, v0.6.4 spec 9), a tap on the page (it shows practice view's
    /// controls), a touch on the shown controls (they stay a while longer), and their time over (they hide, whatever the
    /// clock says, as the timer may wake a moment early).
    enum Event {
        case tap, touch, hide
        case selection(shown: Bool)
        case playback(shown: Bool)
    }

    /// Which of the elements over the music the page shows.
    struct Chrome: Equatable {
        var menu: Bool
        var transport: Bool
        var selectionHead: Bool
        var pieceHead: Bool
        var loopStrip: Bool
        var hints: Bool
        var controls: Bool
    }

    /// How long a tap shows the controls, as the web app's REVEAL_MS.
    static let revealFor: TimeInterval = 3

    /// The playback controls hidden: practice view.
    private(set) var on: Bool
    /// Until when practice view's own controls show; nil while they do not.
    private(set) var revealUntil: Date?
    /// The selection controls hidden (#582 item 10): the selection menu and its hints. Track A's selection popup reads it
    /// through `ScoreController.selectionControlsShown`.
    private(set) var selectionHidden: Bool

    init(on: Bool = false, revealUntil: Date? = nil, selectionHidden: Bool = false) {
        self.on = on
        self.revealUntil = on ? revealUntil : nil
        self.selectionHidden = selectionHidden
    }

    /// The view after `event` at `now`: the selection controls change only by their own event, the playback controls only by
    /// theirs.
    func after(_ event: Event, at now: Date) -> PracticeView {
        switch event {
        case .tap:
            return on ? PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor), selectionHidden: selectionHidden) : self
        case .touch:
            guard on, let until = revealUntil, now < until else { return self }
            return PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor), selectionHidden: selectionHidden)
        case .hide:
            return on ? PracticeView(on: true, selectionHidden: selectionHidden) : self
        case let .selection(shown):
            return PracticeView(on: on, revealUntil: revealUntil, selectionHidden: !shown)
        case let .playback(shown):
            if shown { return PracticeView(selectionHidden: selectionHidden) }
            return on ? self : PracticeView(on: true, selectionHidden: selectionHidden)
        }
    }

    func shown(at now: Date) -> Shown {
        guard on else { return .off }
        guard let until = revealUntil, now < until else { return .on }
        return .reveal
    }

    /// What the score's page shows at `now`, in Expand or not: Expand has its own bar and pill, so in it the page is the
    /// normal workspace and leaving it shows practice view again if it was on.
    func shown(at now: Date, focused: Bool) -> Shown {
        focused ? .off : shown(at: now)
    }

    /// Whether the selection controls show at all, in Expand or not: Expand has its own bar, so in it they are as normal.
    func selectionShown(focused: Bool) -> Bool {
        focused || !selectionHidden
    }

    /// What the page shows at `now`, in Expand or not.
    func chrome(at now: Date, focused: Bool) -> Chrome {
        Self.chrome(shown(at: now, focused: focused), selectionShown: selectionShown(focused: focused))
    }

    /// Where the two switches are kept on the device: whether each group shows (true by default, the normal workspace).
    static let selectionKey = "score.selectionControlsShown"
    static let playbackKey = "score.playbackControlsShown"

    /// The two visibilities as the player last set them on this device; both shown the first time.
    static func saved(in defaults: UserDefaults = .standard) -> PracticeView {
        func shown(_ key: String) -> Bool { defaults.object(forKey: key) as? Bool ?? true }
        return PracticeView(on: !shown(playbackKey), selectionHidden: !shown(selectionKey))
    }

    /// Keeps the two visibilities on the device; practice view's controls shown for a moment are not kept.
    func keep(in defaults: UserDefaults = .standard) {
        defaults.set(!selectionHidden, forKey: Self.selectionKey)
        defaults.set(!on, forKey: Self.playbackKey)
    }

    /// What the bar's button says it holds, for VoiceOver: which groups are hidden now, empty while both show.
    var hiddenKeys: [String] {
        (selectionHidden ? ["practice.selectionControls"] : []) + (on ? ["practice.playbackControls"] : [])
    }

    /// How long from `now` until the controls hide again; 0 when they are not shown.
    func revealLeft(at now: Date) -> TimeInterval {
        guard on, let until = revealUntil else { return 0 }
        return max(0, until.timeIntervalSince(now))
    }

    /// What shows in each state of the playback controls and of the selection controls, each group by its own state only
    /// (#582 item 10); the selection's band, the notes and the black playhead always do. `hints` are the selection's (cleared
    /// · Undo, how to hold); the hint back to the playing goes with the pill (`transport`).
    static func chrome(_ shown: Shown, selectionShown: Bool = true) -> Chrome {
        let playback = shown == .off
        return Chrome(
            menu: selectionShown, transport: playback, selectionHead: playback, pieceHead: true, loopStrip: playback,
            hints: selectionShown, controls: shown == .reveal
        )
    }
}

extension ScoreController {
    /// Practice view in force: not in Expand, which has its own bar and pill.
    var practicing: Bool { practice.on && !focused }

    /// What the page shows now.
    var practiceShown: PracticeView.Shown { practice.shown(at: Date(), focused: focused) }

    var practiceChrome: PracticeView.Chrome { practice.chrome(at: Date(), focused: focused) }

    /// The selection controls visible (#582 item 10): the selection menu and its hints. The state track A's selection popup
    /// and gestures read; set only by its switch and the menu's Hide, never by the playback controls.
    var selectionControlsShown: Bool { practice.selectionShown(focused: focused) }

    /// The playback controls visible (#582 item 10): the pill, the blue playhead, the loop strip, the hint back to playing.
    var playbackControlsShown: Bool { !practicing }

    /// The two switches, the menu's Hide, practice view's Leave, a tap on the page or a touch on the controls. What the
    /// switches set is kept on the device (PracticeView.keep).
    func practise(_ event: PracticeView.Event) {
        let now = Date()
        let before = practice
        practice = practice.after(event, at: now)
        if practice.on != before.on || practice.selectionHidden != before.selectionHidden { practice.keep() }
        practiceTimer?.cancel()
        let left = practice.revealLeft(at: now)
        if left > 0 {
            practiceTimer = Task { [weak self] in
                try? await Task.sleep(for: .seconds(left))
                guard !Task.isCancelled, let self else { return }
                self.practice = self.practice.after(.hide, at: Date())
            }
        }
    }

    /// Tells the page whether practice view is on: it hides the blue playhead and stops offering it to the Pencil (score.js).
    /// ScoreScreen calls it whenever `practicing` changes, Expand entered or left included.
    func sendPractice() {
        Task { await sendVisibilities() }
    }

    /// Tells the page both visibilities as they are now: as it is first drawn (`render`), so a page made again shows what
    /// the switches say, whatever it showed before (v0.6.4, spec 9).
    func sendVisibilities() async {
        let shown = practicing ? "on" : "off"
        _ = try? await webView.callAsyncJavaScript("return motiff.practice(shown)", arguments: ["shown": shown], contentWorld: .page)
        _ = try? await webView.callAsyncJavaScript("return motiff.selectionControls(shown)", arguments: ["shown": selectionControlsShown], contentWorld: .page)
    }

    /// Tells the page whether the selection controls show (#582 item 10): hidden, the selection's handles go with the menu
    /// and the Pencil writes where they were (score.js). ScoreScreen calls it whenever `selectionControlsShown` changes.
    func sendSelectionControls(_ shown: Bool) {
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.selectionControls(shown)", arguments: ["shown": shown], contentWorld: .page) }
    }
}

/// Practice view's own controls (#533 11): Play/Pause and «Выйти из практики», where the pill sits, shown for a few seconds
/// after a tap on the page; a touch on them keeps them a while longer. Leaving shows the playback controls again (their
/// switch turns on with it) and leaves the selection controls as they are (#582 item 10).
struct PracticeControls: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        let playing = player.state != .stopped
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                score.practise(.touch)
                // The pill's own Play: the playback model's press under one position, else Play and Pause as before.
                if score.onePosition { player.apply(.press) } else { player.toggle() }
            } label: {
                Icon(name: playing ? "pause" : "playf")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(text[playing ? "transport.pause" : "transport.play"])
            Button {
                score.practise(.playback(shown: true))
            } label: {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "practice")
                    Text(text["practice.leave"]).dsType(.label)
                }
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: 36)
            }
            .buttonStyle(DSItemStyle())
        }
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["practice.controls"])
    }
}

/// The bar's button of the two visibilities (v0.6.4, spec 9): one press opens their two switches, as Phrases opens its
/// list; it hides and shows nothing itself, so no third state stands over the two. Lit while the list is open; VoiceOver
/// says which groups are hidden. With a keyboard, Escape in practice view shows the playback controls again, as Leave does.
struct PracticeButton: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    @State private var listOpen = false

    var body: some View {
        Button {
            listOpen.toggle()
        } label: {
            HStack(spacing: 0) {
                Icon(name: "practice", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
                Icon(name: "chevd", size: CGFloat.ds("icon-s")).padding(.trailing, CGFloat.ds("s-1"))
            }
        }
        .buttonStyle(BarActionStyle(on: listOpen))
        .accessibilityLabel(text["practice.list"])
        .accessibilityValue(score.practice.hiddenKeys.map { text[$0] }.joined(separator: ", "))
        .accessibilityHint(text["practice.switchesHint"])
        .popover(isPresented: $listOpen) {
            WorkspaceControlsList(score: score, text: text)
                .padding(CGFloat.ds("s-4"))
                .frame(width: 300, alignment: .leading)
                .presentationCompactAdaptation(.popover)
        }
        .background {
            if score.practicing {
                Button("") { score.practise(.playback(shown: true)) }
                    .keyboardShortcut(.escape, modifiers: [])
                    .opacity(0)
                    .accessibilityHidden(true)
                    .allowsHitTesting(false)
            }
        }
    }
}

/// The two visibilities of the workspace (#582 item 10, v0.6.4 spec 9), each its own switch and nothing over them: the
/// selection controls (the menu and its hints) and the playback controls (the pill, the blue playhead, the loop strip);
/// turning one never moves the other, and each shows the state the page shows.
struct WorkspaceControlsList: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            toggle(text["practice.selectionControls"], isOn: !score.practice.selectionHidden) { score.practise(.selection(shown: $0)) }
            toggle(text["practice.playbackControls"], isOn: !score.practice.on) { score.practise(.playback(shown: $0)) }
            Text(text["practice.listHint"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func toggle(_ label: String, isOn: Bool, set: @escaping (Bool) -> Void) -> some View {
        Toggle(label, isOn: Binding(get: { isOn }, set: set))
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .tint(Color.ds("accent"))
    }
}
