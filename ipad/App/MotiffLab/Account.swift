import Foundation
@preconcurrency import MSAL
import UIKit

/// Where players sign in (decision record 0014): the web app's External ID tenant, its app registration and the
/// Motiff Lab API's scopes, in `Account.json` (the same shape and values as the published web app's
/// `appsettings.json`). The iPad signs in with MSAL on the registration's iOS redirect,
/// `msauth.<bundle id>://auth` (infra/azure/ipad.ps1).
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

    /// The options in the app, or nil in a build without sign-in; then nothing about accounts shows.
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

/// The player's account, when the app has sign-in: who is signed in, tokens for the API and the hosted scan server,
/// and the plan the API answers. As in the web app (Account/CloudAccount.cs), a tester's choice of Pro or no Pro is kept
/// on this device and sent with every request to the API and the scan server (`Motiff-Plan`), which trust it only from a
/// tester. Signing in is optional: everything on the device works without it, and the free tier sends nothing anywhere.
@MainActor
final class Account: ObservableObject {
    static let choiceKey = "ml-test-plan"
    static let planHeader = "Motiff-Plan"

    /// The signed-in player's name or email, or nil when nobody is signed in.
    @Published private(set) var player: String?
    /// The plan as the API answered it; nil while signed out or when the API could not be reached.
    @Published private(set) var plan: AccountPlan?
    /// A tester's choice on this device; nil for the account's own plan.
    @Published private(set) var choice: Plan?
    @Published private(set) var busy = false
    @Published var problem: String?

    let options: AccountOptions?
    private var made: MSALPublicClientApplication?
    private let defaults: UserDefaults
    private let session: URLSession
    private let text: UIText
    private var account: MSALAccount?

    init(options: AccountOptions? = .load(), defaults: UserDefaults = .standard, session: URLSession = .shared, text: UIText = .current) {
        self.options = options
        self.defaults = defaults
        self.session = session
        self.text = text
        choice = defaults.string(forKey: Self.choiceKey).flatMap(Plan.init(rawValue:))
    }

    /// False in a build without sign-in.
    var isAvailable: Bool { options != nil }

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
    func refreshPlan() async {
        guard let options, player != nil, let token = await token(options.apiScope),
              let request = request(path: "account", token: token)
        else { return }
        guard let answer = try? await session.data(for: request), (answer.1 as? HTTPURLResponse)?.statusCode == 200 else { return }
        plan = AccountPlan.read(answer.0)
    }

    /// Whether the signed-in player may use the feature; false when signed out.
    func allows(_ feature: ProFeature) -> Bool { plan?.allows(feature) == true }

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

    /// name, then email (External ID may send `emails`), then preferred_username; MSAL's "unknown" is not a name.
    static func displayName(_ claims: [String: Any], username: String?) -> String {
        func text(_ key: String) -> String? {
            let value = (claims[key] as? String) ?? (claims[key] as? [String])?.first
            return value?.trimmingCharacters(in: .whitespaces).isEmpty == false ? value : nil
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
