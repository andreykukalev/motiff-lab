import UIKit
import XCTest

/// Screenshots of the app's screens for the redesign's mockups, in Russian, in landscape and portrait and in the dark
/// appearance (the `Screens` scheme, run by the `iPad` workflow by hand or for a release tag). It walks the app as a
/// player would: the empty library, Add piece, the example piece and what floats over its score, back to the library,
/// then a PDF piece. A screen that cannot be reached is written down as SCREEN-MISS and the walk goes on, so one missing
/// button costs one picture, not the run. The pictures go to the folder in SCREENS_DIR (the workflow passes it as
/// TEST_RUNNER_SCREENS_DIR) and are attached to the test result as well.
@MainActor
final class ScreensUITests: XCTestCase {
    private var app: XCUIApplication!
    private var tag = ""
    /// The app's own theme for the walk, Daylight or Night, through its kept settings (DeviceSettings.storeKey).
    private var theme = "daylight"

    func testLandscape() { walk(.landscapeLeft, dark: false) }

    func testPortrait() { walk(.portrait, dark: false) }

    func testLandscapeDark() { walk(.landscapeLeft, dark: true) }

    func testPortraitDark() { walk(.portrait, dark: true) }

    // MARK: - The walk

    private func walk(_ orientation: UIDeviceOrientation, dark: Bool) {
        continueAfterFailure = true
        tag = (orientation.isLandscape ? "land" : "port") + (dark ? "-dark" : "")
        theme = dark ? "night" : "daylight"
        XCUIDevice.shared.orientation = orientation
        launch(["-ml-screens-reset"])

        library()
        score()
        backToLibrary()
        pdf()

        app.terminate()
        XCUIDevice.shared.orientation = .portrait
    }

    private func library() {
        guard wait(app.buttons["Добавить пьесу"].firstMatch, "library") else { return }
        shot("02-library-empty")
        app.buttons["Добавить пьесу"].firstMatch.tap()
        let demo = button(beginning: "Попробовать демо")
        guard wait(demo, "add piece") else { return }
        shot("03-add-piece")
        demo.tap()
    }

    private func score() {
        guard wait(app.buttons["Фразы"], "score", timeout: 60) else { return }
        if !wait(bracket(0), "phrase brackets in the web view", timeout: 15) {
            // What the web view shows to accessibility, for the next time the walk is tuned.
            print("SCREEN-TREE \(tag):\n\(app.webViews.firstMatch.debugDescription.prefix(6000))")
        }
        pause(2)
        shot("10-score")

        // The phrases popover: three levels and the brackets switch.
        if tap(app.buttons["Фразы"], "phrases") {
            shot("12-phrases-levels")
            dismissPopover()
        }

        // A phrase chosen on its bracket, and the menu over it.
        if choosePhrase(), wait(button(beginning: "Фокус"), "selection menu") {
            shot("13-selection-menu")
            if tap(app.buttons["Ещё для выделения"], "menu more") {
                shot("14-selection-menu-more")
                tap(app.buttons["Ещё для выделения"], "menu more again")
            }
        }

        // The piano roll in its drawer.
        if tap(app.buttons["Фортепианный ролл"].firstMatch, "roll") {
            pause(2)
            shot("15-piano-roll")
            tap(app.buttons["Фортепианный ролл"].firstMatch, "roll again")
        }

        // Insights on the phrase, and a way tried from a fact.
        if tap(app.buttons["Инсайты"].firstMatch, "insights") {
            pause(2)
            shot("16-insights")
            let tryIt = app.buttons.matching(NSPredicate(format: "label == 'Попробовать'")).firstMatch
            if tap(tryIt, "try") {
                pause(2)
                shot("18-insights-try")
            }
            tap(app.buttons["Инсайты"].firstMatch, "insights again")
        }

        // Focus on the phrase, with the variation tried as an ossia when there is one.
        if tap(button(beginning: "Фокус"), "focus") {
            pause(2)
            shot("21-focus")
            tap(button(beginning: "Назад к странице"), "back to the page")
            pause(1)
        }

        // A bookmark over another phrase (a second tap on the chosen one would lift it into focus again).
        leaveFocus()
        if choosePhrase(), tap(button(beginning: "Закладка"), "bookmark") {
            pause(1)
            shot("22-bookmark-card")
            tap(app.buttons["Готово"], "bookmark done")
            pause(1)
            shot("24-marks-on-page")
        }

        // The contents drawer and its three tabs.
        leaveFocus()
        let title = app.buttons.matching(NSPredicate(format: "label ENDSWITH 'открыть оглавление'")).firstMatch
        if tap(title, "contents") {
            pause(1)
            shot("25-contents")
            if tap(app.buttons["Заметки"].firstMatch, "notes tab") { shot("26-contents-notes") }
            if tap(app.buttons["Страницы"].firstMatch, "pages tab") {
                pause(2)
                shot("27-contents-pages")
            }
            tap(title, "contents again")
        }

        // The pen with its palette, and a stroke drawn with a finger.
        if tap(app.buttons["Перо"], "pen") {
            let window = app.windows.firstMatch
            let from = window.coordinate(withNormalizedOffset: CGVector(dx: 0.3, dy: 0.45))
            let to = window.coordinate(withNormalizedOffset: CGVector(dx: 0.55, dy: 0.47))
            from.press(forDuration: 0.1, thenDragTo: to)
            pause(1)
            shot("28-pen-palette")
            tap(app.buttons["Перо"], "pen again")
        }

        // More, then all the settings, then other versions.
        if tap(app.buttons["Ещё"].firstMatch, "more") {
            shot("30-more")
            if tap(button(beginning: "Все настройки"), "all settings") {
                pause(1)
                shot("32-settings")
                if tap(app.buttons["Оформление"].firstMatch, "appearance section") { shot("33-settings-appearance") }
                if !tap(app.buttons["Закрыть настройки"], "close settings") { app.swipeDown() }
                pause(1)
            } else {
                dismissPopover()
            }
        }
        if tap(app.buttons["Ещё"].firstMatch, "more for versions"), tap(button(beginning: "Добавить версию"), "add version") {
            pause(1)
            shot("50-versions-drawer")
            tap(app.buttons["Закрыть другие версии"], "close versions")
        }
    }

