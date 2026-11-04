import XCTest
@testable import MotiffLab

/// Sign-in and the plan (0014, 0018 section 12): the web app's tenant and API, the iOS redirect the registration
/// knows, the API's answer read as the web app reads it, and a tester's choice sent with every request.
@MainActor
final class AccountTests: XCTestCase {
    func testTheAppSignsInWhereTheWebAppDoes() throws {
        let options = try XCTUnwrap(AccountOptions.load(), "Account.json is in the app and complete")
        XCTAssertEqual(URL(string: options.authority)?.host, "motifflab.ciamlogin.com")
        XCTAssertTrue(options.apiScope.hasSuffix("/Projects.ReadWrite"))
        XCTAssertTrue(options.scanScope.hasSuffix("/Scan"))
        XCTAssertEqual(URL(string: options.api)?.scheme, "https")
        XCTAssertNoThrow(try Account.authority(options), "MSAL takes the External ID authority")
        XCTAssertTrue(Account(options: options).isAvailable)
        // MSAL keeps its tokens in the keychain, which an unsigned simulator build cannot use: shown, not checked.
        do {
            _ = try Account(options: options).client()
            print("MSAL-CLIENT ready")
        } catch {
            print("MSAL-CLIENT \(error)")
        }
    }

    func testSignInComesBackOnTheRegisteredRedirect() throws {
        let bundle = try XCTUnwrap(Bundle.main.bundleIdentifier)
        XCTAssertEqual(AccountOptions.redirect(), "msauth.\(bundle)://auth")
        let types = try XCTUnwrap(Bundle.main.object(forInfoDictionaryKey: "CFBundleURLTypes") as? [[String: Any]])
        let schemes = types.flatMap { $0["CFBundleURLSchemes"] as? [String] ?? [] }
        XCTAssertTrue(schemes.contains("msauth.\(bundle)"), "\(schemes)")
        let queried = Bundle.main.object(forInfoDictionaryKey: "LSApplicationQueriesSchemes") as? [String] ?? []
        XCTAssertEqual(Set(queried), ["msauthv2", "msauthv3"])
    }

    func testThePlanIsReadAsTheAPIAnswersIt() throws {
        let tester = try XCTUnwrap(AccountPlan.read(Data(#"{"plan":"free","tester":true,"chosen":"free","features":{"scan":false,"transcribe":false,"saveOnline":false,"assistant":false}}"#.utf8)))
        XCTAssertEqual(tester, AccountPlan(plan: .free, isTester: true, chosen: .free, features: []))
        let pro = try XCTUnwrap(AccountPlan.read(Data(#"{"plan":"pro","tester":false,"chosen":null,"features":{"scan":true,"transcribe":true,"saveOnline":true,"assistant":true}}"#.utf8)))
        XCTAssertTrue(pro.isPro)
        XCTAssertNil(pro.chosen)
        XCTAssertEqual(pro.features, Set(ProFeature.allCases))
        XCTAssertNil(AccountPlan.read(Data("not json".utf8)))
    }

    func testATestersChoiceTravelsWithEveryRequest() async throws {
        let defaults = try XCTUnwrap(UserDefaults(suiteName: "AccountTests"))
        defaults.removePersistentDomain(forName: "AccountTests")
        let options = try XCTUnwrap(AccountOptions.load())
        let account = Account(options: options, defaults: defaults)
        XCTAssertFalse(account.allows(.scan), "signed out: no Pro feature")

        let plain = try XCTUnwrap(account.request(path: "account", token: "t"))
        XCTAssertEqual(plain.url?.absoluteString, options.api + "/account")
        XCTAssertEqual(plain.value(forHTTPHeaderField: "Authorization"), "Bearer t")
        XCTAssertNil(plain.value(forHTTPHeaderField: Account.planHeader))

        await account.choose(.free)
        XCTAssertEqual(account.request(path: "account", token: "t")?.value(forHTTPHeaderField: "Motiff-Plan"), "free")
        XCTAssertEqual(Account(options: options, defaults: defaults).choice, .free, "kept on this device")

        await account.choose(nil)
        XCTAssertNil(account.request(path: "account", token: "t")?.value(forHTTPHeaderField: "Motiff-Plan"))
        XCTAssertNil(defaults.string(forKey: Account.choiceKey))
    }

    func testABuildWithoutSignInShowsNoAccount() {
        XCTAssertFalse(Account(options: nil).isAvailable)
        let empty = AccountOptions(authority: "", clientId: "", apiScope: "", scanScope: "", api: "", scan: "")
        XCTAssertFalse(empty.isConfigured)
    }
}
