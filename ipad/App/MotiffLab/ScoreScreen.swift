import SwiftUI
import WebKit

/// A piece's score (0018 section 2, design/kit/screens/s2_score_page.png): the score is the whole screen, A4 pages in one
/// column in portrait and two to a row in landscape, with the pencil marks on them (0019 section 2.2). Verovio's SVG is in
/// one web view that runs the web app's score modules (ScoreController, Web/score.js). Nothing is docked: the bar floats
/// over the top with the way back, the title, the pages in view and the actions, and the pill floats at the bottom with
/// play, the tempo, the metronome and the count-in. Colours come from the design kit's tokens, Daylight or Night with
/// the iPad's appearance. In phrase focus (0018 section 4) the selection lifts into a card across the screen with the
/// page dimmed behind it, previous and next on the card's edges, and the bar becomes the focus bar. The piano roll opens
/// in a drawer from the bottom (section 4), Insights in a sheet over the right third in landscape or the lower half in
/// portrait (section 6), and the Contents, the player's bookmarks and the pages, in a drawer over the left of the page in
/// landscape or the lower half in portrait (section 5); one of the three at a time.
struct ScoreScreen: View {
    let piece: OpenPiece
    private let text = UIText.current
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var account: Account
    @EnvironmentObject private var settings: DeviceSettings
    @StateObject private var player = Player()
    @StateObject private var score = ScoreController()
    @StateObject private var roll = RollController()
    @State private var more = false
    @StateObject private var insights = InsightsController()
    /// The piano roll's drawer (0018 section 4), from the bar or the selection menu.
    @State private var rollOpen = false
    /// The Insights sheet (0018 section 6), from the bar or the selection menu.
    @State private var insightsOpen = false
    @StateObject private var contents = ContentsController()
    /// The Contents drawer (0018 section 5), from the piece's title in the bar.
    @State private var contentsOpen = false
    /// The Check drawer (0018 section 9), from More or a flag on the score; where Insights goes, and one of the two.
    @State private var checkOpen = false
    /// The Settings sheet (0018 section 10), from More: over the score, which follows each change at once.
    @State private var settingsOpen = false

    /// The Insights sheet's width beside the score in landscape (ds.css `.sheet`).
    static let sheetWidth: CGFloat = 380
    /// The Contents drawer's width over the left of the page in landscape (ds.css `.sheet.left`).
    static let contentsWidth: CGFloat = 320

    var body: some View {
        GeometryReader { screen in
            let landscape = screen.size.width > screen.size.height
            page(
                drawer: rollOpen ? drawerHeight(screen.size) : 0,
                sheet: insightsOpen || checkOpen ? (landscape ? .side : .bottom(screen.size.height / 2)) : .none,
                contentsPlace: contentsOpen ? (landscape ? .side : .bottom(screen.size.height / 2)) : .none
            )
        }
    }

    /// Where the Insights sheet is: none, beside the score on the right, or over the lower half.
    enum SheetPlace: Equatable {
        case none, side
        case bottom(CGFloat)

        var bottomHeight: CGFloat {
            if case .bottom(let height) = self { return height }
            return 0
        }
    }

    /// What loops while a variation is heard: what play takes for the selection, on to the variation's end.
    private var variationRange: Range<Double>? {
        guard let range = score.playRange?.range else { return nil }
        return range.lowerBound..<max(range.upperBound, insights.trial?.end ?? 0)
    }

    /// The drawer's height: 40% of the screen in landscape and 30% in portrait, as the web app's.
    private func drawerHeight(_ size: CGSize) -> CGFloat {
        (size.width > size.height ? 0.4 : 0.3) * size.height
    }

    private var pillShown: Bool {
        (score.selection != nil && settings["guidance.transportPill"] != "playing") || player.state != .stopped
    }

    /// What the sheet over the right holds: Check, or Insights.
    @ViewBuilder private func sheetContent() -> some View {
        if checkOpen {
            CheckDrawer(score: score, text: text) { checkOpen = false }
        } else {
            InsightsSheet(insights: insights, score: score, player: player, range: variationRange, text: text) { insightsOpen = false }
        }
    }

