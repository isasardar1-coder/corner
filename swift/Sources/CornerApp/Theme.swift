// Corner for macOS - colours (T, the two skins Sage and Glass), the font (Gilroy, bundled), and the grid both modes
// are drawn on (M: text sizes, insets, gaps). Port of T in Corner.cs and the grid in Ui.cs.
import AppKit
import SwiftUI
import CoreText
import CornerCore

extension String {
    /// C# Trim().
    var trimmed: String { return trimmingCharacters(in: .whitespacesAndNewlines) }
}

extension Color {
    /// "#RRGGBB" or WPF's "#AARRGGBB".
    init(hex: String) {
        var s = hex
        if s.hasPrefix("#") { s.removeFirst() }
        let v = UInt64(s, radix: 16) ?? 0
        let a: Double, r: Double, g: Double, b: Double
        if s.count == 8 {
            a = Double((v >> 24) & 0xFF) / 255
            r = Double((v >> 16) & 0xFF) / 255
            g = Double((v >> 8) & 0xFF) / 255
            b = Double(v & 0xFF) / 255
        } else {
            a = 1
            r = Double((v >> 16) & 0xFF) / 255
            g = Double((v >> 8) & 0xFF) / 255
            b = Double(v & 0xFF) / 255
        }
        self.init(.sRGB, red: r, green: g, blue: b, opacity: a)
    }
}

/// The colours, each named for what it is used for, with a value for Sage and one for Glass.
enum T {
    static let names = ["Sage", "Glass"]
    static var skin = 0
    static var glass: Bool { return skin == 1 }

    private static var cache: [String: Color] = [:]
    private static func p(_ sage: String, _ glass: String) -> Color {
        let key = T.glass ? glass : sage
        if let c = cache[key] { return c }
        let c = Color(hex: key)
        cache[key] = c
        return c
    }

    static func use(_ s: Int) { skin = s == 1 ? 1 : 0 }

    // surfaces, from the back forwards
    static var bg: Color { return p("#E8ECDB", "#9EEDF7FA") }
    static var edge: Color { return p("#D3D9C2", "#FFD9E7F0") }
    static var tray: Color { return p("#D9DFC7", "#73C3D0DD") }
    static var field: Color { return p("#FFFFFF", "#D1F7FAFD") }
    static var lift: Color { return p("#EEF1E4", "#B8F7FBFF") }
    static var raised: Color { return p("#E8ECDB", "#E6E4EEF4") }
    static var ctl: Color { return p("#C6CFB1", "#999AAFBD") }
    static var line: Color { return p("#E3E7D6", "#C9C9D9E3") }
    static var hover: Color { return p("#0F16281D", "#301F5268") }
    static var ring: Color { return p("#D9DFC7", "#B3A9BFD0") }
    static var pop: Color { return p("#FFFFFF", "#F7F7FAFD") }
    // text
    static var text: Color { return p("#141A12", "#18232D") }
    static var muted: Color { return p("#535C4B", "#465564") }
    static var faint: Color { return p("#586150", "#4D5C6A") }
    // the dark colour
    static var strong: Color { return p("#16281D", "#F21A2A38") }
    static var onStrong: Color { return p("#FFFFFF", "#F8FCFF") }
    static var onStrongSoft: Color { return p("#C9D3BE", "#D2E0EA") }
    static var strongLine: Color { return p("#7C8C7D", "#A77E9AAA") }
    static var strongLift: Color { return p("#24392C", "#A8385266") }
    // the accent
    static var accent: Color { return p("#B4C12A", "#A9D5FF") }
    static var onAccent: Color { return p("#141A12", "#142638") }
    static var soft: Color { return p("#EEF2B4", "#CDE1F2FF") }
    static var sand: Color { return p("#CDC6AB", "#CCD8D5C9") }
    static var danger: Color { return p("#A8401F", "#C04435") }
    static var dangerSoft: Color { return p("#F4DDD3", "#DDF3D7D4") }

    /// Glass only: the tint laid over the frosted backdrop, and the light on its top edge.
    static var glassTint: LinearGradient {
        return LinearGradient(colors: [Color(hex: "#9EEDF7FA"), Color(hex: "#9EF2F3F8")], startPoint: .topLeading, endPoint: .bottomTrailing)
    }
    static var glassSheen: LinearGradient {
        return LinearGradient(colors: [Color(hex: "#70FFFFFF"), Color(hex: "#00FFFFFF")], startPoint: .top, endPoint: .bottom)
    }
    static var glassRim: LinearGradient {
        return LinearGradient(colors: [Color(hex: "#D9FFFFFF"), Color(hex: "#85F9FFFF"), Color(hex: "#B6FFFFFF")], startPoint: .topLeading, endPoint: .bottomTrailing)
    }
    /// The panel's shadow.
    static var shadow: Color {
        return glass ? Color(hex: "#101923").opacity(0.3) : Color(.sRGB, red: 60.0 / 255, green: 54.0 / 255, blue: 34.0 / 255, opacity: 0.3)
    }
    /// The sand backdrop behind the test renders.
    static let backdrop = Color(.sRGB, red: 185.0 / 255, green: 177.0 / 255, blue: 151.0 / 255, opacity: 1)
}

