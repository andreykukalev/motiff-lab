import Foundation
@preconcurrency import MSAL
import UIKit

/// Where players sign in (decision record 0014): the web app's External ID tenant, its app registration and the
/// Motiff Lab API's scopes, in `Account.json` (the same shape and values as the published web app's
/// `appsettings.json`), with one environment's servers (`EnvironmentSetup`). The iPad signs in with MSAL on the
/// registration's iOS redirect, `msauth.<bundle id>://auth` (infra/azure/ipad.ps1).
struct AccountOptions: Decodable, Equatable, Sendable {
    /// https://<domain>.ciamlogin.com/<tenant id>
    var authority: String
    var clientId: String
    /// api://<API client id>/Projects.ReadWrite
    var apiScope: String
    /// api://<API client id>/Scan
    var scanScope: String
    /// The Motiff Lab API's address.
    var api: String
    /// The hosted scan server's address. Only this address gets the player's Scan token.
    var scan: String

    enum CodingKeys: String, CodingKey {
        case authority = "Authority", clientId = "ClientId", apiScope = "ApiScope", scanScope = "ScanScope", api = "Api", scan = "Scan"
    }

    private struct File: Decodable {
        let account: AccountOptions
        enum CodingKeys: String, CodingKey { case account = "Account" }
    }

    var isConfigured: Bool {
        !authority.isEmpty && !clientId.isEmpty && !apiScope.isEmpty && URL(string: api)?.host != nil
    }

    /// Main's options in the app, or nil in a build without sign-in; then nothing about accounts shows.
    static func load(bundle: Bundle = .main) -> AccountOptions? {
        guard let url = bundle.url(forResource: "Account", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let options = try? JSONDecoder().decode(File.self, from: data).account,
              options.isConfigured
        else { return nil }
        return options
    }

    /// The redirect MSAL comes back on, registered for the iOS platform of the app registration.
    static func redirect(bundle: Bundle = .main) -> String {
        "msauth.\(bundle.bundleIdentifier ?? "com.andreykukalev.motifflab")://auth"
    }
}

/// A plan (0018 section 12): what runs on the device is free, what needs the cloud or an AI model is Pro. The names
/// are the servers' (MotiffLab.MusicCore.Accounts.Plans).
enum Plan: String, Sendable, CaseIterable {
    case free
    case pro
}

/// The features that need Pro, by their names in the API's answer.
enum ProFeature: String, Sendable, CaseIterable {
    case scan
    case transcribe
    case saveOnline
    case assistant
}

/// What the API says the player's plan is (`GET /account`):
/// {"plan":"pro","tester":true,"chosen":"free"|null,"features":{"scan":true,…}}.
struct AccountPlan: Equatable, Sendable {
    let plan: Plan
    let isTester: Bool
    let chosen: Plan?
    let features: Set<ProFeature>

    var isPro: Bool { plan == .pro }

    func allows(_ feature: ProFeature) -> Bool { features.contains(feature) }

    static func read(_ data: Data) -> AccountPlan? {
        guard let answer = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        let plan = (answer["plan"] as? String).flatMap(Plan.init(rawValue:)) ?? .free
        let tester = answer["tester"] as? Bool ?? false
        let chosen = (answer["chosen"] as? String).flatMap(Plan.init(rawValue:))
        let allowed = answer["features"] as? [String: Any] ?? [:]
        let features = Set(ProFeature.allCases.filter { allowed[$0.rawValue] as? Bool == true })
        return AccountPlan(plan: plan, isTester: tester, chosen: chosen, features: features)
    }
}

/// The name and photo the player chose for their profile (Andrei, 2026-11-06): the API's `/profile`, kept with the
/// account on any plan, as the web app keeps it (Account/CloudAccount.cs). `{"name": "…" | null, "photo": "data:…" | null}`.
struct PlayerProfile: Equatable, Sendable {
    var name: String?
    /// A data URL of a small square picture.
    var photo: String?

