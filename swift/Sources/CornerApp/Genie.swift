// Corner for macOS - the animations. The Genie: the panel's picture drawn as thin slices between a small square (the
// icon just inside the panel's corner, or the corner panel itself) and the panel's place; the far slices leave the
// square first, so the sides bend like a funnel whose neck is the square. It flies in a see-through window of its
// own that never takes a click, while the real panel waits unseen in its place and takes over when it lands.
// Used when the panel opens and closes, when Corner starts (out of the app icon), and between the corner panel and
// Full Mode. Also here: the parts that grow in when the pointer comes onto the panel, the energy view sliding down
// under the chips, the energy % counting up, and the test renders that show all of it as frames side by side.
// Port of Launch.cs (GenieHost, LaunchZoom, GenieHide, the warp between Mini and Full, CycleGenie, DemoLaunchFrames)
// and of Reveal and SlideIn in Focus.cs.
import AppKit
import SwiftUI
import CornerCore

// ---------------------------------------------------------------- the curves

enum GenieMath {
    /// A critically damped spring from 0 to 1: fast away, settling without overshoot.
    static func spring(_ x: Double) -> Double {
        let w: Double = 8
        let f: Double = 1 - (1 + w * x) * exp(-w * x)
        let whole: Double = 1 - (1 + w) * exp(-w)
        return f / whole
    }

    static func smooth(_ v: Double) -> Double {
        let x: Double = max(0, min(1, v))
        return x * x * (3 - 2 * x)
    }

    static func lerp(_ a: CGFloat, _ b: CGFloat, _ k: Double) -> CGFloat {
        return a + (b - a) * CGFloat(k)
    }

    /// WPF's QuinticEase, easing out: what the parts that grow in follow.
    static func out(_ v: Double) -> Double {
        let u: Double = 1 - max(0, min(1, v))
        return 1 - u * u * u * u * u
    }

    /// WPF's CubicEase, easing out.
    static func soft(_ v: Double) -> Double {
        let u: Double = 1 - max(0, min(1, v))
        return 1 - u * u * u
    }
}

// ---------------------------------------------------------------- one flight

/// One picture on its way out of the neck, or back into it. Its rectangles are measured from the top left of the
/// window it is drawn in, y pointing down (as in the Windows code); draw turns them over for AppKit.
final class GenieWarp {
    static let maxSlices: Int = 200
    static let lag: Double = 0.75      // how far the neck's end lags the far end: the curve of the funnel
    static let tailMs: Double = 280    // closing: what is in the neck stays a moment after the panel has gone in, then fades

    let to: CGRect            // the panel's place
    let neck: CGRect          // the icon's square, or the corner panel's place
    let neckAtTop: Bool
    let neckPic: CGImage?     // the corner panel's picture in the neck (the switch to Full Mode)
    let logo: CGImage?        // the app icon in the neck: only when Corner starts
    let opening: Bool
    let ms: Double
    let coarse: Bool          // a very large picture is scaled the plain way: it only shows while it is bent
    private(set) var slices: [CGImage] = []
    private(set) var at: [Double] = []   // the slices' edges, 0...1 down the picture
    var t: Double             // 0 = in the neck, 1 = the panel in place
    var tail: Double = 1

    var lingers: Bool { return logo != nil || neckPic != nil }

    init?(pic: CGImage, to: CGRect, neck: CGRect, neckAtTop: Bool, neckPic: CGImage?, logo: CGImage?, opening: Bool, ms: Double) {
        let pw: Int = pic.width
        let ph: Int = pic.height
        self.to = to
        self.neck = neck
        self.neckAtTop = neckAtTop
        self.neckPic = neckPic
        self.logo = logo
        self.opening = opening
        self.ms = ms
        self.coarse = pw * ph > 2_000_000 // (Full Mode on a screen with two pixels to the point)
        self.t = opening ? 0 : 1
        if pw < 2 || ph < 4 { return nil }
        let n: Int = max(2, min(GenieWarp.maxSlices, ph / 2)) // two pixel rows each, or more: smooth curved sides
        var edges: [Int] = []
        for i in 0...n {
            let y: Double = Double(ph) * Double(i) / Double(n)
            edges.append(Int(y.rounded()))
        }
        var cut: [CGImage] = []
        var marks: [Double] = []
        for i in 0..<n {
            let rows: Int = max(1, edges[i + 1] - edges[i])
            let band = CGRect(x: 0, y: edges[i], width: pw, height: rows) // (a picture's rows count from its top)
            guard let c = pic.cropping(to: band) else { return nil }
            cut.append(c)
        }
        for i in 0...n { marks.append(Double(edges[i]) / Double(ph)) }
        slices = cut
        at = marks
    }

    /// Moves it to `e` milliseconds after its start. True when it has finished.
    func step(_ e: Double) -> Bool {
        let x: Double = ms > 0 ? min(1, max(0, e) / ms) : 1
        t = opening ? x : 1 - x
        if !opening && lingers { tail = max(0, 1 - max(0, e - ms) / GenieWarp.tailMs) }
        return x >= 1 && (opening || !lingers || tail <= 0)
    }

    /// A rectangle measured from the top, as AppKit wants it in a view `h` tall.
    static func up(_ r: CGRect, _ h: CGFloat) -> CGRect {
        return CGRect(x: r.minX, y: h - r.maxY, width: r.width, height: r.height)
    }

