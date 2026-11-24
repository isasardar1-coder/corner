// Corner for macOS - colours (T, the five skins: Sage, Glass, Metal, Ceramic, Paper), the font (Gilroy, bundled),
// and the grid both modes are drawn on (M: text sizes, insets, gaps). Port of T in Corner.cs and the grid in Ui.cs.
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

/// Tells the windows that are not the panel (the notice, the welcome window) that the skin has changed, or that
/// Glass has gone from light to dark or back (`skin` is T.look).
final class SkinWatch: ObservableObject {
    static let shared = SkinWatch()
    @Published var skin = 0
}

/// The colours, each named for what it is used for, with a value for each of the five skins: Sage, Glass, Metal,
/// Ceramic and Paper, in the order of `Skin` in settings.json (0 to 4, the same numbers as on Windows). Glass has
/// two values, as on Windows: light glass, and dark glass (`dark`), which is not a skin of its own.
///   Sage:  a pale sage panel with darker trays, white cards and pills, forest green for whatever is open, and
///          lime for the main action, a chosen tab, a switch that is on, the timers and the peaks.
///   Glass: Liquid Glass, clear glass over what is behind the panel (Glass.swift). Its surfaces are thin layers
///          of white; the dark pills are navy glass; the accent is frosted, nearly opaque white glass and the main
///          button bright clear glass, both with dark text. Nothing in it has a hue of its own but the red of
///          "overdue". On dark glass the text is white and the strong colour is white with dark text on it.
///   Metal, Ceramic, Paper: the made skins, whose depth is drawn in Material.swift.
enum T {
    static let names = ["Sage", "Glass", "Metal", "Ceramic", "Paper"]
    static let says = ["Sage, forest and lime", "Clear glass over what is behind", "Brushed silver, white enamel and steel",
                       "Sage stoneware and glazed porcelain", "Blue card and white paper"]
    static var skin = 0
    static var glass: Bool { return skin == 1 }
    /// Glass only: the glass is dark, because what is behind it is (GlassLook, Glass.swift).
    static var dark = false
    /// The skin as a number that also changes when Glass goes dark: what a view keeps to be drawn again.
    static var look: Int { return skin == 1 && dark ? 5 : skin }
    /// Metal, Ceramic or Paper: a skin whose boxes are given depth (Material.swift).
    static var made: Bool { return skin >= 2 }

    private static var cache: [String: Color] = [:]
    private static func p(_ sage: String, _ glass: String, _ darkGlass: String, _ metal: String, _ ceramic: String, _ paper: String) -> Color {
        let key: String
        switch skin {
        case 1: key = dark ? darkGlass : glass
        case 2: key = metal
        case 3: key = ceramic
        case 4: key = paper
        default: key = sage
        }
        if let c = cache[key] { return c }
        let c = Color(hex: key)
        cache[key] = c
        return c
    }

    /// `darkGlass`: nil leaves the glass as light or dark as it was.
    static func use(_ s: Int, darkGlass: Bool? = nil) {
        skin = s < 0 || s >= names.count ? 0 : s
        if let d = darkGlass { dark = d }
        if SkinWatch.shared.skin != look { SkinWatch.shared.skin = look }
    }

