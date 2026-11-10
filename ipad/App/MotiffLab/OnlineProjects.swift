import SwiftUI

/// A project the player saved online, as the API lists it (`GET /projects`, decision record 0014): its id and the name
/// of the file it was opened from.
struct OnlineProject: Identifiable, Equatable, Sendable {
    let id: String
    let fileName: String

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
            return OnlineProject(id: id, fileName: row["fileName"] as? String ?? "")
        }
    }
}

/// The projects saved online, read on the iPad (#335), a first step of the sync (#324): the library lists those that
/// are not on this iPad, and a tap downloads one as a new project file, which is then an ordinary project here. Only
/// `GET` goes to the API, so nothing saved online is ever changed or deleted from the iPad.
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

    /// The player's projects saved online; nil when signed out, offline or when the API cannot be reached.
    func list() async -> [OnlineProject]? {
        guard case .success(let data) = await get("projects") else { return nil }
        return OnlineProject.list(data)
    }

    /// The projects listed online whose id is in no project file here, in the API's order.
    static func notHere(_ online: [OnlineProject], here: Set<String>) -> [OnlineProject] {
        online.filter { !here.contains($0.id) }
    }

    /// The project file saved online, its bytes as the API sends them.
    func download(_ id: String) async -> Result<Data, Failure> {
        let allowed = CharacterSet.urlPathAllowed.subtracting(CharacterSet(charactersIn: "/"))
        guard let escaped = id.addingPercentEncoding(withAllowedCharacters: allowed) else { return .failure(.notFound) }
        return await get("projects/" + escaped)
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
        guard let made = await request(path) else { return .failure(.signedOut) }
        do {
            let (data, response) = try await session.data(for: made)
            switch (response as? HTTPURLResponse)?.statusCode ?? 0 {
            case 200..<300: return .success(data)
            case 401: return .failure(.signedOut)
            case 404: return .failure(.notFound)
            default: return .failure(.problem)
            }
        } catch {
            return .failure(.offline)
        }
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