    /// The Contents drawer where it goes: over the left of the page in landscape, from the bottom in portrait.
    @ViewBuilder private func contentsDrawer(_ place: SheetPlace) -> some View {
        switch place {
        case .none:
            EmptyView()
        case .side:
            ContentsDrawer(contents: contents, score: score, text: text) { contentsOpen = false }
                .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                .dsShadow("shadow-sheet")
                .frame(width: Self.contentsWidth)
                .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                .padding([.leading, .bottom], CGFloat.ds("s-3"))
                .transition(.move(edge: .leading).combined(with: .opacity))
        case .bottom(let height):
            VStack {
                Spacer(minLength: 0)
                ContentsDrawer(contents: contents, score: score, text: text) { contentsOpen = false }
                    .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                    .dsShadow("shadow-sheet")
                    .frame(height: height)
                    .padding(.horizontal, CGFloat.ds("s-3"))
            }
            .transition(.move(edge: .bottom))
            .ignoresSafeArea(edges: .bottom)
        }
    }

    private func page(drawer: CGFloat, sheet: SheetPlace, contentsPlace: SheetPlace) -> some View {
        // The chain is cut into parts so the compiler type-checks each on its own.
        let covered = max(drawer, sheet.bottomHeight, contentsPlace.bottomHeight)
        let laid = layers(drawer: drawer, sheet: sheet, contentsPlace: contentsPlace, covered: covered)
        return following(checking(watching(wired(laid), covered: covered, contentsSide: contentsPlace == .side)))
    }

