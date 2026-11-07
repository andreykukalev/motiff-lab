import SwiftUI
import WebKit

/// A draft's view engraved over a few bars for Compare versions: Verovio's SVG and the ids of the notes from the version
/// (plum) and of the player's own (amber).
struct MergeCardImage: Equatable, Sendable {
    var svg: String
    var version: [String]
    var own: [String]
}

/// One view's notes on paper, in a small web view that scrolls sideways when the notes are larger than the window.
struct MergeCard: UIViewRepresentable {
    let image: MergeCardImage

    final class Coordinator {
        var shown: MergeCardImage?
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIView(context: Context) -> WKWebView {
        let view = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        view.isOpaque = false
        view.backgroundColor = .clear
        view.scrollView.backgroundColor = .clear
        view.scrollView.alwaysBounceVertical = false
        return view
    }

    func updateUIView(_ view: WKWebView, context: Context) {
        guard context.coordinator.shown != image else { return }
        context.coordinator.shown = image
        view.loadHTMLString(html, baseURL: nil)
    }

    /// The SVG on the paper of the score, its version notes plum and the player's own amber, as the web app's phrase card.
    private var html: String {
        let tokens = DesignTokens.stands.light
        let paper = tokens["paper"] ?? "#FCF9F2"
        let ink = tokens["notation"] ?? "#1F1B16"
        let plum = tokens["plum"] ?? "#6B3E7A"
        let amber = tokens["amber"] ?? "#F0B429"
        let ids = (try? JSONSerialization.data(withJSONObject: ["version": image.version, "own": image.own])).map { String(decoding: $0, as: UTF8.self) } ?? "{}"
        return """
        <!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
        html, body { margin: 0; background: \(paper); color: \(ink); }
        svg { display: block; fill: \(ink); color: \(ink); }
        g.note.from-version { fill: \(plum); stroke: \(plum); color: \(plum); }
        g.note.own-note { fill: \(amber); stroke: \(amber); color: \(amber); }
        </style></head><body>
        \(image.svg)
        <script>
        const ids = \(ids);
        const version = new Set(ids.version), own = new Set(ids.own);
        for (const note of document.querySelectorAll("g.note")) {
            const id = note.id.replace(/-\\d+$/, "");
            if (version.has(note.id) || version.has(id)) note.classList.add("from-version");
            if (own.has(note.id) || own.has(id)) note.classList.add("own-note");
        }
        </script></body></html>
        """
    }
}

/// «Сравнение версий» (testing item 34): the full-screen window over the same draft. At the left the changes grouped by
/// kind with Take the group, filtered; at the right the change shown in three views stacked (Mine, Version, Result), with
/// how much they show, the size of the notes, the choices and the keys M, V, ← → and ⌘Z / ⇧⌘Z of a keyboard.
struct CompareVersionsScreen: View {
    @ObservedObject var score: ScoreController
    let text: UIText
    @State private var filter = "all"
    @State private var see = "bar"
    @State private var zoom = 100
    @State private var cards: [String: MergeCardImage] = [:]

    private struct CardKey: Equatable {
        var at: Int?
        var see: String
        var zoom: Int
        var merge: MergeInfo?
    }

    var body: some View {
        VStack(spacing: 0) {
            if let merge = score.merge {
                header(merge)
                Rectangle().fill(Color.ds("hairline")).frame(height: 1)
                HStack(alignment: .top, spacing: 0) {
                    list(merge)
                        .frame(width: 300)
                    Rectangle().fill(Color.ds("hairline")).frame(width: 1)
                    main(merge)
                }
            }
        }
        .background(Color.ds("surface").ignoresSafeArea())
        .task(id: CardKey(at: score.mergeAt, see: see, zoom: zoom, merge: score.merge)) {
            var fresh: [String: MergeCardImage] = [:]
            for view in ["mine", "version", "result"] {
                if let card = await score.mergeCard(view, see: see, zoom: zoom) { fresh[view] = card }
            }
            cards = fresh
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["merge.window"])
    }

