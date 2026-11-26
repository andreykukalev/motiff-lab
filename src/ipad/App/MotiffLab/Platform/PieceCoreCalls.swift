import Foundation

/// The port's calls whose answers are not typed yet (#654): each named once here, as a case whose raw value is the call's
/// name, and sent by `send` on the open piece or by `sendWithoutPiece` on piece 0. The features read the answer's text
/// as they did before; a call moves to a typed method of `PieceCore` (PieceCore.swift) with its typed answer. Both are
/// the call itself, run on the core's thread by the caller's `MusicCore.run`, so a caller that makes two calls in one
/// run still makes them together.
extension PieceCore {
    /// The calls on an open piece (a case is named after its call, tests/shared/facade-calls-contract.test.mjs).
    enum Call: String, Sendable {
        // The score: Check, the marks and notes, the tempo, where the player was.
        case check = "check"
        case checkResolve = "check.resolve"
        case marks = "marks"
        case noteAdd = "note.add"
        case noteChange = "note.change"
        case noteRemove = "note.remove"
        case inkAdd = "ink.add"
        case inkRemove = "ink.remove"
        case highlightAdd = "highlight.add"
        case highlightRemove = "highlight.remove"
        case tempoSet = "tempo.set"
        case resume = "resume"
        case resumeSave = "resume.save"
        case notation = "notation"
        // Phrases, the piece's own settings and its bookmarks.
        case phrases = "phrases"
        case phrasesEdit = "phrases.edit"
        case settings = "settings"
        case settingsOverride = "settings.override"
        case bookmarks = "bookmarks"
        case bookmarkAdd = "bookmark.add"
        case bookmarkChange = "bookmark.change"
        case bookmarkRemove = "bookmark.remove"
        case bookmarkForm = "bookmark.form"
        // Insights: the map, understanding, the circle, Try and its ideas; the reference's key on the piece.
        case dna = "dna"
        case understand = "understand"
        case circle = "circle"
        case tryWay = "try"
        case ideaKeep = "idea.keep"
        case variationClear = "variation.clear"
        case referenceKey = "reference.key"
        // Versions and sources.
        case performances = "performances"
        case performance = "performance"
        case performanceAdd = "performance.add"
        case performanceRename = "performance.rename"
        case sources = "sources"
        case sourcesMakeWorking = "sources.makeWorking"
        case sourcesNotesOpen = "sources.notes.open"
        case sourcesNotesHear = "sources.notes.hear"
        case sourcesNotesRecognise = "sources.notes.recognise"
        case sourcesNotesRemove = "sources.notes.remove"
        case sourcesPdf = "sources.pdf"
        case sourcesViewHide = "sources.view.hide"
        case sourcesOriginalAdd = "sources.original.add"
        case sourcesOriginalRemove = "sources.original.remove"
        case sourcesRelink = "sources.relink"
        case sourcesHeld = "sources.held"
        // Drafts: made, renamed, removed, undone, and each step of one.
        case draftsAdd = "drafts.add"
        case draftsRename = "drafts.rename"
        case draftsRemove = "drafts.remove"
        case draftsUndo = "drafts.undo"
        case draftsTake = "drafts.take"
        case draftsLeave = "drafts.leave"
        case draftsTakeFrom = "drafts.takeFrom"
        case draftsTranspose = "drafts.transpose"
        case draftsTempo = "drafts.tempo"
        case draftsLeaveSeam = "drafts.leaveSeam"
        case draftsBridge = "drafts.bridge"
        case draftsLeaveBridge = "drafts.leaveBridge"
        case draftsAnchor = "drafts.anchor"
        case draftsUnanchor = "drafts.unanchor"
        case draftsTakeSure = "drafts.takeSure"
        case draftsBase = "drafts.base"
        case draftsPlace = "drafts.place"
        case draftsAccept = "drafts.accept"
        case draftsMovePlace = "drafts.movePlace"
        case draftsAddFromRecording = "drafts.addFromRecording"
        case draftsResize = "drafts.resize"
        case draftsCarry = "drafts.carry"
        // The assistant's tools and context, on the piece or in a draft.
        case assistantCall = "assistant.call"
        case assistantContext = "assistant.context"
        case draftsCall = "drafts.call"
        case draftsContext = "drafts.context"
        // The project file, a PDF piece's document, its ink and bookmarks, and its pieces.
        case projectSave = "project.save"
        case project = "project"
        case document = "document"
        case documentRead = "document.read"
        case documentReplace = "document.replace"
        case documentInkAdd = "document.ink.add"
        case documentInkRemove = "document.ink.remove"
        case documentBookmarkAdd = "document.bookmark.add"
        case documentBookmarkRemove = "document.bookmark.remove"
        case documentBookmarkRename = "document.bookmark.rename"
        case piecesFind = "pieces.find"
        case piecesSave = "pieces.save"
    }

    /// The calls on piece 0, which need no open piece.
    enum NoPiece: String, Sendable {
        case settings = "settings"
        case settingsState = "settings.state"
        case referenceKey = "reference.key"
        case referenceChord = "reference.chord"
        case referenceScale = "reference.scale"
        case referenceSound = "reference.sound"
        case referenceSounding = "reference.sounding"
        case referenceExplorer = "reference.explorer"
        case libraryPlan = "library.plan"
        case libraryRelink = "library.relink"
        case libraryConfirm = "library.confirm"
        case libraryLinked = "library.linked"
        case libraryLink = "library.link"
        case projectSummary = "project.summary"
        case projectMetadata = "project.metadata"
        case projectDrafts = "project.drafts"
        case projectSync = "project.sync"
        case projectOnline = "project.online"
        case projectPulled = "project.pulled"
        case projectResolve = "project.resolve"
        case projectSame = "project.same"
        case projectJoin = "project.join"
        case scanVector = "scan.vector"
        case scanJoin = "scan.join"
        case scanInsert = "scan.insert"
        case recordingParts = "recording.parts"
        case partsSplit = "parts.split"
    }

    /// `call` on this piece with the request, music-core's answer as its text; run it in `MusicCore.run`.
    func send(_ call: Call, _ request: String? = nil) -> String {
        MusicCore.call(piece: piece, call.rawValue, request)
    }

    /// `call` on piece 0 with the request, music-core's answer as its text; run it in `MusicCore.run`.
    static func sendWithoutPiece(_ call: NoPiece, _ request: String? = nil) -> String {
        MusicCore.call(piece: 0, call.rawValue, request)
    }
}
