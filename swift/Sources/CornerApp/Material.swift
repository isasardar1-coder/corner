// Corner for macOS - the three made skins: Metal (brushed silver, white enamel, steel), Ceramic (sage stoneware and
// glazed porcelain) and Paper (blue card and white paper). Their colours are in T (Theme.swift); this is what gives
// them depth. Every box says what it is by the Tone it is filled with (a tray, a card or pill, the strong colour,
// the accent), and is drawn accordingly: a shadow under it, then its fill, then its grain, bevel and gloss over that.
// As on Windows nothing here is blurred: a soft shadow is a few faint shapes one over the other, an edge is a
// stroke with a gradient along it, and the grains are small tiled pictures. Port of Material.cs.
// Also here: Plate, the one place every box of every skin is drawn (as RimBorder is on Windows, Glass.cs). A box
// of the Glass skin is drawn by Glass.box (Glass.swift).
import AppKit
import SwiftUI

/// The seeded System.Random of .NET (a subtractive generator), so the grains come out as they do on Windows.
struct NetRandom {
    private var a: [Int] = Array(repeating: 0, count: 56)
    private var inext: Int = 0
    private var inextp: Int = 21
    private static let big: Int = 2147483647

    init(_ seed: Int) {
        var mj: Int = 161803398 - abs(seed)
        a[55] = mj
        var mk: Int = 1
        for i in 1..<55 {
            let ii: Int = (21 * i) % 55
            a[ii] = mk
            mk = mj - mk
            if mk < 0 { mk += NetRandom.big }
            mj = a[ii]
        }
        for _ in 1..<5 {
            for i in 1..<56 {
                a[i] -= a[1 + (i + 30) % 55]
                if a[i] < 0 { a[i] += NetRandom.big }
            }
        }
    }

    /// The next number, from 0 up to (not including) 1.
    mutating func next() -> Double {
        inext += 1
        if inext >= 56 { inext = 1 }
        inextp += 1
        if inextp >= 56 { inextp = 1 }
        var r: Int = a[inext] - a[inextp]
        if r == NetRandom.big { r -= 1 }
        if r < 0 { r += NetRandom.big }
        a[inext] = r
        return Double(r) * (1.0 / Double(NetRandom.big))
    }
}

/// The outline of a box with round corners in the skin in use. The made skins draw arcs of a circle, as Windows
/// does, so that the rings of an edge lie evenly inside one another; Sage and Glass keep their smoother corners.
func boxShape(_ radius: CGFloat) -> RoundedRectangle {
    return RoundedRectangle(cornerRadius: radius, style: T.made ? .circular : .continuous)
}

/// A box being drawn: where it is in the drawing and its corner radius. Its corners are arcs of a circle.
struct MatBox {
    let rect: CGRect
    let r: CGFloat

    /// The box's outline, `inset` further in.
    func path(_ inset: CGFloat = 0) -> Path {
        let q: CGRect = rect.insetBy(dx: inset, dy: inset)
        let half: CGFloat = min(q.width, q.height) / 2
        let rr: CGFloat = max(0, min(r - inset, half))
        return Path(roundedRect: q, cornerRadius: rr, style: .circular)
    }

    /// The outline of the shadow it casts: `out` larger all round and `down` lower.
    func cast(_ down: CGFloat, _ out: CGFloat) -> Path {
        let q: CGRect = rect.insetBy(dx: -out, dy: -out).offsetBy(dx: 0, dy: down)
        let half: CGFloat = min(q.width, q.height) / 2
        return Path(roundedRect: q, cornerRadius: min(r + out, half), style: .circular)
    }
}

enum Mat {
    static let metal = 2, ceramic = 3, paper = 4

    // ------------------------------------------------------------ what a box is

    enum Role { case plain, pressed, raised, strong, accent, soft }

    static func role(_ t: Tone) -> Role {
        switch t {
        case .tray: return .pressed
        case .field, .lift, .raised, .pop, .knob, .dangerSoft: return .raised
        case .strong, .dip, .strongLift: return .strong
        case .accent, .accentOn, .main: return .accent
        case .soft: return .soft
        case .clear, .ctl: return .plain
        }
    }

