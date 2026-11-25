// Corner for macOS - the Glass skin as Liquid Glass: clear glass that shows what is behind it softly blurred, with a
// bright double edge; light over light things and dark over dark ones. Port of Glass.cs. Its colours are in T
// (Theme.swift). Here: what is behind the glass (the Mac's own live blur behind the window, GlassBack; in a test
// render a drawn wallpaper, Backdrop), the sheet a panel is made of (GlassSheet), the rim, glow and gloss of the
// boxes on it (Glass.box, drawn by Plate in Material.swift), and whether the glass is light or dark (GlassLook).
// Not ported: the picture bent inwards at the rim. Windows takes a picture of the screen when the panel opens; here
// the blur is the system's and stays live, and the screen is never read.
import AppKit
import SwiftUI

// ---------------------------------------------------------------- light or dark

/// Liquid Glass is light over light things and dark over dark ones. Windows measures the picture it took of the
/// screen. A Mac app cannot see what is behind its window without the screen-recording permission, which Corner
/// does not ask for: so the wallpaper is measured where the desktop shows, and a window in the way counts as light
/// or dark as macOS is set to (Behind.swift); with no wallpaper to go by, all of it does. It is settled each time
/// a panel, the notice or the welcome window opens, and when Glass is chosen in Settings.
/// A test render has a drawn wallpaper behind it, and that is measured as on Windows: dark glass below 30%
/// lightness, back to light above 38%. CORNER_GLASS=dark or light (test renders only) settles it by hand.
enum GlassLook {
    /// A test render: CORNER_DATA is set and the app was started with --demo.
    static let test: Bool = ProcessInfo.processInfo.environment["CORNER_DATA"] != nil && CommandLine.arguments.contains("--demo")

    static let forced: Bool? = {
        if !test { return nil }
        let v: String = ProcessInfo.processInfo.environment["CORNER_GLASS"] ?? ""
        if v == "dark" { return true }
        if v == "light" { return false }
        return nil
    }()

    static var systemDark: Bool {
        guard let app = NSApp else { return false }
        let found: NSAppearance.Name? = app.effectiveAppearance.bestMatch(from: [NSAppearance.Name.aqua, NSAppearance.Name.darkAqua])
        return found == NSAppearance.Name.darkAqua
    }

    /// Settles light or dark for a panel that is opening over `screenRect` (the screen's own coordinates, as a
    /// window's frame is). True when the glass changed: everything that is showing has to be drawn again.
    static func adapt(_ screenRect: CGRect) -> Bool {
        if !T.glass { return false }
        var dark: Bool = T.dark
        if let f = forced {
            dark = f
        } else if Backdrop.kind != nil {
            let l: Double = Backdrop.light(screenRect)
            if l >= 0 { dark = T.dark ? l < 0.38 : l < 0.30 } // (between the two it stays as it was)
        } else {
            let sys: Bool = systemDark
            let l: Double = Behind.light(screenRect, systemDark: sys) // (Behind.swift)
            if l >= 0 { dark = T.dark ? l < 0.38 : l < 0.30 } else { dark = sys }
        }
        if dark == T.dark { return false }
        T.use(1, darkGlass: dark)
        return true
    }
}

extension PanelController {
    /// The same for the panel, whose window is going to `frame`. The corner panel opens small and then grows, so it
    /// is judged on the part of the screen it will grow over as well.
    func adaptGlass(_ frame: NSRect) {
        if !T.glass { return }
        var r: NSRect = frame.insetBy(dx: shadowM, dy: shadowM)
        let tall: CGFloat = 380 * CGFloat(model.currentScale)
        if !model.full && r.height < tall {
            if model.onTop { r.origin.y -= tall - r.height }
            r.size.height = tall
        }
        if GlassLook.adapt(r) {
            model.changed()
            hosting.layoutSubtreeIfNeeded()
        }
    }
}

// ---------------------------------------------------------------- test renders: a wallpaper behind the glass

/// A test render cannot show the live blur (a bitmap of a window does not hold it), and has no desktop behind it.
/// So in a test render, and only there, a wallpaper is drawn behind the panel: "light" (big waves of strong colour),
/// "dark", or "busy" (windows full of text over the light one), chosen by CORNER_BACKDROP as on Windows. The glass
/// then shows that picture as Windows shows the screen: an eighth of the size, blurred, more colourful, the dark
/// parts lifted under light glass and the bright parts dimmed under dark glass.
enum Backdrop {
    /// Nil outside test renders.
    static let kind: String? = {
        if !GlassLook.test { return nil }
        let b: String = ProcessInfo.processInfo.environment["CORNER_BACKDROP"] ?? ""
        return b == "dark" || b == "busy" ? b : "light"
    }()