    /// The score and everything that floats over it.
    private func layers(drawer: CGFloat, sheet: SheetPlace, contentsPlace: SheetPlace, covered: CGFloat) -> some View {
        let gap = CGFloat.ds("s-3")
        let pillBottom = covered > 0 ? covered + gap : CGFloat.ds("s-4")
        let pillTrailing: CGFloat = sheet == .side ? Self.sheetWidth + gap : 0
        let pillLeading: CGFloat = contentsPlace == .side ? Self.contentsWidth + gap : 0
        return ScoreWebView(score: score, piece: piece, dark: colorScheme == .dark)
            .overlay(alignment: .topLeading) {
                // The menu over the selection, where the page places it (the web app's menuPlace).
                if score.selection != nil, !score.focused, let place = score.menuPlace {
                    SelectionMenu(score: score, player: player, contents: contents, text: text, rollOpen: $rollOpen, insightsOpen: $insightsOpen)
                        .offset(x: place.x, y: place.y)
                }
            }
            .overlay {
                if score.focused {
                    FocusSteps(score: score, text: text)
                }
            }
            .ignoresSafeArea(edges: .bottom)
            .background(Color.ds("desk").ignoresSafeArea())
            .overlay(alignment: .top) {
                VStack(alignment: .trailing, spacing: CGFloat.ds("s-2")) {
                    if score.focused {
                        FocusBar(score: score, player: player, variationRange: variationRange, text: text)
                    } else {
                        ScoreBar(title: piece.title, pages: score.pages, text: text, player: player, score: score, more: $more, rollOpen: $rollOpen, insightsOpen: $insightsOpen, contentsOpen: $contentsOpen, checkOpen: $checkOpen, settingsOpen: $settingsOpen) { dismiss() }
                    }
                    if score.penOn && !score.focused {
                        PenPalette(score: score, text: text)
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-4"))
                .padding(.top, CGFloat.ds("s-3"))
                .animation(.easeOut(duration: 0.16), value: score.penOn)
                .animation(.easeOut(duration: 0.16), value: score.focused)
            }
            .overlay(alignment: .bottom) {
                if rollOpen {
                    RollDrawer(roll: roll, score: score, text: text) { rollOpen = false }
                        .frame(height: drawer)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .bottomTrailing) {
                switch sheet {
                case .none:
                    EmptyView()
                case .side:
                    sheetContent()
                        .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                        .dsShadow("shadow-sheet")
                        .frame(width: Self.sheetWidth)
                        .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                        .padding([.trailing, .bottom], CGFloat.ds("s-3"))
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                case .bottom(let height):
                    sheetContent()
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                        .dsShadow("shadow-sheet")
                        .frame(height: height)
                        .padding(.horizontal, CGFloat.ds("s-3"))
                        .transition(.move(edge: .bottom))
                        .ignoresSafeArea(edges: .bottom)
                }
            }
            .overlay(alignment: .topLeading) { contentsDrawer(contentsPlace) }
            .overlay(alignment: .bottom) {
                // The pill rides above whatever comes up from the bottom, and beside a sheet or a drawer at the side. It
                // shows with a selection or while the music plays, or only while it plays (the setting
                // "guidance.transportPill", 0018 section 10).
                if pillShown {
                    Pill(player: player, score: score, text: text)
                        .padding(.bottom, pillBottom)
                        .padding(.trailing, pillTrailing)
                        .padding(.leading, pillLeading)
                        .transition(.opacity)
                }
            }
            .animation(.easeOut(duration: 0.2), value: rollOpen)
            .animation(.easeOut(duration: 0.2), value: insightsOpen)
            .animation(.easeOut(duration: 0.2), value: contentsOpen)
            .ignoresSafeArea(.keyboard)
    }

    /// What the screen hands its controllers when it appears, and the project file written when it goes.
    private func wired(_ content: some View) -> some View {
        content
            .toolbar(.hidden, for: .navigationBar)
            .task { await wire() }
            .onDisappear {
                player.shutdown()
                // A variation only tried is not kept with the piece: it is left before the project file is written.
                let file = piece.file
                let id = piece.id
                let tried = insights
                Task {
                    await tried.leave()
                    if let file { try? await ProjectFiles.save(piece: id, to: file) }
                }
            }
            .onChange(of: scenePhase) { _, phase in
                // The project file is written whenever the app leaves the screen, so nothing waits for a Save button.
                if phase == .background { save() }
            }
    }

    private func wire() async {
        let heard = player
        let shown = score
        score.noteTapped = { [weak heard] tick in heard?.play(from: tick) }
        // With the sheet open, a tap on the page puts it away and leaves the selection as it is (0018 section 6).
        let sheetOpen = $insightsOpen
        score.blankTapped = {
            guard sheetOpen.wrappedValue else { return false }
            sheetOpen.wrappedValue = false
            return true
        }
        // A flag tapped on the score opens Check on its bar.
        let checking = $checkOpen
        score.checkPicked = { _ in checking.wrappedValue = true }
        insights.placeChosen = { [weak heard] tick in heard?.seek(to: tick) }
        // A way tried sounds at once, looping the selection to the variation's end, and the card in focus shows
        // it as the ossia; left, the original sounds again (0018 section 6).
        let tried = insights
        insights.variationReady = { [weak heard, weak shown, weak tried] notes, end in
            heard?.setVariation(notes)
            shown?.ossia = tried?.trial?.way.label
            if let range = shown?.playRange?.range {
                heard?.hear(.variation, over: range.lowerBound..<max(range.upperBound, end))
            }
        }
        insights.variationLeft = { [weak heard, weak shown] in
            heard?.setVariation(nil)
            shown?.ossia = nil
        }
        roll.barsPicked = { [weak shown] first, last, extend in shown?.selectBars(first, last, extend: extend) }
        roll.cleared = { [weak shown] in shown?.clearSelection() }
        roll.spacePressed = { [weak heard] in heard?.toggle() }
        // The bookmarks are drawn as brackets on the score, and again whenever they change.
        contents.changed = { [weak shown] shelf in shown?.showBookmarks(shelf) }
        await contents.load(piece: piece.id)
        await player.load(piece: piece.id)
    }

    /// The drawers and sheets: what covers the page, and one of them at a time.
    private func watching(_ content: some View, covered: CGFloat, contentsSide: Bool) -> some View {
        content
            .onChange(of: covered) { _, height in
                // Room under the last page for whatever covers the bottom of the score.
                score.insetBottom(height)
            }
            .onChange(of: contentsSide) { _, side in
                // The selection menu keeps to the right of the drawer over the left of the page.
                score.clearLeft(side ? Self.contentsWidth + CGFloat.ds("s-3") : 0)
            }
            .onChange(of: contentsOpen) { _, open in
                if open {
                    rollOpen = false
                    insightsOpen = false
                } else if contents.draft?.id != nil {
                    // A bookmark being changed in the drawer goes with it; one being made over the selection stays.
                    contents.cancel()
                }
            }
            .onChange(of: score.focused) { _, focused in
                // Focus covers the page: the drawer goes, and comes back when the player opens it again.
                if focused { contentsOpen = false }
            }
            .onChange(of: rollOpen) { _, open in
                if open {
                    contentsOpen = false
                    insightsOpen = false
                    roll.show(piece, dark: colorScheme == .dark)
                    roll.showSelection(score.selection, loop: player.loop)
                }
            }
            .onChange(of: insightsOpen) { _, open in
                if open {
                    rollOpen = false
                    contentsOpen = false
                    Task { await insights.load(piece: piece.id, selection: score.selection) }
                } else {
                    // The rings go with the sheet.
                    insights.clearChoice()
                }
            }
    }

    /// Check takes the place of the other drawers and sheets, and goes when one of them opens. The Settings sheet comes
    /// over the score, and the score and the roll follow each change at once.
    private func checking(_ content: some View) -> some View {
        content
            .sheet(isPresented: $settingsOpen) {
                SettingsSheet(settings: settings, account: account, text: text, pieceOverrides: score.pieceOverrides, clearPiece: { score.clearPieceOverrides() })
            }
            .onChange(of: settings.values) { _, _ in
                score.settingsChanged()
                roll.settingsChanged()
            }
            .onChange(of: checkOpen) { _, open in
                if open {
                    insightsOpen = false
                    rollOpen = false
                    contentsOpen = false
                }
            }
            .onChange(of: insightsOpen || rollOpen || contentsOpen || score.focused) { _, other in
                if other { checkOpen = false }
            }
    }

    /// What follows the selection, the player and the fact chosen.
    private func following(_ content: some View) -> some View {
        content
            .onChange(of: insights.chosen) { _, chosen in
                let places = chosen?.places ?? []
                score.highlight(places, scroll: player.state != .playing)
                roll.highlight(places)
            }
            .onChange(of: colorScheme) { _, scheme in
                if rollOpen { roll.show(piece, dark: scheme == .dark) }
            }
            .onChange(of: player.tick) { _, tick in
                score.playhead(tick, playing: player.state == .playing)
                if rollOpen { roll.position(tick, playing: player.state == .playing) }
            }
            .onChange(of: player.state) { old, state in
                // A new Play follows the music again, even after the player scrolled away by hand.
                score.playhead(player.tick, playing: state == .playing, follow: old != .playing && state == .playing)
                if rollOpen { roll.position(player.tick, playing: state == .playing) }
            }
            .onChange(of: score.selection) { _, selection in
                roll.showSelection(selection, loop: player.loop)
                // A bookmark being made belongs to the selection it was started on, and so does a note.
                if let draft = contents.draft, draft.id == nil { contents.cancel() }
                if let note = score.noteDraft, note.onset != selection?.onset || note.end != selection?.end { score.noteDraft = nil }
                // The stream follows the selection while it is open; a variation tried belongs to the passage it was
                // tried on, so it is left either way.
                if insightsOpen {
                    Task { await insights.load(piece: piece.id, selection: selection) }
                } else if insights.trial != nil {
                    Task { await insights.leave() }
                }
            }
            .onChange(of: player.loop) { _, loop in
                roll.showSelection(score.selection, loop: loop)
            }
            .onChange(of: score.playRange) { old, new in
                // A loop follows what play takes for the selection, as on the web: Lead-in, or going back to the page
                // with it on, loops the wider or the narrower span; a new selection stops the old loop.
                guard let loop = player.loop, loop == old?.range else { return }
                if let new, new.selection == old?.selection {
                    player.setLoop(new.range)
                } else {
                    player.setLoop(nil)
                }
            }
    }

    private func save() {
        guard let file = piece.file else { return }
        let id = piece.id
        Task { try? await ProjectFiles.save(piece: id, to: file) }
    }
}

/// The bar over the score (ds.css `.bar`): back to the library, the title, which opens the Contents the way a reader opens
/// a book at its contents, and the pages in view, then Phrases, Pen, the piano roll, Insights, Play and More, in the web
/// app's order.
struct ScoreBar: View {
    let title: String
    let pages: ScoreController.PagesInView
    let text: UIText
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    @Binding var more: Bool
    @Binding var rollOpen: Bool
    @Binding var insightsOpen: Bool
    @Binding var contentsOpen: Bool
    @Binding var checkOpen: Bool
    @Binding var settingsOpen: Bool
    let back: () -> Void
    @State private var phrasesOpen = false

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button(action: back) {
                Icon(name: "back", size: CGFloat.ds("icon-l"))
                    .frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .accessibilityLabel(text["nav.library"])
            Button {
                contentsOpen.toggle()
            } label: {
                Text(title)
                    .font(DesignTokens.font(display: true, weight: 500, size: 17, relativeTo: .headline))
                    .foregroundStyle(contentsOpen ? Color.ds("accent") : Color.ds("ink"))
                    .lineLimit(1)
                    .padding(.horizontal, CGFloat.ds("s-2"))
                    .frame(height: 40)
            }
            .buttonStyle(BarActionStyle(on: contentsOpen))
            .accessibilityLabel(text.format("contents.open", title))
            .accessibilityAddTraits(contentsOpen ? [.isHeader, .isSelected] : .isHeader)
            if let pageText {
                Text(pageText)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("ink-2"))
                    .lineLimit(1)
                    .padding(.leading, CGFloat.ds("s-2"))
            }
            Spacer(minLength: CGFloat.ds("s-2"))
            Button {
                phrasesOpen.toggle()
            } label: {
                Icon(name: "phrases", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: phrasesOpen))
            .accessibilityLabel(text["bar.phrases"])
            .popover(isPresented: $phrasesOpen) { PhrasesPanel(score: score, text: text) }
            Button {
                score.penOn.toggle()
            } label: {
                Icon(name: "pen", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: score.penOn))
            .accessibilityLabel(text["bar.pen"])
            .accessibilityAddTraits(score.penOn ? .isSelected : [])
            Button {
                rollOpen.toggle()
            } label: {
                Icon(name: "roll", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: rollOpen))
            .accessibilityLabel(text["bar.roll"])
            .accessibilityAddTraits(rollOpen ? .isSelected : [])
            Button {
                insightsOpen.toggle()
            } label: {
                Icon(name: "insight", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: insightsOpen))
            .accessibilityLabel(text["bar.insights"])
            .accessibilityAddTraits(insightsOpen ? .isSelected : [])
            Button {
                player.toggle()
            } label: {
                Icon(name: player.state == .stopped ? "play" : "pause", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .disabled(!player.ready)
            .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])
            Button {
                more.toggle()
            } label: {
                Icon(name: "more", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle(on: more))
            .accessibilityLabel(text["bar.more"])
            .popover(isPresented: $more) {
                MorePanel(score: score, text: text, problem: player.problem ?? score.problem) {
                    more = false
                    checkOpen = true
                } openSettings: {
                    more = false
                    settingsOpen = true
                }
            }
        }
        .padding(.leading, CGFloat.ds("s-2"))
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }

    /// "p. 2 of 4", or "pp. 1–2 of 4" with two pages to a row; nothing before the score is laid out.
    private var pageText: String? {
        guard pages.count > 0 else { return nil }
        return pages.first == pages.last
            ? text.format("bar.page", String(pages.first), String(pages.count))
            : text.format("bar.pages", String(pages.first), String(pages.last), String(pages.count))
    }
}

/// An action in the bar (ds.css `.bar .act`): the ink, and the accent on its tint while it is on.
struct BarActionStyle: ButtonStyle {
    var on = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(on ? Color.ds("accent") : Color.ds("ink"))
            .background(on || configuration.isPressed ? Color.ds("accent-tint") : .clear, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Rectangle())
    }
}

/// The pill (ds.css `.pill`): play, the tempo, the metronome and the count-in, floating at the bottom of the score.
struct Pill: View {
    @ObservedObject var player: Player
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            Button {
                player.toggle()
            } label: {
                Icon(name: player.state == .stopped ? "playf" : "pause")
                    .foregroundStyle(Color.ds("on-accent"))
                    .frame(width: 40, height: 40)
                    .background(Color.ds("accent"), in: Circle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])
            if score.selection != nil {
                LoopButton(score: score, player: player, text: text)
            }
            if score.focused {
                Button {
                    score.leadIn.toggle()
                } label: {
                    HStack(spacing: 6) {
                        Icon(name: "leadin")
                        Text(text["focus.leadIn"])
                    }
                }
                .buttonStyle(DSItemStyle(on: score.leadIn))
                .accessibilityHint(text["focus.leadInHint"])
                .accessibilityAddTraits(score.leadIn ? .isSelected : [])
            }
            Menu {
                Picker(text["transport.tempo"], selection: $player.tempoPercent) {
                    ForEach(Player.tempoChoices, id: \.self) { Text("\($0)%").tag($0) }
                }
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "tempo")
                    Text("\(player.tempoPercent)%").monospacedDigit()
                }
            }
            .menuStyle(.button)
            .buttonStyle(DSItemStyle(on: player.tempoPercent != 100))
            .accessibilityLabel(text.format("pill.tempoLabel", String(player.tempoPercent)))
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                player.metronome.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "metronome")
                    Text(text["transport.metronome"])
                }
            }
            .buttonStyle(DSItemStyle(on: player.metronome))
            .accessibilityAddTraits(player.metronome ? .isSelected : [])
            Button {
                player.countIn.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "count")
                    Text(text["transport.countIn"])
                }
            }
            .buttonStyle(DSItemStyle(on: player.countIn))
            .accessibilityAddTraits(player.countIn ? .isSelected : [])
        }
        .disabled(!player.ready)
        .padding(.leading, 6)
        .padding(.trailing, CGFloat.ds("s-2"))
        .frame(height: 52)
        .modifier(FloatingSurface(shadow: "shadow-pill", opacity: 0.92))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["transport.label"])
    }
}