    /// Draws it into a view `h` tall. `pixel` is how many pixels make a point there: the slices' edges are put on
    /// whole pixels, so they meet without a seam and without overlapping.
    func draw(_ ctx: CGContext, height h: CGFloat, pixel: CGFloat) {
        let tc: Double = max(0, min(1, t))
        // closing is not the spring backwards (that sits still for half its time, then snaps): it leaves at once and gathers speed
        let f: Double = opening ? GenieMath.spring(tc) : 1 - pow(1 - tc, 1.6)
        let down: Double = GenieMath.smooth(min(1, f * 1.35)) // how far the panel has slid out of the neck, end to end
        var alpha: Double = min(1, f * 4.5)                   // it takes over from the icon quickly
        if let np = neckPic {
            // the corner panel, handing over to the window flowing out of it
            let na: Double = (1 - min(1, f * 2.4)) * tail
            if na > 0.01 {
                ctx.saveGState()
                ctx.setAlpha(CGFloat(na))
                ctx.draw(np, in: GenieWarp.up(neck, h))
                ctx.restoreGState()
            }
            alpha = min(1, f * 3)
        }
        if alpha > 0.004 {
            let faded: Bool = alpha < 0.995
            let px: CGFloat = max(1, pixel)
            ctx.saveGState()
            ctx.interpolationQuality = coarse ? .none : .low
            if faded {
                // (as one layer, so the picture fades as a whole)
                ctx.setAlpha(CGFloat(alpha))
                ctx.beginTransparencyLayer(auxiliaryInfo: nil)
            }
            let n: Int = slices.count
            for i in 0..<n {
                let v0: Double = at[i]
                let v1: Double = at[i + 1]
                let vm: Double = (v0 + v1) / 2
                let far: Double = neckAtTop ? vm : 1 - vm // 0 at the neck's end of the panel, 1 at the other
                let wide: Double = GenieMath.smooth(f * (1 + GenieWarp.lag) - GenieWarp.lag * (1 - far)) // the far end widens first
                let a0: CGFloat = neck.minY + CGFloat(v0) * neck.height
                let b0: CGFloat = to.minY + CGFloat(v0) * to.height
                let a1: CGFloat = neck.minY + CGFloat(v1) * neck.height
                let b1: CGFloat = to.minY + CGFloat(v1) * to.height
                let y0: CGFloat = (GenieMath.lerp(a0, b0, down) * px).rounded() / px
                let y1: CGFloat = (GenieMath.lerp(a1, b1, down) * px).rounded() / px
                let l: CGFloat = GenieMath.lerp(neck.minX, to.minX, wide)
                let r: CGFloat = GenieMath.lerp(neck.maxX, to.maxX, wide)
                if y1 - y0 < 0.05 || r - l < 0.5 { continue }
                ctx.draw(slices[i], in: CGRect(x: l, y: h - y1, width: r - l, height: y1 - y0))
            }
            if faded { ctx.endTransparencyLayer() }
            ctx.restoreGState()
        }
        // the icon, swelling a little as it fades into the panel
        if neckPic != nil { return }
        guard let lg = logo else { return }
        let rate: Double = opening ? 2.6 : 1.7
        let ia: Double = (1 - min(1, f * rate)) * tail
        if ia <= 0.01 { return }
        let s: CGFloat = neck.width * CGFloat(1 + 0.35 * f)
        let x: CGFloat = neck.midX - s / 2
        let y: CGFloat = neck.midY - s / 2
        let box = CGRect(x: x + s * 0.04, y: y + s * 0.04, width: s * 0.92, height: s * 0.92)
        ctx.saveGState()
        ctx.setAlpha(CGFloat(ia))
        ctx.interpolationQuality = .high
        ctx.draw(lg, in: GenieWarp.up(box, h))
        ctx.restoreGState()
    }
}

// ---------------------------------------------------------------- the window it flies in

/// Draws the flight under way; or holds one picture still (the corner panel, while the window under it changes to Full Mode).
final class GenieView: NSView {
    var warp: GenieWarp? = nil
    var held: CGImage? = nil
    var heldAt: CGRect = .zero

    override var isOpaque: Bool { return false }
    override func hitTest(_ point: NSPoint) -> NSView? { return nil }

    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let h: CGFloat = bounds.height
        if let w = warp {
            let k: CGFloat = window?.backingScaleFactor ?? 2
            w.draw(ctx, height: h, pixel: k)
        } else if let p = held {
            ctx.draw(p, in: GenieWarp.up(heldAt, h))
        }
    }
}

/// See-through, over everything the panel is over, on every Space; clicks pass through and it never takes the keyboard.
final class GenieWindow: NSPanel {
    override var canBecomeKey: Bool { return false }
    override var canBecomeMain: Bool { return false }
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { return frameRect }
}

/// One window's picture in a test frame, and where it is on the screen.
struct GenieLayer {
    let image: CGImage
    let rect: CGRect
    let alpha: CGFloat
}

// ---------------------------------------------------------------- what the shell keeps about the animations

final class GenieStage {
    /// The test renders that show an animation as frames side by side.
    static let demoModes: [String] = ["launch", "launchclose", "hoveropen", "hoverclose", "fullswitch", "fullexit", "cycleopen", "cycleclose",
                                      "hoverback", "hoveragain", "fullearly", "hoverbottom"]
    /// Those renders paint the sand backdrop themselves, behind every frame: the panel's own view is left clear.
    static var clearBackdrop = false
    static let iconSide: CGFloat = 64
    /// What the last picture taken was like: for the log of a test render.
    static var note: String = ""

    weak var shell: PanelController?
    var window: GenieWindow? = nil
    var view: GenieView? = nil
    var warp: GenieWarp? = nil       // the flight under way
    var warpStart: Double = 0
    var warpDone: (() -> Void)? = nil
    var timer: Timer? = nil

    var launchZoom = false      // the next showPanel comes out of the app icon (set as Corner starts)
    var back = false            // the panel goes back into its corner when it hides (it came out with the Genie)
    var icon = false            // ...into the app icon: only the startup panel, until the pointer has been on it
    var pending = false         // a picture is about to fly: the card is being laid out first
    var opening = false         // the panel is coming out of its corner as one picture
    var revealWaiting = false   // ...and the pointer is on it already: the rest comes in when the picture has landed
    var switchWarp = false      // showPanel: nothing fades in, the switch to Full Mode draws the window as one picture
    var miniPic: CGImage? = nil // the corner panel as it was when Full Mode opened, and where it was
    var miniRect: CGRect = .zero
    var reach: CGRect = .null   // where the Full Mode window may come to be: the see-through window covers it from the start
    var entering = false        // the corner panel's picture is on top; the window is about to change to Full Mode under it
    var leaving = false         // Full Mode's picture is on top, flowing back down; the window is about to go under it
    var fades = 0               // (counts the uses of the see-through window: a fade that has been overtaken does nothing)
    var unseenSince: Double = 0 // (see watchUnseen)
    var dirty: CGRect = .zero   // the part of the see-through window the flight under way draws in