    /// The wallpaper is behind the panel in this render. (The renders of an animation paint sand behind their frames.)
    static var on: Bool { return kind != nil && !GenieStage.clearBackdrop }

    static let k: Int = 8                 // the picture is 1/k of the screen
    static let blur: Int = 2              // box radius in the small picture's pixels, three passes: about 20 on screen
    static let saturation: Double = 1.35
    static let liftA: Double = 0.42       // light glass: how far black is lifted towards white (mid-tones much less)
    static let dimA: Double = 0.62        // dark glass: how far white is dimmed (mid-tones less)

    static var raw: [UInt8] = []          // the blurred picture, before it is toned for light or dark glass
    static var rw: Int = 0
    static var rh: Int = 0
    static var madeFor: CGSize = .zero
    static var toned: [Bool: CGImage] = [:]

    static var screen: CGRect {
        return NSScreen.screens.first?.frame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
    }

    static var space: CGColorSpace { return CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB() }

    static func make() {
        guard let kind = kind else { return }
        let s: CGSize = screen.size
        if s == madeFor && !raw.isEmpty { return }
        madeFor = s
        toned = [:]
        raw = []
        let w: Int = max(8, Int(s.width) / k)
        let h: Int = max(8, Int(s.height) / k)
        var px = [UInt8](repeating: 0, count: w * h * 4)
        let sp: CGColorSpace = space
        let drawn: Bool = px.withUnsafeMutableBytes { (buf: UnsafeMutableRawBufferPointer) -> Bool in
            guard let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                      space: sp, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return false }
            // (drawn from the top left, as on Windows)
            ctx.translateBy(x: 0, y: CGFloat(h))
            ctx.scaleBy(x: CGFloat(w) / s.width, y: -CGFloat(h) / s.height)
            paint(ctx, kind, s.width, s.height)
            return true
        }
        if !drawn { return }
        boxBlur(&px, w, h, blur)
        raw = px
        rw = w
        rh = h
    }

    static func clamp(_ v: Double) -> Double { return v < 0 ? 0 : (v > 255 ? 255 : v) }

    /// The picture that is shown, for light or for dark glass.
    static func image(_ dark: Bool) -> CGImage? {
        make()
        if raw.isEmpty { return nil }
        if let found = toned[dark] { return found }
        var p: [UInt8] = raw
        var i: Int = 0
        while i + 3 < p.count {
            var r: Double = Double(p[i])
            var g: Double = Double(p[i + 1])
            var b: Double = Double(p[i + 2])
            let grey: Double = 0.299 * r + 0.587 * g + 0.114 * b
            r = clamp(grey + (r - grey) * saturation)
            g = clamp(grey + (g - grey) * saturation)
            b = clamp(grey + (b - grey) * saturation)
            let l: Double = grey / 255
            if dark {
                let f: Double = 1 - dimA * l
                r *= f
                g *= f
                b *= f
            } else {
                let a: Double = liftA * (1 - l) * (1 - l)
                r += (255 - r) * a
                g += (255 - g) * a
                b += (255 - b) * a
            }
            p[i] = UInt8(clamp(r))
            p[i + 1] = UInt8(clamp(g))
            p[i + 2] = UInt8(clamp(b))
            p[i + 3] = 255
            i += 4
        }
        guard let provider = CGDataProvider(data: Data(p) as CFData) else { return nil }
        let info = CGBitmapInfo(rawValue: CGImageAlphaInfo.noneSkipLast.rawValue)
        guard let img = CGImage(width: rw, height: rh, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: rw * 4, space: space,
                                bitmapInfo: info, provider: provider, decode: nil, shouldInterpolate: true,
                                intent: .defaultIntent) else { return nil }
        toned[dark] = img
        return img
    }

    /// How light the wallpaper is inside a rectangle of the screen (the screen's own coordinates), 0 to 1.
    /// -1 when there is no picture.
    static func light(_ r: CGRect) -> Double {
        make()
        if raw.isEmpty { return -1 }
        let f: CGRect = screen
        let kk: Double = Double(k)
        let left: Double = Double(r.minX - f.minX) / kk
        let right: Double = Double(r.maxX - f.minX) / kk
        let top: Double = Double(f.maxY - r.maxY) / kk
        let bottom: Double = Double(f.maxY - r.minY) / kk
        let x0: Int = max(0, min(rw - 1, Int(max(-1, min(100000, left)))))
        let x1: Int = max(x0 + 1, min(rw, Int(max(-1, min(100000, right.rounded(.up))))))
        let y0: Int = max(0, min(rh - 1, Int(max(-1, min(100000, top)))))
        let y1: Int = max(y0 + 1, min(rh, Int(max(-1, min(100000, bottom.rounded(.up))))))
        var sum: Double = 0
        for y in y0..<y1 {
            for x in x0..<x1 {
                let i: Int = (y * rw + x) * 4
                sum += 0.299 * Double(raw[i]) + 0.587 * Double(raw[i + 1]) + 0.114 * Double(raw[i + 2])
            }
        }
        return sum / Double((x1 - x0) * (y1 - y0)) / 255
    }

    static func boxBlur(_ p: inout [UInt8], _ w: Int, _ h: Int, _ r: Int) {
        if r < 1 { return }
        var t = [UInt8](repeating: 0, count: p.count)
        for _ in 0..<3 {
            pass(p, &t, w, h, r, true)
            pass(t, &p, w, h, r, false)
        }
    }

    static func pass(_ src: [UInt8], _ dst: inout [UInt8], _ w: Int, _ h: Int, _ r: Int, _ across: Bool) {
        let n: Int = across ? w : h
        let lines: Int = across ? h : w
        let step: Int = across ? 4 : w * 4
        let d: Int = 2 * r + 1
        for j in 0..<lines {
            let line: Int = across ? j * w * 4 : j * 4
            for c in 0..<3 {
                var sum: Int = 0
                for q in -r...r {
                    let at: Int = min(n - 1, max(0, q))
                    sum += Int(src[line + at * step + c])
                }
                for i in 0..<n {
                    dst[line + i * step + c] = UInt8(max(0, min(255, sum / d)))
                    let add: Int = min(n - 1, i + r + 1)
                    let drop: Int = max(0, i - r)
                    sum += Int(src[line + add * step + c]) - Int(src[line + drop * step + c])
                }
            }
        }
    }

    // ---- the wallpapers, drawn from the top left in the screen's own units

    static func rgb(_ hex: String, _ a: CGFloat = 1) -> CGColor {
        var s: String = hex
        if s.hasPrefix("#") { s.removeFirst() }
        let v: UInt64 = UInt64(s, radix: 16) ?? 0
        let r: CGFloat = CGFloat((v >> 16) & 0xFF) / 255
        let g: CGFloat = CGFloat((v >> 8) & 0xFF) / 255
        let b: CGFloat = CGFloat(v & 0xFF) / 255
        return CGColor(colorSpace: space, components: [r, g, b, a]) ?? CGColor(red: r, green: g, blue: b, alpha: a)
    }

    /// A soft blob of colour: full at its centre, gone at its edge.
    static func blob(_ ctx: CGContext, _ hex: String, _ cx: CGFloat, _ cy: CGFloat, _ rx: CGFloat, _ ry: CGFloat) {
        let colors: [CGColor] = [rgb(hex, 1), rgb(hex, 0)]
        let places: [CGFloat] = [0, 1]
        guard let g = CGGradient(colorsSpace: space, colors: colors as CFArray, locations: places) else { return }
        ctx.saveGState()
        ctx.translateBy(x: cx, y: cy)
        ctx.scaleBy(x: rx, y: ry)
        ctx.drawRadialGradient(g, startCenter: CGPoint.zero, startRadius: 0, endCenter: CGPoint.zero, endRadius: 1, options: [])
        ctx.restoreGState()
    }

    /// Two colours across the whole screen, at an angle (as WPF lays a gradient given in degrees).
    static func wash(_ ctx: CGContext, _ a: String, _ b: String, _ degrees: Double, _ sw: CGFloat, _ sh: CGFloat) {
        let colors: [CGColor] = [rgb(a), rgb(b)]
        let places: [CGFloat] = [0, 1]
        guard let g = CGGradient(colorsSpace: space, colors: colors as CFArray, locations: places) else { return }
        let rad: Double = degrees * Double.pi / 180
        let end = CGPoint(x: CGFloat(cos(rad)) * sw, y: CGFloat(sin(rad)) * sh)
        // (not cut to the screen: a panel's window reaches a little past the screen's edge)
        ctx.drawLinearGradient(g, start: CGPoint.zero, end: end, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    }

    static func box(_ ctx: CGContext, _ hex: String, _ r: CGRect, _ radius: CGFloat = 0) {
        ctx.setFillColor(rgb(hex))
        if radius > 0 {
            ctx.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
            ctx.fillPath()
        } else {
            ctx.fill(r)
        }
    }

    static func paint(_ ctx: CGContext, _ kind: String, _ sw: CGFloat, _ sh: CGFloat) {
        if kind == "dark" {
            wash(ctx, "#05081C", "#150A33", 40, sw, sh)
            blob(ctx, "#3A1FA8", sw * 0.86, sh * 0.2, sw * 0.2, sw * 0.2)
            blob(ctx, "#0B4FB8", sw * 0.64, sh * 0.5, sw * 0.26, sw * 0.2)
            blob(ctx, "#7A2BD6", sw * 0.97, sh * 0.62, sw * 0.14, sw * 0.16)
            blob(ctx, "#0A2F5C", sw * 0.3, sh * 0.8, sw * 0.3, sw * 0.2)
        } else {
            // big waves of strong colour, like a tablet's wallpaper
            wash(ctx, "#1767E0", "#6C3FE6", 35, sw, sh)
            blob(ctx, "#19C3F2", sw * 0.74, sh * 0.2, sw * 0.2, sw * 0.17)
            blob(ctx, "#2FE0C2", sw * 1.0, sh * 0.05, sw * 0.14, sw * 0.14)
            blob(ctx, "#E052D8", sw * 0.66, sh * 0.62, sw * 0.18, sw * 0.16)
            blob(ctx, "#FF8A3C", sw * 0.9, sh * 0.78, sw * 0.24, sw * 0.2)
            blob(ctx, "#FFC94D", sw * 1.0, sh * 0.5, sw * 0.1, sw * 0.12)
            blob(ctx, "#2A8BF5", sw * 0.4, sh * 0.35, sw * 0.26, sw * 0.2)
        }
        if kind != "busy" { return }
        // windows full of text, a dark one among them: the hardest thing to read over
        let ww: CGFloat = sw * 0.21
        let wh: CGFloat = sh * 0.34
        for i in 0..<12 {
            let x: CGFloat = sw * 0.04 + CGFloat(i % 4) * sw * 0.245
            let y: CGFloat = sh * 0.03 + CGFloat(i / 4) * sh * 0.325
            let night: Bool = i % 3 == 1
            box(ctx, night ? "#15171C" : "#FAFAFA", CGRect(x: x, y: y, width: ww, height: wh), 10)
            for l in 0..<14 {
                let part: CGFloat = CGFloat((l * 37 + i * 11) % 10) / 10
                let lw: CGFloat = ww * (0.35 + 0.55 * part)
                let ly: CGFloat = y + 22 + CGFloat(l) * wh / 16
                box(ctx, night ? "#59D67A" : "#1B1F24", CGRect(x: x + 18, y: ly, width: lw, height: 5))
            }
            box(ctx, i % 2 == 0 ? "#E5484D" : "#3E63DD", CGRect(x: x + ww * 0.62, y: y + wh * 0.55, width: ww * 0.3, height: wh * 0.3))
        }
    }
}