/// Loop, in the pill and in the selection menu: the selection loops, or stops looping (0018 section 7).
struct LoopButton: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var player: Player
    let text: UIText

    var body: some View {
        let range = score.playRange?.range
        let looping = range != nil && player.loop == range
        Button {
            player.setLoop(looping ? nil : range)
        } label: {
            HStack(spacing: 6) {
                Icon(name: "loop")
                Text(text["focus.loop"])
            }
        }
        .buttonStyle(DSItemStyle(on: looping))
        .disabled(!player.ready)
        .accessibilityAddTraits(looping ? .isSelected : [])
    }
}

/// The menu over the selection (ds.css `.menu`, 0018 section 3): the actions a musician takes on a passage. It holds only
/// what applies and what the iPad has so far: Loop, Focus, Bookmark and Note, then the piano roll and Insights (none for a
/// passage without notes), and More with Highlight. Bookmark and Note turn the menu into their cards, as on the web.
struct SelectionMenu: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var player: Player
    @ObservedObject var contents: ContentsController
    let text: UIText
    @Binding var rollOpen: Bool
    @Binding var insightsOpen: Bool
    @State private var more = false

    var body: some View {
        Group {
            if score.noteDraft != nil {
                NoteCard(score: score, text: text)
                    .frame(width: 320)
                    .padding(CGFloat.ds("s-3"))
                    .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else if let draft = contents.draft, draft.id == nil {
                BookmarkCard(contents: contents, text: text) {
                    let selection = score.selection
                    let title = score.bookmarkTitle
                    Task { await contents.save(over: selection, defaultTitle: title) }
                }
                .frame(width: 300)
                .padding(.vertical, CGFloat.ds("s-3"))
                .padding(.horizontal, CGFloat.ds("s-4"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
            } else {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                    actions
                    if more {
                        Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                        HighlightRow(score: score, text: text)
                    }
                }
                .padding(.horizontal, CGFloat.ds("s-2"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .background(Color.ds("surface-2"), in: RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26))
                .overlay(RoundedRectangle(cornerRadius: more ? CGFloat.ds("r-l") : 26).strokeBorder(Color.ds("hairline")))
            }
        }
        .dsShadow("shadow-menu")
        .fixedSize()
        .background {
            GeometryReader { proxy in
                Color.clear
                    .onAppear { score.menuSize(proxy.size) }
                    .onChange(of: proxy.size) { _, size in score.menuSize(size) }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["menu.label"])
    }

    private var actions: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            LoopButton(score: score, player: player, text: text)
            if !score.penOn {
                Button {
                    score.enterFocus()
                } label: {
                    HStack(spacing: 6) {
                        Icon(name: "search")
                        Text(text["focus.enter"])
                    }
                }
                .buttonStyle(DSItemStyle())
                .accessibilityHint(text["focus.enterHint"])
            }
            item("bookmark", text["menu.bookmark"], on: false) {
                score.noteDraft = nil
                contents.start(title: score.bookmarkTitle)
            }
            item("note", text["menu.note"], on: false) {
                contents.cancel()
                score.startNote()
            }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            item("roll", text["bar.roll"], on: rollOpen) { rollOpen.toggle() }
            if !(score.selection?.noteIds.isEmpty ?? true) {
                item("insight", text["bar.insights"], on: insightsOpen) { insightsOpen.toggle() }
            }
            Button {
                more.toggle()
            } label: {
                Icon(name: "more").frame(width: 32, height: 32)
            }
            .buttonStyle(DSItemStyle(on: more || score.selectedHighlight != nil))
            .accessibilityLabel(text["menu.more"])
            .accessibilityAddTraits(more ? .isSelected : [])
        }
    }

    private func item(_ icon: String, _ label: String, on: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Icon(name: icon)
                Text(label)
            }
        }
        .buttonStyle(DSItemStyle(on: on))
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// The focus bar (0018 section 4, the web app's focus bar): Back to the page with its word, where the phrase is, and Play.
/// Escape on a keyboard goes back too. While a variation is tried, Original / Variation / Alternate choose what sounds;
/// Reading / As played comes with transcriptions on the iPad.
struct FocusBar: View {
    @ObservedObject var score: ScoreController
    @ObservedObject var player: Player
    let variationRange: Range<Double>?
    let text: UIText

    var body: some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button {
                score.leaveFocus()
            } label: {
                HStack(spacing: CGFloat.ds("s-1")) {
                    Icon(name: "back", size: CGFloat.ds("icon-l"))
                    Text(text["focus.back"]).dsType(.label)
                }
                .padding(.leading, CGFloat.ds("s-1"))
                .padding(.trailing, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
            }
            .buttonStyle(BarActionStyle())
            .keyboardShortcut(.cancelAction)
            .help(text["focus.backHint"])
            Text(score.focusPosition)
                .dsType(.label)
                .foregroundStyle(Color.ds("ink-2"))
                .monospacedDigit()
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer(minLength: CGFloat.ds("s-2"))
            if player.hasVariation, let variationRange {
                HearingSwitch(player: player, range: variationRange, text: text)
            }
            Button {
                player.toggle()
            } label: {
                Icon(name: player.state == .stopped ? "play" : "pause", size: CGFloat.ds("icon-l")).frame(width: 40, height: 40)
            }
            .buttonStyle(BarActionStyle())
            .disabled(!player.ready)
            .accessibilityLabel(player.state == .stopped ? text["transport.play"] : text["transport.pause"])
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .frame(height: CGFloat.ds("h-touch"))
        .modifier(FloatingSurface())
    }
}

/// Previous and next on the edges of the card in focus (the web app's `.focus-card .step`): 44 pt round buttons, centred
/// on the card, which the page centres between the bar and the pill (Web/score.css `#focus .stage`).
struct FocusSteps: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        HStack {
            step("back", text["focus.previous"], enabled: score.canStepBack) { score.step(-1) }
            Spacer()
            step("fwd", text["focus.next"], enabled: score.canStepOn) { score.step(1) }
        }
        // The stage's inset, the card's 2 pt border and the button's own inset inside it.
        .padding(.horizontal, CGFloat.ds("s-4") + 2 + CGFloat.ds("s-1"))
        .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-5"))
        .padding(.bottom, 52 + CGFloat.ds("s-4") * 2)
        .frame(maxHeight: .infinity)
    }

    private func step(_ icon: String, _ label: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Icon(name: icon, size: CGFloat.ds("icon-l"))
                .foregroundStyle(Color.ds("ink"))
                .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                .background(Color.ds("surface-2"), in: Circle())
                .overlay(Circle().strokeBorder(Color.ds("hairline-strong")))
                .dsShadow("shadow-menu")
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .opacity(enabled ? 1 : 0.4)
        .accessibilityLabel(label)
    }
}