    var revealStart: Double = -1   // the parts growing in under the pointer; -1 = not running
    var cycleStart: Double = -1    // the energy view sliding down, or folding up
    var cycleFrom: CGFloat = 0
    var cycleTo = 0
    var countStart: Double = -1    // the energy % counting up

    var manual = false          // a test render: time stands still until it is moved by hand
    var demoMotion = false      // a test render of an animation: the animations run
    var clock: Double = 0
    var shots: [[GenieLayer]] = []

    /// The animations may run: "Reduce motion" is off; and in a test render only when it is one of an animation.
    var live: Bool {
        guard let s = shell else { return false }
        if s.model.demoRun { return demoMotion }
        return !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    func now() -> Double { return manual ? clock : PanelController.ms() }

    /// Nothing is flying, held or about to fly.
    var idle: Bool { return warp == nil && !pending && !entering && !leaving }

    /// The panel is on its way out of its corner as one picture (or about to be): what the pointer brings in waits.
    var flyingOut: Bool {
        if !opening { return false }
        if pending { return true }
        if let w = warp { return w.opening }
        return false
    }

    /// The app icon's picture (the logo, Resources/logo.png), made small once.
    static let logo: CGImage? = GenieStage.loadLogo()

    static func loadLogo() -> CGImage? {
        guard let url = Resources.url("logo", "png"), let img = NSImage(contentsOf: url) else { return nil }
        var r = NSRect(x: 0, y: 0, width: img.size.width, height: img.size.height)
        guard let big = img.cgImage(forProposedRect: &r, context: nil, hints: nil) else { return nil }
        let side: Int = 256
        let space: CGColorSpace = CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
        guard let ctx = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return big }
        ctx.interpolationQuality = .high
        ctx.draw(big, in: CGRect(x: 0, y: 0, width: side, height: side))
        return ctx.makeImage() ?? big
    }

    func ensureWindow() -> GenieView {
        if let v = view, window != nil { return v }
        let w = GenieWindow(contentRect: NSRect(x: -10000, y: -10000, width: 100, height: 100),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.level = .statusBar
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        w.hidesOnDeactivate = false
        w.isFloatingPanel = true
        w.isReleasedWhenClosed = false
        w.animationBehavior = .none
        w.ignoresMouseEvents = true
        let v = GenieView(frame: NSRect(x: 0, y: 0, width: 100, height: 100))
        v.autoresizingMask = [.width, .height]
        w.contentView = v
        window = w
        view = v
        return v
    }

    // ------------------------------------------------------------ time

    func wake() {
        if manual || timer != nil { return }
        let t = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    /// One frame of everything that is moving.
    func tick() {
        guard let s = shell else { stopTimer(); return }
        let m: AppModel = s.model
        let t: Double = now()
        if let w = warp {
            let finished: Bool = w.step(t - warpStart)
            view?.setNeedsDisplay(dirty)
            if finished {
                let d = warpDone
                let landed: Bool = w.opening
                warp = nil
                warpDone = nil
                if let dd = d { dd() } // the real panel takes over
                if warp == nil {
                    if landed {
                        letGo()
                    } else {
                        view?.warp = nil
                        window?.orderOut(nil)
                    }
                }
            }
        }
        var touched = false
        if revealStart >= 0 {
            let x: Double = min(1, (t - revealStart) / 200)
            m.revealK = CGFloat(GenieMath.out(x))
            if x >= 1 { revealStart = -1; m.revealK = 1 }
            touched = true
        }
        if cycleStart >= 0 {
            let dur: Double = cycleTo == 1 ? 260 : 200
            let x: Double = min(1, (t - cycleStart) / dur)
            let e: CGFloat = CGFloat(GenieMath.out(x))
            m.cycleK = cycleTo == 1 ? cycleFrom + (1 - cycleFrom) * e : cycleFrom * (1 - e)
            if x >= 1 {
                cycleStart = -1
                m.cycleK = -1
                if cycleTo == 0 && m.tab == 1 && !m.full { m.setTab(0) }
            }
            touched = true
        }
        if countStart >= 0 {
            let x: Double = min(1, (t - countStart) / 600)
            m.countK = CGFloat(GenieMath.soft(x))
            if x >= 1 { countStart = -1; m.countK = 1 }
            touched = true
        }
        if touched { m.changed() }
        if warp == nil && revealStart < 0 && cycleStart < 0 && countStart < 0 { stopTimer() }
    }

    /// The picture has landed and the real panel is showing under it, the same to the eye: the picture fades off
    /// over a few frames, so there is no moment with neither of them on the screen.
    func letGo() {
        guard let w = window else { return }
        fades += 1
        let mine: Int = fades
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.07
            w.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            guard let s = self, s.fades == mine else { return }
            s.view?.warp = nil
            w.orderOut(nil)
            w.alphaValue = 1
        })
    }

