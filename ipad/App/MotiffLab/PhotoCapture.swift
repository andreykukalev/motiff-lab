import PDFKit
import SwiftUI
import UIKit
import VisionKit

/// The iPad's document camera (VisionKit, v0.4 «Фото страниц»): several pages in a row, each found on the photo, cropped
/// and its perspective straightened, on the device and free, on any iPad, with a finger. It answers the pages in order,
/// or nothing when the player cancels.
struct DocumentCamera: UIViewControllerRepresentable {
    let done: ([UIImage]) -> Void

    static var isAvailable: Bool { VNDocumentCameraViewController.isSupported }

    func makeCoordinator() -> Coordinator { Coordinator(done: done) }

    func makeUIViewController(context: Context) -> VNDocumentCameraViewController {
        let camera = VNDocumentCameraViewController()
        camera.delegate = context.coordinator
        return camera
    }

    func updateUIViewController(_ camera: VNDocumentCameraViewController, context: Context) {}

    final class Coordinator: NSObject, VNDocumentCameraViewControllerDelegate {
        let done: ([UIImage]) -> Void

        init(done: @escaping ([UIImage]) -> Void) {
            self.done = done
        }

        func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFinishWith scan: VNDocumentCameraScan) {
            done((0..<scan.pageCount).map { scan.imageOfPage(at: $0) })
        }

        func documentCameraViewControllerDidCancel(_ controller: VNDocumentCameraViewController) {
            done([])
        }

        func documentCameraViewController(_ controller: VNDocumentCameraViewController, didFailWithError error: Error) {
            done([])
        }
    }
}

/// The photos of pages as the iPad handles them: kept as JPEG of a sensible size, turned, measured (PageLook), put
/// together as one PDF of images (PDFKit draws it), and a page of that PDF drawn again as a photo to send.
enum PhotoImages {
    /// The long side photos are kept at: enough for the scan server, which reads a copy 1920 pixels wide.
    static let keptSide: CGFloat = 3000
    /// The width of a PDF page in points: an A4 page, its height following the photo.
    static let pageWidth: CGFloat = 595

    /// A photo as JPEG, its long side at most `keptSide`, upright as the camera saw it.
    static func jpeg(_ image: UIImage) -> Data? {
        let size = image.size
        let scale = min(1, keptSide / max(size.width * image.scale, size.height * image.scale, 1))
        let target = CGSize(width: (size.width * image.scale * scale).rounded(), height: (size.height * image.scale * scale).rounded())
        return draw(target) { image.draw(in: CGRect(origin: .zero, size: target)) }.jpegData(compressionQuality: 0.85)
    }

    /// The photo turned a number of quarter turns clockwise.
    static func turned(_ photo: Data, _ turns: Int) -> Data {
        let quarter = ((turns % 4) + 4) % 4
        guard quarter != 0, let image = UIImage(data: photo) else { return photo }
        let size = CGSize(width: image.size.width * image.scale, height: image.size.height * image.scale)
        let target = quarter % 2 == 1 ? CGSize(width: size.height, height: size.width) : size
        let turned = draw(target) {
            guard let context = UIGraphicsGetCurrentContext() else { return }
            context.translateBy(x: target.width / 2, y: target.height / 2)
            context.rotate(by: CGFloat(quarter) * .pi / 2)
            image.draw(in: CGRect(x: -size.width / 2, y: -size.height / 2, width: size.width, height: size.height))
        }
        return turned.jpegData(compressionQuality: 0.85) ?? photo
    }

    /// How the photo looks, measured on a grey copy `PageLook.workSide` pixels on its long side, off the main thread.
    static func look(_ photo: Data) async -> PageLook? {
        await Task.detached(priority: .userInitiated) { () -> PageLook? in
            guard let image = UIImage(data: photo)?.cgImage else { return nil }
            let long = CGFloat(max(image.width, image.height))
            let scale = min(1, CGFloat(PageLook.workSide) / max(long, 1))
            let width = max(3, Int((CGFloat(image.width) * scale).rounded()))
            let height = max(3, Int((CGFloat(image.height) * scale).rounded()))
            var grey = [UInt8](repeating: 0, count: width * height)
            let drawn = grey.withUnsafeMutableBytes { buffer -> Bool in
                guard let context = CGContext(
                    data: buffer.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width,
                    space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue
                ) else { return false }
                context.interpolationQuality = .high
                context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
                return true
            }
            // A quarter turn changes neither number, so the photo is measured as taken.
            return drawn ? PageLook.measure(grey: grey, width: width, height: height) : nil
        }.value
    }

    /// The pages as one PDF: each photo, turned as the player turned it, on a page of its own proportions.
    static func pdf(_ pages: [PhotoPage]) -> Data {
        let photos = pages.compactMap { UIImage(data: turned($0.photo, $0.turns)) }
        let renderer = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: pageWidth, height: pageWidth * 1.414))
        return renderer.pdfData { context in
            for photo in photos {
                let height = pageWidth * photo.size.height / max(photo.size.width, 1)
                let bounds = CGRect(x: 0, y: 0, width: pageWidth, height: height)
                context.beginPage(withBounds: bounds, pageInfo: [:])
                photo.draw(in: bounds)
            }
        }
    }

    /// A page of the PDF drawn again as a JPEG photo, `keptSide` pixels on its long side, to send to the scan server.
    static func photo(of page: PDFPage) -> Data? {
        let bounds = page.bounds(for: .mediaBox)
        guard bounds.width > 0, bounds.height > 0 else { return nil }
        let scale = keptSide / max(bounds.width, bounds.height)
        let size = CGSize(width: (bounds.width * scale).rounded(), height: (bounds.height * scale).rounded())
        let image = draw(size) {
            guard let context = UIGraphicsGetCurrentContext() else { return }
            UIColor.white.setFill()
            context.fill(CGRect(origin: .zero, size: size))
            context.translateBy(x: 0, y: size.height)
            context.scaleBy(x: scale, y: -scale)
            context.translateBy(x: -bounds.minX, y: -bounds.minY)
            page.draw(with: .mediaBox, to: context)
        }
        return image.jpegData(compressionQuality: 0.9)
    }

    /// The PDF with one page drawn from a new photo, the others exactly as they are.
    static func pdf(_ document: PDFDocument, replacing index: Int, with photo: Data) -> Data? {
        guard let copy = document.dataRepresentation().flatMap({ PDFDocument(data: $0) }), index >= 0, index < copy.pageCount,
              let page = PDFDocument(data: pdf([PhotoPage(photo: photo)]))?.page(at: 0) else { return nil }
        copy.removePage(at: index)
        copy.insert(page, at: index)
        return copy.dataRepresentation()
    }

    /// Draws at one pixel a point, whatever the screen.
    private static func draw(_ size: CGSize, _ body: () -> Void) -> UIImage {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = true
        return UIGraphicsImageRenderer(size: size, format: format).image { _ in body() }
    }
}
