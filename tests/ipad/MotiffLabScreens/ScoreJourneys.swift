import XCTest

/// The journeys on the example piece's score (architecture blueprint 6.1, layer T4): each asserts what a player does and
/// sees, through the controls and the app's probe, and names the entries of the UX baseline (`docs/ux-baseline.md`) it
/// protects. Each starts and ends on the score with nothing over the page, no selection and nothing playing.
extension Walk {
    /// The example piece opened from the empty library and played: the sound loads and the playing moves on.
    func openExampleAndPlay() throws {
        try openExample(shots: true)
        for (label, what) in [("Библиотека", "Back"), ("Фразы", "Phrases"), ("Фортепианный ролл", "the piano roll"), ("Инсайты", "Insights"),
                              ("Навигация", "Navigate"), ("Выделение", "Select"), ("Перо", "the Pen"), ("Источники", "Sources")] {
            try need(button(label), "\(what) in the score's bar")
        }
        try need(more, "More in the score's bar")
        try need(button(beginning: "Играть с"), "the pill's Play")
        shot("10-score", settle: 1.5)
        try play("on the piece")
        try stop()
    }

    /// PLAY-04: a selection, its loop, and a new selection while it plays. Цикл turns on as a selection appears (T9),
    /// follows a new selection while it plays (the playing goes to its start), and stays as it was when the selection
    /// changes with it off (T10).
    func selectLoopAndChange() throws {
        try choosePhrase()
        try until("Цикл on as the selection appeared (PLAY-04)") { $0.on("cycle") }
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
    /// from the notes plays inside the passage. PLAY-01 (the card's playhead dragged by its dot) is not walked: the dot is
    /// drawn in the page, and the probe does not say where.
    func rollAndExpand() throws {
        try choosePhrase()
        try press(button("Фортепианный ролл"), "the piano roll in the bar")
        try until("the roll drawer open and drawn", timeout: 15) { $0["roll"] == "drawer" && $0["zoom"] != "none" }
        try need(button(beginning: "Потяните"), "the roll drawer's handle")
        let open = try settledZoom("the roll drawer's zoom")
        shot("15-piano-roll", settle: 1)
        try choosePhrase()
        var seen = open
        try holds(for: 1.5, "the roll's zoom changed with the selection from \(open) (ROLL-01)") { probe in
            seen = probe["zoom"] ?? "none"
            return zoomKept(open, seen)
        }
        askIfMoved(open, seen, "after a new selection")

        try expandFromDrawer()
        try until("Expand from the drawer shows the passage as a roll", timeout: 8) { $0.on("focus") && $0["as"] == "roll" && $0["roll"] == "focus" }
        try playRoundThePassage()
        try press(button("Свернуть"), "Collapse in Expand's bar")
        try until("Collapse gives the roll drawer back", timeout: 8) { !$0.on("focus") && $0["roll"] == "drawer" && $0["zoom"] != "none" }
        let back = try settledZoom("the roll drawer's zoom after Collapse")
        guard zoomKept(open, back) else { throw miss("Expand changed the roll's zoom from \(open) to \(back) (ROLL-01)") }
        askIfMoved(open, back, "after Expand and Collapse")
        try press(button("Фортепианный ролл"), "the piano roll in the bar again")
        try until("the roll drawer put away") { $0["roll"] == "none" }

        try press(button("Фокус"), "Expand in the selection menu")
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

    /// ROLL-01 as the walk can see it, and a question (2026-11-17). The probe reads the zoom control's rounded numbers,
    /// bars and octaves in view (`barsInView` in Scripts/roll/roll-zoom.ts), not the scale. Today the bars go 19 → 18 when
    /// the roll scrolls to a new selection at the start of a piece with a short first bar. Whether a changing number with
    /// the scale kept is a zoom change is a question for Andrei; until he answers, the octaves must stay and the bars may
    /// move by one. The exact scale is pinned on the page by the page scenario `roll-zoom-kept` (PR #646).
    private func zoomKept(_ open: String, _ now: String) -> Bool {
        let was = open.split(separator: "x").compactMap { Int($0) }
        let shown = now.split(separator: "x").compactMap { Int($0) }
        guard was.count == 2, shown.count == 2 else { return false }
        return was[1] == shown[1] && abs(was[0] - shown[0]) <= 1
    }

    /// The question in the walk's log each run while the numbers differ.
    private func askIfMoved(_ open: String, _ now: String, _ when: String) {
        guard now != open else { return }
        print("SCREEN-QUESTION ROLL-01 \(tag) \(when): the roll's zoom control said \(open), now \(now) (bars x octaves in view)")
    }

    /// Expand in the roll drawer's head, or in its More where the head is narrow.
    private func expandFromDrawer() throws {
        let expand = button("Развернуть")
        if expand.exists && expand.isHittable {
            try press(expand, "Expand in the roll drawer")
        } else {
            try press(button(id: "roll.more"), "the roll drawer's More")
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
        try need(button(beginning: "Спросить помощника"), "the Insights sheet")
        let tryIt = button("Попробовать")
        try need(tryIt, "Try on a fact of the phrase", timeout: 20)
        shot("16-insights", settle: 1)
        try press(tryIt, "Try")
        let way = button(id: "insights.tryWay")
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
        try gone(button(beginning: "Спросить помощника"), "the Insights sheet")
        try clearSelection()
    }

    /// INK-01: with the Pen a stroke writes and does not scroll the page; the eraser drawn over it takes it away. The
    /// simulator has no stylus: one finger writes here («Только Apple Pencil» off), so the entry's finger rule (a finger
    /// scrolls) needs the device.
    func penAndEraser() throws {
        try press(button("Перо"), "the Pen in the bar's modes")
        let before = try until("the Pen on") { $0.on("pen") }
        try press(button("Карандаш"), "the pencil in the pen's palette")
        let from = CGVector(dx: 0.30, dy: 0.42)
        let to = CGVector(dx: 0.36, dy: 0.62)
        stroke(from: from, to: to)
        let written = try until("a stroke written with the Pen") { ($0.int("ink") ?? 0) > (before.int("ink") ?? 0) }
        guard written["scroll"] == before["scroll"] else {
            throw WalkMiss(what: "the Pen's stroke scrolled the page from \(before["scroll"] ?? "?") (INK-01)", probe: written.line)
        }
        shot("28-pen-palette")
        try press(button("Ластик"), "the eraser in the pen's palette")
        stroke(from: from, to: to)
        try until("the stroke erased") { ($0.int("ink") ?? 0) < (written.int("ink") ?? 0) }
        try press(button("Убрать карандаш"), "Put the pen away")
        try until("the Pen off") { !$0.on("pen") }
    }

    /// The assistant's panel opened from More on the whole piece, and closed.
    func assistantPanel() throws {
        try press(more, "More")
        try press(button("Спросить о пьесе"), "Ask about the piece in More")
        try until("the assistant's panel open") { $0.on("assistant") }
        try need(button("Новый разговор"), "the assistant's panel")
        shot("35-assistant", settle: 1)
        try press(button("Закрыть"), "Close on the assistant's panel")
        try until("the assistant's panel closed") { !$0.on("assistant") }
    }

    /// The other places of the score for the mockups: Phrases, a bookmark, the Contents, More and Settings, Sources; then
    /// back to the library with the example's card.
    func otherPlaces() throws {
        try press(button("Фразы"), "Phrases")
        try need(app.otherElements["PopoverDismissRegion"], "the Phrases popover")
        shot("12-phrases-levels")
        dismissPopover()

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