    /// The see-through window is wanted again, or dropped: a fade that was taking it off is overtaken.
    func claim() {
        fades += 1
        guard let w = window else { return }
        if w.alphaValue < 1 {
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0
                w.animator().alphaValue = 1
            }, completionHandler: nil)
            w.alphaValue = 1
        }
    }

    /// A test render: moves time on to `ms`, in steps as small as the real frames.
    func advance(to ms: Double) {
        while clock < ms - 0.001 {
            clock = min(ms, clock + 10)
            tick()
        }
    }

    /// The panel is about to show: nothing is half grown.
    func resetGlides() {
        revealStart = -1
        cycleStart = -1
        countStart = -1
        if let m = shell?.model {
            m.revealK = 1
            m.cycleK = -1
            m.countK = 1
        }
    }

    /// The panel is going: what was growing stays as it is, for its picture.
    func freezeGlides() {
        revealStart = -1
        cycleStart = -1
        countStart = -1
    }

    func endCycle() {
        cycleStart = -1
        shell?.model.cycleK = -1
    }

    // ------------------------------------------------------------ pictures

    /// A view as a picture, in its own screen's pixels and colours: drawn back on that screen it is the same to the eye.
    static func raw(_ view: NSView) -> CGImage? {
        let b: NSRect = view.bounds
        if b.width < 1 || b.height < 1 { return nil }
        if let rep = view.bitmapImageRepForCachingDisplay(in: b) {
            if let d = rep.bitmapData { memset(d, 0, rep.bytesPerRow * rep.pixelsHigh) } // (clear first: most of it is see-through)
            view.cacheDisplay(in: b, to: rep)
            if let img = rep.cgImage { return img }
        }
        // (no such bitmap, as for a view in no window: a plain one, two pixels to the point)
        let pw: Int = Int((b.width * 2).rounded())
        let ph: Int = Int((b.height * 2).rounded())
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pw, pixelsHigh: ph, bitsPerSample: 8, samplesPerPixel: 4,
                                         hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        rep.size = b.size
        view.cacheDisplay(in: b, to: rep)
        return rep.cgImage
    }

    /// The panel as a picture, shadow and all. A bitmap of a view does not clip a scroll view's content the way the
    /// screen does, so where a list is longer than its box the picture is put together from two: one without the
    /// lists, and the lists cut to their boxes. Nil when nothing was drawn.
    static func picture(_ view: NSView) -> CGImage? {
        view.layoutSubtreeIfNeeded()
        guard let whole = raw(view) else { return nil }
        var scrolls: [NSScrollView] = []
        overflowing(view, &scrolls)
        var out: CGImage = whole
        var mended = false
        if !scrolls.isEmpty {
            for s in scrolls { s.documentView?.isHidden = true }
            let under: CGImage? = raw(view)
            for s in scrolls { s.documentView?.isHidden = false }
            if let u = under, let fixed = patch(whole, u, scrolls, view) {
                out = fixed
                mended = true
            }
        }
        let empty: Bool = blank(out)
        note = String(out.width) + "x" + String(out.height) + " px, " + String(scrolls.count) + " long lists"
            + (mended ? " cut to their boxes" : "") + (empty ? ", empty" : "")
        return empty ? nil : out
    }

    /// The scroll views whose content does not fit, or has been scrolled.
    static func overflowing(_ v: NSView, _ out: inout [NSScrollView]) {
        for sub in v.subviews {
            if sub.isHidden { continue }
            if let s = sub as? NSScrollView, let doc = s.documentView {
                let clip: NSRect = s.contentView.bounds
                let big: Bool = doc.frame.height > clip.height + 0.5 || doc.frame.width > clip.width + 0.5
                let moved: Bool = abs(clip.origin.x) > 0.5 || abs(clip.origin.y) > 0.5
                if big || moved {
                    out.append(s)
                    continue
                }
            }
            overflowing(sub, &out)
        }
    }

    static func patch(_ whole: CGImage, _ under: CGImage, _ scrolls: [NSScrollView], _ view: NSView) -> CGImage? {
        let w: Int = whole.width
        let h: Int = whole.height
        let vh: CGFloat = view.bounds.height
        if vh < 1 || under.width != w || under.height != h { return nil }
        let scale: CGFloat = CGFloat(h) / vh
        let space: CGColorSpace = whole.colorSpace ?? CGColorSpaceCreateDeviceRGB() // (the picture's own colours, unchanged)
        guard let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        let all = CGRect(x: 0, y: 0, width: w, height: h)
        ctx.draw(under, in: all)
        for s in scrolls {
            let r: NSRect = s.convert(s.bounds, to: view)
            let fromBottom: CGFloat = view.isFlipped ? vh - r.maxY : r.minY
            let x0: CGFloat = (r.minX * scale).rounded()
            let y0: CGFloat = (fromBottom * scale).rounded()
            let x1: CGFloat = (r.maxX * scale).rounded()
            let y1: CGFloat = ((fromBottom + r.height) * scale).rounded()
            ctx.saveGState()
            ctx.clip(to: CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0))
            ctx.setBlendMode(.copy)
            ctx.draw(whole, in: all)
            ctx.restoreGState()
        }
        return ctx.makeImage()
    }

    /// Hardly anything in it: the view had not been drawn yet.
    static func blank(_ img: CGImage) -> Bool {
        let side: Int = 8
        guard let ctx = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
        ctx.interpolationQuality = .low
        ctx.draw(img, in: CGRect(x: 0, y: 0, width: side, height: side))
        guard let data = ctx.data else { return false }
        let row: Int = ctx.bytesPerRow
        let p = data.bindMemory(to: UInt8.self, capacity: row * side)
        var sum: Int = 0
        for y in 0..<side {
            for x in 0..<side { sum += Int(p[y * row + x * 4 + 3]) }
        }
        return sum < side * side * 6
    }
}

// ---------------------------------------------------------------- the shell's part

extension PanelController {
    /// The icon's square, where the panel comes out of and goes back into: just inside the panel's corner.
    /// `card` is the card's place on the screen (AppKit: y points up).
    func iconRect(_ card: CGRect) -> CGRect {
        let s: CGFloat = GenieStage.iconSide
        let inset: CGFloat = 6
        let x: CGFloat = model.onRight ? card.maxX - inset - s : card.minX + inset
        let y: CGFloat = model.onTop ? card.maxY - inset - s : card.minY + inset
        return CGRect(x: x, y: y, width: s, height: s)
    }

    /// The same square where the corner panel opens when it has not been moved: Full Mode comes out of it, and goes
    /// back into it, when the corner panel is not open.
    func cornerIcon() -> CGRect {
        let s: CGFloat = GenieStage.iconSide
        let vf: CGRect = screen?.visibleFrame ?? window.frame
        let m: CGFloat = 12 + 6
        let x: CGFloat = model.onRight ? vf.maxX - m - s : vf.minX + m
        let y: CGFloat = model.onTop ? vf.maxY - m - s : vf.minY + m
        return CGRect(x: x, y: y, width: s, height: s)
    }

