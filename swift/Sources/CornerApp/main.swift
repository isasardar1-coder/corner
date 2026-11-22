// Corner for macOS - start-up. One running copy per data folder, no Dock icon (a menu-bar app), then the shell.
// Options: --peek (show the panel for a few seconds), --demo <png> <mode> (a test render; needs CORNER_DATA),
// --icon <png> (draws the app icon, used by package.sh).
import AppKit
import CornerCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    var shell: PanelController? = nil
    let model = AppModel()
    var demo: (String, String)? = nil
    var peek = false
    var test = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        Fonts.register()
        let s = PanelController(model: model)
        shell = s
        s.start(peek: peek, test: test)
        if let d = demo {
            DispatchQueue.main.asyncAfter(deadline: .now() + 30) { exit(3) } // a render never hangs the build
            DispatchQueue.main.async { s.demo(d.0, d.1) }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { return false }
}

let args = CommandLine.arguments
if args.count >= 3 && args[1] == "--icon" {
    Icon.writeAppIcon(args[2])
    exit(0)
}

let test = ProcessInfo.processInfo.environment["CORNER_DATA"] != nil
if !Platform.takeLock() { exit(0) }

NSSetUncaughtExceptionHandler { e in Store.log("Uncaught: " + e.name.rawValue + " " + (e.reason ?? "")) }

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let delegate = AppDelegate()
delegate.test = test
delegate.peek = args.contains("--peek")
if test && args.count >= 3 && args[1] == "--demo" {
    delegate.demo = (args[2], args.count > 3 ? args[3] : "tasks")
}
app.delegate = delegate
app.run()