// ---------------------------------------------------------------- what is behind the glass

/// The live sheets of glass. While a picture of a panel is taken (the animations, Genie.swift) each stands in for
/// its live blur with a flat fill: a bitmap of a window does not hold the blur.
enum GlassLive {
    static let views = NSHashTable<GlassBackView>.weakObjects()

    static func hold(_ on: Bool) {
        for v in views.allObjects {
            v.flat = on
            v.effect?.isHidden = on
            v.needsDisplay = true
        }
    }
}

/// What is behind a sheet of glass, cut to the sheet's shape. On a Mac: the system's own blur of whatever is behind
/// the window (NSVisualEffectView), light or dark with the glass; it stays live. In a test render: the drawn
/// wallpaper as the glass shows it (Backdrop), placed where the window is on the screen. `wall` (test renders):
/// the wallpaper itself, sharp, as the desktop behind the panel.
final class GlassBackView: NSView {
    var radius: CGFloat = 0
    var dark: Bool = false
    var wall: Bool = false
    var flat: Bool = false
    var effect: NSVisualEffectView? = nil
    private var maskFor: CGSize = .zero
    private var maskRadius: CGFloat = -1
    private var watching: [NSObjectProtocol] = []

    override var isFlipped: Bool { return true }

    deinit {
        for t in watching { NotificationCenter.default.removeObserver(t) }
    }

