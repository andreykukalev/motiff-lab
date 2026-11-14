import SwiftUI
import XCTest

@testable import MotiffLab

/// The design kit as the app reads it (0018): the web app's tokens.css in Daylight and Night, its type styles and sizes,
/// the kit's faces, and the plural forms of the interface's counts.
final class DesignTests: XCTestCase {
    func testBothStandsAreReadFromTheTokens() {
        let stands = DesignTokens.stands
        XCTAssertEqual(stands.light["accent"], "#2F5BD6")
        XCTAssertEqual(stands.dark["accent"], "#7D9CFF")
        XCTAssertEqual(stands.light["s-4"], "16px")
        XCTAssertNil(stands.dark["s-4"], "Night changes colours only")
    }

    func testColoursFollowTheAppearance() {
        let accent = DesignTokens.dynamicColor("accent")
        let light = accent.resolvedColor(with: UITraitCollection(userInterfaceStyle: .light))
        let dark = accent.resolvedColor(with: UITraitCollection(userInterfaceStyle: .dark))
        XCTAssertNotEqual(light, dark)
        var alpha: CGFloat = 0
        DesignTokens.dynamicColor("hairline").resolvedColor(with: UITraitCollection(userInterfaceStyle: .light)).getRed(nil, green: nil, blue: nil, alpha: &alpha)
        XCTAssertEqual(alpha, 0.12, accuracy: 0.001)
        XCTAssertNil(DesignTokens.parseColor("var(--ink)"))
    }

    func testShadowsAreReadFromTheTokens() throws {
        let layer = try XCTUnwrap(DesignTokens.shadowLayer("0 6px 20px rgba(31,28,24,.18)"))
        XCTAssertEqual(layer.y, 6)
        XCTAssertEqual(layer.blur, 20)
        XCTAssertNotNil(DesignTokens.shadowLayer(DesignTokens.stands.light["shadow-paper"]), "the first of two layers")
        XCTAssertEqual(DesignTokens.shadow("shadow-pill").radius, 10)
    }

    func testTypeAndSizesComeFromTheTokens() {
        XCTAssertEqual(DSType.label.token, TypeToken(weight: 500, size: 13, lineHeight: 18, display: false))
        XCTAssertTrue(DSType.display.token.display)
        XCTAssertEqual(DesignTokens.length("r-m"), 12)
        XCTAssertEqual(DesignTokens.length("h-touch"), 44)
        XCTAssertEqual(DesignTokens.length("no-such"), 0)
    }

    func testTheKitsFacesAreRegistered() {
        DesignTokens.registerFonts()
        for face in ["sans-400", "sans-500", "sans-600", "serif-400", "serif-500", "serif-600"] {
            let name = DesignTokens.faces[face] ?? ""
            XCTAssertNotNil(UIFont(name: name, size: 15), "\(face) is not registered: \(DesignTokens.faces)")
        }
        XCTAssertEqual(DesignTokens.faces["serif-500"], "SourceSerif4-Medium")
    }

    func testCountsTakeTheLanguagesPluralForms() {
        XCTAssertEqual(UIText(language: "en").count("library.ideas", 1), "1 idea")
        XCTAssertEqual(UIText(language: "en").count("library.ideas", 3), "3 ideas")
        XCTAssertEqual(UIText.pluralForm("ru", 1), "one")
        XCTAssertEqual(UIText.pluralForm("ru", 3), "few")
        XCTAssertEqual(UIText.pluralForm("ru", 12), "many")
        XCTAssertEqual(UIText.pluralForm("ru", 22), "few")
    }

    /// The kit's timing and easing, for the app's motion.
    func testMotionComesFromTheTokens() {
        XCTAssertEqual(DesignTokens.duration("t-base"), 0.2, accuracy: 0.0001)
        XCTAssertEqual(DesignTokens.duration("t-fast"), 0.12, accuracy: 0.0001)
        XCTAssertEqual(DesignTokens.duration("no-such"), 0)
        XCTAssertEqual(DesignTokens.ease, [0.2, 0.8, 0.2, 1])
    }

    /// What is typed in a field, and the swiped actions' names, read in both stands (#479: the Night ink on the paper was
    /// light on light): at least 4.5:1, WCAG AA for body text.
    @MainActor func testFieldsAndSwipedActionsReadInBothStands() {
        for style in [UIUserInterfaceStyle.light, .dark] {
            let traits = UITraitCollection(userInterfaceStyle: style)
            for (text, fill) in [(FieldLook.ink, FieldLook.background), (LineSwipe.label, LineSwipe.editFill), (LineSwipe.label, LineSwipe.deleteFill)] {
                let ratio = Self.contrast(DesignTokens.dynamicColor(text).resolvedColor(with: traits), DesignTokens.dynamicColor(fill).resolvedColor(with: traits))
                XCTAssertGreaterThanOrEqual(ratio, 4.5, "\(text) on \(fill), \(style == .dark ? "Night" : "Daylight")")
            }
        }
    }

    /// A line's swipe (#479): it opens past half its actions or with a flick to the left, never moves right of its place
    /// and only a little past the actions.
    @MainActor func testALineOpensPastHalfItsActionsOrWithAFlick() {
        let half = LineSwipe.revealed / 2
        XCTAssertTrue(LineSwipe.opens(rest: 0, moved: -half - 1, speed: 0))
        XCTAssertFalse(LineSwipe.opens(rest: 0, moved: -half + 1, speed: 0))
        XCTAssertTrue(LineSwipe.opens(rest: 0, moved: -CGFloat.ds("s-4"), speed: -CGFloat.ds("h-touch") * 20))
        XCTAssertFalse(LineSwipe.opens(rest: -LineSwipe.revealed, moved: CGFloat.ds("s-4"), speed: CGFloat.ds("h-touch") * 20))
        XCTAssertTrue(LineSwipe.opens(rest: -LineSwipe.revealed, moved: CGFloat.ds("s-4"), speed: 0))
        XCTAssertEqual(LineSwipe.offset(rest: 0, moved: CGFloat.ds("s-12")), 0)
        XCTAssertEqual(LineSwipe.offset(rest: 0, moved: -LineSwipe.revealed * 3), -LineSwipe.revealed - CGFloat.ds("s-6"))
    }

    private static func contrast(_ one: UIColor, _ other: UIColor) -> CGFloat {
        let lighter = max(luminance(one), luminance(other))
        let darker = min(luminance(one), luminance(other))
        return (lighter + 0.05) / (darker + 0.05)
    }

    private static func luminance(_ color: UIColor) -> CGFloat {
        var red: CGFloat = 0, green: CGFloat = 0, blue: CGFloat = 0
        color.getRed(&red, green: &green, blue: &blue, alpha: nil)
        let linear = { (value: CGFloat) -> CGFloat in value <= 0.03928 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4) }
        return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue)
    }

    /// The library's header keeps the page's padding below the status bar, even where the safe area leaves the bar out.
    @MainActor func testTheLibraryLeavesRoomForTheStatusBar() {
        XCTAssertEqual(LibraryView.statusBar(0), CGFloat.ds("s-6"))
        XCTAssertEqual(LibraryView.statusBar(CGFloat.ds("s-12")), CGFloat.ds("s-12"))
    }
}
