// Corner for macOS - the shared controls: icons from path strings, buttons (always pills), chips, switches,
// segmented choices, energy bars, steppers, boxes to type in, a wrapping row and a scroll view that fits its content.
// Port of the controls in Ui.cs and Corner.cs.
import AppKit
import SwiftUI
import CornerCore

// ---------------------------------------------------------------- icons

/// Reads the small subset of SVG/WPF path syntax Corner's icons use: M L H V C S A Z, absolute coordinates.
enum PathParser {
    private static var cache: [String: Path] = [:]

    static func tokens(_ d: String) -> [String] {
        var out: [String] = []
        var cur = ""
        for ch in d {
            if ch.isLetter && ch != "e" && ch != "E" {
                if !cur.isEmpty { out.append(cur); cur = "" }
                out.append(String(ch))
            } else if ch == " " || ch == "," {
                if !cur.isEmpty { out.append(cur); cur = "" }
            } else if ch == "-" && !cur.isEmpty && !cur.hasSuffix("e") {
                out.append(cur); cur = "-"
            } else {
                cur.append(ch)
            }
        }
        if !cur.isEmpty { out.append(cur) }
        return out
    }

    static func parse(_ d: String) -> Path {
        if let p = cache[d] { return p }
        let t = tokens(d)
        var p = Path()
        var i = 0
        var cmd: Character = "M"
        var cur = CGPoint.zero
        var start = CGPoint.zero
        var lastCtrl: CGPoint? = nil
        func num() -> CGFloat {
            if i >= t.count { return 0 }
            let v = Double(t[i]) ?? 0
            i += 1
            return CGFloat(v)
        }
        while i < t.count {
            if let c = t[i].first, c.isLetter {
                cmd = c
                i += 1
                if cmd == "Z" || cmd == "z" {
                    p.closeSubpath()
                    cur = start
                    lastCtrl = nil
                    continue
                }
                if i >= t.count { break }
            }
            switch cmd {
            case "M":
                cur = CGPoint(x: num(), y: num())
                p.move(to: cur)
                start = cur
                cmd = "L"
                lastCtrl = nil
            case "L":
                cur = CGPoint(x: num(), y: num())
                p.addLine(to: cur)
                lastCtrl = nil
            case "H":
                cur = CGPoint(x: num(), y: cur.y)
                p.addLine(to: cur)
                lastCtrl = nil
            case "V":
                cur = CGPoint(x: cur.x, y: num())
                p.addLine(to: cur)
                lastCtrl = nil
            case "C":
                let c1 = CGPoint(x: num(), y: num())
                let c2 = CGPoint(x: num(), y: num())
                let e = CGPoint(x: num(), y: num())
                p.addCurve(to: e, control1: c1, control2: c2)
                lastCtrl = c2
                cur = e
            case "S":
                var c1 = cur
                if let lc = lastCtrl { c1 = CGPoint(x: 2 * cur.x - lc.x, y: 2 * cur.y - lc.y) }
                let c2 = CGPoint(x: num(), y: num())
                let e = CGPoint(x: num(), y: num())
                p.addCurve(to: e, control1: c1, control2: c2)
                lastCtrl = c2
                cur = e
            case "A":
                let rx = num()
                _ = num() // ry: every arc in Corner is circular
                _ = num() // rotation
                let large = num() > 0.5
                let sweep = num() > 0.5
                let e = CGPoint(x: num(), y: num())
                for q in arc(cur, e, rx, large, sweep) { p.addLine(to: q) }
                cur = e
                lastCtrl = nil
            default:
                i += 1
            }
        }
        cache[d] = p
        return p
    }

