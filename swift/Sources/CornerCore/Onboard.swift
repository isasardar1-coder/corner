// Port of Corner.Core/src/Onboard.cs: the user's name (Profile; profile.txt beside tasks.json).
// The welcome window itself is UI and lives in the app.
import Foundation

/// The user's name (profile.txt beside tasks.json). Empty when not given.
public enum Profile {
    static var file: String { return Store.path("profile.txt") }

    public static var name: String {
        get {
            guard let text = try? String(contentsOfFile: file, encoding: .utf8) else { return "" }
            var scalars = Array(text.unicodeScalars)
            if let f = scalars.first, f == "\u{FEFF}" { scalars.removeFirst() }
            var view = String.UnicodeScalarView()
            view.append(contentsOf: scalars)
            return String(view).trimmed
        }
        set {
            do { try Store.writeText(file, newValue.trimmed) } catch { Store.log(error) }
        }
    }
}
