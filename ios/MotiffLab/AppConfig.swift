import Foundation

/// What the build sets in Info.plist: where the web app lives (Config/Base.xcconfig, MOTIFF_WEB_HOST).
enum AppConfig {
    /// The deployed web app. The same address the browser opens, so projects, settings and the scan server work as there.
    static let webURL: URL = {
        let text = Bundle.main.object(forInfoDictionaryKey: "MotiffWebURL") as? String ?? ""
        return URL(string: text) ?? URL(string: "https://nice-smoke-0169fb403.6.azurestaticapps.net/")!
    }()

    static let version: String = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"

    /// Whether an address belongs to the web app; anything else opens outside the app.
    static func isWebApp(_ url: URL) -> Bool {
        url.host?.lowercased() == webURL.host?.lowercased()
    }
}