    /// A circular arc as short lines (SVG's endpoint form, y pointing down; sweep 1 = clockwise on screen).
    static func arc(_ p0: CGPoint, _ p1: CGPoint, _ rIn: CGFloat, _ large: Bool, _ sweep: Bool) -> [CGPoint] {
        let x1p = Double(p0.x - p1.x) / 2
        let y1p = Double(p0.y - p1.y) / 2
        var r = Double(rIn)
        if r <= 0 { return [p1] }
        let lam = (x1p * x1p + y1p * y1p) / (r * r)
        if lam > 1 { r *= lam.squareRoot() }
        let r2 = r * r
        let d2 = x1p * x1p + y1p * y1p
        var coef = 0.0
        if d2 > 0 { coef = max(0, (r2 - d2) / d2).squareRoot() }
        if large == sweep { coef = -coef }
        let cxp = coef * y1p
        let cyp = -coef * x1p
        let cx = cxp + Double(p0.x + p1.x) / 2
        let cy = cyp + Double(p0.y + p1.y) / 2
        let th1 = atan2((y1p - cyp) / r, (x1p - cxp) / r)
        var dth = atan2((-y1p - cyp) / r, (-x1p - cxp) / r) - th1
        if !sweep && dth > 0 { dth -= 2 * Double.pi }
        if sweep && dth < 0 { dth += 2 * Double.pi }
        let n = max(4, Int(abs(dth) / (Double.pi / 24)))
        var pts: [CGPoint] = []
        for k in 1...n {
            let a = th1 + dth * Double(k) / Double(n)
            pts.append(CGPoint(x: cx + r * cos(a), y: cy + r * sin(a)))
        }
        pts[pts.count - 1] = p1
        return pts
    }
}

struct GlyphShape: Shape {
    let d: String
    func path(in rect: CGRect) -> Path { return PathParser.parse(d) }
}

/// An icon drawn with round ends, in a box of w x h (the path's own coordinates).
struct Glyph: View {
    let d: String
    let color: Color
    var th: CGFloat = 1.4
    var w: CGFloat = 10
    var h: CGFloat = 10
    var fill: Color? = nil

    var body: some View {
        ZStack {
            if let f = fill { GlyphShape(d: d).fill(f) }
            GlyphShape(d: d).stroke(color, style: StrokeStyle(lineWidth: th, lineCap: .round, lineJoin: .round))
        }
        .frame(width: w, height: h)
    }
}

enum Icons {
    static let cross = "M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5"
    static let plus = "M7,2.5 V11.5 M2.5,7 H11.5"
    static let gear = "M6,4.2 A1.8,1.8 0 1 1 5.99,4.2 Z M6,0.8 V2.2 M6,9.8 V11.2 M0.8,6 H2.2 M9.8,6 H11.2 M2.3,2.3 L3.3,3.3 M8.7,8.7 L9.7,9.7 M2.3,9.7 L3.3,8.7 M8.7,3.3 L9.7,2.3"
    static let full = "M1.5,4 V1.5 H4 M6,1.5 H8.5 V4 M8.5,6 V8.5 H6 M4,8.5 H1.5 V6"
    static let back = "M8.5,5 H1.5 M4.5,2 L1.5,5 L4.5,8"
    static let bell = "M5,1.2 C3.3,1.2 2.3,2.6 2.3,4.3 V6.2 L1.3,7.8 H8.7 L7.7,6.2 V4.3 C7.7,2.6 6.7,1.2 5,1.2 Z M4.2,9.3 H5.8"
    static let clock = "M6,1 A5,5 0 1 1 5.99,1 Z M6,3.4 V6 L7.9,7.1"
    static let chevron = "M2.5,1 L5.5,4 L2.5,7"
    static let tick = "M5,9.4 L7.9,12.2 L13.2,6.2"
    static let wave = "M1.5,9 C3.5,1 5.5,1 7,7 S10.5,12 12.5,4"
    static let lines = "M2.5,4 H11.5 M2.5,7 H11.5 M2.5,10 H11.5"
    static let left = "M8.5,3.5 L5,7 L8.5,10.5"
    static let down = "M3.5,5.5 L7,9 L10.5,5.5"
    static let right = "M5.5,3.5 L9,7 L5.5,10.5"
    static let play = "M2,1.5 L7.5,5 L2,8.5 Z"
    static let pause = "M2.5,1.5 V8.5 M6.5,1.5 V8.5"
    static let moon = "M11.5,8.5 A5,5 0 0 1 5.5,2.5 A5,5 0 1 0 11.5,8.5 Z"
    static let debt = "M7,2 A5,5 0 1 1 6.99,2 Z M4.5,7 H9.5"
    static let bed = "M1.5,10.5 V4.5 M1.5,8 H12.5 V10.5 M12.5,8 V6.5 A2,2 0 0 0 10.5,4.5 H6 V8"
    static let smallTick = "M1.5,5.5 L4,8 L8.5,2"
    static let smallPlus = "M5,1 V9 M1,5 H9"
}

// ---------------------------------------------------------------- buttons, chips, switches

