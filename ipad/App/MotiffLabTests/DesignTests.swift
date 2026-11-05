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
}