    func set(_ radius: CGFloat, _ dark: Bool, _ wall: Bool) {
        self.radius = radius
        self.dark = dark
        self.wall = wall
        if Backdrop.kind == nil && !wall {
            if effect == nil {
                let e = NSVisualEffectView(frame: bounds)
                e.material = .hudWindow
                e.blendingMode = .behindWindow
                e.state = .active
                e.autoresizingMask = [.width, .height]
                addSubview(e)
                effect = e
                GlassLive.views.add(self)
            }
            effect?.appearance = NSAppearance(named: dark ? NSAppearance.Name.vibrantDark : NSAppearance.Name.vibrantLight)
            remask()
        }
        needsDisplay = true
    }

    /// The blur is cut to the sheet's shape by a mask of its own.
    func remask() {
        guard let e = effect else { return }
        let s: CGSize = bounds.size
        if s.width < 1 || s.height < 1 { return }
        if s == maskFor && radius == maskRadius { return }
        maskFor = s
        maskRadius = radius
        let r: CGFloat = radius
        e.maskImage = NSImage(size: s, flipped: false) { rect in
            guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
            ctx.addPath(RoundedRectangle(cornerRadius: r, style: .continuous).path(in: rect).cgPath)
            ctx.setFillColor(CGColor(gray: 0, alpha: 1))
            ctx.fillPath()
            return true
        }
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        effect?.frame = bounds
        remask()
        needsDisplay = true
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        for t in watching { NotificationCenter.default.removeObserver(t) }
        watching = []
        guard let w = window, Backdrop.kind != nil else { return }
        // a test render: the picture is drawn for where the window is
        for n in [NSWindow.didMoveNotification, NSWindow.didResizeNotification] {
            let t = NotificationCenter.default.addObserver(forName: n, object: w, queue: OperationQueue.main) { [weak self] _ in
                self?.needsDisplay = true
            }
            watching.append(t)
        }
        needsDisplay = true
    }