/// Phrases, from the bar (0018 section 3): the level the brackets show, Short, Phrases or Sentences, and how many units
/// the piece has at it. It never lists the phrases.
struct PhrasesPanel: View {
    @ObservedObject var score: ScoreController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["phrases.levelTitle"]).overline()
            Picker(text["phrases.levelTitle"], selection: Binding(get: { score.phrases?.level ?? "phrase" }, set: { score.showLevel($0) })) {
                Text(text["phrases.levelShort"]).tag("short")
                Text(text["phrases.levelPhrases"]).tag("phrase")
                Text(text["phrases.levelSentences"]).tag("sentence")
            }
            .pickerStyle(.segmented)
            if let phrases = score.phrases {
                Text(text.count(countKey(phrases.level), phrases.counts[phrases.level] ?? phrases.units.count))
                    .dsType(.caption)
                    .foregroundStyle(Color.ds("ink-2"))
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 320, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private func countKey(_ level: String) -> String {
        switch level {
        case "short": "phrases.countShort"
        case "sentence": "phrases.countSentences"
        default: "phrases.countPhrases"
        }
    }
}

/// The pen palette under the bar while the Pen is on (ds.css `.pen-palette`): the four inks, the highlighter, the eraser
/// Undo and the layers. The Pencil writes with the chosen one; a double tap on it switches as the iPad's settings say.
struct PenPalette: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    @State private var layersOpen = false

    var body: some View {
        HStack(spacing: CGFloat.ds("s-1")) {
            ForEach(ScoreController.Ink.allCases, id: \.self) { ink in
                let chosen = score.tool == .pen(ink)
                Button {
                    score.tool = .pen(ink)
                } label: {
                    Circle()
                        .fill(Color(uiColor: ink.color))
                        .frame(width: 20, height: 20)
                        .padding(2)
                        .overlay(Circle().strokeBorder(chosen ? Color.ds("accent") : Color.ds("hairline-strong"), lineWidth: chosen ? 2 : 1))
                        .frame(width: CGFloat.ds("h-touch"), height: CGFloat.ds("h-touch"))
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(text[ink.labelKey])
                .accessibilityAddTraits(chosen ? .isSelected : [])
            }
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                score.tool = .highlighter
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "highlight", size: CGFloat.ds("icon-s"))
                    Text(text["pencil.highlighter"])
                }
            }
            .buttonStyle(DSItemStyle(on: score.tool == .highlighter))
            .accessibilityAddTraits(score.tool == .highlighter ? .isSelected : [])
            Button(text["pencil.eraser"]) {
                score.tool = .eraser
            }
            .buttonStyle(DSItemStyle(on: score.tool == .eraser))
            .accessibilityAddTraits(score.tool == .eraser ? .isSelected : [])
            Rectangle().fill(Color.ds("hairline")).frame(width: 1, height: 22).padding(.horizontal, 2)
            Button {
                score.undo()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "undo", size: CGFloat.ds("icon-s"))
                    Text(text["pencil.undo"])
                }
            }
            .buttonStyle(DSItemStyle())
            .disabled(!score.canUndo)
            Button {
                layersOpen.toggle()
            } label: {
                HStack(spacing: 6) {
                    Icon(name: "layers", size: CGFloat.ds("icon-s"))
                    Text(text["layers.title"])
                }
            }
            .buttonStyle(DSItemStyle(on: layersOpen || !score.hiddenLayers.isEmpty))
            .popover(isPresented: $layersOpen) {
                LayersList(score: score, text: text)
                    .padding(CGFloat.ds("s-4"))
                    .frame(width: 300, alignment: .leading)
                    .presentationCompactAdaptation(.popover)
            }
        }
        .padding(.horizontal, CGFloat.ds("s-2"))
        .padding(.vertical, CGFloat.ds("s-1"))
        .background(Color.ds("surface-2"), in: Capsule())
        .overlay(Capsule().strokeBorder(Color.ds("hairline")))
        .dsShadow("shadow-menu")
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["pencil.label"])
    }
}