    private func header(_ merge: MergeInfo) -> some View {
        HStack(spacing: CGFloat.ds("s-2")) {
            Button { score.comparing = false } label: {
                Label { Text(text["merge.toNotes"]) } icon: { Icon(name: "back") }
            }
            .buttonStyle(DSButtonStyle(kind: .quiet))
            .keyboardShortcut(.escape, modifiers: [])
            Text(text["merge.window"])
                .dsType(.heading)
                .foregroundStyle(Color.ds("ink"))
                .accessibilityAddTraits(.isHeader)
            Text(merge.name).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
            Spacer()
            Text(text.format("merge.decided", String(merge.decided), String(merge.total)))
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
            Button { score.undoDraft() } label: {
                Label { Text(text["merge.undo"]) } icon: { Icon(name: "undo", size: CGFloat.ds("icon-s")) }
            }
            .disabled(!merge.canUndo)
            .keyboardShortcut("z", modifiers: .command)
            Button { score.redoDraft() } label: {
                Label { Text(text["merge.redo"]) } icon: { Icon(name: "undo", size: CGFloat.ds("icon-s")).scaleEffect(x: -1, y: 1) }
            }
            .disabled(!merge.canRedo)
            .keyboardShortcut("z", modifiers: [.command, .shift])
            Button(text["merge.resetShort"]) { score.resetDraft() }
                .disabled(!merge.changed)
            Button(text["merge.cancel"]) { score.cancelDraft() }
            Button { score.applyMerge() } label: {
                Label { Text(text["merge.apply"]) } icon: { Icon(name: "check", size: CGFloat.ds("icon-s")) }
            }
            .buttonStyle(DSButtonStyle(kind: .primary, compact: true))
        }
        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
        .padding(.horizontal, CGFloat.ds("s-4"))
        .padding(.vertical, CGFloat.ds("s-2"))
    }

    private func shown(_ merge: MergeInfo) -> [MergeInfo.Bar] {
        merge.bars.filter { bar in
            switch filter {
            case "sure": bar.sure
            case "doubtful": !bar.sure
            case "decided": bar.choice != nil
            default: true
            }
        }
    }