    /// Where this view's top left corner is on the screen, measured from the screen's top left.
    func spot() -> CGPoint? {
        guard let w = window else { return nil }
        let r: NSRect = w.convertToScreen(convert(bounds, to: nil))
        let f: CGRect = Backdrop.screen
        return CGPoint(x: r.minX - f.minX, y: f.maxY - r.maxY)
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let b: NSRect = bounds
        if b.width < 1 || b.height < 1 { return }
        let f: CGRect = Backdrop.screen
        if wall {
            guard let kind = Backdrop.kind, let at = spot() else { return }
            ctx.saveGState()
            ctx.clip(to: b)
            ctx.translateBy(x: -at.x, y: -at.y)
            Backdrop.paint(ctx, kind, f.width, f.height)
            ctx.restoreGState()
            return
        }
        let pictured: Bool = Backdrop.on
        if !pictured && !flat && Backdrop.kind == nil { return } // on a Mac the live blur is all there is to see
        ctx.saveGState()
        ctx.addPath(RoundedRectangle(cornerRadius: radius, style: .continuous).path(in: b).cgPath)
        ctx.clip()
        if pictured, let at = spot(), let img = Backdrop.image(dark) {
            ctx.interpolationQuality = .high
            ctx.translateBy(x: -at.x, y: f.height - at.y)
            ctx.scaleBy(x: 1, y: -1)
            ctx.draw(img, in: CGRect(x: 0, y: 0, width: f.width, height: f.height))
        } else {
            // the flat stand-in: what Windows shows when the screen could not be read
            ctx.setFillColor(Backdrop.rgb(dark ? "#141A28" : "#DCE6F0"))
            ctx.fill(b)
        }
        ctx.restoreGState()
    }
}

struct GlassBack: NSViewRepresentable {
    let radius: CGFloat
    let dark: Bool
    var wall: Bool = false

