// Corner for macOS - the menu-bar icon (a template image: the hot-corner picture, a screen with a mark in its chosen
// corner, drawn black so macOS can colour it for the menu bar) and the app icon (the sunburst logo, drawn once by
// package.sh with `Corner --icon <png>`).
//
// On Windows both are pictures, not paths: Logo.cs and HotIcon.cs hold PNGs. The app icon here is that same logo
// picture (Resources/logo.png). The menu-bar icon is the hot-corner picture drawn again as plain shapes, with the places
// and sizes measured from the dark picture (design/hot-corner-sunburst/hc-dark.png), because a template image keeps
// only the alpha of a picture and the glass picture is one solid slab.
import AppKit
import CornerCore

enum Icon {
    // The hot-corner picture, measured from hc-dark.png. The screen is 1260 x 929 px there; every number below is a
    // share of the screen's width (x, sizes) or of its height (y, from the top).
    private static let screenAspect: CGFloat = 929.0 / 1260.0 // height / width
    private static let screenRadius: CGFloat = 0.19           // corner radius / width
    private static let chosenSize: CGFloat = 0.18             // the chosen corner's mark (top right), side / width
    private static let chosenX: CGFloat = 0.805               // its centre
    private static let chosenY: CGFloat = 0.240
    private static let dotSize: CGFloat = 0.097               // the other three marks, side / width
    private static let dotLeft: CGFloat = 0.181               // their centres
    private static let dotRight: CGFloat = 0.819
    private static let dotTop: CGFloat = 0.241
    private static let dotBottom: CGFloat = 0.747
    private static let markRadius: CGFloat = 0.30             // a mark's corner radius / its side
    private static let dotCut: CGFloat = 0.6                  // how much of the ink a small mark takes away (the chosen one takes all)

    static func menuBar() -> NSImage {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: false) { _ in
            guard let cg = NSGraphicsContext.current?.cgContext else { return false }
            // The screen, 16.5 pt wide, in the middle of the 18 pt square.
            let w: CGFloat = 16.5
            let h: CGFloat = w * Icon.screenAspect
            let x0: CGFloat = (18 - w) / 2
            let top: CGFloat = 18 - (18 - h) / 2 // the y of the screen's top edge (y points up here)
            let screen = CGRect(x: x0, y: top - h, width: w, height: h)
            let r: CGFloat = w * Icon.screenRadius

            // (A layer of its own, so the cutting takes ink from the screen only and never from what lies behind the icon.)
            cg.saveGState()
            cg.beginTransparencyLayer(auxiliaryInfo: nil)
            cg.setFillColor(CGColor(gray: 0, alpha: 1))
            cg.addPath(CGPath(roundedRect: screen, cornerWidth: r, cornerHeight: r, transform: nil))
            cg.fillPath()

            // The marks are cut out of the screen: the chosen one right through, the other three part of the way.
            cg.setBlendMode(.destinationOut)
            let small: CGFloat = w * Icon.dotSize
            let dots: [(CGFloat, CGFloat)] = [(Icon.dotLeft, Icon.dotTop), (Icon.dotLeft, Icon.dotBottom), (Icon.dotRight, Icon.dotBottom)]
            cg.setFillColor(CGColor(gray: 0, alpha: Icon.dotCut))
            for d in dots {
                cg.addPath(Icon.mark(centreX: x0 + w * d.0, centreY: top - h * d.1, side: small))
                cg.fillPath()
            }
            cg.setFillColor(CGColor(gray: 0, alpha: 1))
            cg.addPath(Icon.mark(centreX: x0 + w * Icon.chosenX, centreY: top - h * Icon.chosenY, side: w * Icon.chosenSize))
            cg.fillPath()
            cg.endTransparencyLayer()
            cg.restoreGState()
            return true
        }
        img.isTemplate = true
        return img
    }

    /// One mark of the hot-corner picture: a rounded square about a centre.
    private static func mark(centreX: CGFloat, centreY: CGFloat, side: CGFloat) -> CGPath {
        let rect = CGRect(x: centreX - side / 2, y: centreY - side / 2, width: side, height: side)
        let r: CGFloat = side * Icon.markRadius
        return CGPath(roundedRect: rect, cornerWidth: r, cornerHeight: r, transform: nil)
    }

    /// The app icon at 1024 x 1024: the sunburst logo (Resources/logo.png) in the 824 x 824 tile area, clear outside it.
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
        if let url = Resources.url("logo", "png"), let logo = NSImage(contentsOf: url) {
            ctx.imageInterpolation = .high
            logo.draw(in: NSRect(x: 100, y: 100, width: 824, height: 824), from: NSRect.zero, operation: .sourceOver, fraction: 1)
        }
        ctx.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        if let data = rep.representation(using: .png, properties: [:]) {
            try? data.write(to: URL(fileURLWithPath: path))
        }
    }
}
