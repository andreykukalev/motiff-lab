import CoreText
import SwiftUI
import UIKit

/// The design kit of the redesign (0018; design/kit/tokens.css and design/kit/fonts, copied into the app): every colour,
/// type style, space, radius and size the screens use, read from the web app's own tokens so Swift defines none of its own.
/// Daylight is the `:root` block, Night the `[data-theme="dark"]` one, chosen with the iPad's appearance.
enum DesignTokens {
    /// The tokens of each stand, by name without the leading dashes.
    struct Stands: Sendable {
        let light: [String: String]
        let dark: [String: String]
    }

    static let stands: Stands = {
        guard let url = Bundle.main.url(forResource: "tokens", withExtension: "css"),
              let css = try? String(contentsOf: url, encoding: .utf8) else { return Stands(light: [:], dark: [:]) }
        return parse(css)
    }()

    /// The two stands of a tokens.css: the values in `:root { … }` and in `[data-theme="dark"] { … }`, each with the
    /// first value of a name.
    static func parse(_ css: String) -> Stands {
        Stands(light: block(css, after: ":root {"), dark: block(css, after: "[data-theme=\"dark\"] {"))
    }

    private static func block(_ css: String, after opening: String) -> [String: String] {
        guard let start = css.range(of: opening), let end = css.range(of: "}", range: start.upperBound..<css.endIndex) else { return [:] }
        let body = String(css[start.upperBound..<end.lowerBound])
        var values: [String: String] = [:]
        let pattern = try! NSRegularExpression(pattern: #"--([a-z0-9-]+):\s*([^;]+);"#)
        for match in pattern.matches(in: body, range: NSRange(body.startIndex..., in: body)) {
            guard let name = Range(match.range(at: 1), in: body), let value = Range(match.range(at: 2), in: body) else { continue }
            let key = String(body[name])
            if values[key] == nil { values[key] = body[value].trimmingCharacters(in: .whitespaces) }
        }
        return values
    }

    /// A token's Daylight colour, such as "ink-carmine" or "amber": for the player's ink, which keeps its colour on the
    /// paper in either stand. Grey when the token is missing, so a gap shows.
    static func color(_ name: String) -> UIColor {
        parseColor(stands.light[name]) ?? .systemGray
    }

    /// A token's colour in the stand the iPad's appearance asks for.
    static func dynamicColor(_ name: String) -> UIColor {
        let light = parseColor(stands.light[name]) ?? .systemGray
        let dark = parseColor(stands.dark[name]) ?? light
        return UIColor { traits in traits.userInterfaceStyle == .dark ? dark : light }
    }

    /// "#RRGGBB" or "rgba(r,g,b,a)"; nil for anything else.
    static func parseColor(_ text: String?) -> UIColor? {
        guard let text = text?.trimmingCharacters(in: .whitespaces) else { return nil }
        if text.hasPrefix("#"), text.count == 7, let value = UInt32(text.dropFirst(), radix: 16) {
            return UIColor(red: CGFloat((value >> 16) & 0xFF) / 255, green: CGFloat((value >> 8) & 0xFF) / 255, blue: CGFloat(value & 0xFF) / 255, alpha: 1)
        }
        if text.hasPrefix("rgba("), text.hasSuffix(")") {
            let parts = text.dropFirst(5).dropLast().split(separator: ",").compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }
            guard parts.count == 4 else { return nil }
            return UIColor(red: parts[0] / 255, green: parts[1] / 255, blue: parts[2] / 255, alpha: parts[3])
        }
        return nil
    }

    /// A length token in points, such as "s-4" (16) or "r-m" (12); 0 when it is missing.
    static func length(_ name: String) -> CGFloat {
        guard let text = stands.light[name], text.hasSuffix("px"), let value = Double(text.dropLast(2)) else { return 0 }
        return CGFloat(value)
    }

    /// A shadow token's first layer, such as "shadow-pill": "0 6px 20px rgba(31,28,24,.18)", in the stand the iPad's
    /// appearance asks for. SwiftUI's radius is about half the CSS blur.
    static func shadow(_ name: String) -> (color: Color, radius: CGFloat, x: CGFloat, y: CGFloat) {
        let light = shadowLayer(stands.light[name])
        let dark = shadowLayer(stands.dark[name]) ?? light
        guard let light, let dark else { return (.clear, 0, 0, 0) }
        let color = UIColor { traits in traits.userInterfaceStyle == .dark ? dark.color : light.color }
        return (Color(uiColor: color), light.blur / 2, light.x, light.y)
    }

