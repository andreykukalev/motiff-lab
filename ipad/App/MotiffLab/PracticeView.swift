import SwiftUI
import WebKit

/// Practice view (#533 11), the web app's rule (Scripts/layout/practice-view.ts) on the iPad: one state of the view that keeps
/// the page clean while the player plays. The bar's «Практика» and the selection menu's Hide enter it, the same button and
/// the controls' «Выйти из практики» leave it. It hides the selection menu, the pill, the blue playhead of the selection,
/// the roll's loop strip and the hints of the selection and of playback; the notes, the selection's band and the black
/// playhead of the piece stay, the black one still dragged and a tap still placing it (Andrei, 16:08 UTC). A tap on the
/// page also shows Play/Pause and Leave for a few seconds, as a video player does. Nothing else changes: playback, the
/// loop, the selection and the position go on, so leaving shows them as they are. View state only: not in the project
/// file, not a step of the selection's history.
struct PracticeView: Equatable {
    /// The normal workspace, practice view, or practice view with its controls shown for a while.
    enum Shown: String {
        case off, on, reveal
    }

    /// Entering or leaving, a tap on the page (it shows the controls), a touch on the shown controls (they stay a while
    /// longer), and their time over (they hide, whatever the clock says, as the timer may wake a moment early).
    enum Event {
        case enter, leave, toggle, tap, touch, hide
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

    private(set) var on: Bool
    /// Until when the controls show; nil while they do not.
    private(set) var revealUntil: Date?

    init(on: Bool = false, revealUntil: Date? = nil) {
        self.on = on
        self.revealUntil = revealUntil
    }

    /// The view after `event` at `now`.
    func after(_ event: Event, at now: Date) -> PracticeView {
        switch event {
        case .enter:
            return PracticeView(on: true)
        case .leave:
            return PracticeView()
        case .toggle:
            return on ? PracticeView() : PracticeView(on: true)
        case .tap:
            return on ? PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor)) : self
        case .touch:
            guard on, let until = revealUntil, now < until else { return self }
            return PracticeView(on: true, revealUntil: now.addingTimeInterval(Self.revealFor))
        case .hide:
            return on ? PracticeView(on: true) : self
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

    /// Whether the bar's «Практика» is lit, the kit's active state (#543 7): while the selection menu and the controls show,
    /// that is practice view off; not in practice view. It is read from the same state as what the page shows, so every way
    /// in and out (the button, the menu's Hide, «Выйти из практики», Expand left) moves the two together.
    static func lit(_ shown: Shown) -> Bool {
        chrome(shown).menu
    }

    /// How long from `now` until the controls hide again; 0 when they are not shown.
    func revealLeft(at now: Date) -> TimeInterval {
        guard on, let until = revealUntil else { return 0 }
        return max(0, until.timeIntervalSince(now))
    }

    /// What shows in each state; the selection's band and the notes always do.
    static func chrome(_ shown: Shown) -> Chrome {
        let normal = shown == .off
        return Chrome(menu: normal, transport: normal, selectionHead: normal, pieceHead: true, loopStrip: normal, hints: normal, controls: shown == .reveal)
    }
}

extension ScoreController {
    /// Practice view in force: not in Expand, which has its own bar and pill.
    var practicing: Bool { practice.on && !focused }

    /// What the page shows now.
    var practiceShown: PracticeView.Shown { practice.shown(at: Date(), focused: focused) }

    var practiceChrome: PracticeView.Chrome { PracticeView.chrome(practiceShown) }

    /// The bar's «Практика», the menu's Hide, the controls' Leave, a tap on the page or a touch on the controls.
    func practise(_ event: PracticeView.Event) {
        let now = Date()
        practice = practice.after(event, at: now)
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
        let shown = practicing ? "on" : "off"
        Task { _ = try? await webView.callAsyncJavaScript("return motiff.practice(shown)", arguments: ["shown": shown], contentWorld: .page) }
    }
}

/// Practice view's own controls (#533 11): Play/Pause and «Выйти из практики», where the pill sits, shown for a few seconds
/// after a tap on the page; a touch on them keeps them a while longer.
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
                score.practise(.leave)
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

/// The bar's «Практика» (#533 11): in and out of practice view by one press. Lit while the selection menu and the controls
/// show, dark in practice view (#543 7, PracticeView.lit); VoiceOver names it by what it shows, selected while they show.
struct PracticeButton: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        let lit = PracticeView.lit(score.practiceShown)
        Button {
            score.practise(.toggle)
        } label: {
            Icon(name: "practice", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
        }
        .buttonStyle(BarActionStyle(on: lit))
        // With a keyboard, Escape leaves practice view before it would clear the selection.
        .keyboardShortcut(score.practicing ? KeyboardShortcut(.escape, modifiers: []) : nil)
        .accessibilityLabel(text["bar.practice"])
        .accessibilityHint(text["practice.hint"])
        .accessibilityAddTraits(lit ? .isSelected : [])
    }
}