    /// Where the Full Mode window can come to be, whatever its height turns out to be (see targetFrame).
    func fullReach() -> CGRect {
        guard let scr = screen else { return CGRect.null }
        let vf: CGRect = scr.visibleFrame
        let s: CGFloat = CGFloat(model.fullScale)
        let w: CGFloat = fullW * s + 2 * shadowM
        var x: CGFloat = vf.midX - w / 2
        var top: CGFloat = vf.maxY - 20 + shadowM
        if Settings.get("MacFullMoved", 0) > 0.5 {
            x = max(vf.minX - shadowM, min(vf.maxX - w + shadowM, CGFloat(Settings.get("MacFullX", Double(x)))))
            top = max(vf.minY + 260, min(vf.maxY + shadowM, CGFloat(Settings.get("MacFullTop", Double(top)))))
        }
        // (the card may come out a little taller than the tallest it is meant to be: some room to spare)
        let tallest: CGFloat = max(300 * s, vf.height - 40) + 2 * shadowM + 48
        return CGRect(x: x, y: top - tallest, width: w, height: tallest)
    }

    /// Calls `then` when the card has stopped changing size (parts of it measure themselves, which takes a pass or
    /// two), after at least `least` looks a frame apart; and anyway after a quarter of a second. Dropped when the
    /// panel has been shown or hidden again meanwhile.
    func settleThen(_ g: Int, _ last: CGSize, _ tries: Int, _ least: Int, _ then: @escaping () -> Void) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.016) { [weak self] in
            guard let s = self, s.gen == g else { return }
            s.hosting.layoutSubtreeIfNeeded()
            let size: CGSize = s.cardSize
            let same: Bool = abs(size.width - last.width) < 0.5 && abs(size.height - last.height) < 0.5
            if (same && tries + 1 >= least) || tries >= 14 { then() } else { s.settleThen(g, size, tries + 1, least, then) }
        }
    }

    // ------------------------------------------------------------ out of the corner, and back in

    /// From showPanel: the window is in its place, unseen. Its picture flies as soon as the card is laid out.
    func genieSoon(icon: Bool) {
        fx.pending = true
        fx.opening = true
        // (as Corner starts the card is laid out for the first time: a few looks more)
        settleThen(gen, cardSize, 0, icon ? 3 : 1) { [weak self] in self?.genieOpen(icon: icon) }
    }

    /// Out of the icon's square; with the app icon in it when Corner starts.
    func genieOpen(icon: Bool) {
        if !model.shown || model.full {
            fx.pending = false
            fx.opening = false
            return
        }
        place(animated: false)
        var ok = false
        if let pic = GenieStage.picture(hosting) {
            let to: CGRect = window.frame
            let neck: CGRect = iconRect(to.insetBy(dx: shadowM, dy: shadowM))
            ok = runGenie(pic, to: to, neck: neck, neckPic: nil, opening: true, ms: icon ? 560 : 420, showIcon: icon,
                          done: { [weak self] in self?.landed() })
        }
        if model.demoRun { Store.log("genie open: " + (ok ? "flying" : "no picture") + " window " + NSStringFromRect(window.frame) + " picture " + GenieStage.note) }
        if ok {
            fx.back = true
            fx.icon = icon
            return
        }
        // no picture: it fades in where it is
        fx.pending = false
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.16
            window.animator().alphaValue = 1
        }, completionHandler: nil)
        fx.opening = false
        if fx.revealWaiting { fx.revealWaiting = false; model.reveal() }
    }

    /// The picture has landed: the real panel takes over.
    func landed() {
        window.alphaValue = 1
        fx.opening = false
        if fx.revealWaiting { fx.revealWaiting = false; model.reveal() } // the pointer came onto it on its way out
    }

    /// From hidePanel: back into the icon's square. False when it cannot; hidePanel then fades as before.
    func genieHide(done: @escaping () -> Void) -> Bool {
        if model.full { return false }
        stopGenie()
        guard window.isVisible, let pic = GenieStage.picture(hosting) else { return false }
        let to: CGRect = window.frame
        let neck: CGRect = iconRect(to.insetBy(dx: shadowM, dy: shadowM))
        let withIcon: Bool = fx.icon
        fx.icon = false
        if !runGenie(pic, to: to, neck: neck, neckPic: nil, opening: false, ms: withIcon ? 460 : 380, showIcon: withIcon, done: done) { return false }
        // The picture is on top now, the same to the eye. The panel under it goes two frames later, so there is no
        // moment with neither of them on the screen (the picture has hardly begun to move by then).
        let g: Int = gen
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.034) { [weak self] in
            guard let s = self, s.gen == g else { return }
            s.window.alphaValue = 0
        }
        return true
    }

    /// Drops whatever is flying or held, without its ending.
    func stopGenie() {
        let flying: Bool = fx.warp != nil || fx.pending
        fx.opening = false
        fx.revealWaiting = false
        fx.pending = false
        fx.warp = nil
        fx.warpDone = nil
        if let v = fx.view {
            v.warp = nil
            v.held = nil
        }
        fx.claim()
        if let w = fx.window, w.isVisible { w.orderOut(nil) }
        if flying { window.alphaValue = model.shown ? 1 : 0 }
    }

    /// Puts the see-through window over the two places and starts the flight. `to` and `neck` are on the screen.
    func runGenie(_ pic: CGImage, to: CGRect, neck: CGRect, neckPic: CGImage?, opening: Bool, ms: Double, showIcon: Bool,
                  reach: CGRect = .null, done: (() -> Void)?) -> Bool {
        let area: CGRect = to.union(neck).union(reach).insetBy(dx: -16, dy: -16).integral // (the icon swells past its square)
        let toIn = CGRect(x: to.minX - area.minX, y: area.maxY - to.maxY, width: to.width, height: to.height)
        let neckIn = CGRect(x: neck.minX - area.minX, y: area.maxY - neck.maxY, width: neck.width, height: neck.height)
        guard let warp = GenieWarp(pic: pic, to: toIn, neck: neckIn, neckAtTop: model.onTop, neckPic: neckPic,
                                   logo: showIcon ? GenieStage.logo : nil, opening: opening, ms: ms) else { return false }
        let v: GenieView = fx.ensureWindow()
        guard let w = fx.window else { return false }
        fx.claim()
        fx.pending = false
        fx.warp = warp
        fx.warpDone = done
        fx.warpStart = fx.now()
        fx.dirty = GenieWarp.up(toIn.union(neckIn).insetBy(dx: -16, dy: -16), area.height) // (only this much is drawn again each frame)
        _ = warp.step(0)
        v.held = nil
        v.warp = warp
        if w.frame != area { w.setFrame(area, display: false) }
        v.needsDisplay = true
        w.orderFrontRegardless()
        w.displayIfNeeded()
        fx.wake()
        return true
    }

    // ------------------------------------------------------------ between the corner panel and Full Mode
    // The two are one window at two sizes, so the switch is drawn in the see-through window: the corner panel flows
    // out into the Full Mode window, and Full Mode flows back down into the corner. No icon unless the corner panel
    // was not open (Full Mode from the menu): then it comes out of the corner's square.

    /// From enterFull, before the corner panel goes: its picture, the neck the Full Mode window flows out of.
    func grabMini() {
        fx.miniPic = nil
        fx.miniRect = .zero
        let open: Bool = model.shown && !model.full && window.isVisible
        stopGenie()
        if !open { return }
        if let p = GenieStage.picture(hosting) {
            fx.miniPic = p
            fx.miniRect = window.frame
        }
    }

    /// The corner panel's picture on top, so the window can change underneath unseen.
    func holdMini() {
        guard let pic = fx.miniPic else { return }
        let r: CGRect = fx.miniRect
        // (the window is put where the flight will need it, so it does not have to change while it is on the screen)
        let area: CGRect = r.union(fx.reach).insetBy(dx: -16, dy: -16).integral
        let v: GenieView = fx.ensureWindow()
        guard let w = fx.window else { return }
        fx.claim()
        v.warp = nil
        v.held = pic
        v.heldAt = CGRect(x: r.minX - area.minX, y: area.maxY - r.maxY, width: r.width, height: r.height)
        w.setFrame(area, display: false)
        v.needsDisplay = true
        w.orderFrontRegardless()
        w.displayIfNeeded()
    }

    /// From enterFull, once the Full Mode window is laid out in its place: out of the corner panel (or the square).
    func warpIntoFull() {
        fx.pending = false
        if !model.full || !model.shown || fx.leaving { return }
        place(animated: false)
        var ok = false
        let before: CGRect = fx.window?.frame ?? CGRect.zero
        if let pic = GenieStage.picture(hosting) {
            let neck: CGRect = fx.miniPic != nil ? fx.miniRect : cornerIcon()
            ok = runGenie(pic, to: window.frame, neck: neck, neckPic: fx.miniPic, opening: true, ms: 600, showIcon: false,
                          reach: fx.miniPic != nil ? fx.reach : CGRect.null, done: { [weak self] in self?.window.alphaValue = 1 })
        }
        if model.demoRun {
            Store.log("warp into full: " + (ok ? "flying" : "no picture") + " window " + NSStringFromRect(window.frame) + " picture " + GenieStage.note
                      + "; see-through window " + NSStringFromRect(before) + " then " + NSStringFromRect(fx.window?.frame ?? CGRect.zero))
        }
        fx.miniPic = nil
        if !ok {
            stopGenie()
            window.alphaValue = 1
        }
    }

    /// From exitFull, before the window goes: back down into the corner's square. False when it cannot.
    func warpOutOfFull() -> Bool {
        stopGenie()
        guard window.isVisible, let pic = GenieStage.picture(hosting) else { return false }
        return runGenie(pic, to: window.frame, neck: cornerIcon(), neckPic: nil, opening: false, ms: 480, showIcon: false, done: nil)
    }

    /// From mouseTick, a safeguard: the panel waits unseen only while its picture is flying. Should it ever be left
    /// open and unseen with nothing flying for more than half a second, it is simply shown.
    func watchUnseen(_ now: Double) {
        if model.shown && fx.idle && window.isVisible && window.alphaValue < 0.05 {
            if fx.unseenSince == 0 {
                fx.unseenSince = now
            } else if now - fx.unseenSince > 600 {
                fx.unseenSince = 0
                fx.opening = false
                window.alphaValue = 1
            }
        } else {
            fx.unseenSince = 0
        }
    }

    // ------------------------------------------------------------ growing in under the pointer

    /// From AppModel.reveal: the header, the chips and what else the pointer brings in grow from nothing over 200 ms.
    /// False when nothing moves ("Reduce motion", a still test render): reveal then does what it did before.
    func growIn() -> Bool {
        if !fx.live || model.full { return false }
        model.revealK = 0
        fx.revealStart = fx.now()
        model.changed()
        fx.wake()
        return true
    }

    // ------------------------------------------------------------ test renders

    /// Test hook: an animation as frames side by side in one picture. Time is moved by hand to each frame's moment,
    /// so the frames are the same on every run; the pauses between are only for the card to lay itself out.
    func demoMotion(_ path: String, _ mode: String) {
        GenieStage.clearBackdrop = true
        FitScrollFlag.plain = false // the real scroll views: the pictures are taken as they are on a real screen
        let early: Bool = mode.hasPrefix("launch") || mode.hasPrefix("hover") || mode == "fullearly" // the panel itself flies out
        if early { demoBegin() }
        if mode.hasPrefix("launch") { fx.launchZoom = true }
        if mode == "hoverbottom" { model.setCorner(3) } // bottom left: the funnel the other way up, the panel growing upwards
        demoSeed(mode)
        showPanel(peekMs: 600000)
        model.reveal()
        model.changed()
        let open: [Double] = [30, 80, 130, 190, 280, 450, 700]
        let close: [Double] = [120, 240, 330, 420, 520, 640]
        // (a flight starts a moment after it is asked for, once the card is laid out: the frames wait for that)
        let flying: (PanelController) -> Bool = { s in s.fx.warp != nil }
        switch mode {
        case "launch", "hoveropen", "hoverbottom":
            demoWhen(0, flying) { s in s.demoFrames(path, open, 0, 0) }
        case "launchclose", "hoverclose":
            demoWhen(0, flying) { s in
                s.fx.advance(to: 900)
                s.demoAfter(0.5) { s2 in
                    s2.hidePanel()
                    s2.demoFrames(path, close, 900, 0)
                }
            }
        case "fullswitch":
            demoAfter(1.2) { s in
                s.demoBegin()
                s.enterFull()
                s.demoWhen(0, flying) { s2 in s2.demoFrames(path, [40, 110, 190, 280, 390, 590, 700], 0, 0) }
            }
        case "fullexit":
            demoAfter(1.2) { s in
                s.demoBegin()
                s.enterFull()
                s.demoWhen(0, flying) { s2 in
                    s2.fx.advance(to: 1500)
                    s2.demoAfter(0.5) { s3 in
                        s3.exitFull()
                        s3.demoFrames(path, [60, 150, 240, 330, 430, 600], 1500, 0)
                    }
                }
            }
        case "cycleopen":
            demoAfter(1.2) { s in
                s.demoBegin()
                s.model.cycle(to: 1)
                s.demoFrames(path, [30, 90, 160, 240, 330, 600], 0, 0)
            }
        case "hoverback":
            // the pointer leaves while the panel is still flying out: it goes back in from where it would have landed
            demoWhen(0, flying) { s in
                s.fx.advance(to: 200)
                s.demoAfter(0.4) { s2 in
                    s2.fx.shots.append(s2.demoShot(200))
                    s2.hidePanel()
                    s2.demoFrames(path, [20, 100, 200, 300, 400], 200, 0)
                }
            }
        case "hoveragain":
            // opened again before it has finished going back in: the next task alone flies out, not the old picture
            demoWhen(0, flying) { s in
                s.fx.advance(to: 900)
                s.demoAfter(0.5) { s2 in
                    s2.hidePanel()
                    s2.fx.advance(to: 1100)
                    s2.demoAfter(0.4) { s3 in
                        s3.fx.shots.append(s3.demoShot(1100))
                        s3.showPanel(peekMs: 600000)
                        s3.demoWhen(0, flying) { s4 in s4.demoFrames(path, [30, 130, 280, 450, 600], 1100, 0) }
                    }
                }
            }
        case "fullearly":
            // Full Mode entered while the corner panel is still flying out
            demoWhen(0, flying) { s in
                s.fx.advance(to: 200)
                s.demoAfter(0.4) { s2 in
                    s2.fx.shots.append(s2.demoShot(200))
                    s2.enterFull()
                    s2.demoWhen(0, flying) { s3 in s3.demoFrames(path, [40, 190, 390, 560, 700], 200, 0) }
                }
            }
        default: // cycleclose
            demoAfter(1.2) { s in
                s.model.setTab(1)
                s.demoAfter(0.7) { s2 in
                    s2.demoBegin()
                    s2.model.cycle(to: 0)
                    s2.demoFrames(path, [30, 100, 170, 240, 320, 600], 0, 0)
                }
            }
        }
    }

    /// From here on the animations run, on a clock moved by hand.
    func demoBegin() {
        fx.manual = true
        fx.demoMotion = true
    }

    func demoAfter(_ seconds: Double, _ work: @escaping (PanelController) -> Void) {
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) { [weak self] in
            guard let s = self else { exit(1) }
            work(s)
        }
    }

    /// Waits, in real time, until `ready`, and a moment more for the card to lay itself out (eight seconds at most).
    func demoWhen(_ tries: Int, _ ready: @escaping (PanelController) -> Bool, _ work: @escaping (PanelController) -> Void) {
        demoAfter(0.05) { s in
            if ready(s) || tries >= 160 {
                s.demoAfter(0.4, work)
            } else {
                s.demoWhen(tries + 1, ready, work)
            }
        }
    }

    /// Moves time to each of `times` (milliseconds after `base`) in turn and takes a frame there; then writes them.
    func demoFrames(_ path: String, _ times: [Double], _ base: Double, _ i: Int) {
        fx.advance(to: base + times[i])
        demoAfter(0.22) { s in
            s.fx.shots.append(s.demoShot(times[i]))
            if i + 1 < times.count {
                s.demoFrames(path, times, base, i + 1)
            } else {
                s.demoStrip(path)
                exit(0)
            }
        }
    }

    /// What is on the screen now: the panel's window if it can be seen, and the see-through window over it.
    func demoShot(_ at: Double) -> [GenieLayer] {
        var layers: [GenieLayer] = []
        var said: String = "frame " + String(Int(at)) + " ms:"
        if window.isVisible && window.alphaValue > 0.01 {
            if let p = GenieStage.picture(hosting) {
                layers.append(GenieLayer(image: p, rect: window.frame, alpha: window.alphaValue))
                said += " panel " + NSStringFromRect(window.frame)
            } else {
                said += " panel (no picture)"
            }
        }
        if let w = fx.window, w.isVisible, let v = fx.view {
            if let p = GenieStage.raw(v) {
                layers.append(GenieLayer(image: p, rect: w.frame, alpha: 1))
                said += " flying " + NSStringFromRect(w.frame)
            }
        }
        Store.log(said + " card " + NSStringFromSize(cardSize))
        return layers
    }

    /// The frames side by side on the sand backdrop, each showing the same piece of the screen.
    func demoStrip(_ path: String) {
        let shots: [[GenieLayer]] = fx.shots
        var stage: CGRect = CGRect.null
        for s in shots {
            for l in s { stage = stage.union(l.rect) }
        }
        if stage.isNull || stage.width < 2 || stage.height < 2 {
            Store.log("no window was to be seen in any frame: nothing written") // (the build then says "no render")
            return
        }
        stage = stage.insetBy(dx: -10, dy: -10)
        let q: CGFloat = min(1, 1100 / stage.width) // frames wider than that (most of a large screen) are drawn smaller
        let fw: Int = Int((stage.width * q).rounded())
        let fh: Int = Int((stage.height * q).rounded())
        let gapPx: Int = 8
        let n: Int = max(1, shots.count)
        let wide: Int = n * fw + (n - 1) * gapPx
        guard let ctx = CGContext(data: nil, width: wide, height: fh, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
        ctx.interpolationQuality = .high
        let sand = CGColor(red: 185.0 / 255, green: 177.0 / 255, blue: 151.0 / 255, alpha: 1)
        for i in 0..<n {
            let x0: CGFloat = CGFloat(i * (fw + gapPx))
            let frame = CGRect(x: x0, y: 0, width: CGFloat(fw), height: CGFloat(fh))
            ctx.saveGState()
            ctx.clip(to: frame)
            ctx.setFillColor(sand)
            ctx.fill(frame)
            let layers: [GenieLayer] = i < shots.count ? shots[i] : []
            for l in layers {
                let x: CGFloat = x0 + (l.rect.minX - stage.minX) * q
                let y: CGFloat = (l.rect.minY - stage.minY) * q
                ctx.setAlpha(l.alpha)
                ctx.draw(l.image, in: CGRect(x: x, y: y, width: l.rect.width * q, height: l.rect.height * q))
            }
            ctx.restoreGState()
        }
        guard let img = ctx.makeImage() else { return }
        let rep = NSBitmapImageRep(cgImage: img)
        if let data = rep.representation(using: .png, properties: [:]) {
            do { try data.write(to: URL(fileURLWithPath: path)) } catch { Store.log(error) }
        }
    }
}

