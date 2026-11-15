import Foundation

/// What the API says of the assistant for the signed-in player (`GET /assistant/status`): whether a model is on, the
/// account may use it, it is Pro, and how many turns are left today.
struct AssistantStatus: Equatable, Sendable {
    var configured: Bool
    var allowed: Bool
    var pro: Bool
    var left: Int

    init(configured: Bool, allowed: Bool, pro: Bool, left: Int) {
        self.configured = configured
        self.allowed = allowed
        self.pro = pro
        self.left = left
    }

    init?(_ json: JSONValue?) {
        guard let json, case .object = json else { return nil }
        self.init(configured: json["configured"]?.isTrue ?? false, allowed: json["allowed"]?.isTrue ?? false,
                  pro: json["pro"]?.isTrue ?? false, left: json["left"]?.int ?? 0)
    }

    /// Why nothing may be sent, as a UI text key; nil when a question may go (the web app's StatusNote).
    var note: String? {
        if !configured { return "mentor.notConfigured" }
        if !allowed { return "mentor.notAllowed" }
        if !pro { return "mentor.pro" }
        if left <= 0 { return "mentor.dailyCap" }
        return nil
    }
}

/// Why the API did not answer, as the conversation says it (the web app's RelayProblem).
enum RelayProblem: String, Equatable, Sendable {
    case notSignedIn, unreachable, notAllowed, pro, tooLarge, dailyCap, refused

    /// The UI text key that says it; every one ends with what was (not) sent.
    var note: String {
        switch self {
        case .notSignedIn: "mentor.signIn"
        case .notAllowed: "mentor.notAllowed"
        case .pro: "mentor.pro"
        case .tooLarge: "mentor.tooLarge"
        case .dailyCap: "mentor.dailyCap"
        case .refused: "mentor.refused"
        case .unreachable: "mentor.unreachable"
        }
    }

    /// The problem an API answer names in its "code", or what its status says.
    static func of(status: Int, body: Data) -> RelayProblem {
        switch JSONValue.parse(body)?["code"]?.string {
        case "notAllowed": .notAllowed
        case "pro": .pro
        case "tooLarge": .tooLarge
        case "dailyCap": .dailyCap
        default: status == 401 ? .notSignedIn : .refused
        }
    }
}

/// The assistant's relay on the Motiff Lab API (0028 section 4), as the web app's AssistantRelay: `GET /assistant/status`
/// before anything is sent, then one `POST /assistant/turn` at a time with the turn the device built. Nothing goes without
/// a signed-in player's token (`Account.apiRequest`), which carries a tester's plan choice like every other API request.
struct AssistantRelay {
    let session: URLSession
    /// A request to the API at a path with the player's token; nil when nobody is signed in, and then nothing is sent.
    let request: (String) async -> URLRequest?

    /// How long a turn may take: the model's answer, after a cold start of the API.
    static let answerWithin: TimeInterval = 60

    func status() async -> (AssistantStatus?, RelayProblem?) {
        guard let request = await self.request("assistant/status") else { return (nil, .notSignedIn) }
        let (answer, problem) = await send(request)
        guard let answer else { return (nil, problem) }
        return (AssistantStatus(answer), AssistantStatus(answer) == nil ? .unreachable : nil)
    }

    /// The server's reply to the turn's body, or the problem with it.
    func turn(_ body: Data) async -> (AssistantReply?, RelayProblem?) {
        guard var request = await self.request("assistant/turn") else { return (nil, .notSignedIn) }
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        let (answer, problem) = await send(request)
        guard let answer else { return (nil, problem) }
        guard let reply = AssistantReply(answer) else { return (nil, .unreachable) }
        return (reply, nil)
    }

    private func send(_ request: URLRequest) async -> (JSONValue?, RelayProblem?) {
        var request = request
        request.timeoutInterval = Self.answerWithin
        guard let answered = try? await session.data(for: request), let status = (answered.1 as? HTTPURLResponse)?.statusCode else {
            return (nil, .unreachable)
        }
        let body = answered.0
        guard (200..<300).contains(status) else { return (nil, RelayProblem.of(status: status, body: body)) }
        guard let answer = JSONValue.parse(body) else { return (nil, .unreachable) }
        return (answer, nil)
    }
}