/// Gilroy, bundled with the app and registered for this process. The system font when the files are missing.
enum Fonts {
    static var medium: String? = nil
    static var semi: String? = nil
    static var italic: String? = nil
    /// Gilroy looks a touch larger than the system font at the same size.
    static var k: CGFloat = 1

    static func register() {
        let files = [("Gilroy-Medium", 0), ("Gilroy-SemiBold", 1), ("Gilroy-SemiBoldItalic", 2)]
        for (name, which) in files {
            guard let url = Resources.url(name, "ttf") else { continue }
            var err: Unmanaged<CFError>? = nil
            _ = CTFontManagerRegisterFontsForURL(url as CFURL, .process, &err)
            var ps: String? = nil
            if let arr = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor], let d = arr.first {
                ps = CTFontDescriptorCopyAttribute(d, kCTFontNameAttribute) as? String
            }
            if ps == nil { ps = name }
            if let p = ps, NSFont(name: p, size: 12) == nil { continue }
            switch which {
            case 0: medium = ps
            case 1: semi = ps
            default: italic = ps
            }
        }
        if medium != nil && semi != nil { k = 0.97 } else { medium = nil; semi = nil; italic = nil }
    }

    static func ui(_ size: CGFloat, _ semibold: Bool = false) -> Font {
        if let n = semibold ? semi : medium { return Font.custom(n, fixedSize: size) }
        return Font.system(size: size, weight: semibold ? .semibold : .regular)
    }

    /// The one italic: "Overdue".
    static func lean(_ size: CGFloat) -> Font {
        if let n = italic { return Font.custom(n, fixedSize: size) }
        return Font.system(size: size, weight: .semibold).italic()
    }

    static func ns(_ size: CGFloat, _ semibold: Bool = false) -> NSFont {
        if let n = semibold ? semi : medium, let f = NSFont(name: n, size: size) { return f }
        return NSFont.systemFont(ofSize: size, weight: semibold ? .semibold : .regular)
    }

    /// How wide a piece of text is in the chosen font.
    static func width(_ text: String, _ size: CGFloat, _ semibold: Bool = false) -> CGFloat {
        let a = NSAttributedString(string: text, attributes: [.font: ns(size, semibold)])
        return ceil(a.size().width)
    }
}

/// Where the bundled files are: Corner.app/Contents/Resources, or SwiftPM's resource bundle beside a bare executable.
enum Resources {
    static func url(_ name: String, _ ext: String) -> URL? {
        let fm = FileManager.default
        var dirs: [URL] = []
        if let r = Bundle.main.resourceURL { dirs.append(r) }
        if let exe = Bundle.main.executableURL?.resolvingSymlinksInPath().deletingLastPathComponent() {
            let b = exe.appendingPathComponent("Corner_CornerApp.bundle")
            dirs.append(b.appendingPathComponent("Contents/Resources"))
            dirs.append(b)
            dirs.append(b.appendingPathComponent("Resources"))
            dirs.append(exe)
        }
        for d in dirs {
            let u = d.appendingPathComponent(name + "." + ext)
            if fm.fileExists(atPath: u.path) { return u }
        }
        return nil
    }
}

// ---------------------------------------------------------------- the grid

let cardW: CGFloat = 312      // the corner panel
let fullW: CGFloat = 800      // Full Mode: two equal columns of fullW / 2
let fullMinH: CGFloat = 540
let rowOver: CGFloat = 6      // how far a task row's hover highlight reaches past its content
let shadowM: CGFloat = 30     // room round the card for its shadow, inside the window

/// The grid for the mode being shown. The corner panel (slim) is tighter and snaps text to four sizes.
struct M {
    var slim: Bool

    func z(_ size: CGFloat) -> CGFloat {
        let step: CGFloat
        if !slim { step = size }
        else if size <= 10 { step = 10 }
        else if size <= 11.5 { step = 11 }
        else if size <= 13.5 { step = 12 }
        else if size <= 16 { step = 13.5 }
        else { step = size }
        return (step * Fonts.k * 2).rounded() / 2
    }
    func sp(_ roomy: CGFloat, _ slimV: CGFloat) -> CGFloat { return slim ? slimV : roomy }
    var side: CGFloat { return slim ? 12 : 18 }
    var padX: CGFloat { return slim ? 12 : 14 }
    var padY: CGFloat { return slim ? 11 : 13 }
    var gapY: CGFloat { return slim ? 8 : 10 }
    var radius: CGFloat { return slim ? 20 : 22 }
    var sideMid: CGFloat { return slim ? side : 8 }

    func font(_ size: CGFloat, _ semibold: Bool = false) -> Font { return Fonts.ui(z(size), semibold) }
    func head(_ size: CGFloat) -> Font { return Fonts.ui(z(size), true) }
}

struct MKey: EnvironmentKey { static let defaultValue = M(slim: true) }
extension EnvironmentValues {
    var m: M {
        get { return self[MKey.self] }
        set { self[MKey.self] = newValue }
    }
}