// ---------------------------------------------------------------- inside the card

/// The parts of the corner panel that grow in or fold away.
enum GrowPart {
    case header   // the header: comes in with the pointer
    case hover    // the check-ins and the row of chips: the same
    case focus    // the next task: it stays, with the space under it
    case tasks    // the add box and the list: come in with the pointer, and fold away while the energy view is open
    case energy   // the energy view: slides down under the chips, and folds back up
}

extension AppModel {
    /// How far the energy view is open in the corner panel: 0 on the tasks side, 1 on the energy side, between
    /// while it slides.
    var energyOpen: CGFloat {
        if cycleK >= 0 { return max(0, min(1, cycleK)) }
        return tab == 1 ? 1 : 0
    }

    /// How much of a part shows at this moment (all of it, unless something is moving).
    func grow(_ part: GrowPart) -> Grow {
        let gap: CGFloat = m.gapY
        let open: CGFloat = energyOpen
        let sliding: Bool = cycleK >= 0
        let shutting: Bool = sliding && (shell?.fx.cycleTo ?? 1) == 0
        let grown: CGFloat = full ? 1 : revealK
        switch part {
        case .header: return Grow(k: grown, gap: 0, folding: false)
        case .hover: return Grow(k: grown, gap: gap, folding: false)
        case .focus: return Grow(k: 1, gap: gap, folding: false)
        case .tasks: return Grow(k: min(grown, 1 - open), gap: gap, folding: sliding && !shutting)
        case .energy: return Grow(k: open, gap: gap, folding: shutting)
        }
    }

