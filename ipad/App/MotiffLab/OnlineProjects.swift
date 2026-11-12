import SwiftUI

/// A project the player saved online, as the API lists it (`GET /projects`, decision record 0014): its id, the name
/// of the file it was opened from, and when it was saved online, as the API writes it (the sync compares it as it is).
struct OnlineProject: Identifiable, Equatable, Sendable {
    let id: String
    let fileName: String
    var savedAt: String?

    /// The title the web gives it (Score/ScoreTitle.cs): the file name without the extensions of the files it comes
    /// from, "Für Elise.musicxml" as "Für Elise"; the id when the name is empty.
    var title: String {
        let title = Self.title(of: fileName)
        return title.isEmpty ? id : title
    }

    private static let extensions: Set<String> = ["mid", "midi", "kar", "musicxml", "xml", "mxl", "json", "motiff", "pdf"]

    static func title(of fileName: String) -> String {
        var title = fileName.trimmingCharacters(in: .whitespacesAndNewlines)
        // "piece.motiff.json" loses both.
        for _ in 0..<2 {
            let found = (title as NSString).pathExtension
            guard !found.isEmpty, extensions.contains(found.lowercased()), found.count + 1 < title.count else { break }
            title = String(title.dropLast(found.count + 1))
        }
        return title
    }

    /// The rows of the API's answer, `[{"id": "p_…", "fileName": "…", "lastOpened": "…", …}]`; nil when it is not a list.
    static func list(_ data: Data) -> [OnlineProject]? {
        guard let rows = (try? JSONSerialization.jsonObject(with: data)) as? [[String: Any]] else { return nil }
        return rows.compactMap { row in
            guard let id = row["id"] as? String, !id.isEmpty else { return nil }
            return OnlineProject(id: id, fileName: row["fileName"] as? String ?? "", savedAt: row["savedAt"] as? String)
        }
    }
}

/// The projects saved online, on the iPad: listed, fetched with their ETag and sent with `If-Match` by the sync
/// (version 0.2, `CloudSync`), and one listed but not here downloaded as a new project file (#335). Nothing saved online
/// is ever deleted from the iPad.
struct OnlineProjects {
    /// Why a project did not come, in the web's words (Account/OnlineProjects.cs).
    enum Failure: Error, Equatable {
        case signedOut, notFound, offline, problem

        var key: String {
            switch self {
            case .signedOut: "online.signInFirst"
            case .notFound: "online.notFound"
            case .offline: "online.offline"
            case .problem: "online.problem"
            }
        }
    }

    let session: URLSession
    /// A request to the API for a path, with the player's token and a tester's choice (`Account.request`); nil while
    /// nobody is signed in.
    let request: (String) async -> URLRequest?
    /// False where the app may not write (Main in a version build, `EnvironmentSetup.writes`): then only reads go out,
    /// and a save is refused here without being sent.
    var writable = true

    /// The player's projects saved online; nil when signed out, offline or when the API cannot be reached.
    func list() async -> [OnlineProject]? {
        guard case .success(let data) = await get("projects") else { return nil }
        return OnlineProject.list(data)
    }

    /// The projects listed online whose id is in no project file here, in the API's order.
    static func notHere(_ online: [OnlineProject], here: Set<String>) -> [OnlineProject] {
        online.filter { !here.contains($0.id) }
    }

    /// What the API answered a project sent to it.
    enum Sent: Equatable, Sendable {
        /// Saved online; its new ETag.
        case saved(etag: String?)
        /// A free account keeps `Plans.FreeProjectsOnline` pieces online and this new one is beyond them (0025 section 8).
        case overQuota
        /// Changed on another device since this one saw it (412): the next pass fetches it and keeps both.
        case changedOnline
        case failed(Failure)
    }

    /// The project file saved online, its bytes as the API sends them.
    func download(_ id: String) async -> Result<Data, Failure> {
        await fetch(id).map { $0.data }
    }

    /// The project file saved online with its ETag.
    func fetch(_ id: String) async -> Result<(data: Data, etag: String?), Failure> {
        guard let path = Self.path(of: id) else { return .failure(.notFound) }
        return await send(path) { _ in }
            .map { (data: $0.data, etag: $0.response.value(forHTTPHeaderField: "ETag")) }
            .mapError(Self.failure)
    }