    private func list(_ merge: MergeInfo) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                SegmentedChoice(options: ["all", "sure", "doubtful", "decided"].map { ($0, text["merge.filter.\($0)"]) }, chosen: filter, label: text["merge.filter"]) {
                    filter = $0
                }
                let bars = shown(merge)
                ForEach(["addedNotes", "otherNotes", "rhythm"], id: \.self) { group in
                    let inGroup = bars.filter { $0.group == group }
                    if !inGroup.isEmpty {
                        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                            HStack(spacing: CGFloat.ds("s-1")) {
                                Text(text[groupKey(group)] + " · \(inGroup.count)").overline()
                                Spacer()
                                Button { score.chooseMergeGroup(group, "mine") } label: {
                                    RoundedRectangle(cornerRadius: 2).fill(mergeViewColor("mine")).frame(width: 10, height: 10)
                                }
                                .accessibilityLabel(text["merge.groupMine"])
                                Button { score.chooseMergeGroup(group, "version") } label: {
                                    RoundedRectangle(cornerRadius: 2).fill(mergeViewColor("version")).frame(width: 10, height: 10)
                                }
                                .accessibilityLabel(text["merge.groupVersion"])
                            }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                            ForEach(inGroup) { bar in
                                MergeRow(score: score, text: text, bar: bar, detail: "\(bar.marker) · \(bar.confidence) %")
                            }
                        }
                    }
                }
            }
            .padding(CGFloat.ds("s-4"))
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(text["merge.changesList"])
    }

    @ViewBuilder private func main(_ merge: MergeInfo) -> some View {
        if let at = score.mergeAt, let current = merge.bars.first(where: { $0.bar == at }) {
            ScrollView {
                VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
                    MergeStep(score: score, text: text, current: current)
                    HStack(spacing: CGFloat.ds("s-2")) {
                        MergeScope(score: score, text: text).fixedSize()
                        MergeChoiceButton(text: text["merge.takeMine"] + "  M", view: "mine", on: current.choice == "mine") { score.chooseMerge("mine") }
                            .keyboardShortcut("m", modifiers: [])
                        MergeChoiceButton(text: text["merge.takeVersion"] + "  V", view: "version", on: current.choice == "version") { score.chooseMerge("version") }
                            .keyboardShortcut("v", modifiers: [])
                        Button(text["merge.notDifference"]) { score.chooseMerge("notADifference") }
                            .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        if current.choice != nil {
                            Button(text["merge.resetBar"]) { score.undecideMerge(at) }
                                .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                        }
                    }
                    Text(text.format("merge.stateLine", mergeChoiceName(current.choice, text)))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Text(text["merge.see"]).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
                        SegmentedChoice(options: ["bar", "phrase", "sentence"].map { ($0, text["merge.see.\($0)"]) }, chosen: see, label: text["merge.see"]) {
                            see = $0
                        }
                        .fixedSize()
                        Spacer()
                        Button("−") { zoom = max(60, zoom - 10) }
                            .accessibilityLabel(text["merge.zoomOut"])
                        Text("\(zoom) %").dsType(.caption).foregroundStyle(Color.ds("ink-2")).monospacedDigit()
                        Button("+") { zoom = min(160, zoom + 10) }
                            .accessibilityLabel(text["merge.zoomIn"])
                    }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                    ForEach(["mine", "version", "result"], id: \.self) { view in
                        VStack(alignment: .leading, spacing: CGFloat.ds("s-1")) {
                            HStack(spacing: CGFloat.ds("s-2")) {
                                Circle().fill(mergeViewColor(view)).frame(width: 8, height: 8)
                                Text(text["merge.view.\(view)"]).font(DesignTokens.font(weight: 600, size: 14)).foregroundStyle(Color.ds("ink"))
                                if view == "version" { Text("· " + merge.name).dsType(.caption).foregroundStyle(Color.ds("ink-2")) }
                                if view == "result" { Text("· " + text["merge.resultHint"]).dsType(.caption).foregroundStyle(Color.ds("ink-2")) }
                                Spacer()
                                Button { score.hearMerge(view) } label: { Icon(name: "play", size: CGFloat.ds("icon-s")).frame(width: 32, height: 32) }
                                    .buttonStyle(BarActionStyle())
                                    .accessibilityLabel(text.format("merge.hearView", text["merge.view.\(view)"]))
                            }
                            if let card = cards[view] {
                                MergeCard(image: card).frame(height: CGFloat(140 * zoom / 100))
                            } else {
                                Color.ds("paper").frame(height: CGFloat(140 * zoom / 100))
                            }
                        }
                        .padding(CGFloat.ds("s-2"))
                        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-m")).strokeBorder(mergeViewColor(view), lineWidth: 1.5))
                    }
                    MergeListen(score: score, text: text)
                }
                .padding(CGFloat.ds("s-4"))
            }
            .background {
                // ← → through the changes from a keyboard.
                HStack {
                    Button("") { score.stepMerge(-1) }.keyboardShortcut(.leftArrow, modifiers: [])
                    Button("") { score.stepMerge(1) }.keyboardShortcut(.rightArrow, modifiers: [])
                }
                .opacity(0)
                .accessibilityHidden(true)
            }
        } else {
            Text(text["merge.nothing"])
                .dsType(.caption)
                .foregroundStyle(Color.ds("ink-2"))
                .padding(CGFloat.ds("s-4"))
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func groupKey(_ group: String) -> String {
        switch group {
        case "addedNotes": "merge.group.added"
        case "rhythm": "merge.group.rhythm"
        default: "merge.group.other"
        }
    }
}
