import SwiftUI

/// The sheets and drawers over the score (0018 sections 5 and 6): where the sheet over the right goes and what it holds
/// (Sources, Check, a draft's map or Insights), and the Contents drawer. Which of them is open is `PanelState`'s.
extension ScoreScreenContent {
    /// Where the Insights sheet is: none, beside the score on the right, or over the lower half.
    enum SheetPlace: Equatable {
        case none, side
        case bottom(CGFloat)

        var bottomHeight: CGFloat {
            if case .bottom(let height) = self { return height }
            return 0
        }
    }

    /// Where the sheet over the score goes: Insights, Check, Other versions and a draft's map (v1.1) beside the score in
    /// landscape and over the lower half in portrait, so the pills and toasts make way; Sources always as a panel over the lower half (#582 item 4), so it is plainly apart from
    /// the document and its view.
    func sheetPlace(landscape: Bool, height: CGFloat) -> SheetPlace {
        if panels[.versions] && sourcesOn { return .bottom(height / 2) }
        return panels.sheetOpen || mapOwner != nil ? (landscape ? .side : .bottom(height / 2)) : .none
    }

    /// The Sources of the piece a draft's view belongs to while the draft's map is open, in the sheet's place (v1.1);
    /// nil while it is closed or on any screen that is no draft's.
    var mapOwner: SourcesController? {
        piece.draft != nil && draftMap?.wrappedValue != nil ? pieceUnder : nil
    }

    /// Where a sheet at the side starts: under the bar; a draft's map under the draft's pill too, which stays in reach.
    private var sideSheetTop: CGFloat {
        mapOwner == nil ? Self.barBottom : barsBottom
    }

    /// What the sheet over the right holds: Sources, Check, a draft's map (v1.1, DraftMap.swift), or Insights.
    @ViewBuilder private func sheetContent() -> some View {
        if panels[.versions] && sourcesOn {
            sourcesSheet
        } else if panels[.check] {
            CheckDrawer(score: score, checkModel: checkModel, selectionModel: selectionModel, text: text, close: { change(.close(.check)) }, back: panels.fromMore.contains(.check) ? (label: text["bar.more"], action: { backToMore(.check) }) : nil)
        } else if !panels.sheetOpen, let owner = mapOwner, let draft = piece.draft {
            DraftMap(score: score, seams: score.seamsModel, techniques: score.draftModel, draft: draft, owner: owner, text: text, reopen: reopen)
        } else {
            InsightsSheet(insights: insights, selectionModel: selectionModel, player: player, range: variationRange, text: text) { change(.close(.insights)) }
        }
    }

    /// The sheet over the right where it goes: beside the score under the bars, or over the lower half.
    @ViewBuilder func sideSheet(_ sheet: SheetPlace) -> some View {
        switch sheet {
        case .none:
            EmptyView()
        case .side:
            sheetContent()
                .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                .dsShadow("shadow-sheet")
                .frame(width: ScoreScreen.sheetWidth)
                .padding(.top, sideSheetTop)
                .padding([.trailing, .bottom], CGFloat.ds("s-3"))
                .transition(.move(edge: .trailing).combined(with: .opacity))
        case .bottom(let height):
            sheetContent()
                .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                .dsShadow("shadow-sheet")
                .frame(height: height)
                .padding(.horizontal, CGFloat.ds("s-3"))
                .transition(.move(edge: .bottom))
                .ignoresSafeArea(edges: .bottom)
        }
    }

    /// The Contents drawer where it goes: over the left of the page in landscape, from the bottom in portrait.
    @ViewBuilder func contentsDrawer(_ place: SheetPlace) -> some View {
        switch place {
        case .none:
            EmptyView()
        case .side:
            ContentsDrawer(contents: contents, score: score, selectionModel: selectionModel, text: text) { change(.close(.contents)) }
                .clipShape(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")))
                .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-l")).strokeBorder(Color.ds("hairline")))
                .dsShadow("shadow-sheet")
                .frame(width: ScoreScreen.contentsWidth)
                .padding(.top, CGFloat.ds("s-3") + CGFloat.ds("h-touch") + CGFloat.ds("s-2"))
                .padding([.leading, .bottom], CGFloat.ds("s-3"))
                .transition(.move(edge: .leading).combined(with: .opacity))
        case .bottom(let height):
            VStack {
                Spacer(minLength: 0)
                ContentsDrawer(contents: contents, score: score, selectionModel: selectionModel, text: text) { change(.close(.contents)) }
                    .clipShape(UnevenRoundedRectangle(topLeadingRadius: CGFloat.ds("r-l"), topTrailingRadius: CGFloat.ds("r-l")))
                    .dsShadow("shadow-sheet")
                    .frame(height: height)
                    .padding(.horizontal, CGFloat.ds("s-3"))
            }
            .transition(.move(edge: .bottom))
            .ignoresSafeArea(edges: .bottom)
        }
    }
}

extension PanelState {
    /// A panel as a binding for the body that read this value (a sheet's or popover's `isPresented`, a toggle): what it sets
    /// goes through `change`.
    func binding(_ panel: Panel, change: @escaping (Intent) -> Void) -> Binding<Bool> {
        Binding(get: { self[panel] }, set: { change($0 ? .open(panel) : .close(panel)) })
    }
}
