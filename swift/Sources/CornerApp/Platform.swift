// Corner for macOS - what Corner needs from the system: time since the last input, a sound, a system notice,
// showing a file in Finder, open at login (a LaunchAgent, which needs no signing) and one running copy.
// The same jobs as Platform.cs in the Avalonia port.
import AppKit
import CoreGraphics
import CornerCore

enum Platform {
    /// Seconds since the last key press or mouse movement. All Corner looks at: no apps, no window titles.
    static func idleSeconds() -> Double {
        let any = CGEventType(rawValue: UInt32.max) ?? .mouseMoved
        let v = CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: any)
        return v.isFinite ? v : 0
    }

    /// Presenting or full screen. macOS has no simple public answer, so never.
    static func busy() -> Bool { return false }

    private static var sound: NSSound? = nil

    /// The notice's ping: ping.wav from the app's resources, else the system's own sound.
    static func ping() {
        if let u = Resources.url("ping", "wav"), let s = NSSound(contentsOf: u, byReference: true) {
            sound?.stop()
            sound = s
            s.volume = 1
            s.play()
        } else {
            NSSound.beep()
        }
    }

    /// A notice in Notification Centre. An unsigned app has no notification identity, so it is sent through
    /// osascript (it then shows under Script Editor), as the Avalonia port does.
    static func notify(_ title: String, _ text: String) {
        func q(_ s: String) -> String {
            return "\"" + s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
        }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        p.arguments = ["-e", "display notification " + q(text) + " with title " + q(title)]
        do { try p.run() } catch { Store.log(error) }
    }

    /// Shows a file selected in Finder, or opens a folder.
    static func reveal(_ path: String) {
        let u = URL(fileURLWithPath: path)
        if FileManager.default.fileExists(atPath: path) && !path.hasSuffix("/") {
            NSWorkspace.shared.activateFileViewerSelecting([u])
        } else {
            NSWorkspace.shared.open(u.deletingLastPathComponent())
        }
    }

    // ------------------------------------------------------------ one running copy

    private static var lockFd: Int32 = -1

    /// True when this is the only copy using this data folder (a lock file there, held while running).
    static func takeLock() -> Bool {
        let path = Store.path(".corner.lock")
        let fd = open(path, O_CREAT | O_RDWR, 0o644)
        if fd < 0 { return true }
        if flock(fd, LOCK_EX | LOCK_NB) != 0 { close(fd); return false }
        lockFd = fd
        return true
    }
}

/// Open at login: a LaunchAgent in ~/Library/LaunchAgents that opens the app.
enum Autostart {
    static let label = "app.corner.todo.swift"
    static var plist: String {
        return NSString(string: NSHomeDirectory()).appendingPathComponent("Library/LaunchAgents/" + label + ".plist")
    }

    /// The .app folder when running from one, else the executable itself.
    static var appPath: String {
        let b = Bundle.main.bundlePath
        if b.hasSuffix(".app") { return b }
        return Bundle.main.executablePath ?? b
    }

    static var inApp: Bool { return Bundle.main.bundlePath.hasSuffix(".app") }

    static var enabled: Bool { return FileManager.default.fileExists(atPath: plist) }

    static func set(_ on: Bool) {
        do {
            if !on {
                if FileManager.default.fileExists(atPath: plist) { try FileManager.default.removeItem(atPath: plist) }
                return
            }
            func esc(_ s: String) -> String {
                return s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;")
            }
            let app = appPath
            let args = app.hasSuffix(".app")
                ? "<string>/usr/bin/open</string><string>-a</string><string>" + esc(app) + "</string>"
                : "<string>" + esc(app) + "</string>"
            let text = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                + "<plist version=\"1.0\"><dict>\n"
                + "  <key>Label</key><string>" + label + "</string>\n"
                + "  <key>ProgramArguments</key><array>" + args + "</array>\n"
                + "  <key>RunAtLoad</key><true/>\n"
                + "</dict></plist>\n"
            let dir = (plist as NSString).deletingLastPathComponent
            try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: nil)
            try text.write(toFile: plist, atomically: true, encoding: .utf8)
        } catch {
            Store.log(error)
        }
    }
}
