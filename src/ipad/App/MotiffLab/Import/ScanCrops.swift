import PDFKit
import UIKit

/// A bar's box on a scanned page as the scan server's evidence gives it (decision record 0035 section 6, v1.3 i-2): in the
/// pixels of the page image after the server turned it `rotated` degrees clockwise. Pure geometry: where that box is on
/// the page as the PDF shows it, and how the crop is turned to look as on the server's image.
struct ScanCrop: Equatable, Sendable {
    /// Left, top, right, bottom.
    var box: [Double]
    var width: Double
    var height: Double
    /// 0, 90, 180 or 270.
    var rotated: Int

    /// `scan.join`'s "crop" of a bar; nil when it has none or it is not one.
    init?(_ object: Any?) {
        guard let object = object as? [String: Any], let box = (object["box"] as? [NSNumber])?.map(\.doubleValue), box.count == 4,
              let width = (object["width"] as? NSNumber)?.doubleValue, let height = (object["height"] as? NSNumber)?.doubleValue,
              width > 0, height > 0 else { return nil }
        self.init(box: box, width: width, height: height, rotated: (object["rotated"] as? NSNumber)?.intValue ?? 0)
    }

    init(box: [Double], width: Double, height: Double, rotated: Int) {
        self.box = box
        self.width = width
        self.height = height
        self.rotated = ((rotated % 360) + 360) % 360
    }

    /// The page as the PDF shows it, before the server turned it: as wide and high as that.
    var pageSize: CGSize { rotated == 90 || rotated == 270 ? CGSize(width: height, height: width) : CGSize(width: width, height: height) }

    /// The box on the page as the PDF shows it, from 0 to 1 from its top left.
    var pageRect: CGRect {
        let page = pageSize
        func unturned(_ x: Double, _ y: Double) -> CGPoint {
            switch rotated {
            case 90: CGPoint(x: y, y: page.height - x)
            case 180: CGPoint(x: page.width - x, y: page.height - y)
            case 270: CGPoint(x: page.width - y, y: x)
            default: CGPoint(x: x, y: y)
            }
        }
        let a = unturned(box[0], box[1]), b = unturned(box[2], box[3])
        let rect = CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
        return CGRect(x: rect.minX / page.width, y: rect.minY / page.height, width: rect.width / page.width, height: rect.height / page.height)
            .intersection(CGRect(x: 0, y: 0, width: 1, height: 1))
    }

    /// How the crop of the page is turned to show as on the server's image.
    var orientation: UIImage.Orientation {
        switch rotated {
        case 90: .right
        case 180: .down
        case 270: .left
        default: .up
        }
    }
}

enum ScanCrops {
    /// The bars to check of a `scan.join` answer kept on this iPad (`ScanReviewStore`): each with its variants and its crop
    /// cut from `pdf`'s page, `pages` naming the PDF page of each of the answer's pages in order. Nothing is kept when the
    /// answer has no bar to check or no fingerprint. A `scan.insert` answer (v1.3 i-1b) adds its page's bars to those kept
    /// for the notes it went into ("was"), each from "firstBar" on moved on by its "bars", crops and all. Writes files; sends nothing.
    static func keepReview(_ answer: [String: Any], pages: [Int], pdf: Data, in root: URL = ScanReviewStore.root) {
        guard let fingerprint = answer["fingerprint"] as? String else { return }
        var bars: [ScanReview.Bar] = []
        var crops: [Int: Data] = [:]
        if let was = answer["was"] as? String, let first = answer["firstBar"] as? Int, let put = answer["bars"] as? Int, let kept = ScanReviewStore.load(was, in: root) {
            for bar in kept.bars {
                let moved = bar.bar < first ? bar.bar : bar.bar + put
                if bar.cropped, let png = ScanReviewStore.crop(was, bar: bar.bar, in: root) { crops[moved] = png }
                bars.append(ScanReview.Bar(bar: moved, variants: bar.variants, cropped: crops[moved] != nil))
            }
        }
        let document = PDFDocument(data: pdf)
        for (index, page) in (answer["pages"] as? [[String: Any]] ?? []).enumerated() {
            let merge = page["merge"] as? [String: Any]
            let checked = (merge?["bars"] as? [[String: Any]] ?? []).filter { $0["level"] as? String == "check" }
            var image: UIImage?
            for entry in checked {
                guard let bar = entry["bar"] as? Int else { continue }
                let variants = (entry["variants"] as? [[String: Any]] ?? []).compactMap(ScanVariant.init)
                if let crop = ScanCrop(entry["crop"]), index < pages.count, let pdfPage = document?.page(at: pages[index] - 1) {
                    image = image ?? drawn(pdfPage, size: crop.pageSize)
                    if let image, let png = cut(image, crop) { crops[bar] = png }
                }
                bars.append(ScanReview.Bar(bar: bar, variants: variants, cropped: crops[bar] != nil))
            }
        }
        guard !bars.isEmpty else { return }
        ScanReviewStore.save(ScanReview(fingerprint: fingerprint, bars: bars), crops: crops, in: root)
    }

    /// The PDF page as the PDF shows it (its own turn applied), drawn `size` pixels wide and high at scale 1 in 8-bit
    /// colour, white under it, as the server's image: by Quartz, which maps the page's crop box and turn into the page as shown.
    static func drawn(_ page: PDFPage, size: CGSize) -> UIImage? {
        guard let source = page.pageRef else { return nil }
        let box = source.getBoxRect(.cropBox)
        let shown = source.rotationAngle % 180 == 0 ? box.size : CGSize(width: box.height, height: box.width)
        guard shown.width > 0, shown.height > 0 else { return nil }
        return UIGraphicsImageRenderer(size: size, format: plain()).image { context in
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            let quartz = context.cgContext
            quartz.translateBy(x: 0, y: size.height)
            quartz.scaleBy(x: size.width / shown.width, y: -size.height / shown.height)
            quartz.concatenate(source.getDrawingTransform(.cropBox, rect: CGRect(origin: .zero, size: shown), rotate: 0, preserveAspectRatio: true))
            quartz.drawPDFPage(source)
        }
    }

    /// Scale 1 and 8-bit colour, so a crop is as many pixels as the server's image and its PNG small.
    private static func plain() -> UIGraphicsImageRendererFormat {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.preferredRange = .standard
        return format
    }

    /// The crop's part of the page image, turned as on the server's image, as PNG.
    private static func cut(_ page: UIImage, _ crop: ScanCrop) -> Data? {
        guard let source = page.cgImage else { return nil }
        let rect = crop.pageRect
        let pixels = CGRect(x: rect.minX * CGFloat(source.width), y: rect.minY * CGFloat(source.height), width: rect.width * CGFloat(source.width), height: rect.height * CGFloat(source.height)).integral
        guard !pixels.isEmpty, let part = source.cropping(to: pixels) else { return nil }
        let turned = UIImage(cgImage: part, scale: 1, orientation: crop.orientation)
        return UIGraphicsImageRenderer(size: turned.size, format: plain()).pngData { _ in turned.draw(at: .zero) }
    }
}
