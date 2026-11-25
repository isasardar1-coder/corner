// Corner for macOS - how light what is behind the glass is, as far as a Mac app may know without the
// screen-recording permission (which Corner does not ask for). Two things are open to every app: the picture that is
// the desktop's wallpaper, and where windows stand (not what is in them). So where the desktop shows, the wallpaper
// is measured; where a window is in the way, it is taken to be as light or dark as macOS is set to.
import AppKit
import CornerCore
import ImageIO

enum Behind {
    /// The wallpaper, made very small: its lightness, 0 to 1, row by row from the top.
    struct Small {
        let w: Int
        let h: Int
        let light: [Double]
    }

    private static var path: String = ""
    private static var changed: Date? = nil
    private static var kept: Small? = nil

    /// Folders macOS asks the user about before an app may read in them, and other disks: left alone.
    static func mayRead(_ p: String) -> Bool {
        let home = NSHomeDirectory()
        if p.contains(".photoslibrary") { return false }
        for ok in ["/System/Library/", "/Library/", home + "/Library/Application Support/com.apple", home + "/Pictures/"] {
            if p.hasPrefix(ok) { return true }
        }
        return false
    }

    /// Nil when there is no one picture to go by: a wallpaper that changes with the time of day, a video, a folder.
    static func wallpaper(_ screen: NSScreen) -> Small? {
        guard let u = NSWorkspace.shared.desktopImageURL(for: screen), u.isFileURL, mayRead(u.path) else { return nil }
        let at: Date? = (try? FileManager.default.attributesOfItem(atPath: u.path))?[.modificationDate] as? Date
        if u.path == path && at == changed { return kept }
        path = u.path
        changed = at
        kept = load(u)
        return kept
    }

    static func load(_ u: URL) -> Small? {
        guard let src = CGImageSourceCreateWithURL(u as CFURL, nil), CGImageSourceGetCount(src) == 1 else { return nil }
        let opts: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: 96]
        guard let img = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
        return small(img)
    }

    static func small(_ img: CGImage) -> Small? {
        let w: Int = img.width
        let h: Int = img.height
        if w < 2 || h < 2 { return nil }
        var px = [UInt8](repeating: 0, count: w * h * 4)
        guard let space = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        let drawn: Bool = px.withUnsafeMutableBytes { (buf: UnsafeMutableRawBufferPointer) -> Bool in
            guard let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                      space: space, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return false }
            ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
            return true
        }
        if !drawn { return nil }
        var light = [Double](repeating: 0, count: w * h)
        for i in 0..<(w * h) {
            let r: Double = Double(px[i * 4])
            let g: Double = Double(px[i * 4 + 1])
            let b: Double = Double(px[i * 4 + 2])
            light[i] = (0.299 * r + 0.587 * g + 0.114 * b) / 255
        }
        return Small(w: w, h: h, light: light)
    }

    /// The wallpaper's lightness at a place on a screen, `x` and `y` from its top left: it fills the screen, centred.
    static func at(_ s: Small, _ x: CGFloat, _ y: CGFloat, _ size: CGSize) -> Double {
        let k: CGFloat = max(size.width / CGFloat(s.w), size.height / CGFloat(s.h))
        let px: CGFloat = (x - size.width / 2) / k + CGFloat(s.w) / 2
        let py: CGFloat = (y - size.height / 2) / k + CGFloat(s.h) / 2
        let i: Int = max(0, min(s.w - 1, Int(px)))
        let j: Int = max(0, min(s.h - 1, Int(py)))
        return s.light[j * s.w + i]
    }

    /// Where ordinary windows of other apps stand, measured from the main screen's top left.
    static func windows() -> [CGRect] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return [] }
        let me: Int = Int(ProcessInfo.processInfo.processIdentifier)
        var out: [CGRect] = []
        for w in list {
            if (w[kCGWindowLayer as String] as? Int) != 0 || (w[kCGWindowOwnerPID as String] as? Int) == me { continue }
            if let a = w[kCGWindowAlpha as String] as? Double, a < 0.5 { continue }
            guard let d = w[kCGWindowBounds as String] as? NSDictionary, let r = CGRect(dictionaryRepresentation: d as CFDictionary) else { continue }
            out.append(r)
        }
        return out
    }

    /// How light it is behind a rectangle of the screen (the screen's own coordinates), 0 to 1. -1: not known.
    static func light(_ r: CGRect, systemDark: Bool) -> Double {
        let mid = CGPoint(x: r.midX, y: r.midY)
        guard let first = NSScreen.screens.first else { return -1 }
        let screen: NSScreen = NSScreen.screens.first(where: { $0.frame.contains(mid) }) ?? first
        guard let paper = wallpaper(screen) else { return -1 }
        return mix(r, paper, screen.frame, first.frame.maxY, windows(), systemDark)
    }

    /// 12 by 12 places in the rectangle: the wallpaper's lightness, or the window's that is in the way.
    static func mix(_ r: CGRect, _ paper: Small, _ screen: CGRect, _ mainTop: CGFloat, _ wins: [CGRect], _ systemDark: Bool) -> Double {
        let n: Int = 12
        var sum: Double = 0
        for j in 0..<n {
            for i in 0..<n {
                let x: CGFloat = r.minX + r.width * (CGFloat(i) + 0.5) / CGFloat(n)
                let y: CGFloat = r.minY + r.height * (CGFloat(j) + 0.5) / CGFloat(n)
                let p = CGPoint(x: x, y: mainTop - y)
                if wins.contains(where: { $0.contains(p) }) {
                    sum += systemDark ? 0.15 : 0.92
                } else {
                    sum += at(paper, x - screen.minX, screen.maxY - y, screen.size)
                }
            }
        }
        return sum / Double(n * n)
    }

    /// Test renders write this to the log: the sum on made-up wallpapers and windows, and what is really here.
    static func report() -> String {
        let scr = CGRect(x: 0, y: 0, width: 1000, height: 600)
        let half = Small(w: 2, h: 2, light: [0.1, 0.9, 0.1, 0.9]) // dark on the left, light on the right
        let left = CGRect(x: 20, y: 300, width: 300, height: 200)
        let right = CGRect(x: 680, y: 300, width: 300, height: 200)
        let over = CGRect(x: 0, y: 0, width: 400, height: 600)
        func f(_ v: Double) -> String { return String(format: "%.2f", v) }
        let a = f(mix(left, half, scr, 600, [], false))
        let b = f(mix(right, half, scr, 600, [], false))
        let c = f(mix(left, half, scr, 600, [over], false))
        let d = f(mix(right, half, scr, 600, [CGRect(x: 600, y: 100, width: 400, height: 200)], true))
        let here = f(light(CGRect(x: 100, y: 100, width: 300, height: 300), systemDark: GlassLook.systemDark))
        return "behind: left " + a + ", right " + b + ", left under a light window " + c + ", right under a dark window " + d
            + ", here " + here + " from " + (NSScreen.screens.first.flatMap { NSWorkspace.shared.desktopImageURL(for: $0)?.path } ?? "none")
            + (kept.map { " " + String($0.w) + "x" + String($0.h) + " max " + f($0.light.max() ?? -1) } ?? " unread") + ", windows " + String(windows().count)
    }
}
