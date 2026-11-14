import Foundation

/// How a photo of a page looks before it is sent (v0.4, experiment «Фото страниц»): how dark its print is against the paper
/// and how sharp its edges are, measured on the iPad as the scan server measures them (services/omr/app/quality.py, a
/// port), so the warning here says what the server's page report would say. Numbers only; nothing about the music.
struct PageLook: Equatable, Sendable {
    /// How steep the edges of the print are against its contrast; about 1 is crisp.
    var sharpness: Double
    /// How much darker the darkest print is than the paper around it, in grey levels (0–255).
    var contrast: Int

    /// The long side of the copy measured, as on the server.
    static let workSide = 2000
    /// Fainter print than this makes a weak page (music-core `PageReport.WeakContrast`).
    static let weakContrast = 95
    /// Softer edges than this make a weak page (music-core `PageReport.WeakSharpness`).
    static let weakSharpness = 0.8

    var isDark: Bool { contrast < Self.weakContrast }
    var isBlurry: Bool { sharpness < Self.weakSharpness }

    /// Measures a greyscale image, one byte a pixel, row by row (already scaled to `workSide` on its long side).
    static func measure(grey: [UInt8], width: Int, height: Int) -> PageLook {
        guard width > 2, height > 2, grey.count >= width * height else { return PageLook(sharpness: 0, contrast: 0) }
        let contrast = percentile(ink(grey, width, height), 0.995)
        guard contrast > 0 else { return PageLook(sharpness: 0, contrast: 0) }
        let edges = percentile(laplacian(grey, width, height), 0.995)
        return PageLook(sharpness: (Double(edges) / Double(contrast) * 100).rounded() / 100, contrast: contrast)
    }

    /// How much darker each pixel is than the paper around it: the page reduced 16 times, the lightest of 5 × 5 there, and
    /// that spread back over the page, so shadows and uneven light drop out.
    static func ink(_ grey: [UInt8], _ width: Int, _ height: Int) -> [UInt8] {
        let cellsX = (width + 15) / 16, cellsY = (height + 15) / 16
        var reduced = [Double](repeating: 0, count: cellsX * cellsY)
        for cellY in 0..<cellsY {
            for cellX in 0..<cellsX {
                var sum = 0, count = 0
                for y in (cellY * 16)..<min(height, cellY * 16 + 16) {
                    for x in (cellX * 16)..<min(width, cellX * 16 + 16) {
                        sum += Int(grey[y * width + x])
                        count += 1
                    }
                }
                reduced[cellY * cellsX + cellX] = Double(sum) / Double(max(1, count))
            }
        }
        var paper = reduced
        for cellY in 0..<cellsY {
            for cellX in 0..<cellsX {
                var lightest = 0.0
                for y in max(0, cellY - 2)...min(cellsY - 1, cellY + 2) {
                    for x in max(0, cellX - 2)...min(cellsX - 1, cellX + 2) { lightest = max(lightest, reduced[y * cellsX + x]) }
                }
                paper[cellY * cellsX + cellX] = lightest.rounded()
            }
        }
        var result = [UInt8](repeating: 0, count: width * height)
        for y in 0..<height {
            let fy = min(max((Double(y) + 0.5) / 16 - 0.5, 0), Double(cellsY - 1))
            let y0 = Int(fy), y1 = min(y0 + 1, cellsY - 1), wy = fy - Double(y0)
            for x in 0..<width {
                let fx = min(max((Double(x) + 0.5) / 16 - 0.5, 0), Double(cellsX - 1))
                let x0 = Int(fx), x1 = min(x0 + 1, cellsX - 1), wx = fx - Double(x0)
                let top = paper[y0 * cellsX + x0] * (1 - wx) + paper[y0 * cellsX + x1] * wx
                let bottom = paper[y1 * cellsX + x0] * (1 - wx) + paper[y1 * cellsX + x1] * wx
                let light = Int((top * (1 - wy) + bottom * wy).rounded())
                result[y * width + x] = UInt8(clamping: light - Int(grey[y * width + x]))
            }
        }
        return result
    }

