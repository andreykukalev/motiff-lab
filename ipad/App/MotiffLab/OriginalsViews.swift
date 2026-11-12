import SwiftUI

/// Where an original's bytes are, in one quiet line (the web's `.source-card .held`): dashed when they are not on this
/// iPad, and in the warning ink when the linked file has changed.
struct OriginalPlaceLabel: View {
    let label: String
    let badge: OriginalBadge

    var body: some View {
        let away = !badge.isHere
        Text(label)
            .dsType(.caption)
            .foregroundStyle(badge == .changed ? Color.ds("warning") : Color.ds("ink-2"))
            .padding(.horizontal, away ? CGFloat.ds("s-2") : 0)
            .overlay {
                if away {
                    RoundedRectangle(cornerRadius: CGFloat.ds("r-xs"))
                        .strokeBorder(Color.ds("hairline-strong"), style: StrokeStyle(lineWidth: 1, dash: [CGFloat.ds("s-1"), CGFloat.ds("s-1")]))
                }
            }
    }
}

/// An original in Sources that is not kept in the piece here (version 0.3): its name and kind, where it is, and for the
/// PDF «Привязать оригинал» from Files, or, once linked, Open PDF and Copy to iPad. A picked file that is the original
/// asks once how to keep it: where it lies, as a link, or as a copy in the piece.
struct OriginalCard: View {
    @ObservedObject var originals: OriginalsController
    let original: OriginalEntry
    let text: UIText
    /// Opens the linked PDF over the score; nil while it is not linked here.
    var openPdf: (() -> Void)?
    /// Opens Files to pick the same file.
    let relink: () -> Void

    var body: some View {
        let badge = originals.badge(original)
        VStack(alignment: .leading, spacing: CGFloat.ds("s-2")) {
            HStack(spacing: CGFloat.ds("s-3")) {
                Icon(name: original.kind == "audio" ? "mic" : "pages").foregroundStyle(Color.ds("ink-2"))
                VStack(alignment: .leading, spacing: 0) {
                    Text(original.shown)
                        .font(DesignTokens.font(weight: 600, size: 15))
                        .foregroundStyle(Color.ds("ink"))
                    Text(OriginalAway.kindText(original.kind, text))
                        .dsType(.caption)
                        .foregroundStyle(Color.ds("ink-2"))
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .accessibilityElement(children: .combine)
            OriginalPlaceLabel(label: badge.text(text), badge: badge)
            if badge == .changed {
                caption(text["originals.changedHint"])
            }
            if original.isDocument {
                actions(badge)
            }
            if originals.loading == original.id {
                HStack(spacing: CGFloat.ds("s-2")) {
                    ProgressView()
                    caption(text["originals.loading"])
                }
            }
            if let status = originals.status[original.id] {
                caption(status)
            }
        }
        .padding(CGFloat.ds("s-3"))
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(RoundedRectangle(cornerRadius: CGFloat.ds("r-s")).strokeBorder(Color.ds("hairline")))
        .confirmationDialog(
            text["originals.linkOrCopy"],
            isPresented: Binding(get: { originals.choice?.id == original.id }, set: { if !$0 { originals.choice = nil } }),
            titleVisibility: .visible,
            presenting: originals.choice
        ) { picked in
            if picked.bookmark != nil {
                Button(text["originals.keepWhere"]) { Task { await originals.keepLink(picked) } }
            }
            Button(text["originals.copyToIpad"]) { Task { await originals.keepCopy(picked) } }
            Button(text["common.cancel"], role: .cancel) {}
        } message: { _ in
            Text(text["originals.linkOrCopyHint"])
        }
    }

    /// Linked: Open PDF and Copy to iPad. Elsewhere, nowhere or changed: «Привязать оригинал», with what it does.
    @ViewBuilder private func actions(_ badge: OriginalBadge) -> some View {
        if badge == .linked {
            HStack(spacing: CGFloat.ds("s-2")) {
                if let openPdf {
                    Button(text["sources.openPdf"], action: openPdf)
                        .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
                }
                Button(text["originals.copyToIpad"]) { Task { await originals.copyLinked(original) } }
                    .buttonStyle(DSButtonStyle(kind: .quiet, compact: true))
            }
            .disabled(originals.loading != nil)
        } else {
            Button(text["originals.relink"], action: relink)
                .buttonStyle(DSButtonStyle(kind: .secondary, compact: true))
                .disabled(originals.loading != nil)
                .accessibilityHint(text["originals.relinkHintIpad"])
            caption(text["originals.relinkHintIpad"])
        }
    }

    /// A line under the original, wrapped as long as it needs.
    private func caption(_ words: String) -> some View {
        Text(words).fixedSize(horizontal: false, vertical: true).dsType(.caption).foregroundStyle(Color.ds("ink-2"))
    }
}