    // surfaces, from the back forwards (Glass: how much white each lays over what is under it)
    static var bg: Color { return p("#E8ECDB", "#38FFFFFF", "#14FFFFFF", "#B2B4B6", "#DCDFCB", "#C9E0F1") }
    static var edge: Color { return p("#D3D9C2", "#FFD4E2EE", "#FF39455E", "#8E9093", "#C6CAB3", "#B3CFE3") }
    static var tray: Color { return p("#D9DFC7", "#40FFFFFF", "#14FFFFFF", "#A3A5A8", "#CFD3BC", "#BBD4E7") }
    static var field: Color { return p("#FFFFFF", "#57FFFFFF", "#1CFFFFFF", "#EDEDEE", "#F0EEE7", "#F5F4F2") }
    static var lift: Color { return p("#EEF1E4", "#6BFFFFFF", "#26FFFFFF", "#F6F6F7", "#F6F4EE", "#FBFAF9") }
    static var raised: Color { return p("#E8ECDB", "#5CFFFFFF", "#21FFFFFF", "#E1E2E4", "#E6E6D8", "#DDE9F3") }
    static var ctl: Color { return p("#C6CFB1", "#2E16304A", "#3DFFFFFF", "#C4C6C9", "#C3C9AE", "#A9C4D9") }
    static var line: Color { return p("#E3E7D6", "#2416304A", "#26FFFFFF", "#D9DADC", "#E0E0D2", "#DFE7EE") }
    static var hover: Color { return p("#0F16281D", "#3DFFFFFF", "#1FFFFFFF", "#14000000", "#0F16281D", "#14204868") }
    static var ring: Color { return p("#D9DFC7", "#3316304A", "#3DFFFFFF", "#D0D2D5", "#D5D9C3", "#B5CEE0") }
    static var pop: Color { return p("#FFFFFF", "#F7F4F8FC", "#F71B2433", "#FFFFFF", "#FBFAF6", "#FFFFFF") }
    // text
    static var text: Color { return p("#141A12", "#0F1822", "#FFFFFF", "#121416", "#141A12", "#14202B") }
    static var muted: Color { return p("#535C4B", "#D6142438", "#D1FFFFFF", "#3F4347", "#4E5746", "#44566A") }
    static var faint: Color { return p("#586150", "#BD142438", "#B3FFFFFF", "#4A4E53", "#566050", "#4F6074") }
    // the dark colour: a chip whose list or page is open, and the prompts that need an answer. On dark glass it
    // is white, with dark text on it
    static var strong: Color { return p("#16281D", "#EB132A40", "#E0F4F8FF", "#0F2416", "#132717", "#1E3721") }
    static var onStrong: Color { return p("#FFFFFF", "#FFFFFF", "#0E1A26", "#FFFFFF", "#FFFFFF", "#FFFFFF") }
    static var onStrongSoft: Color { return p("#C9D3BE", "#CFE3F0", "#B8152433", "#C3CEC4", "#C9D3BE", "#C5D2C6") }
    static var strongLine: Color { return p("#7C8C7D", "#73BFE6F2", "#59152433", "#6F8273", "#7C8C7D", "#7E9482") }
    static var strongLift: Color { return p("#24392C", "#33FFFFFF", "#1F152433", "#1F3A28", "#24392C", "#2F4C34") }
    // the dip on the chart: dark in every skin
    static var dip: Color { return p("#16281D", "#EB132A40", "#A6050A12", "#0F2416", "#132717", "#1E3721") }
    static var onDip: Color { return p("#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF") }
    // the accent: a running timer, the peaks. Soft is its pale form: today, the row being timed, the Done bar
    static var accent: Color { return p("#B4C12A", "#DEFFFFFF", "#D9F4F8FF", "#5F656B", "#BECB2B", "#3E79AF") }
    static var onAccent: Color { return p("#141A12", "#0F1822", "#0E1A26", "#FFFFFF", "#141A12", "#FFFFFF") }
    static var soft: Color { return p("#EEF2B4", "#94FFFFFF", "#42FFFFFF", "#9EA2A6", "#CFD94F", "#A9C6DD") }
    /// The accent seen on the strong colour: a prompt's caption.
    static var accentOn: Color { return p("#B4C12A", "#EDF3F7FB", "#1B2A3A", "#D9DCDF", "#C6D233", "#A9C6DD") }
    // the main button ("Done", "Plan my day"), the chosen one of a set of tabs, a switch that is on and the next
    // task's waiting timer: lime in Sage and Ceramic, steel in Metal, blue in Paper, bright clear glass in Glass
    static var main: Color { return p("#B4C12A", "#CCFFFFFF", "#E0F4F8FF", "#5F656B", "#BECB2B", "#3E79AF") }
    static var onMain: Color { return p("#141A12", "#0F1822", "#0E1A26", "#FFFFFF", "#141A12", "#FFFFFF") }
    /// The knob of a switch that is off.
    static var knob: Color { return p("#FFFFFF", "#F5FFFFFF", "#F0FFFFFF", "#FFFFFF", "#FBFAF6", "#FFFFFF") }
    /// The hours of expected sleep on the chart.
    static var sand: Color { return p("#CDC6AB", "#59B4C0CC", "#33C9D3DA", "#C8CACD", "#C2C8AD", "#A3BFD6") }
    static var danger: Color { return p("#A8401F", "#C4260F", "#FF8A75", "#A8321A", "#A8401F", "#B03A1E") }
    static var dangerSoft: Color { return p("#F4DDD3", "#73F2B5A8", "#4DFF6B55", "#F0D9D2", "#F3DDD2", "#F2DCD4") }

    /// The panel's shadow.
    static var shadow: Color {
        switch skin {
        case 1: return dark ? Color.black.opacity(0.5) : Color(hex: "#16244A").opacity(0.3)
        case 2: return Color(.sRGB, red: 20.0 / 255, green: 22.0 / 255, blue: 26.0 / 255, opacity: 0.36)
        case 3: return Color(.sRGB, red: 70.0 / 255, green: 60.0 / 255, blue: 36.0 / 255, opacity: 0.3)
        case 4: return Color(.sRGB, red: 40.0 / 255, green: 56.0 / 255, blue: 74.0 / 255, opacity: 0.24)
        default: return Color(.sRGB, red: 60.0 / 255, green: 54.0 / 255, blue: 34.0 / 255, opacity: 0.3)
        }
    }
    /// What is behind the panel in a test render: sand, or for Glass a drawn wallpaper (Glass.swift).
    static var backdrop: DemoBackdrop { return DemoBackdrop() }
    static let sandBackdrop = Color(.sRGB, red: 185.0 / 255, green: 177.0 / 255, blue: 151.0 / 255, opacity: 1)
}

/// What a box is filled with. Every box is drawn in one place (Plate, in Material.swift), and in the made skins
/// it says what it is by this: a tray is pressed in, a card or a pill is raised, and so on.
enum Tone {
    case clear, tray, field, lift, raised, pop, knob, dangerSoft, strong, dip, strongLift, accent, accentOn, main, soft, ctl

    var color: Color {
        switch self {
        case .clear: return Color.clear
        case .tray: return T.tray
        case .field: return T.field
        case .lift: return T.lift
        case .raised: return T.raised
        case .pop: return T.pop
        case .knob: return T.knob
        case .dangerSoft: return T.dangerSoft
        case .strong: return T.strong
        case .dip: return T.dip
        case .strongLift: return T.strongLift
        case .accent: return T.accent
        case .accentOn: return T.accentOn
        case .main: return T.main
        case .soft: return T.soft
        case .ctl: return T.ctl
        }
    }
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