    /// The Laplacian of the page (four neighbours less four times the pixel), cut to 0–255, with the edge rows and
    /// columns kept as they are, as Pillow's 3 × 3 kernel filter leaves them.
    static func laplacian(_ grey: [UInt8], _ width: Int, _ height: Int) -> [UInt8] {
        var result = grey
        for y in 1..<(height - 1) {
            for x in 1..<(width - 1) {
                let index = y * width + x
                let around = Int(grey[index - 1]) + Int(grey[index + 1]) + Int(grey[index - width]) + Int(grey[index + width])
                result[index] = UInt8(clamping: around - 4 * Int(grey[index]))
            }
        }
        return result
    }

    /// The level below which the fraction of the pixels lies.
    static func percentile(_ levels: [UInt8], _ fraction: Double) -> Int {
        var histogram = [Int](repeating: 0, count: 256)
        for level in levels { histogram[Int(level)] += 1 }
        let wanted = fraction * Double(levels.count)
        var seen = 0
        for (level, count) in histogram.enumerated() {
            seen += count
            if Double(seen) >= wanted { return level }
        }
        return 255
    }
}

/// One photographed page on the review screen: the photo as taken (JPEG), how far the player turned it, and how it looks.
struct PhotoPage: Identifiable, Equatable, Sendable {
    let id: UUID
    var photo: Data
    /// Quarter turns clockwise, 0–3, applied when the pages become the PDF.
    var turns = 0
    /// Measured once the photo is in; nil while it is being measured.
    var look: PageLook?

    init(id: UUID = UUID(), photo: Data, turns: Int = 0, look: PageLook? = nil) {
        self.id = id
        self.photo = photo
        self.turns = turns
        self.look = look
    }
}

/// The pages on the review screen, in the order they will be in the PDF: moved, turned, taken out or photographed again.
struct PhotoPageList: Equatable, Sendable {
    var pages: [PhotoPage] = []

    func number(of id: UUID) -> Int? { pages.firstIndex { $0.id == id }.map { $0 + 1 } }

    /// The page moved to stand where `target` stands now: before it when it comes from further on, after it otherwise.
    mutating func move(_ id: UUID, to target: UUID) {
        guard id != target, let from = pages.firstIndex(where: { $0.id == id }), let to = pages.firstIndex(where: { $0.id == target }) else { return }
        let page = pages.remove(at: from)
        pages.insert(page, at: to)
    }

    /// One place earlier (-1) or later (+1), for VoiceOver and Switch Control.
    mutating func step(_ id: UUID, by offset: Int) {
        guard let from = pages.firstIndex(where: { $0.id == id }) else { return }
        let to = from + offset
        guard pages.indices.contains(to) else { return }
        pages.swapAt(from, to)
    }

    /// A quarter turn clockwise.
    mutating func turn(_ id: UUID) {
        guard let index = pages.firstIndex(where: { $0.id == id }) else { return }
        pages[index].turns = (pages[index].turns + 1) % 4
    }

    mutating func remove(_ id: UUID) { pages.removeAll { $0.id == id } }

    /// The page photographed again: the new photo in its place, upright, to be measured again.
    mutating func replace(_ id: UUID, with photo: Data) {
        guard let index = pages.firstIndex(where: { $0.id == id }) else { return }
        pages[index] = PhotoPage(id: id, photo: photo)
    }

    mutating func measured(_ id: UUID, _ look: PageLook) {
        guard let index = pages.firstIndex(where: { $0.id == id }) else { return }
        pages[index].look = look
    }

    /// What to photograph again before sending, page by page.
    var warnings: [PageWarning] {
        pages.enumerated().compactMap { index, page in PageWarning(page: index + 1, look: page.look) }
    }
}

/// A page to photograph again before it is sent: «Страница 4 нечёткая, переснимите».
struct PageWarning: Equatable, Sendable {
    var page: Int
    var dark: Bool
    var blurry: Bool

    init?(page: Int, look: PageLook?) {
        guard let look, look.isDark || look.isBlurry else { return nil }
        self.page = page
        dark = look.isDark
        blurry = look.isBlurry
    }

    func text(_ text: UIText) -> String {
        let key = dark && blurry ? "photos.warnDarkBlurry" : dark ? "photos.warnDark" : "photos.warnBlurry"
        return text.format(key, String(page))
    }
}