/// A button, always a pill. kind: 0 = the main action (T.main), 1 = secondary (a plain pill), 2 = quiet (text only).
/// on: what it sits on: 0 = the panel or a tray, 1 = white, 2 = the dark prompt.
struct Btn: View {
    let text: String
    let kind: Int
    var on: Int = 0
    var stretch: Bool = false
    let action: () -> Void
    @Environment(\.m) var m
    @State private var hot = false

    var ink: Color {
        if kind == 0 { return T.onMain }
        if on == 2 { return kind == 1 ? T.onStrong : (hot ? T.onStrong : T.onStrongSoft) }
        if kind == 1 { return T.text }
        return hot ? T.text : T.muted
    }
    var ground: Tone {
        if kind == 0 { return .main }
        if kind == 2 { return .clear }
        if on == 2 { return hot ? .strongLift : .clear }
        if on == 1 { return hot ? .ctl : .raised }
        return hot ? .lift : .field
    }

    var body: some View {
        let quiet = kind == 2
        let hp: CGFloat = m.slim ? (quiet ? 5 : 12) : (quiet ? 6 : 15)
        let vp: CGFloat = m.slim ? 5 : 7
        Text(text)
            .font(m.font(11.5, kind == 0))
            .foregroundColor(ink)
            .lineLimit(1)
            .fixedSize()
            .frame(maxWidth: stretch ? .infinity : nil)
            .padding(.horizontal, hp)
            .padding(.top, vp)
            .padding(.bottom, vp + 1)
            .plate(ground)
            .overlay(Capsule().strokeBorder(kind == 1 && on == 2 ? T.strongLine : Color.clear, lineWidth: 1))
            .opacity(kind == 0 && hot ? 0.88 : 1)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

/// A chip: a white pill, forest green while what it stands for is open or on.
struct Chip<Content: View>: View {
    let on: Bool
    var width: CGFloat? = nil
    let action: () -> Void
    @ViewBuilder let content: () -> Content
    @State private var hot = false

    var body: some View {
        content()
            .frame(width: width, height: 30)
            .frame(maxWidth: width == nil ? .infinity : nil)
            .plate(on ? .strong : (hot ? .lift : .field))
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

/// An on/off switch. On, its track is the colour of the main button and of a chosen tab, with the knob in that
/// colour's text colour. It is flat in every skin.
struct Toggle2: View {
    let on: Bool
    let action: () -> Void
    var body: some View {
        ZStack(alignment: on ? .trailing : .leading) {
            Capsule().fill(on ? T.main : T.ctl)
            Circle().fill(on ? T.onMain : T.knob).frame(width: 14, height: 14).padding(.horizontal, 3)
        }
        .frame(width: 36, height: 20)
        .contentShape(Capsule())
        .onTapGesture { Click.play(); action() } // (Sound.swift)
    }
}

/// One choice out of a few, as joined segments; the chosen one is the colour of the main button. Drawn for a
/// white surface.
struct Segmented: View {
    let names: [String]
    let chosen: Int
    let pick: (Int) -> Void
    @Environment(\.m) var m

    var body: some View {
        HStack(spacing: 0) {
            ForEach(0..<names.count, id: \.self) { i in
                Text(names[i])
                    .font(m.font(11))
                    .foregroundColor(i == chosen ? T.onMain : T.muted)
                    .fixedSize()
                    .padding(.horizontal, 11)
                    .padding(.top, 3)
                    .padding(.bottom, 4)
                    .plate(i == chosen ? .main : .clear)
                    .contentShape(Capsule())
                    .onTapGesture { if i != chosen { pick(i) } }
            }
        }
        .padding(3)
        .plate(.raised)
    }
}

/// A small capital heading for a group.
struct Caps: View {
    let text: String
    var color: Color? = nil
    @Environment(\.m) var m
    var body: some View {
        Text(text.uppercased()).font(m.font(10, true)).foregroundColor(color ?? T.muted).lineLimit(1)
    }
}

/// A thin rule, for white surfaces.
struct Rule: View {
    var color: Color? = nil
    var body: some View { Rectangle().fill(color ?? T.line).frame(height: 1) }
}

/// Energy as one to three bars (0 = not set: three empty bars).
struct Bars: View {
    let level: Int
    var onColor: Color? = nil
    var body: some View {
        HStack(alignment: .bottom, spacing: 2) {
            ForEach(0..<3, id: \.self) { i in
                RoundedRectangle(cornerRadius: 1)
                    .fill(i < level ? (onColor ?? T.strong) : T.ctl)
                    .frame(width: 3, height: [4.0, 7.0, 11.0][i])
            }
        }
        .frame(width: 13, height: 11, alignment: .bottom)
    }
}

/// A small round-ended button, for the - and + steppers. Drawn for a white surface.
struct StepBtn: View {
    let sign: String
    var height: CGFloat = 20
    let action: () -> Void
    @Environment(\.m) var m
    @State private var hot = false
    var body: some View {
        Text(sign)
            .font(m.font(12))
            .foregroundColor(T.text)
            .frame(width: 26, height: height)
            .plate(hot ? .ctl : .raised)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

/// A pale pill to pick a date with (Today, Tomorrow, ...).
struct DatePill: View {
    let text: String
    let action: () -> Void
    @Environment(\.m) var m
    @State private var hot = false
    var body: some View {
        Text(text)
            .font(m.font(11.5))
            .foregroundColor(T.text)
            .fixedSize()
            .padding(.leading, m.slim ? 9 : 11)
            .padding(.trailing, m.slim ? 8 : 10)
            .padding(.top, m.slim ? 3 : 4)
            .padding(.bottom, m.slim ? 4 : 5)
            .plate(hot ? .ctl : .raised)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

/// A round button holding one icon (the cross, the gear ...) that turns red or dark under the mouse.
struct IconHit: View {
    let d: String
    var color: Color? = nil
    var hotColor: Color? = nil
    var size: CGFloat = 10
    var box: CGFloat = 16
    var th: CGFloat = 1.4
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Glyph(d: d, color: hot ? (hotColor ?? T.text) : (color ?? T.muted), th: th, w: size, h: size)
            .frame(width: box, height: box)
            .contentShape(Rectangle())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

/// A setting on one line: what it is on the left, its control on the right.
struct SettingLine<Control: View>: View {
    let title: String
    var sub: String? = nil
    @ViewBuilder let control: () -> Control
    @Environment(\.m) var m
    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(m.font(12.5)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
                if let s = sub, !s.isEmpty {
                    Text(s).font(m.font(10.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
            control()
        }
        .padding(.vertical, m.sp(10, 9))
    }
}

// ---------------------------------------------------------------- boxes to type in

/// A plain text box with a pale hint while it is empty.
struct HintField: View {
    let hint: String
    @Binding var text: String
    var size: CGFloat = 12
    var color: Color? = nil
    var focus: FocusState<Bool>.Binding? = nil
    var onSubmit: () -> Void = {}
    var onEscape: () -> Void = {}
    @Environment(\.m) var m

    var body: some View {
        ZStack(alignment: .leading) {
            if text.isEmpty {
                Text(hint).font(m.font(size)).foregroundColor(T.faint).lineLimit(1).allowsHitTesting(false)
            }
            field
        }
    }

    @ViewBuilder var field: some View {
        if let f = focus {
            TextField("", text: $text)
                .textFieldStyle(.plain)
                .font(m.font(size))
                .foregroundColor(color ?? T.text)
                .focused(f)
                .onSubmit(onSubmit)
                .onExitCommand(perform: onEscape)
        } else {
            TextField("", text: $text)
                .textFieldStyle(.plain)
                .font(m.font(size))
                .foregroundColor(color ?? T.text)
                .onSubmit(onSubmit)
                .onExitCommand(perform: onEscape)
        }
    }
}

/// A clock-time box that takes digits and ":" only, with an am/pm switch beside it that follows what is typed
/// (a bedtime of 11 is pm, of 1 is am) until it is clicked. Port of TimeEntry in Energy.cs.
final class TimeEntryModel: ObservableObject, Identifiable {
    let id = UUID()
    let hint: String
    let waking: Bool
    let daytime: Bool
    @Published var pm: Bool
    @Published var bad = false
    var manual = false
    @Published var text: String = "" {
        didSet {
            let clean = String(text.filter { $0.isNumber || $0 == ":" }.prefix(5))
            if clean != text { text = clean }
            bad = false
            if !manual, let r = read() {
                let h = r.0
                if daytime { pm = h >= 12 || h <= 6 } else if waking { pm = h >= 12 } else { pm = (h >= 6 && h <= 11) || h >= 13 }
            }
        }
    }

    init(_ hint: String, waking: Bool, daytime: Bool = false) {
        self.hint = hint
        self.waking = waking
        self.daytime = daytime
        self.pm = !waking
    }

    func read() -> (Int, Int)? {
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        var h = 0, mi = 0
        if parts.count == 2 {
            guard parts[0].count >= 1, parts[0].count <= 2, parts[1].count == 2, let a = Int(parts[0]), let b = Int(parts[1]) else { return nil }
            h = a; mi = b
        } else if parts.count == 1 {
            let s = String(parts[0])
            if s.count >= 1 && s.count <= 2 { guard let a = Int(s) else { return nil }; h = a }
            else if s.count == 3 || s.count == 4 {
                guard let a = Int(s.prefix(s.count - 2)), let b = Int(s.suffix(2)) else { return nil }
                h = a; mi = b
            } else { return nil }
        } else { return nil }
        return h <= 23 && mi <= 59 ? (h, mi) : nil
    }

    /// Seconds since midnight, or nil when what is typed is not a time.
    func get() -> TimeInterval? {
        guard let r = read() else { return nil }
        var h = r.0
        let mi = r.1
        if h >= 1 && h <= 12 {
            if pm { if h != 12 { h += 12 } } else if h == 12 { h = 0 }
        }
        return TimeInterval(h * 3600 + mi * 60)
    }

    func fill(_ d: Date) {
        manual = true
        pm = d.hour >= 12
        var h = d.hour % 12
        if h == 0 { h = 12 }
        text = String(h) + ":" + (d.minute < 10 ? "0" : "") + String(d.minute)
    }
}

struct TimeEntryView: View {
    @ObservedObject var e: TimeEntryModel
    var surface: Tone? = nil
    var onSubmit: () -> Void = {}
    @Environment(\.m) var m
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: 0) {
            HintField(hint: e.hint, text: $e.text, size: 12.5, color: e.bad ? T.danger : T.text, focus: $focused, onSubmit: onSubmit)
                .frame(width: 44)
            Text(e.pm ? "pm" : "am")
                .font(m.font(11, true))
                .foregroundColor(T.strong)
                .padding(.leading, 5)
                .padding(.trailing, 2)
                .contentShape(Rectangle())
                .onTapGesture { e.manual = true; e.pm.toggle() }
        }
        .padding(.leading, 10)
        .padding(.trailing, 7)
        .padding(.vertical, 5)
        .frame(width: 92, alignment: .leading)
        .plate(surface ?? .field)
        .overlay(Capsule().strokeBorder(focused ? T.strong : Color.clear, lineWidth: 1.5))
    }
}

/// A small box for a number of minutes.
struct MiniBox: View {
    @Binding var text: String
    var width: CGFloat = 24
    var bad: Bool = false
    var surface: Tone? = nil
    var onSubmit: () -> Void = {}
    @Environment(\.m) var m

    var body: some View {
        TextField("", text: Binding(get: { text }, set: { v in text = String(v.filter { $0.isNumber }.prefix(3)) }))
            .textFieldStyle(.plain)
            .font(m.font(11.5))
            .foregroundColor(bad ? T.danger : T.text)
            .frame(width: width)
            .onSubmit(onSubmit)
            .padding(.leading, 10)
            .padding(.trailing, 9)
            .padding(.vertical, 5)
            .plate(surface ?? .field)
    }
}

// ---------------------------------------------------------------- layout helpers

/// Items laid out left to right, wrapping onto new lines (WPF's WrapPanel). Works on macOS 12.
struct FlowRow: View {
    let items: [AnyView]
    var hs: CGFloat = 6
    var vs: CGFloat = 6
    @State private var height: CGFloat = 0

    var body: some View {
        GeometryReader { g in
            self.content(g.size.width)
        }
        .frame(height: height)
    }

    private func content(_ width: CGFloat) -> some View {
        var x: CGFloat = 0
        var y: CGFloat = 0
        let last = items.count - 1
        return ZStack(alignment: .topLeading) {
            ForEach(0..<items.count, id: \.self) { i in
                items[i]
                    .padding(.trailing, hs)
                    .padding(.bottom, vs)
                    .alignmentGuide(.leading) { d in
                        if abs(x - d.width) > width && x != 0 { x = 0; y -= d.height }
                        let r = x
                        if i == last { x = 0 } else { x -= d.width }
                        return r
                    }
                    .alignmentGuide(.top) { _ in
                        let r = y
                        if i == last { y = 0 }
                        return r
                    }
            }
        }
        .background(GeometryReader { g in Color.clear.preference(key: FlowHeightKey.self, value: g.size.height) })
        .onPreferenceChange(FlowHeightKey.self) { h in if abs(h - height) > 0.5 { height = h } }
    }
}

struct FlowHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

struct FitHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

/// Test renders draw a plain clipped box instead of a scroll view: a bitmap of the window (cacheDisplay) does not
/// clip a scroll view's content the way the screen does.
enum FitScrollFlag { static var plain = false }

/// What a FitScroll is cut off with: its box, or one with round corners a little in from the sides.
struct FitClip: Shape {
    let radius: CGFloat
    let inset: CGFloat
    let style: RoundedCornerStyle
    func path(in r: CGRect) -> Path {
        if radius <= 0 { return Path(r) }
        return Path(roundedRect: r.insetBy(dx: inset, dy: 0), cornerRadius: radius, style: style)
    }
}

/// A vertical scroll view as tall as its content, up to maxHeight.
/// What is inside is made again when the skin changes (its id is the skin's look), so that every part of it is drawn in
/// the new colours; the scroll view itself stays, and with it how far it is scrolled.
struct FitScroll<Content: View>: View {
    let maxHeight: CGFloat
    var round: CGFloat = 0 // when it scrolls, what is seen of it is cut off with round corners of this size (a card that scrolls)
    var inset: CGFloat = 0 // and this far in from the sides
    var skin: Int = T.skin // (kept: a FitScroll that is otherwise the same as before is not looked at again)
    var look: Int = T.look // (the same number, but another when Glass goes from light to dark)
    @ViewBuilder let content: () -> Content
    @State private var h: CGFloat = 0

    var body: some View {
        // A made skin: the last box casts a shadow below itself (Material.swift), which needs room inside what is
        // cut off. When everything fits, the view reaches that much further down, into the margin under it; when
        // it scrolls, the room is at the end of what scrolls.
        let room: CGFloat = skin >= 2 ? 8 : 0
        let over: CGFloat = h <= maxHeight ? room : 0
        let cut: CGFloat = h > maxHeight ? round : 0
        let clip = FitClip(radius: cut, inset: cut > 0 ? inset : 0, style: skin >= 2 ? .circular : .continuous)
        Group {
            if FitScrollFlag.plain {
                VStack(spacing: 0) {
                    content()
                        .id(look)
                        .fixedSize(horizontal: false, vertical: true)
                        .background(GeometryReader { g in Color.clear.preference(key: FitHeightKey.self, value: g.size.height) })
                    Spacer(minLength: 0)
                }
                .frame(height: max(1, min(h, maxHeight)), alignment: .top)
                .padding(.bottom, over)
                .clipShape(clip)
                .padding(.bottom, -over)
            } else {
                ScrollView(.vertical, showsIndicators: false) {
                    content()
                        .id(look)
                        .background(GeometryReader { g in Color.clear.preference(key: FitHeightKey.self, value: g.size.height) })
                        .padding(.bottom, room)
                }
                .frame(height: max(1, min(h, maxHeight)) + over)
                .clipShape(clip)
                .padding(.bottom, -over)
            }
        }
        .onPreferenceChange(FitHeightKey.self) { v in if abs(v - h) > 0.5 { h = v } }
        .transformPreference(FitHeightKey.self) { $0 = 0 }
    }
}

/// A box with the panel's corner radius: a tray (.tray) or a white card (.field).
struct CardBox<Content: View>: View {
    let surface: Tone
    var padded: Bool = true
    @ViewBuilder let content: () -> Content
    @Environment(\.m) var m
    var body: some View {
        VStack(alignment: .leading, spacing: 0) { content() }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, padded ? m.padX : 0)
            .padding(.vertical, padded ? m.padY : 0)
            .plate(surface, radius: m.radius, rim: m.radius)
    }
}

extension View {
    func any() -> AnyView { return AnyView(self) }

    /// The tooltip, where the system offers one.
    func tip(_ text: String?) -> some View {
        return help(text ?? "")
    }
}
