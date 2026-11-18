import SwiftUI

/// Whether a chat's scroll follows its end (Andrei, 2026-11-18, the chat at the selection): new text brings the latest
/// line into view, unless the player has scrolled up from the end; scrolled back down to it, the chat follows again. Each
/// measure of the chat's end says whether to scroll to it now. After new text the end is out of view until the scroll
/// has brought it in (settling): that is the text growing, not the player scrolling up.
struct ChatFollow<Content: Equatable>: Equatable {
    private(set) var following = true
    private var settling = false
    private var seen: Content?

    /// The end of the chat measured `end` points below the top of its view, `height` points tall, showing `content`: true
    /// when the chat should scroll to its end now.
    mutating func measured(_ content: Content, end: CGFloat, height: CGFloat) -> Bool {
        let inView = end <= height + 1
        if content != seen {
            seen = content
            settling = following && !inView
            return settling
        }
        if settling {
            settling = !inView
            return settling
        }
        following = inView
        return false
    }
}

/// A chat's scroll view that follows its end (ChatFollow), by finger or Pencil: the scroll view's own pan, which nothing
/// over it takes (the chat's card drags by its handle only, MenuDrag).
struct FollowingScroll<Content: Equatable, Rows: View>: View {
    let content: Content
    let rows: () -> Rows
    @State private var follow = ChatFollow<Content>()
    @State private var height: CGFloat = 0
    @State private var end: CGFloat = 0

    init(following content: Content, @ViewBuilder _ rows: @escaping () -> Rows) {
        self.content = content
        self.rows = rows
    }

    var body: some View {
        ScrollViewReader { reader in
            ScrollView {
                VStack(spacing: 0) {
                    rows()
                    Color.clear.frame(height: 1).id(Self.endId).background {
                        GeometryReader { proxy in
                            Color.clear.preference(key: ChatEndKey.self, value: proxy.frame(in: .named(Self.space)).maxY)
                        }
                    }
                }
            }
            .coordinateSpace(.named(Self.space))
            .background {
                GeometryReader { proxy in
                    Color.clear
                        .onAppear { height = proxy.size.height }
                        .onChange(of: proxy.size.height) { _, now in height = now }
                }
            }
            .onPreferenceChange(ChatEndKey.self) { now in end = now }
            .onChange(of: [end, height]) { _, _ in measure(reader) }
        }
    }

    private func measure(_ reader: ScrollViewProxy) {
        guard height > 0, follow.measured(content, end: end, height: height) else { return }
        reader.scrollTo(Self.endId, anchor: .bottom)
    }

    private static var endId: String { "chat-end" }
    private static var space: String { "chat" }
}

private struct ChatEndKey: PreferenceKey {
    static let defaultValue: CGFloat = 0

    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = nextValue()
    }
}