/// More, from the bar: Check for a piece with flags, the layers of marks, what the app owes the piano and the engraving (CC BY 3.0 asks for the piano's
/// credit wherever it plays), and what went wrong, if anything did. The layout, the PDF and the other tools come with later
/// steps.
struct MorePanel: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    let problem: String?
    let openCheck: () -> Void
    let openSettings: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let problem {
                Text(problem)
                    .dsType(.label)
                    .foregroundStyle(Color.ds("danger"))
            }
            if score.check.hasFlags {
                Button(action: openCheck) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "check")
                        Text(score.check.open > 0 ? text.count("check.toReview", score.check.openBars.count) : text["check.title"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            }
            LayersList(score: score, text: text)
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Button(action: openSettings) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Icon(name: "dial")
                    Text(text["settings.all"])
                }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            Rectangle().fill(Color.ds("hairline")).frame(height: 1)
            Text(credit)
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .tint(Color.ds("accent"))
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 340, alignment: .leading)
        .presentationCompactAdaptation(.popover)
    }

    private var credit: AttributedString {
        let markdown = "\(text["credit.piano"]) [Salamander Grand Piano V3](https://archive.org/details/SalamanderGrandPianoV3) \(text["credit.by"]) Alexander Holm, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), \(text["credit.shortened"]) \(text["credit.engraving"]) [Verovio](https://www.verovio.org) 6.3.0, [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html), [\(text["credit.source"])](https://github.com/rism-digital/verovio/tree/version-6.3.0)."
        return (try? AttributedString(markdown: markdown)) ?? AttributedString(markdown)
    }
}

struct ScoreWebView: UIViewRepresentable {
    @ObservedObject var score: ScoreController
    let piece: OpenPiece
    let dark: Bool

    /// A web view made at launch, so the first score does not wait for WebKit's process to start (0019).
    @MainActor private static var warm: WKWebView?

    @MainActor static func prewarm() {
        guard warm == nil else { return }
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.loadHTMLString("<!doctype html><title></title>", baseURL: nil)
        warm = view
    }

    func makeUIView(context: Context) -> WKWebView {
        score.webView
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        score.show(piece, dark: dark)
    }
}
