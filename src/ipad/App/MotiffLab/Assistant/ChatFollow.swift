import SwiftUI

/// How a chat's scroll follows what comes into it (Andrei, 2026-11-18), for the panel and the chat at the selection alike.
/// A change of the rows' height is the chat growing; a move of their end at the same height is the player scrolling, and
/// the chat follows while its end is in view. Growing while it follows, the chat shows a new answer from its start or its
/// end (`newAnswer`, the caller's choice), and anything else that grows at its end by its end; the player's own question
/// always comes into view. Scrolled up, nothing pulls the player back.
struct ChatFollow: Equatable {
    enum Scroll: Equatable {
        case none, end
        /// The start of the row with this id.
        case start(String)
    }

    enum NewAnswer: Equatable {
        case start, end
    }

    let newAnswer: NewAnswer
    private(set) var following = true
    private var rows: CGFloat?
    private var newest: String?

    init(newAnswer: NewAnswer) {
        self.newAnswer = newAnswer
    }

    /// The rows measured, `rows` tall with their end `end` below the top of a view `height` tall; `newest` names the last
    /// row (an answer, or the player's question on its way when `asked`): where to scroll now.
    mutating func measured(rows: CGFloat, end: CGFloat, height: CGFloat, newest: String?, asked: Bool) -> Scroll {
        guard rows != self.rows else {
            if height > 0 { following = end <= height + 1 }
            return .none
        }
        let isNew = newest != self.newest
        self.rows = rows
        self.newest = newest
        if asked && isNew {
            following = true
            return .end
        }
        guard following else { return .none }
        if isNew, let newest, newAnswer == .start {
            // Shown from its start, a long answer's end is below: the player reads down, the chat follows no more.
            following = rows <= height + 1
            return .start(newest)
        }
        return .end
    }
}

/// A chat's scroll view under ChatFollow, by finger or Pencil (nothing over it takes its pan: the chat at the selection's
/// card drags by its head only, MenuDrag). The rows give each answer its id (`.id`), as `newest` names it.
struct FollowingScroll<Rows: View>: View {
    let newest: String?
    let asked: Bool
    let rows: () -> Rows
    @State private var follow: ChatFollow
    @State private var height: CGFloat = 0

    init(newAnswer: ChatFollow.NewAnswer, newest: String?, asked: Bool = false, @ViewBuilder _ rows: @escaping () -> Rows) {
        self.newest = newest
        self.asked = asked
        self.rows = rows
        _follow = State(initialValue: ChatFollow(newAnswer: newAnswer))
    }

    var body: some View {
        ScrollViewReader { reader in
            ScrollView {
                VStack(spacing: 0) {
                    rows()
                            .onGeometryChange(for: CGRect.self) { $0.frame(in: .named(Self.space)) } action: { frame in
                            switch follow.measured(rows: frame.height, end: frame.maxY, height: height, newest: newest, asked: asked) {
                            case .none: break
                            case .end: reader.scrollTo(Self.end, anchor: .bottom)
                            case let .start(id): reader.scrollTo(id, anchor: .top)
                            }
                        }
                    Color.clear.frame(height: 0).id(Self.end)
                }
            }
            .coordinateSpace(.named(Self.space))
            .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { height = $0 }
        }
    }

    private static var end: String { "chat-end" }
    private static var space: String { "chat" }
}