    static func box(_ rect: CGRect, _ radius: CGFloat?) -> MatBox {
        let half: CGFloat = min(rect.width, rect.height) / 2
        if let r = radius { return MatBox(rect: rect, r: min(r, half)) }
        return MatBox(rect: rect, r: half) // a pill
    }

    // ------------------------------------------------------------ the grains

    /// A small picture that is laid side by side to fill a box, one of its pixels to a point.
    struct Tile {
        let image: Image
        let w: CGFloat
        let h: CGFloat
    }

    static var brushed: Tile? = nil
    static var brushedSoft: Tile? = nil
    static var grain: Tile? = nil
    static var grainSoft: Tile? = nil
    static var grainStrong: Tile? = nil
    static var speckle: Tile? = nil
    private static var grainsMade = false

    /// `v` holds a value for each pixel: above nought a touch of white, below it a touch of black, `k` times as strong.
    static func tile(_ w: Int, _ h: Int, _ v: [Double], _ k: Double) -> Tile? {
        var px = [UInt8](repeating: 0, count: w * h * 4)
        for i in 0..<(w * h) {
            let s: Double = v[i] * k
            let scaled: Double = min(255.0, abs(s) * 255.0)
            let a: UInt8 = UInt8(scaled)
            let c: UInt8 = s > 0 ? a : 0 // (the colour is stored already weighed by its strength)
            px[i * 4] = c
            px[i * 4 + 1] = c
            px[i * 4 + 2] = c
            px[i * 4 + 3] = a
        }
        guard let provider = CGDataProvider(data: Data(px) as CFData) else { return nil }
        let space: CGColorSpace = CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB()
        let info = CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue)
        guard let cg = CGImage(width: w, height: h, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: w * 4, space: space,
                               bitmapInfo: info, provider: provider, decode: nil, shouldInterpolate: false,
                               intent: .defaultIntent) else { return nil }
        let image: Image = Image(decorative: cg, scale: 1).interpolation(.none)
        return Tile(image: image, w: CGFloat(w), h: CGFloat(h))
    }

    static func makeGrains() {
        if grainsMade { return }
        grainsMade = true
        var rnd = NetRandom(7)
        // brushed metal: long fine streaks, and each row a little lighter or darker than the next
        let bw: Int = 320, bh: Int = 160, reach: Int = 34
        var rows = [Double](repeating: 0, count: bh)
        var streak = [Double](repeating: 0, count: bw * bh)
        for y in 0..<bh { rows[y] = rnd.next() * 2 - 1 }
        let spread: Double = Double(2 * reach + 1).squareRoot()
        for y in 0..<bh {
            var line = [Double](repeating: 0, count: bw)
            for x in 0..<bw { line[x] = rnd.next() * 2 - 1 }
            var sum: Double = 0
            for k in -reach...reach { sum += line[(k + bw) % bw] }
            for x in 0..<bw {
                streak[y * bw + x] = sum / spread
                sum += line[(x + reach + 1) % bw] - line[(x - reach + bw) % bw]
            }
        }
        var metalV = [Double](repeating: 0, count: bw * bh)
        for y in 0..<bh {
            let row: Double = 0.5 * (rows[y] + rows[(y + 1) % bh]) * 0.45
            for x in 0..<bw { metalV[y * bw + x] = row + streak[y * bw + x] * 0.55 }
        }
        brushed = tile(bw, bh, metalV, 0.085)
        brushedSoft = tile(bw, bh, metalV, 0.068)
        // paper: a fine even grain
        let gw: Int = 192, gh: Int = 192
        var gv = [Double](repeating: 0, count: gw * gh)
        for i in 0..<gv.count { gv[i] = rnd.next() * 2 - 1 }
        var paperV = [Double](repeating: 0, count: gw * gh)
        for y in 0..<gh {
            for x in 0..<gw {
                let beside: Double = gv[y * gw + (x + 1) % gw]
                let below: Double = gv[((y + 1) % gh) * gw + x]
                paperV[y * gw + x] = (gv[y * gw + x] + beside + below) / 3
            }
        }
        grain = tile(gw, gh, paperV, 0.06)
        grainSoft = tile(gw, gh, paperV, 0.038)
        grainStrong = tile(gw, gh, paperV, 0.075)
        // stoneware: almost smooth, with a dark or light speck here and there
        var sv = [Double](repeating: 0, count: gw * gh)
        for i in 0..<sv.count {
            let q: Double = rnd.next()
            if q < 0.0022 { sv[i] = -0.085 } else if q < 0.004 { sv[i] = 0.12 } else { sv[i] = (rnd.next() * 2 - 1) * 0.016 }
        }
        speckle = tile(gw, gh, sv, 1)
    }

    // ------------------------------------------------------------ gradients

    private static var grads: [String: Gradient] = [:]

    /// A gradient from a short description: "W.9@0 K.1@1" is white at 90% at the start and black at 10% at the
    /// end. `k` scales every strength (for rings that fade as they go in).
    static func g(_ spec: String, _ k: Double = 1) -> Gradient {
        let key: String = k == 1 ? spec : spec + " x" + String(k)
        if let found = grads[key] { return found }
        var stops: [Gradient.Stop] = []
        for part in spec.split(separator: " ") {
            guard let at = part.firstIndex(of: "@") else { continue }
            let strength: String = String(part[part.index(after: part.startIndex)..<at])
            let place: String = String(part[part.index(after: at)...])
            let a: Double = max(0, min(1, (Double("0" + strength) ?? 0) * k))
            let v: Double = part.hasPrefix("W") ? 1 : 0
            let c: Color = Color(.sRGB, red: v, green: v, blue: v, opacity: a)
            stops.append(Gradient.Stop(color: c, location: CGFloat(Double("0" + place) ?? 0)))
        }
        let made = Gradient(stops: stops)
        grads[key] = made
        return made
    }

    /// Where a gradient starts and ends across a box. `dir`: "v" top to bottom, "h" left to right, "d" top left to
    /// bottom right. A diagonal one is laid as Windows lays it: across the box's own proportions, so that the same
    /// shade runs along the other diagonal however long and narrow the box is.
    static func ends(_ dir: Character, _ box: CGRect) -> (CGPoint, CGPoint) {
        let from = CGPoint(x: box.minX, y: box.minY)
        if dir == "h" { return (from, CGPoint(x: box.maxX, y: box.minY)) }
        if dir == "d" {
            let w: CGFloat = box.width, h: CGFloat = box.height
            let q: CGFloat = 2 * w * h / max(1, w * w + h * h)
            return (from, CGPoint(x: box.minX + h * q, y: box.minY + w * q))
        }
        return (from, CGPoint(x: box.minX, y: box.maxY))
    }

    // ------------------------------------------------------------ the strokes everything is drawn with

    /// A gradient over the whole box.
    static func wash(_ ctx: GraphicsContext, _ b: MatBox, _ dir: Character, _ spec: String) {
        let e: (CGPoint, CGPoint) = ends(dir, b.rect)
        ctx.fill(b.path(), with: .linearGradient(g(spec), startPoint: e.0, endPoint: e.1))
    }

    /// A line round the box, `inset` in from its edge, with a gradient along it.
    static func ring(_ ctx: GraphicsContext, _ b: MatBox, _ dir: Character, _ spec: String, _ k: Double, _ thick: CGFloat, _ inset: CGFloat) {
        if 2 * inset + thick >= min(b.rect.width, b.rect.height) { return }
        let reach: CGFloat = inset - thick / 2
        let e: (CGPoint, CGPoint) = ends(dir, b.rect.insetBy(dx: reach, dy: reach))
        ctx.stroke(b.path(inset), with: .linearGradient(g(spec, k), startPoint: e.0, endPoint: e.1), lineWidth: thick)
    }

    /// A line round the box in black or white, `a` strong.
    static func edge(_ ctx: GraphicsContext, _ b: MatBox, _ white: Bool, _ a: Double, _ thick: CGFloat, _ inset: CGFloat) {
        if 2 * inset + thick >= min(b.rect.width, b.rect.height) { return }
        let c: Color = Color(.sRGB, white: white ? 1 : 0, opacity: a)
        ctx.stroke(b.path(inset), with: .color(c), lineWidth: thick)
    }

    /// A grain over the whole box, laid from its top left corner.
    static func grainFill(_ ctx: GraphicsContext, _ tile: Tile?, _ b: MatBox) {
        guard let t = tile else { return }
        var c = ctx
        c.clip(to: b.path())
        let img = c.resolve(t.image)
        var y: CGFloat = b.rect.minY
        while y < b.rect.maxY {
            var x: CGFloat = b.rect.minX
            while x < b.rect.maxX {
                c.draw(img, in: CGRect(x: x, y: y, width: t.w, height: t.h))
                x += t.w
            }
            y += t.h
        }
    }

    // ------------------------------------------------------------ a box

    // the shadow under a box that stands up from what it is on: (down, out, strength), nearest first
    static let metalShadow: [Double] = [1, 0, 0.26, 2, 0.6, 0.12, 3.6, 1.6, 0.05]
    static let ceramicShadow: [Double] = [1, 0, 0.10, 2.2, 0.8, 0.07, 4, 2, 0.045, 6.5, 3.5, 0.025]
    static let paperShadow: [Double] = [0.8, 0, 0.12, 2, 0.8, 0.07, 4.5, 2.2, 0.035]
    static let paperLaid: [Double] = [0.8, 0, 0.06, 2, 1, 0.03] // a tray in Paper: a sheet laid on the card
    /// How far a shadow may reach past its box: the drawing of a box that casts one is this much larger all round.
    static let room: CGFloat = 16

    static func shadow(_ skin: Int, _ role: Role) -> [Double]? {
        if role == .plain { return nil }
        if skin == metal { return role == .pressed ? nil : metalShadow }
        if skin == ceramic { return role == .pressed ? nil : ceramicShadow }
        return role == .pressed ? paperLaid : paperShadow
    }

    /// Before the box's own fill: the shadow it casts.
    static func under(_ ctx: GraphicsContext, _ skin: Int, _ role: Role, _ b: MatBox) {
        guard let sh = shadow(skin, role), b.rect.width >= 6, b.rect.height >= 6 else { return }
        let s: CGFloat = max(0.55, min(1.5, b.rect.height / 34)) // a small pill stands less high than a card
        var i: Int = 0
        while i + 2 < sh.count {
            let down: CGFloat = CGFloat(sh[i]) * s
            let out: CGFloat = CGFloat(sh[i + 1]) * s
            let c: Color = Color(.sRGB, white: 0, opacity: sh[i + 2])
            ctx.fill(b.cast(down, out), with: .color(c))
            i += 3
        }
    }

    /// After the fill: grain, bevel and gloss.
    static func over(_ ctx: GraphicsContext, _ skin: Int, _ role: Role, _ pill: Bool, _ b: MatBox) {
        if role == .plain || b.rect.width < 6 || b.rect.height < 6 { return }
        makeGrains()
        if skin == metal { metalBox(ctx, role, b) } else if skin == ceramic { ceramicBox(ctx, role, pill, b) } else { paperBox(ctx, role, b) }
    }

    static func metalBox(_ ctx: GraphicsContext, _ role: Role, _ b: MatBox) {
        if role == .pressed {
            // sunk into the plate: brushed like it, dark under its top edge, bright along its bottom one
            grainFill(ctx, brushed, b)
            for i in 0..<4 {
                let k: Double = pow(1 - Double(i) / 4.0, 1.5)
                ring(ctx, b, "v", "K.36@0 K0@.42 W0@.6 W.7@1", k, 1.2, CGFloat(i) + 0.6)
            }
        } else if role == .raised {
            // white enamel: a crisp bevel, lit from above
            wash(ctx, b, "v", "W.3@0 W0@.4 K0@.6 K.07@1")
            ring(ctx, b, "v", "W1@0 W.3@.45 K.1@.7 K.24@1", 1, 1.2, 0.6)
        } else if role == .strong {
            // dark lacquer: a hard dark edge and a thin line of light inside it
            wash(ctx, b, "v", "W.11@0 W0@.45")
            edge(ctx, b, false, 0.5, 1, 0.5)
            ring(ctx, b, "v", "W.34@0 W0@.5 W.08@1", 1, 1, 1.5)
        } else {
            // steel: brushed, with a band of light across it
            grainFill(ctx, role == .accent ? brushed : brushedSoft, b)
            wash(ctx, b, "h", "K.16@0 W.5@.28 W.12@.55 K.14@1")
            ring(ctx, b, "v", "W.85@0 W.1@.5 K.34@1", 1, 1.1, 0.55)
        }
    }

    static func ceramicBox(_ ctx: GraphicsContext, _ role: Role, _ pill: Bool, _ b: MatBox) {
        if role == .pressed {
            // pressed into the slab: a soft shade under its top edge and a soft lip of light at the bottom
            for i in 0..<6 {
                let k: Double = pow(1 - Double(i) / 6.0, 1.5)
                ring(ctx, b, "v", "K.15@0 K0@.4 W0@.62 W.55@1", k, 1.25, CGFloat(i) + 0.6)
            }
            return
        }
        let white: Bool = role == .raised
        // glaze: rounded like a pillow, so light above and shade below, and bright where it turns towards the light
        var glaze: String = "W.24@0 W0@.5 K0@.7 K.1@1"
        if white { glaze = pill ? "W.5@0 W0@.5 K0@.62 K.06@1" : "W.3@0 W0@.3 K0@.75 K.04@1" }
        wash(ctx, b, "v", glaze)
        if !white { edge(ctx, b, false, role == .strong ? 0.32 : 0.12, 1, 0.5) }
        if white {
            ring(ctx, b, "v", "W.95@0 W.3@.4 K.02@.7 K.07@1", 1, 1.4, 0.7)
        } else {
            ring(ctx, b, "v", "W.6@0 W0@.38 W0@.8 W.1@1", 1, 1.2, 1.7)
        }
    }

    static func paperBox(_ ctx: GraphicsContext, _ role: Role, _ b: MatBox) {
        // paper: a grain, stronger on the dark card, and the faintest light along a top edge
        if role != .raised || b.rect.height > 60 { // (a small white pill goes without)
            grainFill(ctx, role == .strong ? grainStrong : (role == .raised ? grainSoft : grain), b)
        }
        if role != .pressed { ring(ctx, b, "v", role == .strong ? "W.14@0 W0@.3" : "W.7@0 W0@.3", 1, 1, 0.5) }
    }

    /// The panel itself, over its own colour: its grain and its edge.
    static func sheet(_ ctx: GraphicsContext, _ skin: Int, _ b: MatBox) {
        if b.rect.width < 8 || b.rect.height < 8 { return }
        makeGrains()
        if skin == metal {
            // a brushed plate with a polished chamfer round it
            grainFill(ctx, brushed, b)
            wash(ctx, b, "d", "W.14@0 W0@.3 W.07@.6 W0@.85 K.05@1")
            ring(ctx, b, "d", "W.95@0 W.4@.5 W.8@1", 1, 1.6, 0.8)
            edge(ctx, b, false, 0.2, 1, 2.2)
            edge(ctx, b, true, 0.22, 1, 3.2)
        } else if skin == ceramic {
            // a slab with a soft rounded edge: light on the side towards the light, shade on the other
            grainFill(ctx, speckle, b)
            for i in 0..<8 {
                let k: Double = pow(1 - Double(i) / 8.0, 1.4)
                ring(ctx, b, "d", "W.6@0 W0@.42 K0@.58 K.12@1", k, 1.25, CGFloat(i) + 0.6)
            }
        } else {
            // a sheet of card
            grainFill(ctx, grain, b)
            ring(ctx, b, "v", "W.55@0 W0@.2", 1, 1, 0.5)
        }
    }
}

