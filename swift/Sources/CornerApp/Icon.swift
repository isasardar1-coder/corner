// Corner for macOS - the menu-bar icon (a template image: a rounded square with a tick, drawn black so macOS can
// colour it for the menu bar) and the app icon (drawn once by package.sh with `Corner --icon <png>`).
import AppKit
import CornerCore

enum Icon {
    static func menuBar() -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            NSColor.black.setStroke()
            let box = NSBezierPath(roundedRect: NSRect(x: 2.5, y: 2.5, width: 13, height: 13), xRadius: 4, yRadius: 4)
            box.lineWidth = 1.6
            box.stroke()
            let tick = NSBezierPath()
            tick.move(to: NSPoint(x: 5.6, y: 9.2))
            tick.line(to: NSPoint(x: 8, y: 6.6))
            tick.line(to: NSPoint(x: 12.6, y: 11.6))
            tick.lineWidth = 1.7
            tick.lineCapStyle = .round
            tick.lineJoinStyle = .round
            tick.stroke()
            return true
        }
        img.isTemplate = true
        return img
    }

    /// The app icon at 1024 x 1024: a sage tile with a lime disc and a dark tick.
    static func writeAppIcon(_ path: String) {
        let size = 1024
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let ctx = NSGraphicsContext(bitmapImageRep: rep) else { return }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ctx
        let s = CGFloat(size)
        NSColor.clear.setFill()
        NSRect(x: 0, y: 0, width: s, height: s).fill()
        let tile = NSBezierPath(roundedRect: NSRect(x: 100, y: 100, width: 824, height: 824), xRadius: 185, yRadius: 185)
        NSColor(srgbRed: 0xE8 / 255.0, green: 0xEC / 255.0, blue: 0xDB / 255.0, alpha: 1).setFill()
        tile.fill()
        NSColor(srgbRed: 0xD3 / 255.0, green: 0xD9 / 255.0, blue: 0xC2 / 255.0, alpha: 1).setStroke()
        tile.lineWidth = 6
        tile.stroke()
        let disc = NSBezierPath(ovalIn: NSRect(x: 262, y: 262, width: 500, height: 500))
        NSColor(srgbRed: 0xB4 / 255.0, green: 0xC1 / 255.0, blue: 0x2A / 255.0, alpha: 1).setFill()
        disc.fill()
        let tick = NSBezierPath()
        tick.move(to: NSPoint(x: 395, y: 520))
        tick.line(to: NSPoint(x: 480, y: 432))
        tick.line(to: NSPoint(x: 640, y: 608))
        tick.lineWidth = 46
        tick.lineCapStyle = .round
        tick.lineJoinStyle = .round
        NSColor(srgbRed: 0x16 / 255.0, green: 0x28 / 255.0, blue: 0x1D / 255.0, alpha: 1).setStroke()
        tick.stroke()
        NSGraphicsContext.restoreGraphicsState()
        if let data = rep.representation(using: .png, properties: [:]) {
            try? data.write(to: URL(fileURLWithPath: path))
        }
    }
}