    func makeNSView(context: Context) -> GlassBackView {
        let v = GlassBackView()
        v.set(radius, dark, wall)
        return v
    }

    func updateNSView(_ v: GlassBackView, context: Context) {
        v.set(radius, dark, wall)
    }
}

/// What is behind the panel in a test render: sand; for Glass the drawn wallpaper.
struct DemoBackdrop: View {
    var skin: Int = T.skin // (kept, so that a change of skin draws it again)

    var body: some View {
        if skin == 1 && Backdrop.on {
            GlassBack(radius: 0, dark: false, wall: true)
        } else {
            T.sandBackdrop
        }
    }
}

// ---------------------------------------------------------------- the glass itself

/// What the sheet and the boxes on it are drawn with: the values of T.Use in Corner.cs, light glass and dark glass.
enum Glass {
    static func white(_ a: Double) -> Color { return Color(.sRGB, white: 1, opacity: a) }

    static func three(_ a: String, _ b: String, _ c: String) -> Gradient {
        return Gradient(stops: [
            Gradient.Stop(color: Color(hex: a), location: 0),
            Gradient.Stop(color: Color(hex: b), location: 0.5),
            Gradient.Stop(color: Color(hex: c), location: 1),
        ])
    }

    static func fade(_ a: String) -> Gradient {
        return Gradient(stops: [
            Gradient.Stop(color: Color(hex: a), location: 0),
            Gradient.Stop(color: Color(hex: "#00FFFFFF"), location: 0.62),
            Gradient.Stop(color: Color(hex: "#00FFFFFF"), location: 1),
        ])
    }

    // a box on the glass: the hairline round it (bright along the top, faint down the sides, some again at the
    // bottom), and the gloss across the upper half of a pill
    static let boxRim: Gradient = three("#E0FFFFFF", "#47FFFFFF", "#94FFFFFF")
    static let boxRimDark: Gradient = three("#80FFFFFF", "#1FFFFFFF", "#3DFFFFFF")
    static let gloss: Gradient = fade("#30FFFFFF")
    static let glossDark: Gradient = fade("#17FFFFFF")
    // the sheet: its two hairlines, lit from the top left
    static let sheetRim: Gradient = three("#F2FFFFFF", "#61FFFFFF", "#C2FFFFFF")
    static let sheetRimDark: Gradient = three("#A6DCE6FF", "#2BFFFFFF", "#66DCE6FF")
    static let sheetRim2: Gradient = three("#A6FFFFFF", "#38FFFFFF", "#80FFFFFF")
    static let sheetRim2Dark: Gradient = three("#47FFFFFF", "#12FFFFFF", "#2BFFFFFF")

    /// The outline of a box, `inset` further in. `radius` nil: a pill.
    static func outline(_ rect: CGRect, _ radius: CGFloat?, _ inset: CGFloat) -> Path {
        let q: CGRect = rect.insetBy(dx: inset, dy: inset)
        let half: CGFloat = min(q.width, q.height) / 2
        if let r = radius {
            let rr: CGFloat = max(0, min(r - inset, half))
            return RoundedRectangle(cornerRadius: rr, style: .continuous).path(in: q)
        }
        return Path(roundedRect: q, cornerRadius: max(0, half), style: .circular)
    }

    /// The soft glow just inside a box's edge and the bright hairline round it.
    static func edge(_ ctx: GraphicsContext, _ rect: CGRect, _ radius: CGFloat?, _ dark: Bool) {
        if rect.width < 6 || rect.height < 6 { return }
        ctx.stroke(outline(rect, radius, 1.5), with: .color(white(dark ? 0.059 : 0.122)), lineWidth: 2)
        let top = CGPoint(x: rect.minX, y: rect.minY)
        let bottom = CGPoint(x: rect.minX, y: rect.maxY)
        ctx.stroke(outline(rect, radius, 0.5), with: .linearGradient(dark ? boxRimDark : boxRim, startPoint: top, endPoint: bottom), lineWidth: 1)
    }