    /// The energy % as it shows while it counts up.
    func counted(_ pct: Int) -> Int {
        if countK >= 1 { return pct }
        let v: Double = Double(pct) * Double(max(0, countK))
        return max(1, Int(v.rounded()))
    }

    /// From the energy chip: the energy view drops down under the chips, as an extension of the panel, and folds
    /// back up the same way (the slide of CycleGenie in Launch.cs). The % counts up as it opens.
    func cycle(to: Int) {
        guard let s = shell else { setTab(to); return }
        let fx: GenieStage = s.fx
        let from: CGFloat = energyOpen
        if full || !shown || !fx.live || isBare || promptOnly || settingsOpen || to == tab {
            setTab(to)
            return
        }
        if to == 1 { setTab(1) } else { fx.endCycle() } // the chips change at once; folding up, when it is done
        fx.cycleFrom = from
        fx.cycleTo = to == 1 ? 1 : 0
        fx.cycleStart = fx.now()
        cycleK = from
        if to == 1 {
            countK = 0
            fx.countStart = fx.now()
        }
        changed()
        fx.wake()
    }
}

struct GrowHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

/// Shows `k` of a part's height, from its top, fading with it: the part grows in from nothing, or folds away, and
/// what is under it moves with it (SlideIn in Focus.cs). The part carries the space that follows it in its column
/// (`gap`), so that grows with it too. With k = 1 it is the part and that space, as they would be without this.
struct Grow: ViewModifier {
    let k: CGFloat
    var gap: CGFloat = 0
    var folding: Bool = false
    @State private var h: CGFloat = 0

    func body(content: Content) -> some View {
        let kk: CGFloat = max(0, min(1, k))
        let moving: Bool = kk < 1
        let tall: CGFloat? = moving ? max(0, (h + gap) * kk) : nil
        let fade: Double = moving ? Grow.fade(Double(kk), folding) : 1
        let room: CGFloat = moving ? 0 : 40 // at rest the clip stands well clear, so nothing that reaches past the part is cut
        return content
            .fixedSize(horizontal: false, vertical: true)
            .background(GeometryReader { g in Color.clear.preference(key: GrowHeightKey.self, value: g.size.height) })
            .onPreferenceChange(GrowHeightKey.self) { v in if abs(v - h) > 0.5 { h = v } }
            .transformPreference(GrowHeightKey.self) { $0 = 0 }
            .padding(.bottom, gap)
            .frame(height: tall, alignment: .top)
            .opacity(fade)
            .padding(room)
            .clipped()
            .padding(-room)
    }

    /// The height follows a quintic ease and the fade a cubic one, over nearly the same time: the fade, from the height.
    static func fade(_ k: Double, _ folding: Bool) -> Double {
        if folding { return pow(max(0, k), 0.6) }
        return 1 - pow(max(0, 1 - k), 0.6)
    }
}