    static func shadowLayer(_ text: String?) -> (x: CGFloat, y: CGFloat, blur: CGFloat, color: UIColor)? {
        guard let text else { return nil }
        let pattern = try! NSRegularExpression(pattern: #"^\s*(-?[\d.]+)(?:px)?\s+(-?[\d.]+)(?:px)?\s+([\d.]+)(?:px)?\s+(rgba\([^)]*\)|#[0-9A-Fa-f]{6})"#)
        guard let match = pattern.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              let x = Range(match.range(at: 1), in: text).flatMap({ Double(text[$0]) }),
              let y = Range(match.range(at: 2), in: text).flatMap({ Double(text[$0]) }),
              let blur = Range(match.range(at: 3), in: text).flatMap({ Double(text[$0]) }),
              let color = Range(match.range(at: 4), in: text).flatMap({ parseColor(String(text[$0])) }) else { return nil }
        return (CGFloat(x), CGFloat(y), CGFloat(blur), color)
    }

    /// A type token, such as "t-label": "500 13px/18px var(--font-ui)".
    static func type(_ name: String) -> TypeToken {
        TypeToken.parse(stands.light[name] ?? "")
    }

    /// Registers the kit's fonts (Source Sans 3 and Source Serif 4) for the app, once, before the first screen.
    static func registerFonts() {
        _ = faces
    }

    /// The kit's faces by PostScript name, keyed "sans-500" or "serif-400" from their file names
    /// (`source-sans-3-latin-500-normal.woff2`). The files name their family oddly (Source Sans 3 comes as
    /// "Source Sans 3 ExtraLight", cut from the variable font), so the app asks for a face by its exact name.
    static let faces: [String: String] = register(bundle: .main)

    static func register(bundle: Bundle) -> [String: String] {
        let urls = bundle.urls(forResourcesWithExtension: "woff2", subdirectory: "fonts") ?? []
        guard !urls.isEmpty else { return [:] }
        CTFontManagerRegisterFontURLs(urls as CFArray, .process, true, nil)
        var found: [String: String] = [:]
        for url in urls {
            let file = url.deletingPathExtension().lastPathComponent
            guard let weight = file.split(separator: "-").compactMap({ Int($0) }).first(where: { $0 >= 100 }),
                  let descriptors = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor],
                  let descriptor = descriptors.first,
                  let name = CTFontDescriptorCopyAttribute(descriptor, kCTFontNameAttribute) as? String else { continue }
            found["\(file.hasPrefix("source-serif") ? "serif" : "sans")-\(weight)"] = name
        }
        return found
    }

    /// One of the kit's faces at a size that grows with the reader's text size; the system face when the kit's is missing.
    /// The kit has 400, 500 and 600: a weight in between takes the nearest, and anything bolder takes 600.
    static func font(display: Bool = false, weight: Int, size: CGFloat, relativeTo style: Font.TextStyle = .body) -> Font {
        let cut = weight < 450 ? 400 : weight < 550 ? 500 : 600
        if let name = faces["\(display ? "serif" : "sans")-\(cut)"] {
            return Font.custom(name, size: size, relativeTo: style)
        }
        let fallback: Font.Weight = cut == 400 ? .regular : cut == 500 ? .medium : .semibold
        return Font.system(size: size, weight: fallback, design: display ? Font.Design.serif : Font.Design.default)
    }
}

/// A type style from tokens.css: weight, size and line height, in the UI face or the display face.
struct TypeToken: Equatable, Sendable {
    var weight: Int
    var size: CGFloat
    var lineHeight: CGFloat
    var display: Bool

    static func parse(_ text: String) -> TypeToken {
        let parts = text.split(separator: " ")
        let weight = parts.first.flatMap { Int($0) } ?? 400
        var size: CGFloat = 15
        var line: CGFloat = 22
        if parts.count > 1 {
            let sizes = parts[1].split(separator: "/").map { Double($0.replacingOccurrences(of: "px", with: "")) ?? 0 }
            if let first = sizes.first, first > 0 { size = first }
            if sizes.count > 1, sizes[1] > 0 { line = sizes[1] }
        }
        return TypeToken(weight: weight, size: size, lineHeight: line, display: text.contains("--font-display"))
    }

    /// The face at the token's size, growing with the reader's text size.
    var font: Font {
        DesignTokens.font(display: display, weight: weight, size: size, relativeTo: display ? .title : .body)
    }
}

/// The kit's type styles by name (0018 section 13).
enum DSType: String {
    case display = "t-display", title = "t-title", heading = "t-heading", body = "t-body", bodyStrong = "t-body-strong"
    case label = "t-label", caption = "t-caption", overline = "t-overline"

    var token: TypeToken { DesignTokens.type(rawValue) }
}

extension Color {
    /// A token's colour in the iPad's appearance, such as `.ds("ink-2")`.
    static func ds(_ name: String) -> Color {
        Color(uiColor: DesignTokens.dynamicColor(name))
    }
}

extension CGFloat {
    /// A length token, such as `.ds("s-4")`.
    static func ds(_ name: String) -> CGFloat {
        DesignTokens.length(name)
    }
}

extension View {
    /// Sets one of the kit's type styles, with its line height.
    func dsType(_ style: DSType) -> some View {
        let token = style.token
        return font(token.font).lineSpacing(Swift.max(0, token.lineHeight - token.size))
    }