    /// A box lying on the glass: its fill, a pill's gloss across its upper half, then its glow and rim.
    static func box(_ ctx: GraphicsContext, _ rect: CGRect, _ radius: CGFloat?, _ color: Color, _ dark: Bool) {
        if rect.width < 1 || rect.height < 1 { return }
        let shape: Path = outline(rect, radius, 0)
        ctx.fill(shape, with: .color(color))
        if rect.width < 6 || rect.height < 6 { return }
        if radius == nil {
            let top = CGPoint(x: rect.minX, y: rect.minY)
            let bottom = CGPoint(x: rect.minX, y: rect.maxY)
            ctx.fill(shape, with: .linearGradient(dark ? glossDark : gloss, startPoint: top, endPoint: bottom))
        }
        edge(ctx, rect, radius, dark)
    }

    /// The sheet a panel is made of, over what is behind it: a thin veil, a glow that fades in from the edge, and
    /// two bright hairlines lit from the top left.
    static func sheet(_ ctx: GraphicsContext, _ rect: CGRect, _ radius: CGFloat, _ dark: Bool) {
        if rect.width < 8 || rect.height < 8 { return }
        let r: CGFloat = min(radius, min(rect.width, rect.height) / 2)
        ctx.fill(outline(rect, r, 0), with: .color(white(dark ? 0.078 : 0.22)))
        let rings: Int = 7
        for i in 0..<rings {
            let fall: Double = pow(1 - Double(i) / Double(rings), 1.6)
            let a: Double = (dark ? 0.13 : 0.21) * fall
            ctx.stroke(outline(rect, r, CGFloat(i) + 0.5), with: .color(white(a)), lineWidth: 1.2)
        }
        let e2: (CGPoint, CGPoint) = Mat.ends("d", rect.insetBy(dx: 2.15, dy: 2.15))
        ctx.stroke(outline(rect, r, 2.6), with: .linearGradient(dark ? sheetRim2Dark : sheetRim2, startPoint: e2.0, endPoint: e2.1), lineWidth: 0.9)
        let e1: (CGPoint, CGPoint) = Mat.ends("d", rect)
        ctx.stroke(outline(rect, r, 0.5), with: .linearGradient(dark ? sheetRimDark : sheetRim, startPoint: e1.0, endPoint: e1.1), lineWidth: 1.1)
    }
}

/// A box on the glass (Plate, Material.swift).
struct GlassPlate: View {
    let tone: Tone
    let radius: CGFloat? // nil: a pill
    let dark: Bool

    var body: some View {
        let color: Color = tone.color
        let r: CGFloat? = radius
        let d: Bool = dark
        return Canvas { ctx, size in
            Glass.box(ctx, CGRect(x: 0, y: 0, width: size.width, height: size.height), r, color, d)
        }
        .allowsHitTesting(false)
    }
}

/// The glow and rim alone, over a box that fills itself (the plan's ticket).
struct GlassEdge: View {
    let radius: CGFloat
    let on: Bool
    let dark: Bool

    var body: some View {
        if on {
            Canvas { ctx, size in
                Glass.edge(ctx, CGRect(x: 0, y: 0, width: size.width, height: size.height), radius, dark)
            }
            .allowsHitTesting(false)
        }
    }
}

/// Everything outside a box with round corners, `room` wide: what a sheet's shadow is cut to, so that nothing of
/// it lies under the glass. (The hole is half a unit larger than the box, so none of the box that casts it shows.)
struct GlassHole: Shape {
    let radius: CGFloat
    let room: CGFloat

    func path(in rect: CGRect) -> Path {
        var p = Path(rect)
        p.addPath(RoundedRectangle(cornerRadius: radius + 0.5, style: .continuous).path(in: rect.insetBy(dx: room - 0.5, dy: room - 0.5)))
        return p
    }
}

/// The sheet of glass a panel, the notice and the welcome window are made of (PanelBackground, CardView.swift).
/// From the back: its shadow, what is behind it (GlassBack), then the veil, the glow and the two hairlines.
struct GlassSheet: View {
    let radius: CGFloat
    let dark: Bool

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        let room: CGFloat = 60
        let r: CGFloat = radius
        let d: Bool = dark
        return ZStack {
            shape.fill(Color.black)
                .shadow(color: T.shadow, radius: 16, x: 0, y: 10)
                .padding(room)
                .mask(GlassHole(radius: r, room: room).fill(style: FillStyle(eoFill: true)))
                .padding(-room)
                .allowsHitTesting(false)
            GlassBack(radius: r, dark: d)
            Canvas { ctx, size in
                Glass.sheet(ctx, CGRect(x: 0, y: 0, width: size.width, height: size.height), r, d)
            }
            .allowsHitTesting(false)
        }
    }
}
