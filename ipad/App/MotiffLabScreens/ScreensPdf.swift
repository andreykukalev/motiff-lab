import UIKit

/// The PDF the UI tests open (ScreensUITests, SwitchUITests).
enum ScreensPdf {
    /// A three-page A4 PDF that looks like printed music from far away: a title, then ten systems of two staves a
    /// page, with bar lines. It stands in for a player's own PDF, which never goes into the repository. `scanned` lays
    /// a grey grain of a scanner's size under each page, so the file weighs megabytes as a scanned score does.
    static func write(name: String = "Elise.pdf", scanned: Bool = false) -> URL? {
        let grain = scanned ? grainImage() : nil
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(name)
        let page = CGRect(x: 0, y: 0, width: 595, height: 842)
        let renderer = UIGraphicsPDFRenderer(bounds: page)
        do {
            try renderer.writePDF(to: url) { context in
                for number in 1...3 {
                    context.beginPage()
                    let cg = context.cgContext
                    grain?.draw(in: page)
                    var top: CGFloat = 70
                    if number == 1 {
                        let title = NSAttributedString(string: "Für Elise", attributes: [.font: UIFont(name: "Times New Roman", size: 26) ?? UIFont.systemFont(ofSize: 26)])
                        title.draw(at: CGPoint(x: (page.width - title.size().width) / 2, y: 48))
                        let composer = NSAttributedString(string: "L. van Beethoven", attributes: [.font: UIFont(name: "Times New Roman", size: 12) ?? UIFont.systemFont(ofSize: 12)])
                        composer.draw(at: CGPoint(x: page.width - 60 - composer.size().width, y: 88))
                        top = 120
                    }
                    cg.setStrokeColor(UIColor.black.cgColor)
                    cg.setLineWidth(0.6)
                    var system = top
                    while system + 60 < page.height - 50 {
                        for staff in 0..<2 {
                            let y = system + CGFloat(staff) * 38
                            for line in 0..<5 {
                                cg.move(to: CGPoint(x: 50, y: y + CGFloat(line) * 4.5))
                                cg.addLine(to: CGPoint(x: page.width - 50, y: y + CGFloat(line) * 4.5))
                            }
                        }
                        for bar in 0...4 {
                            let x = 50 + (page.width - 100) * CGFloat(bar) / 4
                            cg.move(to: CGPoint(x: x, y: system))
                            cg.addLine(to: CGPoint(x: x, y: system + 56))
                        }
                        cg.strokePath()
                        cg.setFillColor(UIColor.black.cgColor)
                        for note in 0..<16 {
                            let x = 70 + (page.width - 140) * CGFloat(note) / 16
                            let y = system + CGFloat((note * 5) % 9) * 2.25
                            cg.fillEllipse(in: CGRect(x: x, y: y, width: 5.5, height: 4.2))
                        }
                        system += 84
                    }
                    let folio = NSAttributedString(string: "\(number)", attributes: [.font: UIFont.systemFont(ofSize: 10)])
                    folio.draw(at: CGPoint(x: page.width / 2, y: page.height - 34))
                }
            }
            return url
        } catch {
            print("SCREEN-WRITE \(url.path): \(error)")
            return nil
        }
    }

    /// Noise that does not compress, about two megabytes a page, light enough for the lines to read over it.
    private static func grainImage() -> UIImage? {
        let width = 1240, height = 1754
        var bytes = [UInt8](repeating: 0, count: width * height)
        var seed: UInt32 = 2_463_534_242
        for index in bytes.indices {
            seed ^= seed << 13
            seed ^= seed >> 17
            seed ^= seed << 5
            bytes[index] = 215 + UInt8(seed % 40)
        }
        guard let provider = CGDataProvider(data: Data(bytes) as CFData),
              let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 8, bytesPerRow: width,
                                  space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGBitmapInfo(rawValue: 0), provider: provider,
                                  decode: nil, shouldInterpolate: false, intent: .defaultIntent) else { return nil }
        return UIImage(cgImage: image)
    }
}