    static func read(_ data: Data) -> PlayerProfile? {
        guard let answer = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        func text(_ key: String) -> String? {
            guard let value = answer[key] as? String, !value.isEmpty else { return nil }
            return value
        }
        return PlayerProfile(name: text("name"), photo: text("photo"))
    }

    /// The photo as a picture, or nil.
    var image: UIImage? { Self.image(photo) }

    static func image(_ photo: String?) -> UIImage? {
        guard let photo, let comma = photo.firstIndex(of: ","),
              let data = Data(base64Encoded: String(photo[photo.index(after: comma)...]))
        else { return nil }
        return UIImage(data: data)
    }

    /// The picture's middle square, 256 pixels across, as a JPEG data URL: the web app's size and quality
    /// (Scripts/account/photo.ts), a few tens of kilobytes, so it travels inside the profile itself.
    static func squarePhoto(_ image: UIImage, pixels: CGFloat = 256) -> String? {
        let side = min(image.size.width, image.size.height)
        guard side > 0 else { return nil }
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = true
        let scale = pixels / side
        let width = image.size.width * scale
        let height = image.size.height * scale
        let square = UIGraphicsImageRenderer(size: CGSize(width: pixels, height: pixels), format: format).image { _ in
            image.draw(in: CGRect(x: (pixels - width) / 2, y: (pixels - height) / 2, width: width, height: height))
        }
        guard let jpeg = square.jpegData(compressionQuality: 0.85) else { return nil }
        return "data:image/jpeg;base64," + jpeg.base64EncodedString()
    }
}

/// The player's account, when the app has sign-in: who is signed in, tokens for the API and the hosted scan server,
/// and the plan the API answers. As in the web app (Account/CloudAccount.cs), a tester's choice of Pro or no Pro is kept
/// on this device and sent with every request to the API and the scan server (`Motiff-Plan`), which trust it only from a
/// tester. Signing in is optional: everything on the device works without it, and the free tier sends nothing anywhere.
/// It talks to one server environment at a time (`EnvironmentSetup`, Andrei 2026-11-12): Main or Test, switched in
/// Settings with the sign-in kept; in a version build Main is read only unless the player allowed writing.
@MainActor
final class Account: ObservableObject {
    static let choiceKey = "ml-test-plan"
    static let planHeader = "Motiff-Plan"
    /// How long an ask for the plan or the profile waits: the API scales to zero, and its cold start takes 25-35 s.
    static let coldStartTimeout: TimeInterval = 60

    /// The signed-in player's name or email, or nil when nobody is signed in.
    @Published private(set) var player: String?
    /// The plan as the API answered it; nil while signed out or when the API could not be reached.
    @Published private(set) var plan: AccountPlan?
    /// Whether the API is being asked for the plan now: Settings says «Проверяю…» rather than "did not answer".
    @Published private(set) var planAsking = false
    /// Whether the last ask for the plan failed (no token, no answer, not 200): only then Settings says "did not answer".
    @Published private(set) var planFailed = false
    /// A tester's choice on this device; nil for the account's own plan.
    @Published private(set) var choice: Plan?
    /// The name and photo the player chose; nil while signed out, before the API answers, or when it cannot be reached.
    @Published private(set) var profile: PlayerProfile?
    @Published private(set) var busy = false
    @Published var problem: String?
    /// The server environment the app works in now, and whether writing to Main was allowed for this build.
    @Published private(set) var environment: ServerEnvironment
    @Published private(set) var mainWritesAllowed: Bool
    /// Grows each time the Test library is cleared on this iPad (`TestLibrary`), so the library reads its pieces again.
    @Published private(set) var libraryCleared = 0

    /// The environments this build knows; nil in a build without sign-in.
    let setup: EnvironmentSetup?
    private var made: MSALPublicClientApplication?
    private let defaults: UserDefaults
    private let session: URLSession
    private let text: UIText
    private var account: MSALAccount?
    /// Counts the asks for the plan, so only the latest one's answer is shown.
    private var planAsks = 0