// ---------------------------------------------------------------- the views that draw them

/// A box of a made skin, all in one drawing: the shadow it casts, its fill, and its grain, bevel and gloss.
/// The drawing is larger than the box by the room a shadow needs.
struct MatCanvas: View {
    let tone: Tone
    let radius: CGFloat? // nil: a pill
    let skin: Int

    var body: some View {
        let role: Mat.Role = Mat.role(tone)
        let room: CGFloat = Mat.shadow(skin, role) == nil ? 0 : Mat.room
        let color: Color = tone.color
        return Canvas { ctx, size in
            let rect = CGRect(x: room, y: room, width: size.width - 2 * room, height: size.height - 2 * room)
            if rect.width < 1 || rect.height < 1 { return }
            let b: MatBox = Mat.box(rect, radius)
            Mat.under(ctx, skin, role, b)
            ctx.fill(b.path(), with: .color(color))
            Mat.over(ctx, skin, role, radius == nil, b)
        }
        .padding(-room)
        .allowsHitTesting(false)
    }
}

/// The panel itself in a made skin: its grain and its edge, over the panel's colour (PanelBackground, CardView.swift).
struct SheetGrain: View {
    let skin: Int
    let radius: CGFloat

    var body: some View {
        Canvas { ctx, size in
            let rect = CGRect(x: 0, y: 0, width: size.width, height: size.height)
            Mat.sheet(ctx, skin, Mat.box(rect, radius))
        }
        .allowsHitTesting(false)
    }
}

