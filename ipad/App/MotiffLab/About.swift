import SwiftUI

/// Which build this is (Andrei, 2026-11-13: to tell that the latest version is installed). `ipad/build-version.sh` writes
/// it into the app's Info.plist: `MotiffBuildVersion`, the tag it was built from ("v0.5.2"), or the branch and the
/// commit ("develop 1a2b3c4") when it is no tag; `MotiffBuildCommit`, the commit; `MotiffBuildDate`, when it was built
/// (ISO 8601, UTC). A build from Xcode alone has none of them and says its version from the project with "dev".
struct BuildInfo: Equatable, Sendable {
    let version: String
    let commit: String
    let built: Date?

    static func read(_ info: [String: Any]) -> BuildInfo {
        func value(_ key: String) -> String { (info[key] as? String ?? "").trimmingCharacters(in: .whitespaces) }
        let shown = value("MotiffBuildVersion")
        let marketing = value("CFBundleShortVersionString")
        let number = value("CFBundleVersion")
        let project = number.isEmpty ? marketing : "\(marketing) (\(number))"
        let fallback = marketing.isEmpty ? "dev" : "\(project) · dev"
        return BuildInfo(
            version: shown.isEmpty ? fallback : shown,
            commit: value("MotiffBuildCommit"),
            built: ISO8601DateFormatter().date(from: value("MotiffBuildDate"))
        )
    }

    static let bundled = read(Bundle.main.infoDictionary ?? [:])

    /// The date and time in the interface language, "13 нояб. 2026 г., 14:05".
    func builtText(_ language: String) -> String? {
        guard let built else { return nil }
        let format = DateFormatter()
        format.locale = Locale(identifier: language)
        format.dateStyle = .medium
        format.timeStyle = .short
        return format.string(from: built)
    }
}

/// Settings · About: the version, the commit and the date of this build, and the environment the iPad works in; in Test,
/// «Очистить тестовую библиотеку».
struct AboutPanel: View {
    @ObservedObject var account: Account
    let text: UIText
    var build = BuildInfo.bundled

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            row(text["about.version"], build.version)
            if !build.commit.isEmpty {
                row(text["about.commit"], build.commit)
            }
            if let built = build.builtText(text.language) {
                row(text["about.built"], built)
            }
            if account.isAvailable {
                HStack(spacing: CGFloat.ds("s-2")) {
                    row(text["about.environment"], text[account.environment.nameKey])
                    EnvironmentMark(account: account, text: text)
                }
            }
            if TestLibrary.offered(account) {
                TestLibraryPanel(account: account, text: text)
            }
        }
    }

    private func row(_ label: String, _ value: String) -> some View {
        HStack(spacing: CGFloat.ds("s-3")) {
            Text(label).dsType(.label).foregroundStyle(Color.ds("ink-2"))
            Text(value).dsType(.label).foregroundStyle(Color.ds("ink")).textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }
}