    init(setup: EnvironmentSetup? = .bundled, defaults: UserDefaults = .standard, session: URLSession = .shared, text: UIText = .current) {
        self.setup = setup
        self.defaults = defaults
        self.session = session
        self.text = text
        choice = defaults.string(forKey: Self.choiceKey).flatMap(Plan.init(rawValue:))
        environment = setup.map { $0.current(chosen: defaults.string(forKey: $0.choiceKey)) } ?? .main
        mainWritesAllowed = EnvironmentSetup.mainWritesAllowed(defaults)
    }

    /// An account on one environment's servers only: Main, read and written as in a build from main.
    convenience init(options: AccountOptions?, defaults: UserDefaults = .standard, session: URLSession = .shared, text: UIText = .current) {
        self.init(setup: options.map { EnvironmentSetup(main: $0, test: nil, buildDefault: .main) }, defaults: defaults, session: session, text: text)
    }

    /// The sign-in and the servers of the environment the app works in; nil in a build without sign-in.
    var options: AccountOptions? { setup?.options(environment) }

    /// False in a build without sign-in.
    var isAvailable: Bool { setup != nil }

    /// A version build (a commit not in main yet): it starts in Test, and Main is read only there by default.
    var isVersionBuild: Bool { setup?.isVersionBuild == true }

    /// Whether Settings shows the environment row: in a version build, for a tester, or away from the build's default.
    var offersEnvironmentChoice: Bool {
        setup?.offersChoice(current: environment, isTester: plan?.isTester == true) == true
    }

    /// Whether the app may change anything online now: false only in Main in a version build until writing is allowed.
    var writes: Bool { setup?.writes(environment, mainAllowed: mainWritesAllowed) ?? true }

    /// Switches the app to the environment (Settings, after the player confirmed it): from now on the library is that
    /// environment's own project files and sync records, and every request goes to its servers. The sign-in stays (one
    /// tenant); the plan and the profile are asked of the environment's API again.
    func switchEnvironment(to target: ServerEnvironment) async {
        guard let setup, setup.test != nil, target != environment else { return }
        defaults.set(target.rawValue, forKey: setup.choiceKey)
        environment = target
        plan = nil
        profile = nil
        await refreshPlan()
        await refreshProfile()
    }

    /// Allows writing to Main in a version build, for this build only, or takes it back.
    func allowMainWrites(_ allowed: Bool) {
        if allowed {
            defaults.set(EnvironmentSetup.build(), forKey: EnvironmentSetup.mainWritesKey)
        } else {
            defaults.removeObject(forKey: EnvironmentSetup.mainWritesKey)
        }
        mainWritesAllowed = allowed
    }

    /// The signed-in player's copies in the Test database deleted with the API's POST /me/reset (the profile kept there goes
    /// too), for «Очистить всё»; the reset's stamp is kept as applied here at once, so this iPad does not clear again, and
    /// the player's other devices clear their test library at their next sync (`TestReset`). Only in Test and only on
    /// develop's own API (`TestLibrary.deletesOnline`): nothing is ever asked of Main's database.
    func deleteTestCopies() async -> TestLibrary.Online {
        guard player != nil else { return .signedOut }
        guard TestLibrary.deletesOnline(setup, in: environment) else { return .notDevelop }
        guard var request = await apiRequest("me/reset", in: .test) else { return .failed }
        request.httpMethod = "POST"
        request.timeoutInterval = Self.coldStartTimeout
        guard let answer = try? await session.data(for: request), let response = answer.1 as? HTTPURLResponse, response.statusCode == 200
        else { return .failed }
        if let stamp = TestReset.stamp(response.value(forHTTPHeaderField: TestReset.header)) { resetApplied(stamp) }
        if environment == .test { profile = nil }
        return .deleted
    }

    /// Keeps a reset stamp as applied in Test (`TestReset`): after this iPad's own «Очистить всё», or one from another device.
    func resetApplied(_ stamp: Int64) {
        TestReset.record(stamp, in: defaults, environment: .test)
    }