    private func backToLibrary() {
        leaveFocus()
        guard tap(app.buttons["Библиотека"].firstMatch, "back to the library") else { return }
        if wait(app.buttons["Добавить пьесу"].firstMatch, "library with a card") {
            pause(2)
            shot("04-library")
        }
    }

    /// A PDF piece (0023): pages as they were added, the pen, the metronome and More.
    private func pdf() {
        guard let file = makePdf() else { return miss("pdf file") }
        app.terminate()
        launch(["-ml-screens-open", file.path])
        guard wait(app.buttons["Метроном"].firstMatch, "pdf piece", timeout: 30) else { return }
        pause(2)
        shot("40-pdf-piece")
        if tap(app.buttons["Перо"], "pdf pen") {
            let window = app.windows.firstMatch
            window.coordinate(withNormalizedOffset: CGVector(dx: 0.3, dy: 0.4))
                .press(forDuration: 0.1, thenDragTo: window.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.42)))
            pause(1)
            shot("41-pdf-pen")
            tap(app.buttons["Перо"], "pdf pen again")
        }
        if tap(app.buttons["Метроном"].firstMatch, "metronome") {
            pause(1)
            shot("42-pdf-metronome")
            tap(app.buttons["Убрать метроном"], "close metronome")
        }
        if tap(app.buttons["Ещё"].firstMatch, "pdf more") {
            shot("43-pdf-more")
            dismissPopover()
        }
    }

    // MARK: - Helpers

    private func launch(_ arguments: [String]) {
        app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages", "(ru)", "-AppleLocale", "ru_RU", "-motiff.settings", "{\"appearance.theme\" = \"\(theme)\";}", "-ml-screens"] + arguments
        app.launch()
    }

    /// Back to the page when a step left the phrase in focus, where the bar is the focus bar.
    private func leaveFocus() {
        let back = button(beginning: "Назад к странице")
        if back.exists {
            back.tap()
            pause(1)
        }
    }

    /// Another phrase chosen: by the app's hidden button (ScreensHook in Debug builds), which picks phrase 2, then 3, so
    /// a second choice never lifts the first into focus.
    private func choosePhrase() -> Bool {
        let chooser = app.buttons["ml-screens-phrase"]
        guard chooser.waitForExistence(timeout: 5) else {
            miss("phrase chooser")
            return false
        }
        chooser.tap()
        pause(1.5)
        return true
    }

    private func bracket(_ index: Int) -> XCUIElement {
        app.webViews.buttons.matching(NSPredicate(format: "label CONTAINS[c] 'фраз' OR label CONTAINS[c] 'предлож'")).element(boundBy: index)
    }

    private func button(beginning prefix: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    @discardableResult
    private func wait(_ element: XCUIElement, _ name: String, timeout: TimeInterval = 10) -> Bool {
        if element.waitForExistence(timeout: timeout) { return true }
        miss(name)
        return false
    }

    @discardableResult
    private func tap(_ element: XCUIElement, _ name: String) -> Bool {
        guard element.waitForExistence(timeout: 8), element.isHittable else {
            miss(name)
            return false
        }
        element.tap()
        pause(0.8)
        return true
    }

    /// A popover closes on a tap outside it.
    private func dismissPopover() {
        let region = app.otherElements["PopoverDismissRegion"]
        if region.exists { region.tap() } else { app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.98)).tap() }
        pause(0.8)
    }

    private func pause(_ seconds: TimeInterval) {
        Thread.sleep(forTimeInterval: seconds)
    }

    private func miss(_ name: String) {
        print("SCREEN-MISS \(tag): \(name)")
    }

    private func shot(_ name: String) {
        pause(0.6)
        let screenshot = XCUIScreen.main.screenshot()
        let png = upright(screenshot.image)
        let attachment = XCTAttachment(screenshot: screenshot)
        attachment.name = "\(name)-\(tag)"
        attachment.lifetime = .keepAlways
        add(attachment)
        if let folder = ProcessInfo.processInfo.environment["SCREENS_DIR"], !folder.isEmpty {
            let url = URL(fileURLWithPath: folder).appendingPathComponent("\(name)-\(tag).png")
            do { try png.write(to: url) } catch { print("SCREEN-WRITE \(url.path): \(error)") }
        }
        print("SCREEN \(name)-\(tag)")
    }

    /// The screen comes in the device's portrait pixels; in landscape (left) it is turned so the picture reads upright.
    private func upright(_ image: UIImage) -> Data {
        guard tag.hasPrefix("land"), let cg = image.cgImage else { return image.pngData() ?? Data() }
        let turned = UIImage(cgImage: cg, scale: 1, orientation: .left)
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        return UIGraphicsImageRenderer(size: turned.size, format: format).pngData { _ in turned.draw(at: .zero) }
    }

    /// A three-page A4 PDF that looks like printed music from far away: a title, then ten systems of two staves a
    /// page, with bar lines. It stands in for a player's own PDF, which never goes into the repository.
    private func makePdf() -> URL? {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("Elise.pdf")
        let page = CGRect(x: 0, y: 0, width: 595, height: 842)
        let renderer = UIGraphicsPDFRenderer(bounds: page)
        do {
            try renderer.writePDF(to: url) { context in
                for number in 1...3 {
                    context.beginPage()
                    let cg = context.cgContext
                    var top: CGFloat = 70
                    if number == 1 {
                        let title = NSAttributedString(string: "Für Elise", attributes: [.font: UIFont(name: "Times New Roman", size: 26) ?? UIFont.systemFont(ofSize: 26)])
                        title.draw(at: CGPoint(x: (page.width - title.size().width) / 2, y: 48))
                        let composer = NSAttributedString(string: "L. van Beethoven", attributes: [.font: UIFont(name: "Times New Roman", size: 12) ?? UIFont.systemFont(ofSize: 12)])
                        composer.draw(at: CGPoint(x: page.width - 60 - composer.size().width, y: 88))
                        top = 120
                    }
                    cg.setStrokeColor(UIColor.black.cgColor)
                    cg.setLineWidth(0.6)
                    var system = top
                    while system + 60 < page.height - 50 {
                        for staff in 0..<2 {
                            let y = system + CGFloat(staff) * 38
                            for line in 0..<5 {
                                cg.move(to: CGPoint(x: 50, y: y + CGFloat(line) * 4.5))
                                cg.addLine(to: CGPoint(x: page.width - 50, y: y + CGFloat(line) * 4.5))
                            }
                        }
                        for bar in 0...4 {
                            let x = 50 + (page.width - 100) * CGFloat(bar) / 4
                            cg.move(to: CGPoint(x: x, y: system))
                            cg.addLine(to: CGPoint(x: x, y: system + 56))
                        }
                        cg.strokePath()
                        cg.setFillColor(UIColor.black.cgColor)
                        for note in 0..<16 {
                            let x = 70 + (page.width - 140) * CGFloat(note) / 16
                            let y = system + CGFloat((note * 5) % 9) * 2.25
                            cg.fillEllipse(in: CGRect(x: x, y: y, width: 5.5, height: 4.2))
                        }
                        system += 84
                    }
                    let folio = NSAttributedString(string: "\(number)", attributes: [.font: UIFont.systemFont(ofSize: 10)])
                    folio.draw(at: CGPoint(x: page.width / 2, y: page.height - 34))
                }
            }
            return url
        } catch {
            print("SCREEN-WRITE \(url.path): \(error)")
            return nil
        }
    }
}
