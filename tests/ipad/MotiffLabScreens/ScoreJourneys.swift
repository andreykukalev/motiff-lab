import XCTest

/// The journeys on the example piece's score (architecture blueprint 6.1, layer T4): each asserts what a player does and
/// sees, through the controls and the app's probe, and names the entries of the UX baseline (`docs/ux-baseline.md`) it
/// protects. Each starts and ends on the score with nothing over the page, no selection and nothing playing.
extension Walk {
    /// The example piece opened from the empty library and played: the sound loads and the playing moves on.
    func openExampleAndPlay() throws {
        try openExample(shots: true)
        for (label, what) in [("Библиотека", "Back"), ("Выбрать, что показывать", "the list of extra information"), ("Инсайты", "Insights"),
                              ("Навигация", "Navigate"), ("Выделение", "Select"), ("Перо", "the Pen"), ("Источники", "Sources")] {
            try need(button(label), "\(what) in the score's bar")
        }
        try need(more, "More in the score's bar")
        try need(button(beginning: "Играть с"), "the pill's Play")
        // The piano roll and the assistant are the pill's (Andrei's 0.8 test, findings 7 and 8).
        try need(button("Фортепианный ролл"), "the piano roll in the pill")
        try need(button("Ассистент"), "the assistant in the pill")
        shot("10-score", settle: 1.5)
        try play("on the piece")
        try stop()
    }

    /// PLAY-04: a selection, its loop, and a new selection while it plays. Цикл is on from the launch and kept with no
    /// selection (Player.cycle), so the first selection finds it on; it follows a new selection while it plays (the playing
    /// goes to its start), stays as it was when the selection changes with it off (T10) and when the selection goes (T11),
    /// and a selection made where there was none turns it on again (T9).
    func selectLoopAndChange() throws {
        try choosePhrase()
        try until("Цикл on with the first selection, as it is from the launch") { $0.on("cycle") }
        try need(button("Фокус"), "the selection menu")
        try need(button(beginning: "Цикл "), "the pill's Цикл with the selection's span")
        shot("13-selection-menu")
        try press(button("Ещё для выделения"), "the selection menu's More")
        try need(button("Спросить"), "Ask in the selection menu's More")
        shot("14-selection-menu-more")
        try press(button("Ещё для выделения"), "the selection menu's More again")

        try play("on the selection")
        let looping = try until("playing inside the selection") { $0.playing && $0.insideRange }
        try choosePhrase()
        try until("Цикл follows the new selection while it plays: the playing goes to it and loops it (PLAY-04)") {
            $0.playing && $0.on("cycle") && $0["range"] != looping["range"] && $0.insideRange
        }
        try press(button(beginning: "Цикл "), "Цикл")
        try until("Цикл off") { !$0.on("cycle") }
        try choosePhrase()
        try holds(for: 1.5, "Цикл turned on again by a new selection while it was off (PLAY-04)") { !$0.on("cycle") }
        try stop()
        try clearSelection()
        try until("Цикл kept off with the selection gone (PLAY-04)") { !$0.on("cycle") }
        try choosePhrase()
        try until("Цикл on as a selection appeared where there was none (PLAY-04)") { $0.on("cycle") }
        try clearSelection()
    }

