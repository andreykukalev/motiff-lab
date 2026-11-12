import SwiftUI

/// The two server environments a build knows (infra/README.md, "The develop environment"; Andrei, 2026-11-12): Main, the
/// players' API, scan server and real database, and Test, develop's own servers with a test database. Both sign in to the
/// same tenant with the same registrations, so the sign-in stays when the iPad switches. A tool for testing, not a
/// feature: only a version build or a tester sees the switch (`EnvironmentSetup.offersChoice`).
enum ServerEnvironment: String, CaseIterable, Sendable {
    case main
    case test

    /// Where the environment's project files are. Main's are in Documents itself, where every piece was before there were
    /// two environments, so the pieces already on an iPad (synced with Main until version 0.3) stay Main's and never reach
    /// the test database; Test's are in Documents/Test, a folder of their own that Main's library never reads.
    func projectsFolder(in documents: URL) -> URL {
        self == .main ? documents : documents.appendingPathComponent("Test", isDirectory: true)
    }

    /// What the iPad knows of the environment's online copies (`SyncRecords`): Main's in sync.json, as before; Test's in
    /// sync-test.json, so an ETag of one database is never sent to the other.
    func syncRecords(in support: URL) -> URL {
        support.appendingPathComponent(self == .main ? "sync.json" : "sync-test.json")
    }

    /// The environment's project files in this app's Documents, the folder made when first needed.
    var projectsFolder: URL {
        let folder = projectsFolder(in: FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0])
        if self != .main { try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true) }
        return folder
    }

    /// The environment's sync records in this app's Application Support.
    var syncRecordsURL: URL {
        syncRecords(in: FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0])
    }

    /// The environment's name in Settings: «Основное» or «Тестовое».
    var nameKey: String { "environment.\(rawValue)" }
}

/// The environments of a build and its rules, read from `Account.json`: Main's servers are the "Account" block's own
/// (the published web app's values), Test's are under "Environments", and "Default" is where the build starts: "main"
/// in the committed file and for a build of a commit in main, "test" for a version build (`ipad/build-version.sh`).
struct EnvironmentSetup: Equatable, Sendable {
    let main: AccountOptions
    /// Main's sign-in with Test's servers; nil in a build that knows only Main.
    let test: AccountOptions?
    let buildDefault: ServerEnvironment

    init(main: AccountOptions, test: AccountOptions?, buildDefault: ServerEnvironment) {
        self.main = main
        self.test = test
        // A build that knows no Test server can only start in Main.
        self.buildDefault = test == nil ? .main : buildDefault
    }

    /// A version build (a commit not in main yet) starts in Test; a build from main starts in Main.
    var isVersionBuild: Bool { buildDefault == .test }

    func options(_ environment: ServerEnvironment) -> AccountOptions {
        environment == .test ? (test ?? main) : main
    }

    /// Where the environment chosen in Settings is kept: one choice for version builds and one for builds from main, so a
    /// build from main installed after a version starts in Main again unless the switch was used in a build from main.
    var choiceKey: String { "ml-environment." + buildDefault.rawValue }

    /// The environment the iPad works in: the one chosen in Settings, else the build's default.
    func current(chosen: String?) -> ServerEnvironment {
        guard test != nil, let chosen, let environment = ServerEnvironment(rawValue: chosen) else { return buildDefault }
        return environment
    }

    /// Whether Settings shows the environment row: in a version build, for a tester, and whenever the iPad is not in the
    /// build's default, so it can always go back. A player of a build from main never sees it.
    func offersChoice(current: ServerEnvironment, isTester: Bool) -> Bool {
        test != nil && (isVersionBuild || isTester || current != buildDefault)
    }

    /// Whether the iPad may change anything online in the environment: Test always; Main always in a build from main,
    /// and in a version build only when the player allowed it for this build (read only by default).
    func writes(_ environment: ServerEnvironment, mainAllowed: Bool) -> Bool {
        environment == .test || !isVersionBuild || mainAllowed
    }

    /// Where "Allow writing to Main" is kept: the build it was allowed for, so every newly installed build starts read only.
    static let mainWritesKey = "ml-environment.main-writes"

    /// This build, "0.3 (412)": its version and build number (`ipad/build-version.sh` sets both from the tag).
    static func build(bundle: Bundle = .main) -> String {
        let version = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
        let number = bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? ""
        return "\(version) (\(number))"
    }

    /// Whether writing to Main was allowed for this build.
    static func mainWritesAllowed(_ defaults: UserDefaults, for current: String = EnvironmentSetup.build()) -> Bool {
        defaults.string(forKey: mainWritesKey) == current
    }

    private struct File: Decodable {
        let account: AccountOptions
        let environments: Environments?

        struct Environments: Decodable {
            let buildDefault: String?
            let test: Servers?
            enum CodingKeys: String, CodingKey { case buildDefault = "Default", test = "Test" }
        }

        struct Servers: Decodable {
            let api: String
            let scan: String
            enum CodingKeys: String, CodingKey { case api = "Api", scan = "Scan" }
        }

        enum CodingKeys: String, CodingKey { case account = "Account", environments = "Environments" }
    }

