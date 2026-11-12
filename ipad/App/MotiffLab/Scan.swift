import SwiftUI

/// What a scan server read from a PDF: the MusicXML, the engine that read it and how many pages it read.
struct ScanResult: Sendable {
    let musicXml: Data
    let engine: String
    let pages: Int
    /// The server's report on each page (`X-Page-Report`, numbers only), when it sends one.
    var report: String?
}

/// Sheet music scanning on the iPad (0011, 0014, 0023), as the web app's `ScanService` does it: the PDF goes to the scan
/// server, which reads it with homr and gives MusicXML back. In a build with sign-in, Motiff Lab's hosted server is the
/// choice until the player gives their own server's address or turns scanning off in Settings; it reads only for a
/// signed-in player with Pro. In a build without sign-in, scanning is off until the player gives an address. The PDF goes
/// only to that address, and only when the player sends it; the server deletes it once read.
@MainActor
final class ScanServer: ObservableObject {
    static let shared = ScanServer()

    /// Where the player's choice is kept on the device: an address, or `off`.
    static let addressKey = "motiff.scan.address"
    static let off = "off"
    /// The address the service's README starts it on.
    static let suggested = "http://localhost:8765"

    /// What the player chose: their own server's address, `off`, or nil for the hosted server where there is one.
    @Published private(set) var chosen: String?
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        chosen = defaults.string(forKey: Self.addressKey)
    }

    /// The address scanning uses: the player's own server, else the hosted one (nil in a build without sign-in), or nil
    /// once scanning is turned off.
    func address(hosted: URL?) -> URL? {
        switch chosen {
        case Self.off?: nil
        case let text?: Self.parse(text) ?? hosted
        case nil: hosted
        }
    }

    /// Keeps the player's own server, or turns scanning off with nil.
    func choose(_ address: URL?) {
        chosen = address?.absoluteString ?? Self.off
        defaults.set(chosen, forKey: Self.addressKey)
    }

    /// Goes back to Motiff Lab's server, in a build with sign-in.
    func chooseHosted() {
        chosen = nil
        defaults.removeObject(forKey: Self.addressKey)
    }

    /// An absolute http or https address with a host and no path, query or user name; nil otherwise.
    nonisolated static func parse(_ text: String?) -> URL? {
        guard let text = text?.trimmingCharacters(in: .whitespaces), let parts = URLComponents(string: text),
              parts.scheme == "http" || parts.scheme == "https", let host = parts.host, !host.isEmpty,
              parts.path.isEmpty || parts.path == "/", parts.query == nil, parts.fragment == nil, parts.user == nil
        else { return nil }
        var clean = parts
        clean.path = "/"
        return clean.url
    }

    /// Motiff Lab's scan server, in a build with sign-in.
    static func hosted(_ account: Account) -> URL? {
        account.options.flatMap { parse($0.scan) }
    }

    /// True when the address is this device, so the PDF does not leave it.
    nonisolated static func isLocal(_ address: URL) -> Bool {
        ["localhost", "127.0.0.1", "::1"].contains(address.host ?? "")
    }

    /// The engine the server runs, such as "homr 0.7.0", or why it cannot be reached.
    nonisolated static func check(_ address: URL, session: URLSession = .shared) async -> Result<String, ScanProblem> {
        var request = URLRequest(url: address.appendingPathComponent("health"))
        request.timeoutInterval = 10
        do {
            let (body, response) = try await session.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200,
                  let object = try? JSONSerialization.jsonObject(with: body) as? [String: Any],
                  let engine = object["engine"] as? String, !engine.isEmpty
            else { return .failure(.notAScanServer) }
            let version = object["version"] as? String ?? ""
            return .success("\(engine) \(version)".trimmingCharacters(in: .whitespaces))
        } catch {
            return .failure(.unreachable(address))
        }
    }

    /// Sends the PDF to be read: the MusicXML and engine, or why it could not be read. To the hosted server it goes with
    /// the player's Scan token and a tester's plan choice; to any other address with nothing of the player's.
    static func recognise(fileName: String, data: Data, address: URL, account: Account) async -> Result<ScanResult, ScanProblem> {
        await recognisePage(fileName: fileName, data: data, address: address, account: account).result
    }

    /// Sends one file to be read, as `recognise`, with the server's page report (`X-Page-Report`), which comes with a page
    /// that could not be read too (v0.4, «Фото страниц»).
    static func recognisePage(fileName: String, data: Data, address: URL, account: Account) async -> (result: Result<ScanResult, ScanProblem>, report: String?) {
        let boundary = "motiff-\(UUID().uuidString)"
        var request = URLRequest(url: address.appendingPathComponent("recognise"))
        request.httpMethod = "POST"
        request.timeoutInterval = 600
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        if address == hosted(account) {
            guard let options = account.options, let token = await account.token(options.scanScope) else { return (.failure(.signIn), nil) }
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            account.addPlanChoice(to: &request)
        }
        let name = fileName.replacingOccurrences(of: "\"", with: "")
        var body = Data()
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"\(name)\"\r\nContent-Type: application/octet-stream\r\n\r\n".utf8))
        body.append(data)
        body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        request.httpBody = body
        let hostedServer = address == hosted(account)
        do {
            let (answer, response) = try await URLSession.shared.data(for: request)
            let http = response as? HTTPURLResponse
            let report = http?.value(forHTTPHeaderField: "X-Page-Report")
            switch http?.statusCode ?? 0 {
            case 200..<300:
                let engine = http?.value(forHTTPHeaderField: "X-Engine") ?? "unknown engine"
                let pages = Int(http?.value(forHTTPHeaderField: "X-Pages") ?? "") ?? 1
                return (.success(ScanResult(musicXml: answer, engine: engine, pages: pages, report: report)), report)
            case 403 where hostedServer:
                return (.failure(.needsPro), nil)
            case let status:
                let message = (try? JSONSerialization.jsonObject(with: answer) as? [String: Any])?["message"] as? String
                if let message, !message.isEmpty { return (.failure(.server(message)), report) }
                return (.failure(status == 404 ? .notAScanServer : .server("The scan server could not read this file.")), report)
            }
        } catch let error as URLError where error.code == .timedOut {
            return (.failure(.server("The scan server took too long to answer.")), nil)
        } catch {
            return (.failure(.unreachable(address)), nil)
        }
    }
}

