// Updates, the part with no screen: which version is newer, and what GitHub says the latest release is.
// Port of Updater's Parse, IsNewer, Number and ParseRelease (Update.cs). The rest is in CornerApp/Updater.swift.
import Foundation

public enum UpdateInfo {
    public static let repo = "isasardar1-coder/corner"
    public static let asset = "Corner-Mac-Swift.zip"
    static let tagRx = Rx(#""tag_name"\s*:\s*"([^"]+)""#)
    static let zipRx = Rx(#""browser_download_url"\s*:\s*"([^"]*/Corner-Mac-Swift\.zip)""#)

    /// "v1.2.0" or "1.2" as three numbers; nil when it is not a version.
    public static func parse(_ tag: String?) -> [Int]? {
        guard var s = tag?.trimmingCharacters(in: .whitespaces) else { return nil }
        if let f = s.first, f == "v" || f == "V" { s.removeFirst() }
        let parts = s.components(separatedBy: ".")
        if parts.count < 2 || parts.count > 3 { return nil }
        var n: [Int] = []
        for p in parts {
            guard !p.isEmpty, p.allSatisfy({ $0.isASCII && $0.isNumber }), let v = Int(p) else { return nil }
            n.append(v)
        }
        while n.count < 3 { n.append(0) }
        return n
    }

    public static func isNewer(_ tag: String?, than version: String) -> Bool {
        guard let a = parse(tag), let b = parse(version) else { return false }
        return b.lexicographicallyPrecedes(a)
    }

    /// A version as one number for settings.json (which keeps numbers only): 1.2.3 -> 10203.
    public static func number(_ version: String?) -> Double {
        guard let v = parse(version) else { return 0 }
        return Double(v[0] * 10000 + v[1] * 100 + v[2])
    }

    /// The release's tag and the address of its Mac zip; nil when it has none.
    public static func parseRelease(_ json: String) -> (tag: String, zip: String)? {
        guard let t = tagRx.matches(json).first, let z = zipRx.matches(json).first else { return nil }
        return (t.group(1), z.group(1))
    }
}
