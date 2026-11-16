import UIKit
import XCTest

/// A step of a journey that did not hold: the control or screen the walk expected, and the app's probe line then.
struct WalkMiss: Error, CustomStringConvertible {
    let what: String
    let probe: String?

    var description: String { what + (probe.map { " · probe: \($0)" } ?? "") }
}

/// The app's own state as its Debug probe says it (`ScreensProbe.line()`, read from the element `ml-screens-probe`):
/// `screen=score ready=1 … sel=480-2400 range=480-2400 focus=0 … play=stopped tick=0 cycle=1 …`.
struct Probe {
    let line: String
    private let fields: [String: String]

    init(_ line: String) {
        self.line = line
        var fields: [String: String] = [:]
        for word in line.split(separator: " ") {
            guard let equals = word.firstIndex(of: "=") else { continue }
            fields[String(word[..<equals])] = String(word[word.index(after: equals)...])
        }
        self.fields = fields
    }

    subscript(_ name: String) -> String? { fields[name] }

    func on(_ name: String) -> Bool { fields[name] == "1" }

    func int(_ name: String) -> Int? { fields[name].flatMap { Int($0) } }

    /// A span in ticks, `480-2400`; nil for `none`.
    func span(_ name: String) -> Range<Int>? {
        let ends = (fields[name] ?? "").split(separator: "-").compactMap { Int($0) }
        guard ends.count == 2, ends[0] < ends[1] else { return nil }
        return ends[0]..<ends[1]
    }

    var screen: String { fields["screen"] ?? "" }
    var ready: Bool { on("ready") }
    var playing: Bool { fields["play"] == "playing" }
    var tick: Int { int("tick") ?? -1 }

    /// The playing tick inside what Play takes for the selection (in Expand, its passage).
    var insideRange: Bool { span("range")?.contains(tick) ?? false }
}

/// The app as the walk drives it (ScreensUITests): launched in Russian in one appearance, the place a journey starts from,
/// the probe, and the steps a journey takes. A step that does not find what it expects throws `WalkMiss`.
@MainActor
final class Walk {
    /// Where a journey starts or ends: the app just launched with no pieces, the example piece open on its score with
    /// nothing over the page, a piece with notes and its PDF open on its notes, a PDF piece, or the library with pieces.
    enum Start: Equatable {
        case fresh, example, notesAndPdf, pdf, library
    }

    let tag: String
    private let theme: String
    private unowned let test: XCTestCase
    private(set) var app = XCUIApplication()
    /// Where the app is between journeys; nil after a journey failed, so the next one launches again.
    var state: Start?

    init(tag: String, dark: Bool, test: XCTestCase) {
        self.tag = tag
        theme = dark ? "night" : "daylight"
        self.test = test
    }

    // MARK: - Launching

    func launch(_ arguments: [String]) {
        app.terminate()
        app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages", "(ru)", "-AppleLocale", "ru_RU", "-motiff.settings", "{\"appearance.theme\" = \"\(theme)\";}", "-ml-screens"] + arguments
        app.launch()
        state = nil
    }

    /// The app brought to where a journey starts: launched again unless it is there already.
    func arrive(at start: Start) throws {
        guard state != start else { return }
        switch start {
        case .fresh:
            launch(["-ml-screens-reset"])
        case .example:
            launch(["-ml-screens-reset"])
            try openExample(shots: false)
        case .notesAndPdf:
            guard let file = ScreensPdf.write(name: "Elise-notes.pdf") else { throw miss("no PDF written for the piece with notes") }
            launch(["-ml-screens-reset", "-ml-screens-both", "-ml-screens-open", file.path])
            try until("the piece with notes and its PDF open on its notes", timeout: 120) { $0.screen == "score" && $0.ready }
        case .pdf:
            guard let file = ScreensPdf.write() else { throw miss("no PDF written") }
            launch(["-ml-screens-reset", "-ml-screens-open", file.path])
            try until("the PDF piece open", timeout: 60) { $0.screen == "pdf" && $0.ready }
        case .library:
            launch([])
            try need(button("Добавить пьесу"), "the library", timeout: 20)
        }
        state = start
    }

    /// The example piece from the empty library, as a player first opens it: Add piece, then the example.
    func openExample(shots: Bool) throws {
        try need(button("Добавить пьесу"), "Add piece in the empty library", timeout: 20)
        if shots { shot("02-library-empty") }
        try press(button("Добавить пьесу"), "Add piece")
        let demo = button(beginning: "Попробовать демо")
        try need(demo, "the example piece in Add piece")
        if shots { shot("03-add-piece") }
        try press(demo, "the example piece")
        try until("the example piece's score drawn", timeout: 60) { $0.screen == "score" && $0.ready }
    }

    // MARK: - The probe

    func probe() -> Probe? {
        let element = app.descendants(matching: .any)["ml-screens-probe"]
        return element.exists ? Probe(element.label) : nil
    }

    /// Waits until the probe says `holds`; the probe then, or a miss naming `what` with the last line read.
    @discardableResult
    func until(_ what: String, timeout: TimeInterval = 5, _ holds: (Probe) -> Bool) throws -> Probe {
        let start = Date()
        var last: Probe?
        repeat {
            if let probe = probe() {
                last = probe
                if holds(probe) { return probe }
            }
            pause(0.25)
        } while Date().timeIntervalSince(start) < timeout
        throw WalkMiss(what: "\(what): not so after \(Int(timeout)) s", probe: last?.line)
    }

    /// The probe says `holds` all along for `seconds`; a miss at the first line that does not.
    func holds(for seconds: TimeInterval, _ what: String, _ holds: (Probe) -> Bool) throws {
        let start = Date()
        repeat {
            if let probe = probe(), !holds(probe) { throw WalkMiss(what: what, probe: probe.line) }
            pause(0.25)
        } while Date().timeIntervalSince(start) < seconds
    }

