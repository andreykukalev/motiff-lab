import SwiftUI

/// The PDF's overflow: Download the PDF (the bytes as added, through the share sheet). Recognise notes is in the bar.
struct PdfMore: View {
    @ObservedObject var pdf: PdfController
    let text: UIText

    var body: some View {
        VStack(alignment: .leading, spacing: CGFloat.ds("s-3")) {
            if let url = pdf.shareable {
                ShareLink(item: url) {
                    HStack(spacing: CGFloat.ds("s-2")) {
                        Icon(name: "export")
                        Text(text["pdf.download"])
                    }
                }
                .buttonStyle(DSButtonStyle(kind: .quiet))
            }
        }
        .padding(CGFloat.ds("s-4"))
        .frame(width: 320, alignment: .leading)
        .background(Color.ds("surface"))
    }
}