    /// Tells the library that the Test library was cleared.
    func libraryWasCleared() {
        libraryCleared += 1
    }

    /// The name to show: the one the player chose, else the sign-in's; nil when nobody is signed in.
    var shownName: String? { player.map { profile?.name ?? $0 } }

    /// MSAL for the tenant, made when first needed, so a failure shows on the sign-in button rather than hiding the
    /// account. Its token cache is in the keychain, which an unsigned build (the iPad workflow's simulator) may not have.
    func client() throws -> MSALPublicClientApplication {
        if let made { return made }
        guard let options else { throw CancellationError() }
        let configuration = MSALPublicClientApplicationConfig(
            clientId: options.clientId, redirectUri: AccountOptions.redirect(), authority: try Self.authority(options))
        // The app's own keychain group, so signing needs no keychain-sharing capability.
        if let bundle = Bundle.main.bundleIdentifier { configuration.cacheConfig.keychainSharingGroup = bundle }
        let application = try MSALPublicClientApplication(configuration: configuration)
        made = application
        return application
    }

    private var application: MSALPublicClientApplication? { try? client() }

    /// The tenant as MSAL sees it: an External ID (CIAM) authority.
    nonisolated static func authority(_ options: AccountOptions) throws -> MSALAuthority {
        guard let url = URL(string: options.authority) else { throw URLError(.badURL) }
        return try MSALCIAMAuthority(url: url)
    }

    /// The player signed in last time, if any, and their plan. Asks nothing.
    func restore() async {
        guard let application, account == nil, let first = try? application.allAccounts().first else { return }
        account = first
        player = defaults.string(forKey: Self.nameKey(first)) ?? Self.name(of: first, idToken: nil)
        await refreshPlan()
        await refreshProfile()
    }