    /// SEL-03: in Select a drag begun outside the selection replaces it in one gesture, «Выделение заменено · Вернуть»
    /// says so, and «Вернуть» brings the old one back. (A drag begun inside it extends it, SEL-02: not walked here.)
    func selectByDragAndOver() throws {
        let first = try choosePhrase()
        guard let made = first.span("sel") else { throw miss("no selection made by a drag in Select") }
        let second = try choosePhrase()
        guard let new = second.span("sel"), !new.overlaps(made) else {
            throw WalkMiss(what: "a drag begun outside the selection did not replace it (SEL-03)", probe: second.line)
        }
        try need(app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", "Выделение заменено")).firstMatch, "«Выделение заменено» (SEL-03)")
        try press(button("Вернуть"), "«Вернуть» on the toast (SEL-03)")
        try until("«Вернуть» brings the selection replaced back (SEL-03)") { $0["sel"] == first["sel"] }
        try clearSelection()
    }

    /// ROLL-01, PLAY-03: the roll's drawer keeps its zoom when the selection changes and through Expand; Expand from the
    /// drawer shows the passage as a roll, Play there plays it round and round, and Collapse gives the drawer back; Expand
    /// from the selection menu opens in the view used last, the roll (Andrei, 2026-11-17), and in the notes it plays inside
    /// the passage. PLAY-01 (the card's playhead dragged by its dot) is not walked: the dot is drawn in the page, and the
    /// probe does not say where.
    func rollAndExpand() throws {
        try choosePhrase()
        try press(pillRoll(), "the piano roll in the pill")
        try until("the roll drawer open and drawn", timeout: 15) { $0["roll"] == "drawer" && $0["zoom"] != "none" }
        try need(button(beginning: "Потяните"), "the roll drawer's handle")
        let open = try settledZoom("the roll drawer's zoom")
        shot("15-piano-roll", settle: 1)
        try choosePhrase()
        try holds(for: 1.5, "the roll's zoom changed with the selection from \(open) (ROLL-01)") { probe in
            zoomKept(open, probe["zoom"] ?? "none")
        }

        try expandFromDrawer()
        try until("Expand from the drawer shows the passage as a roll", timeout: 8) { $0.on("focus") && $0["as"] == "roll" && $0["roll"] == "focus" }
        try playRoundThePassage()
        try press(button("Свернуть"), "Collapse in Expand's bar")
        try until("Collapse gives the roll drawer back", timeout: 8) { !$0.on("focus") && $0["roll"] == "drawer" && $0["zoom"] != "none" }
        let back = try settledZoom("the roll drawer's zoom after Collapse")
        guard zoomKept(open, back) else { throw miss("Expand changed the roll's zoom from \(open) to \(back) (ROLL-01)") }
        try press(button("Свернуть"), "Collapse on the roll's head")
        try until("the roll drawer put away") { $0["roll"] == "none" }

        try press(button("Фокус"), "Expand in the selection menu")
        // Expand opened from the selection menu shows the view Expand was in last, here the roll chosen from the drawer, not
        // always the notes (PLAY-03, Andrei, 2026-11-17).
        try until("Expand from the selection menu in the view it was in last, the roll", timeout: 8) { $0.on("focus") && $0["as"] == "roll" }
        try press(button("Ноты"), "Notes in Expand's bar")
        try until("Expand from the notes", timeout: 8) { $0.on("focus") && $0["as"] == "notes" }
        try need(button("К странице"), "Back to the page in Expand's bar")
        shot("21-focus", settle: 1.5)
        let playing = try play("in Expand from the notes")
        guard playing.insideRange else { throw WalkMiss(what: "Expand played outside its passage", probe: playing.line) }
        try stop()
        try press(button("К странице"), "Back to the page")
        try until("back on the page") { !$0.on("focus") }
        try clearSelection()
    }

    /// The roll's zoom once it has stayed the same for 1.5 s: the roll reports the bars and octaves in view, which change
    /// while its drawer opens.
    private func settledZoom(_ what: String) throws -> String {
        var last = "none"
        var since = Date()
        let probe = try until("\(what) settled", timeout: 10) { probe in
            let zoom = probe["zoom"] ?? "none"
            if zoom != last {
                last = zoom
                since = Date()
            }
            return zoom != "none" && Date().timeIntervalSince(since) >= 1.5
        }
        return probe["zoom"] ?? "none"
    }

    /// ROLL-01 as the walk can see it. The probe reads the zoom control's rounded numbers, bars and octaves in view
    /// (`barsInView` in Scripts/roll/roll-zoom.ts), not the scale: the bars go 19 → 18 when the roll scrolls to a new
    /// selection at the start of a piece with a short first bar. The zoom is the note width, and the number shown may
    /// differ by one (Andrei, 2026-11-17): the octaves must stay and the bars may move by one. The exact scale is pinned on
    /// the page by the page scenario `roll-zoom-kept`.
    private func zoomKept(_ open: String, _ now: String) -> Bool {
        let was = open.split(separator: "x").compactMap { Int($0) }
        let shown = now.split(separator: "x").compactMap { Int($0) }
        guard was.count == 2, shown.count == 2 else { return false }
        return was[1] == shown[1] && abs(was[0] - shown[0]) <= 1
    }

    /// Expand in the roll drawer's head, or in its More where the head is narrow.
    private func expandFromDrawer() throws {
        let expand = button("Развернуть")
        if expand.exists && expand.isHittable {
            try press(expand, "Expand in the roll drawer")
        } else {
            try press(button(id: "ml-screens-roll-more"), "the roll drawer's More")
            try press(button("Развернуть"), "Expand in the roll drawer's More")
        }
    }

    /// In Expand the pill's Play plays the passage and comes round to its start again (PLAY-03: the passage loops), never
    /// playing outside it.
    private func playRoundThePassage() throws {
        let first = try play("in Expand")
        guard first.insideRange else { throw WalkMiss(what: "Expand played outside its passage (PLAY-03)", probe: first.line) }
        var furthest = first.tick
        try until("Expand's passage played round to its start again (PLAY-03)", timeout: 25) { probe in
            guard probe.playing, probe.insideRange else { return false }
            furthest = max(furthest, probe.tick)
            return probe.tick < furthest - 120
        }
        try stop()
    }

    /// Insights on a phrase, and a way tried from a fact: it sounds as a variation until it is left.
    func insightsAndTry() throws {
        try choosePhrase()
        try press(button("Инсайты"), "Insights")
        try need(button(beginning: "Спросить ассистента"), "the Insights sheet")
        let tryIt = button("Попробовать")
        try need(tryIt, "Try on a fact of the phrase", timeout: 20)
        shot("16-insights", settle: 1)
        try press(tryIt, "Try")
        let way = button(id: "ml-screens-try-way")
        try need(way, "a way to try")
        shot("18-insights-try")
        try press(way, "a way to try")
        try until("the way tried sounds as a variation", timeout: 15) { $0.on("variation") }
        try need(button("Отказаться"), "Leave on the variation tried")
        shot("19-insights-variation", settle: 1)
        try press(button("Отказаться"), "Leave")
        try until("the variation left", timeout: 8) { !$0.on("variation") }
        if probe()?.playing == true { try stop() }
        try press(button("Инсайты"), "Insights again")
        try gone(button(beginning: "Спросить ассистента"), "the Insights sheet")
        try clearSelection()
    }

    /// INK-01, the half the simulator sees: with the Pen a stroke writes and the page stands; the eraser drawn over it takes
    /// it away. The simulator has no stylus and runs with «Только Apple Pencil» off, where every pointer acts as a finger and
    /// a finger writes with the Pen (InputRules), as INK-01 says; «a finger scrolls» holds with the setting on, on the device.
    func penAndEraser() throws {
        try placePlayheadAside()
        try press(button("Перо"), "the Pen in the bar's modes")
        let before = try until("the Pen on") { $0.on("pen") }
        try choose(button("Карандаш"), "the pencil in the pen's palette")
        let from = CGVector(dx: 0.30, dy: 0.42)
        let to = CGVector(dx: 0.36, dy: 0.62)
        stroke(from: from, to: to)
        let written = try until("a stroke written with the Pen (the black playhead at \(before.tick) before it)") {
            ($0.int("ink") ?? 0) > (before.int("ink") ?? 0)
        }
        guard written["scroll"] == before["scroll"] else {
            throw WalkMiss(what: "the Pen's stroke scrolled the page from \(before["scroll"] ?? "?") (INK-01)", probe: written.line)
        }
        shot("28-pen-palette")
        try choose(button("Ластик"), "the eraser in the pen's palette")
        stroke(from: from, to: to)
        try until("the stroke erased") { ($0.int("ink") ?? 0) < (written.int("ink") ?? 0) }
        try press(button("Убрать карандаш"), "Put the pen away")
        try until("the Pen off") { !$0.on("pen") }
    }

    /// The black playhead put on a beat across the page from where the Pen's strokes start, by a tap in Navigate (a tap on a
    /// beat moves it there). With the Pen a finger that presses within reach of the playhead's handle (InputRules.handleReach)
    /// drags the playhead and writes nothing, and the journeys before leave it wherever their playing stopped (run 4: the
    /// stroke wrote nothing, most likely begun at the handle, which Insights' variation had left in the selection beside the
    /// stroke's start). A tap that selected instead (the page not yet in Navigate) is a miss, not a pass.
    private func placePlayheadAside() throws {
        if probe()?["mode"] != "navigate" {
            try press(button("Навигация"), "Navigate in the bar's modes")
            try until("Navigate chosen") { $0["mode"] == "navigate" }
        }
        let before = probe()?.tick
        app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.80, dy: Self.selectRow)).tap()
        try until("the black playhead on the beat tapped in Navigate, nothing selected") {
            !$0.playing && $0.tick != before && $0["sel"] == "none"
        }
    }

    /// The assistant's panel opened from More on the whole piece, and closed; then the pill's assistant opens it and closes
    /// it.
    func assistantPanel() throws {
        try press(more, "More")
        try press(button("Спросить о пьесе"), "Ask about the piece in More")
        try until("the assistant's panel open") { $0.on("assistant") }
        try need(button("Новый разговор"), "the assistant's panel")
        shot("35-assistant", settle: 1)
        try press(button("Закрыть"), "Close on the assistant's panel")
        try until("the assistant's panel closed") { !$0.on("assistant") }
        // The pill's assistant opens the panel again and closes it (Andrei's 0.8 test, finding 8).
        try press(button("Ассистент"), "the assistant in the pill")
        try until("the pill's assistant opens the panel") { $0.on("assistant") }
        try press(button("Ассистент"), "the assistant in the pill again")
        try until("the pill's assistant closes the panel") { !$0.on("assistant") }
    }

    /// The other places of the score for the mockups: the list of extra information with the phrases' marks first, a
    /// bookmark, the Contents, More and Settings, Sources; then back to the library with the example's card.
    func otherPlaces() throws {
        try press(button("Выбрать, что показывать"), "the list of extra information")
        try need(app.switches["Фразы"], "the phrases' marks in the list (the phrases spec, v1.2)")
        shot("12-extra-phrases")
        try dismissPopover()

        try choosePhrase()
        try press(button("Закладка"), "Bookmark in the selection menu")
        try need(button("Готово"), "the new bookmark's card")
        shot("22-bookmark-card", settle: 1)
        try press(button("Готово"), "Done on the bookmark's card")
        shot("24-marks-on-page", settle: 1)
        try clearSelection()

        try press(title, "the title, which opens the Contents")
        try need(button("Страницы"), "the Contents drawer")
        shot("25-contents", settle: 1)
        // The chips scroll sideways: where Notes is off the drawer's width, its picture is not taken.
        let notes = button("Заметки")
        if notes.exists && notes.isHittable {
            notes.tap()
            shot("26-contents-notes")
        }
        try press(button("Страницы"), "Pages in the Contents")
        shot("27-contents-pages", settle: 2)
        try press(title, "the title again")

        try press(more, "More")
        try need(button("Все настройки"), "All settings in More")
        shot("30-more")
        try press(button("Все настройки"), "All settings")
        try need(button("Закрыть настройки"), "the Settings sheet")
        shot("32-settings", settle: 1)
        try press(button("Оформление"), "Appearance in Settings")
        shot("33-settings-appearance")
        try press(button("Закрыть настройки"), "Close settings")

        try press(button("Источники"), "Sources in the bar")
        try need(button("Закрыть другие версии"), "the Sources sheet")
        shot("50-sources", settle: 1)
        try press(button("Закрыть другие версии"), "Close Sources")

        try press(button("Библиотека"), "Back to the library")
        try need(button("Добавить пьесу"), "the library with the example's card")
        shot("04-library", settle: 2)
    }
}