    /// One of the kit's shadows, such as `.dsShadow("shadow-pill")`.
    func dsShadow(_ name: String) -> some View {
        let shadow = DesignTokens.shadow(name)
        return self.shadow(color: shadow.color, radius: shadow.radius, x: shadow.x, y: shadow.y)
    }

    /// The kit's overline: small capitals, spaced, in the third ink.
    func overline() -> some View {
        dsType(.overline).textCase(.uppercase).tracking(0.66).foregroundStyle(Color.ds("ink-3"))
    }
}

/// One of the kit's icons (design/kit/icons.js, written for both apps by tools/icons/build.mjs into Icons.xcassets).
struct Icon: View {
    let name: String
    var size: CGFloat = CGFloat.ds("icon-m")

    var body: some View {
        Image("ic-\(name)")
            .renderingMode(.template)
            .resizable()
            .scaledToFit()
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// The kit's buttons (design/kit/ds.css `.btn`): primary in cobalt, secondary on the card, quiet on its own.
struct DSButtonStyle: ButtonStyle {
    enum Kind { case primary, secondary, quiet, danger }

    var kind: Kind = .secondary
    var compact = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(DesignTokens.font(weight: 500, size: compact ? 13 : 15))
            .foregroundStyle(foreground)
            .padding(.horizontal, compact ? CGFloat.ds("s-3") : CGFloat.ds("s-4"))
            .frame(minHeight: compact ? CGFloat.ds("h-compact") : CGFloat.ds("h-control"))
            .background(background(pressed: configuration.isPressed), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
            .overlay {
                if kind == .secondary || kind == .danger {
                    RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong"))
                }
            }
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Rectangle())
    }

    private var foreground: Color {
        switch kind {
        case .primary: .ds("on-accent")
        case .secondary: .ds("ink")
        case .quiet: .ds("ink-2")
        case .danger: .ds("danger")
        }
    }

    private func background(pressed: Bool) -> Color {
        switch kind {
        case .primary: pressed ? .ds("accent-hover") : .ds("accent")
        case .secondary: pressed ? .ds("surface") : .ds("surface-2")
        case .quiet, .danger: pressed ? .ds("accent-tint") : .clear
        }
    }
}

/// An item of the pill or a palette (ds.css `.pill .it`): quiet ink, and the accent's tint while it is on.
struct DSItemStyle: ButtonStyle {
    var on = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(DesignTokens.font(weight: on ? 600 : 500, size: 13, relativeTo: .callout))
            .foregroundStyle(on ? Color.ds("accent") : Color.ds("ink-2"))
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(minHeight: 36)
            .background(on || configuration.isPressed ? Color.ds("accent-tint") : .clear, in: Capsule())
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Capsule())
    }
}

/// A chip (ds.css `.chip`): a filter, a place, a way to try; the accent's tint and border while it is on.
struct DSChipStyle: ButtonStyle {
    var on = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .lineLimit(1)
            .padding(.horizontal, CGFloat.ds("s-3"))
            .frame(minHeight: 28)
            .background(on || configuration.isPressed ? Color.ds("accent-tint") : Color.ds("surface-2"), in: Capsule())
            .overlay(Capsule().strokeBorder(on ? Color.ds("accent") : Color.ds("hairline-strong")))
            .opacity(isEnabled ? 1 : 0.38)
            .contentShape(Capsule())
    }
}

/// The floating surfaces over the score (ds.css `.bar`, `.pill`): the surface a little see-through over a blur, a hairline
/// and the kit's shadow.
struct FloatingSurface: ViewModifier {
    var shadow = "shadow-menu"
    var opacity = 0.86

    func body(content: Content) -> some View {
        content
            .background(Color.ds("surface").opacity(opacity), in: Capsule())
            .background(.ultraThinMaterial, in: Capsule())
            .overlay(Capsule().strokeBorder(Color.ds("hairline")))
            .dsShadow(shadow)
    }
}

/// A small tag (ds.css `.tag`): ideas kept in plum, bars to check in amber.
struct Tag: View {
    enum Kind { case saved, warn, detected, assumed }

    let text: String
    let kind: Kind

    var body: some View {
        Text(text)
            .font(DesignTokens.font(weight: 600, size: 12, relativeTo: .caption))
            .foregroundStyle(foreground)
            .padding(.horizontal, CGFloat.ds("s-2"))
            .frame(minHeight: 22)
            .background(background, in: RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")))
    }

    private var foreground: Color {
        switch kind {
        case .saved: .ds("plum")
        case .warn: .ds("warning")
        case .detected: .ds("accent")
        case .assumed: .ds("ink-2")
        }
    }

    private var background: Color {
        switch kind {
        case .saved: .ds("plum-tint")
        case .warn: .ds("amber-tint")
        case .detected: .ds("accent-tint")
        case .assumed: .ds("hairline")
        }
    }
}