/// What is behind a box: its colour as a pill, a disc or a box with round corners, and in a made skin its depth too.
struct PlateGround: View {
    let tone: Tone
    let radius: CGFloat? // nil: a pill
    let disc: Bool       // a pill as wide as it is high: drawn as the circle it was before there were made skins
    let skin: Int
    var dark: Bool = false // Glass: dark glass

    var body: some View {
        ZStack {
            if skin == 1 {
                // Glass: a thin layer of white with a glow and a bright rim; a pill is glossy across its upper half
                if tone != .clear { GlassPlate(tone: tone, radius: radius, dark: dark) }
            } else if let r = radius {
                RoundedRectangle(cornerRadius: r, style: skin >= 2 ? .circular : .continuous).fill(tone.color)
            } else if disc {
                Circle().fill(tone.color)
            } else {
                Capsule().fill(tone.color)
            }
            if skin >= 2 && Mat.role(tone) != .plain {
                MatCanvas(tone: tone, radius: radius, skin: skin)
            }
        }
    }
}

/// The one place a box is drawn, in every skin. Sage: its colour. Glass: its colour, its glow, its bright rim and a
/// pill's gloss (Glass.swift). Metal, Ceramic, Paper: its shadow, colour, grain, bevel and gloss, by what its tone
/// says it is.
struct Plate: ViewModifier {
    let tone: Tone
    let radius: CGFloat? // nil: a pill
    let rim: CGFloat?    // Glass: for a box with no fill of its own (.clear), the radius of the glow and rim over it
    let disc: Bool
    let skin: Int        // (kept, so that a change of skin draws the box again)
    let dark: Bool       // (and a change from light glass to dark)

    func body(content: Content) -> some View {
        content
            .background(PlateGround(tone: tone, radius: radius, disc: disc, skin: skin, dark: dark))
            .overlay(GlassEdge(radius: rim ?? 0, on: skin == 1 && tone == .clear && rim != nil, dark: dark))
    }
}

extension View {
    /// Puts the view in a box filled with `tone`: a pill (a round one with `disc`), or with `radius` a box with
    /// round corners.
    func plate(_ tone: Tone, radius: CGFloat? = nil, rim: CGFloat? = nil, disc: Bool = false) -> some View {
        return modifier(Plate(tone: tone, radius: radius, rim: rim, disc: disc, skin: T.skin, dark: T.dark))
    }
}