    /// Sends the copy saved online (`project.online`) as the project's file, over the online copy with `ifMatch` when given.
    func upload(_ id: String, file: String, ifMatch: String?) async -> Sent {
        guard let path = Self.path(of: id) else { return .failed(.notFound) }
        let answer = await send(path) { request in
            request.httpMethod = "PUT"
            request.httpBody = Data(file.utf8)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            if let ifMatch { request.setValue(ifMatch, forHTTPHeaderField: "If-Match") }
        }
        switch answer {
        case .success(let done): return .saved(etag: done.response.value(forHTTPHeaderField: "ETag"))
        case .failure(.changedOnline): return .changedOnline
        case .failure(.overQuota): return .overQuota
        case .failure(.failed(let failure)): return .failed(failure)
        }
    }

    private static func path(of id: String) -> String? {
        let allowed = CharacterSet.urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))
        return id.addingPercentEncoding(withAllowedCharacters: allowed).map { "projects/" + $0 }
    }

    /// Downloads the project into the folder as a new project file, named by its title, and gives where it is. The file
    /// is kept as the API sends it, so its project id is the one saved online. A project already in the folder (the same
    /// id) is never downloaded again or replaced: its own file is given. Nothing is written when the download fails or
    /// is not a project file music-core can read, or belongs to another project.
    func keep(_ project: OnlineProject, in folder: URL = ProjectFiles.folder) async -> Result<URL, Failure> {
        if let here = await Self.file(of: project.id, in: folder) { return .success(here) }
        let data: Data
        switch await download(project.id) {
        case .success(let downloaded): data = downloaded
        case .failure(let failure): return .failure(failure)
        }
        guard await PieceSummary.read(data)?.id == project.id else { return .failure(.problem) }
        let url = ProjectFiles.newURL(for: project.title, in: folder)
        do {
            try data.write(to: url, options: .withoutOverwriting)
        } catch {
            return .failure(.problem)
        }
        return .success(url)
    }

    /// The project file in the folder that holds the project, or nil.
    static func file(of id: String, in folder: URL) async -> URL? {
        for entry in ProjectFiles.list(in: folder) {
            if await PieceSummary.read(entry.url)?.id == id { return entry.url }
        }
        return nil
    }

    private func get(_ path: String) async -> Result<Data, Failure> {
        await send(path) { _ in }.map { $0.data }.mapError(Self.failure)
    }

    private static func failure(_ refused: Refused) -> Failure {
        if case .failed(let failure) = refused { return failure }
        return .problem
    }

    /// Why the API refused a request: a Failure, or what a save meets (412, the quota).
    private enum Refused: Error {
        case failed(Failure), changedOnline, overQuota
    }

    private func send(_ path: String, _ shape: (inout URLRequest) -> Void) async -> Result<(data: Data, response: HTTPURLResponse), Refused> {
        guard var made = await request(path) else { return .failure(.failed(.signedOut)) }
        shape(&made)
        if !writable && (made.httpMethod ?? "GET") != "GET" { return .failure(.failed(.problem)) }
        do {
            let (data, response) = try await session.data(for: made)
            guard let response = response as? HTTPURLResponse else { return .failure(.failed(.problem)) }
            switch response.statusCode {
            case 200..<300: return .success((data, response))
            case 401: return .failure(.failed(.signedOut))
            case 404: return .failure(.failed(.notFound))
            case 412: return .failure(.changedOnline)
            case 403 where Self.code(data) == "quota": return .failure(.overQuota)
            default: return .failure(.failed(.problem))
            }
        } catch {
            return .failure(.failed(.offline))
        }
    }

    /// The "code" of the API's problem answer, `{"message": …, "code": "quota"}`.
    private static func code(_ data: Data) -> String? {
        ((try? JSONSerialization.jsonObject(with: data)) as? [String: Any])?["code"] as? String
    }
}

/// "Saved online, not on this device" (`home.onlineOnly`) under the library's own projects, as on the web
/// (Pages/Home.razor): a card for each project with its title and Get it here.
struct OnlineShelf: View {
    let text: UIText
    let projects: [OnlineProject]
    let get: (OnlineProject) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-4")) {
            Text(text["home.onlineOnly"])
                .overline()
                .accessibilityAddTraits(.isHeader)
            LazyVGrid(columns: LibraryView.columns, alignment: .leading, spacing: CGFloat.ds("s-6")) {
                ForEach(projects) { project in
                    VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                        Text(project.title)
                            .dsType(.heading)
                            .foregroundStyle(Color.ds("ink"))
                            .accessibilityAddTraits(.isHeader)
                        Button(text["home.getHere"]) { get(project) }
                            .buttonStyle(DSButtonStyle(kind: .secondary))
                    }
                    .padding(CGFloat.ds("s-4"))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.ds("surface"), in: RoundedRectangle(cornerRadius: CGFloat.ds("r-m")))
                    .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(Color.ds("hairline")))
                    .accessibilityElement(children: .contain)
                }
            }
        }
    }
}