    /// The setup in an Account.json's bytes; nil when Main's sign-in is not complete (a build without sign-in).
    static func read(_ data: Data) -> EnvironmentSetup? {
        guard let file = try? JSONDecoder().decode(File.self, from: data), file.account.isConfigured else { return nil }
        var test: AccountOptions?
        if let servers = file.environments?.test, URL(string: servers.api)?.host != nil {
            var options = file.account
            options.api = servers.api
            options.scan = servers.scan
            test = options
        }
        let chosen = file.environments?.buildDefault.flatMap(ServerEnvironment.init(rawValue:)) ?? .main
        return EnvironmentSetup(main: file.account, test: test, buildDefault: chosen)
    }

    static func load(bundle: Bundle = .main) -> EnvironmentSetup? {
        guard let url = bundle.url(forResource: "Account", withExtension: "json"), let data = try? Data(contentsOf: url) else { return nil }
        return read(data)
    }

    /// The app's own setup, read once.
    static let bundled: EnvironmentSetup? = load()

    /// The environment this app works in now, read from the preferences each time, so the project files and the sync
    /// follow a switch at once (`ProjectFiles.folder`, `SyncRecords.url`).
    static func current(_ defaults: UserDefaults = .standard) -> ServerEnvironment {
        guard let bundled else { return .main }
        return bundled.current(chosen: defaults.string(forKey: bundled.choiceKey))
    }
}

/// The marks of the environment in the library (Andrei, 2026-11-12: always know which database): «Тест» beside the
/// library's name in Test, as the web test site's title starts with «Тест · », and in a version build a line saying
/// whether Main is read only. Nothing in a build from main working in Main.
struct EnvironmentMark: View {
    @ObservedObject var account: Account
    let text: UIText

    var body: some View {
        if account.environment == .test {
            Text(text["environment.mark"])
                .dsType(.label)
                .foregroundStyle(Color.ds("danger"))
                .padding(.horizontal, CGFloat.ds("s-2"))
                .padding(.vertical, CGFloat.ds("s-1"))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-xs")).strokeBorder(Color.ds("danger")))
                .fixedSize()
                .accessibilityLabel(text["environment.markLabel"])
        }
    }
}

/// The line under the library's name in a version build working in Main: read only, or writing on.
struct MainEnvironmentLine: View {
    @ObservedObject var account: Account
    let text: UIText

    var body: some View {
        if account.environment == .main, account.isVersionBuild {
            Text(text[account.writes ? "environment.writing" : "environment.readOnly"])
                .dsType(.label)
                .foregroundStyle(account.writes ? Color.ds("danger") : Color.ds("ink-2"))
        }
    }
}

/// Settings' environment row (Andrei, 2026-11-12), in the Account section: «Окружение: Основное | Тестовое», each change
/// confirmed in a dialog that names where it goes and that the library changes; in a version build working in Main, the
/// read-only line and «Разрешить запись в Основное», confirmed too.
struct EnvironmentPanel: View {
    @ObservedObject var account: Account
    let text: UIText
    @State private var target: ServerEnvironment?
    @State private var allowing = false

    var body: some View {
        if account.offersEnvironmentChoice {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
                HStack(spacing: CGFloat.ds("s-2")) {
                    Text(text["environment.title"]).dsType(.label).foregroundStyle(Color.ds("ink"))
                    EnvironmentMark(account: account, text: text)
                }
                SegmentedChoice(
                    options: ServerEnvironment.allCases.map { ($0, text[$0.nameKey]) },
                    chosen: account.environment,
                    label: text["environment.title"]
                ) { chosen in
                    if chosen != account.environment { target = chosen }
                }
                Text(text["environment.hint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                if account.environment == .main, account.isVersionBuild {
                    mainWrites
                }
            }
            .alert(text.format("environment.switchTitle", text[(target ?? .main).nameKey]), isPresented: Binding(get: { target != nil }, set: { if !$0 { target = nil } })) {
                Button(text["environment.switch"]) {
                    guard let chosen = target else { return }
                    target = nil
                    Task { await account.switchEnvironment(to: chosen) }
                }
                Button(text["common.cancel"], role: .cancel) { target = nil }
            } message: {
                Text(text[switchMessage])
            }
        }
    }

    private var switchMessage: String {
        guard target == .main else { return "environment.switchToTest" }
        return account.isVersionBuild && !account.mainWritesAllowed ? "environment.switchToMainReadOnly" : "environment.switchToMain"
    }

    /// Main in a version build: read only until the player allows writing for this build.
    private var mainWrites: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
            Toggle(text["environment.allowWrites"], isOn: Binding(get: { account.mainWritesAllowed }, set: { on in
                if on { allowing = true } else { account.allowMainWrites(false) }
            }))
            .dsType(.label)
            .foregroundStyle(Color.ds("ink"))
            .tint(Color.ds("danger"))
            Text(text[account.writes ? "environment.writing" : "environment.readOnly"])
                .dsType(.caption)
                .foregroundStyle(account.writes ? Color.ds("danger") : Color.ds("ink-2"))
            if !account.writes {
                Text(text["environment.readOnlyHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
        .alert(text["environment.allowWritesTitle"], isPresented: $allowing) {
            Button(text["environment.allowWritesConfirm"], role: .destructive) { account.allowMainWrites(true) }
            Button(text["common.cancel"], role: .cancel) {}
        } message: {
            Text(text["environment.allowWritesMessage"])
        }
    }
}