/// Why a scan did not give notes, in the player's words.
enum ScanProblem: Error, Equatable {
    case notAScanServer
    case unreachable(URL)
    case signIn
    case needsPro
    case server(String)

    func text(_ text: UIText) -> String {
        switch self {
        case .notAScanServer: "Something answers at this address, but it is not a Motiff Lab scan server."
        case .unreachable(let address): "The scan server at \(address.absoluteString) does not answer. Check that it is running (see Settings), and that this iPad may reach it."
        case .signIn: text["scan.signIn"]
        case .needsPro: text["scan.needsPro"]
        case .server(let message): message
        }
    }
}

/// Settings' scan server, in a build without Motiff Lab's server (the web app's AccountPanel `#scan`): the address of the
/// player's own server, saved and checked, or scanning turned off.
struct ScanPanel: View {
    @ObservedObject var scan: ScanServer
    let text: UIText
    @State private var address = ""
    @State private var status: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(text["settings.scanTitle"]).dsType(.label).foregroundStyle(Color.ds("ink"))
            Text(text["settings.scanWhat"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            TextField(ScanServer.suggested, text: $address)
                .keyboardType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .onSubmit { Task { await save() } }
                .dsType(.body)
                .foregroundStyle(Color.ds("ink"))
                .padding(.horizontal, CGFloat.ds("s-3"))
                .frame(height: CGFloat.ds("h-control"))
                .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-s")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline-strong")))
                .accessibilityLabel(text["settings.scanAddress"])
            HStack(spacing: CGFloat.ds("s-2")) {
                Button(text["settings.scanSave"]) { Task { await save() } }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                if current != nil {
                    Button(text["settings.scanTurnOff"]) {
                        scan.choose(nil)
                        status = nil
                    }
                    .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                }
            }
            Text(status ?? current.map { text.format("settings.scanStatusUses", $0.absoluteString) } ?? text["settings.scanStatusOff"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .accessibilityAddTraits(.updatesFrequently)
            if let current, !ScanServer.isLocal(current) {
                Text(text["settings.scanElsewhere"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            }
        }
        .onAppear { address = current?.absoluteString ?? "" }
    }

    private var current: URL? { scan.address(hosted: nil) }

    private func save() async {
        guard let parsed = ScanServer.parse(address) else {
            status = text["settings.scanBadAddress"]
            return
        }
        scan.choose(parsed)
        status = text["settings.scanChecking"]
        switch await ScanServer.check(parsed) {
        case .success(let engine): status = text.format("settings.scanConnected", engine)
        case .failure(let problem): status = text.format("settings.scanSavedBut", problem.text(text))
        }
    }
}

/// Read the notes from a PDF piece (0011, 0023; the web app's RecognisePanel): the PDF goes to the scan server, and the
/// notes it reads become a separate piece; the PDF piece stays as it is (Andrei, 2026-11-05). What leaves the device is one
/// line and one switch; on Motiff Lab's server it needs an account and Pro, offered here.
struct ReadNotesPanel: View {
    let address: URL?
    let hosted: Bool
    let signedIn: Bool
    let pro: Bool
    let text: UIText
    /// The panel's heading; Read the notes unless given.
    var heading: String? = nil
    /// What becomes of the notes read, under Send; that they go into this piece unless given.
    var keeps: String? = nil
    /// The key of the line on what is sent and to where ({0} the address), in place of the PDF's.
    var line: String? = nil
    let settings: () -> Void
    /// Sends the PDF and puts the notes into the piece, or adds them as a version; answers why not, or nil once done.
    let read: () async -> String?
    @State private var send = false
    @State private var reading = false
    @State private var problem: String?

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            Text(heading ?? text["pdf.readNotes"]).overline().accessibilityAddTraits(.isHeader)
            if let address {
                if hosted && !signedIn {
                    caption(text["scan.signInPrompt"])
                    Button(text["settings.signIn"], action: settings).buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                } else if hosted && !pro {
                    HStack(alignment: .firstTextBaseline, spacing: CGFloat.ds("s-2")) {
                        Tag(text: "Pro", kind: .saved)
                        caption(text["scan.proPrompt"])
                    }
                } else {
                    let local = ScanServer.isLocal(address)
                    caption(line.map { text.format($0, address.absoluteString) } ?? text.format(local ? "pdf.readLineHere" : "pdf.readLine", address.absoluteString))
                    if !local {
                        Toggle(hosted ? text["pdf.sendHosted"] : text.format("pdf.sendElsewhere", address.host ?? address.absoluteString), isOn: $send)
                            .dsType(.label)
                            .foregroundStyle(Color.ds("ink"))
                            .tint(Color.ds("accent"))
                            .disabled(reading)
                    }
                    Button {
                        Task {
                            reading = true
                            problem = await read()
                            reading = false
                        }
                    } label: {
                        Text(reading ? text["import.scanBusy"] : text["pdf.readSend"])
                    }
                    .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
                    .disabled(reading || !(local || send))
                    caption(keeps ?? text["pdf.readKeeps"])
                }
            } else {
                caption(text["import.scanSetUp"])
                Button(text["import.scanSetUpLink"], action: settings).buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
            }
            if let problem {
                Text(problem).dsType(.caption).foregroundStyle(Color.ds("danger")).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ds("surface"))
    }

    private func caption(_ words: String) -> some View {
        Text(words).dsType(.caption).foregroundStyle(Color.ds("ink-2")).fixedSize(horizontal: false, vertical: true)
    }
}