    func miss(_ what: String) -> WalkMiss {
        WalkMiss(what: what, probe: probe()?.line)
    }

    // MARK: - Controls

    func button(_ label: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label == %@", label)).firstMatch
    }

    func button(beginning prefix: String) -> XCUIElement {
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", prefix)).firstMatch
    }

    func button(id: String) -> XCUIElement {
        app.buttons.matching(identifier: id).firstMatch
    }

    /// More in the bar, on the score and on a PDF (the roll drawer's own More is `roll.more`).
    var more: XCUIElement {
        app.buttons.matching(NSPredicate(format: "label == 'Ещё' AND identifier != 'roll.more'")).firstMatch
    }

    /// The piece's title in the bar, which opens the Contents.
    var title: XCUIElement {
        app.buttons.matching(NSPredicate(format: "label ENDSWITH 'открыть оглавление'")).firstMatch
    }

    /// The control is there within `timeout`, or the journey stops here.
    @discardableResult
    func need(_ element: XCUIElement, _ what: String, timeout: TimeInterval = 10) throws -> XCUIElement {
        guard element.waitForExistence(timeout: timeout) else { throw miss("\(what) is not there") }
        return element
    }

    /// The control is there and takes a press, which it gets.
    func press(_ element: XCUIElement, _ what: String, timeout: TimeInterval = 10) throws {
        try need(element, what, timeout: timeout)
        let start = Date()
        while !element.isHittable {
            guard Date().timeIntervalSince(start) < 3 else { throw miss("\(what) is there but cannot be pressed") }
            pause(0.25)
        }
        element.tap()
        pause(0.8)
    }

    /// The control has gone within `timeout`, or the journey stops here.
    func gone(_ element: XCUIElement, _ what: String, timeout: TimeInterval = 5) throws {
        let start = Date()
        while element.exists {
            guard Date().timeIntervalSince(start) < timeout else { throw miss("\(what) is still there") }
            pause(0.25)
        }
    }

    /// A popover closes on a tap outside it.
    func dismissPopover() {
        let region = app.otherElements["PopoverDismissRegion"]
        if region.exists { region.tap() } else { app.windows.firstMatch.coordinate(withNormalizedOffset: CGVector(dx: 0.02, dy: 0.98)).tap() }
        pause(0.8)
    }

    /// A stroke of one finger across the window, between two places given as shares of its width and height.
    func stroke(from: CGVector, to: CGVector) {
        let window = app.windows.firstMatch
        window.coordinate(withNormalizedOffset: from).press(forDuration: 0.1, thenDragTo: window.coordinate(withNormalizedOffset: to))
        pause(1)
    }

    func pause(_ seconds: TimeInterval) {
        Thread.sleep(forTimeInterval: seconds)
    }

    // MARK: - Steps on the score

    /// Another phrase selected by the app's hidden chooser (ScreensHook, Debug builds), as a tap on its bracket would:
    /// phrase 2, then 3, and so on, so a choice never lifts the selection into Expand. The probe once the selection shows.
    @discardableResult
    func choosePhrase() throws -> Probe {
        let before = probe()?["sel"]
        let chooser = try need(button("ml-screens-phrase"), "the walk's phrase chooser (ScreensHook)", timeout: 5)
        chooser.tap()
        return try until("a new phrase selected by the walk's chooser") { $0.span("sel") != nil && $0["sel"] != before }
    }

    func clearSelection() throws {
        try press(button("Снять выделение"), "Clear selection")
        try until("no selection after Clear selection") { $0["sel"] == "none" }
    }

    /// The pill's Play pressed with the sound loaded: the probe once the playing has moved on.
    @discardableResult
    func play(_ what: String) throws -> Probe {
        try until("the sound loaded", timeout: 30) { $0.on("sound") }
        try press(button(beginning: "Играть с"), "the pill's Play \(what)")
        let first = try until("playing after Play \(what)") { $0.playing }
        return try until("the playing moving on \(what)") { $0.playing && $0.tick > first.tick }
    }

    func stop() throws {
        try press(button(beginning: "Пауза ·"), "the pill's Pause")
        try until("stopped after Pause") { !$0.playing }
    }

    // MARK: - Pictures

    /// The screen as it is now, for the mockups: attached to the test and written to SCREENS_DIR when it is given.
    func shot(_ name: String) {
        pause(0.6)
        let screenshot = XCUIScreen.main.screenshot()
        let attachment = XCTAttachment(screenshot: screenshot)
        attachment.name = "\(name)-\(tag)"
        attachment.lifetime = .keepAlways
        test.add(attachment)
        if let folder = ProcessInfo.processInfo.environment["SCREENS_DIR"], !folder.isEmpty {
            let url = URL(fileURLWithPath: folder).appendingPathComponent("\(name)-\(tag).png")
            do { try upright(screenshot.image).write(to: url) } catch { print("SCREEN-WRITE \(url.path): \(error)") }
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

    /// What the screen showed when a journey failed: its picture and the buttons there (one snapshot of the screen), for
    /// the log.
    func failed(_ journey: String) {
        shot("fail-\(journey)")
        var labels: [String] = []
        func visit(_ node: XCUIElementSnapshot) {
            if node.elementType == .button, !node.label.isEmpty, labels.count < 120 { labels.append(node.label) }
            node.children.forEach(visit)
        }
        if let screen = try? app.snapshot() { visit(screen) }
        print("SCREEN-BUTTONS \(tag) \(journey): \(labels.joined(separator: " | "))")
    }
}