    /// Opens External ID's sign-in page in the app's language, or its sign-up form for a new player.
    func signIn(create: Bool = false) async {
        guard let options, let presenter = Self.presenter() else { return }
        let application: MSALPublicClientApplication
        do {
            application = try client()
        } catch {
            problem = "\(text["account.signInFailed"]) (\(error.localizedDescription))"
            return
        }
        busy = true
        defer { busy = false }
        problem = nil
        let parameters = MSALInteractiveTokenParameters(scopes: [options.apiScope], webviewParameters: MSALWebviewParameters(authPresentationViewController: presenter))
        if !options.scanScope.isEmpty { parameters.extraScopesToConsent = [options.scanScope] }
        parameters.promptType = create ? .create : .selectAccount
        // External ID takes both: ui_locales picks the page's language, mkt its market strings.
        let locale = text.language == "ru" ? "ru-RU" : "en-US"
        parameters.extraQueryParameters = ["ui_locales": locale, "mkt": locale]
        do {
            let (signedIn, idToken) = try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<(MSALAccount, String?), Error>) in
                application.acquireToken(with: parameters) { result, error in
                    if let result { continuation.resume(returning: (result.account, result.idToken)) } else { continuation.resume(throwing: error ?? CancellationError()) }
                }
            }
            account = signedIn
            let name = Self.name(of: signedIn, idToken: idToken)
            player = name
            // MSAL keeps the account but not every claim, so the name shown is kept for the next launch.
            defaults.set(name, forKey: Self.nameKey(signedIn))
            plan = nil
            await refreshPlan()
            await refreshProfile()
        } catch {
            if (error as NSError).domain == MSALErrorDomain, (error as NSError).code == MSALError.userCanceled.rawValue { return }
            problem = text["account.signInFailed"]
        }
    }

    /// Forgets the player's tokens on this device. Their projects stay.
    func signOut() {
        if let application, let account {
            try? application.remove(account)
            defaults.removeObject(forKey: Self.nameKey(account))
        }
        account = nil
        player = nil
        plan = nil
        planFailed = false
        profile = nil
    }

    /// An access token for the scope without asking the player anything, or nil when there is none.
    func token(_ scope: String) async -> String? {
        guard let application, let account, !scope.isEmpty else { return nil }
        let parameters = MSALSilentTokenParameters(scopes: [scope], account: account)
        return await withCheckedContinuation { (continuation: CheckedContinuation<String?, Never>) in
            application.acquireTokenSilent(with: parameters) { result, _ in continuation.resume(returning: result?.accessToken) }
        }
    }

    /// Asks the API for the plan again; nil when it cannot be reached, and then the app offers no Pro feature.
    /// While it asks, `planAsking`; when it got no plan, `planFailed`. An ask overtaken by a newer one (another
    /// environment, a tester's choice) changes nothing.
    func refreshPlan() async {
        guard let options, player != nil else { return }
        planAsks += 1
        let ask = planAsks
        planAsking = true
        planFailed = false
        let answer = await fetch("account", options: options)
        guard ask == planAsks else { return }
        planAsking = false
        guard player != nil else { return }
        if let answer, let read = AccountPlan.read(answer) { plan = read } else { planFailed = true }
    }

    /// Asks again for what is missing while signed in (Settings opens, the app comes back): the plan when there is none
    /// and no ask is under way, the profile when there is none. Asks nothing when signed out.
    func refreshIfMissing() async {
        guard player != nil else { return }
        if plan == nil && !planAsking { await refreshPlan() }
        if profile == nil { await refreshProfile() }
    }

    /// The API's answer at the path with the player's token, waiting long enough for a cold start; nil without a token
    /// or a 200.
    private func fetch(_ path: String, options: AccountOptions) async -> Data? {
        guard let token = await token(options.apiScope), var request = request(path: path, token: token) else { return nil }
        request.timeoutInterval = Self.coldStartTimeout
        guard let answer = try? await session.data(for: request), (answer.1 as? HTTPURLResponse)?.statusCode == 200 else { return nil }
        return answer.0
    }

    /// Whether the signed-in player may use the feature; false when signed out.
    func allows(_ feature: ProFeature) -> Bool { plan?.allows(feature) == true }

    /// Asks the API for the player's profile again; it stays as it was when the API cannot be reached.
    func refreshProfile() async {
        guard let options, player != nil, let answer = await fetch("profile", options: options), player != nil else { return }
        profile = PlayerProfile.read(answer)
    }

    /// Keeps the player's name and photo (nil clears either); nil when kept, else what went wrong in the player's words.
    func saveProfile(name: String?, photo: String?) async -> String? {
        guard writes else { return text["environment.notSaved"] }
        guard let options, player != nil, let token = await token(options.apiScope),
              var request = request(path: "profile", token: token)
        else { return text["profile.notSaved"] }
        request.httpMethod = "PUT"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body: [String: Any] = ["name": name.map { $0 as Any } ?? NSNull(), "photo": photo.map { $0 as Any } ?? NSNull()]
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)
        guard let answer = try? await session.data(for: request), let status = (answer.1 as? HTTPURLResponse)?.statusCode else {
            return text["profile.notSaved"]
        }
        guard status == 200, let saved = PlayerProfile.read(answer.0) else {
            return text[status == 413 ? "profile.photoTooLarge" : "profile.notSaved"]
        }
        profile = saved
        return nil
    }

    /// Switches a tester between Pro and no Pro (nil: the account's own plan), and asks the API again.
    func choose(_ plan: Plan?) async {
        choice = plan
        if let plan { defaults.set(plan.rawValue, forKey: Self.choiceKey) } else { defaults.removeObject(forKey: Self.choiceKey) }
        self.plan = nil
        await refreshPlan()
    }

    /// A request to the API with the player's token and a tester's choice.
    func request(path: String, token: String) -> URLRequest? {
        guard let options, let base = URL(string: options.api.hasSuffix("/") ? options.api : options.api + "/"),
              let url = URL(string: path, relativeTo: base)
        else { return nil }
        var request = URLRequest(url: url)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        addPlanChoice(to: &request)
        return request
    }

    /// A request to the API with a token for its scope, got without asking the player anything; nil when nobody is
    /// signed in or there is no token. Given an environment, nil once the app works in another one, so nothing meant for
    /// one database reaches the other.
    func apiRequest(_ path: String, in environment: ServerEnvironment? = nil) async -> URLRequest? {
        let expected = environment ?? self.environment
        guard let options, player != nil, expected == self.environment, let token = await token(options.apiScope),
              expected == self.environment
        else { return nil }
        return request(path: path, token: token)
    }

    /// The projects the player saved online in the environment the app works in now, with their token on any plan (0014):
    /// listed, fetched and sent by the sync (version 0.2, `CloudSync`), never deleted from the iPad (but for clearing the test
    /// library in Test, `deleteTestCopies`). Its requests stop once
    /// the app switches environment, and it sends nothing where the app may not write (Main in a version build). In Test on
    /// develop's API it also hears of «Очистить всё» from another device (`TestReset`).
    var online: OnlineProjects {
        let environment = self.environment
        let resets: Int64? = TestReset.applies(setup, in: environment) ? TestReset.applied(defaults, in: environment) : nil
        return OnlineProjects(
            session: session, request: { [weak self] path in await self?.apiRequest(path, in: environment) }, writable: writes, resetApplied: resets)
    }

    /// Adds a tester's choice to a request for the API or the hosted scan server; nothing when there is none.
    func addPlanChoice(to request: inout URLRequest) {
        if let choice { request.setValue(choice.rawValue, forHTTPHeaderField: Self.planHeader) }
    }

    private static func nameKey(_ account: MSALAccount) -> String {
        "ml-account-name." + (account.identifier ?? "")
    }

    /// The name to show for the player: from the sign-in's ID token, then the claims MSAL kept, as the web app chooses it.
    private static func name(of account: MSALAccount, idToken: String?) -> String {
        var claims = account.accountClaims ?? [:]
        for profile in account.tenantProfiles ?? [] {
            claims.merge(profile.claims ?? [:]) { kept, _ in kept }
        }
        claims.merge(tokenClaims(idToken)) { _, fromToken in fromToken }
        return displayName(claims, username: account.username)
    }

    /// name, then email (External ID may send `emails`), then preferred_username, as music-core's PlayerName chooses it
    /// for the web app; "unknown" (External ID's name for a player who gave none at sign-up, and MSAL's) is not a name.
    static func displayName(_ claims: [String: Any], username: String?) -> String {
        func text(_ key: String) -> String? {
            let value = ((claims[key] as? String) ?? (claims[key] as? [String])?.first)?.trimmingCharacters(in: .whitespaces)
            guard let value, !value.isEmpty, value.lowercased() != "unknown" else { return nil }
            return value
        }
        if let name = text("name") ?? text("email") ?? text("emails") ?? text("preferred_username") { return name }
        let given = [text("given_name"), text("family_name")].compactMap { $0 }.joined(separator: " ")
        if !given.isEmpty { return given }
        if let username, !username.isEmpty, username.lowercased() != "unknown" { return username }
        return ""
    }

    /// The claims in a JWT's payload; empty when it cannot be read.
    static func tokenClaims(_ token: String?) -> [String: Any] {
        let parts = token?.split(separator: ".") ?? []
        guard parts.count >= 2 else { return [:] }
        var payload = String(parts[1]).replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        payload += String(repeating: "=", count: (4 - payload.count % 4) % 4)
        guard let data = Data(base64Encoded: payload),
              let claims = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return [:] }
        return claims
    }

    /// The view controller on top, which presents the sign-in page.
    private static func presenter() -> UIViewController? {
        let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
        let scene = scenes.first { $0.activationState == .foregroundActive } ?? scenes.first
        var top = scene?.keyWindow?.rootViewController
        while let next = top?.presentedViewController { top = next }
        return top
    }
}
