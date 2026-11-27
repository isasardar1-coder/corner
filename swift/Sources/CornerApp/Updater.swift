// Corner for macOS - updates: Corner looks for a newer release on GitHub a minute after it starts and then every six
// hours. When there is one it downloads Corner-Mac-Swift.zip, takes Corner.app out of it, checks it, puts it in place
// of the running one, and restarts as soon as the panel is closed and no timer is running. Your notes are never
// touched: they are in another folder. Settings > Updates turns this off; "Check now" asks straight away.
// Where macOS does not let Corner replace itself (it was opened from the download without being moved to
// Applications, or the folder cannot be written to), it says so and offers the download page. Port of Update.cs.
import AppKit
import CornerCore

struct UpdateError: Error, CustomStringConvertible {
    let description: String
    init(_ d: String) { description = d }
}

enum Updater {
    static let firstCheckSec: Double = 60
    static let everyHours = 6

    static let version: String = (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? ""

    /// A build that is not a release carries no release's number ("1.0", see package.sh): it never updates itself.
    static var released: Bool { return Autostart.inApp && version != "1.0" && UpdateInfo.parse(version) != nil }

    /// Tests only (CORNER_DATA set): another address to ask, a file:// one.
    static let testURL: String? = ProcessInfo.processInfo.environment["CORNER_DATA"] == nil
        ? nil : ProcessInfo.processInfo.environment["CORNER_UPDATE_URL"]

    static let page = "https://github.com/" + UpdateInfo.repo + "/releases/latest"

    /// What is at an address; called off the main thread.
    static func get(_ address: String) throws -> Data {
        guard let u = URL(string: address), u.scheme == "https" || testURL != nil else { throw UpdateError("bad address " + address) }
        var req = URLRequest(url: u, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 60)
        req.setValue("Corner-updater", forHTTPHeaderField: "User-Agent")
        var out: Data? = nil
        var bad: Error? = nil
        let done = DispatchSemaphore(value: 0)
        let task = URLSession.shared.dataTask(with: req) { data, resp, err in
            if let e = err {
                bad = e
            } else if let h = resp as? HTTPURLResponse, h.statusCode != 200 {
                bad = UpdateError("HTTP " + String(h.statusCode))
            } else {
                out = data
            }
            done.signal()
        }
        task.resume()
        if done.wait(timeout: .now() + 600) == .timedOut {
            task.cancel()
            throw UpdateError("timed out")
        }
        if let e = bad { throw e }
        guard let d = out else { throw UpdateError("nothing came") }
        return d
    }

    static func latest() throws -> (tag: String, zip: String)? {
        let address: String = testURL ?? ("https://api.github.com/repos/" + UpdateInfo.repo + "/releases/latest")
        let data = try get(address)
        guard let text = String(data: data, encoding: .utf8) else { return nil }
        return UpdateInfo.parseRelease(text)
    }

    static func run(_ tool: String, _ args: [String]) throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: tool)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        p.waitUntilExit()
        if p.terminationStatus != 0 { throw UpdateError(tool + " said " + String(p.terminationStatus)) }
    }

    /// Nil when Corner can put a new copy in its place; else why not.
    static func blocked() -> String? {
        let path = Bundle.main.bundlePath
        // opened from the download, macOS runs a hidden read-only copy of it
        if path.contains("/AppTranslocation/") { return "Move Corner.app to Applications and open it from there" }
        let fm = FileManager.default
        if !fm.isWritableFile(atPath: path) || !fm.isWritableFile(atPath: (path as NSString).deletingLastPathComponent) {
            return "Corner can't write to the folder it is in"
        }
        return nil
    }

    /// Downloads the release and puts its Corner.app where the running one is. Returns the folder the old one went to.
    static func install(_ tag: String, _ zipAddress: String) throws -> URL {
        let fm = FileManager.default
        let here = URL(fileURLWithPath: Bundle.main.bundlePath)
        // (a working folder on the same disk, so that putting it in place is a rename)
        let work = try fm.url(for: .itemReplacementDirectory, in: .userDomainMask, appropriateFor: here, create: true)
        do {
            let zip = work.appendingPathComponent(UpdateInfo.asset)
            let bytes = try get(zipAddress)
            try bytes.write(to: zip)
            let out = work.appendingPathComponent("new")
            try run("/usr/bin/ditto", ["-x", "-k", zip.path, out.path])
            let fresh = out.appendingPathComponent("Corner.app")
            try check(fresh, tag)
            let old = work.appendingPathComponent("Corner.old.app")
            try fm.moveItem(at: here, to: old)
            do {
                try fm.moveItem(at: fresh, to: here)
            } catch {
                try? fm.removeItem(at: here)
                try fm.moveItem(at: old, to: here) // put the old one back
                throw error
            }
            return work
        } catch {
            // (the working folder goes, unless the old Corner could not be put back and is still in it)
            if fm.fileExists(atPath: here.path) { try? fm.removeItem(at: work) }
            throw error
        }
    }

    /// The same app, the version it should be, and whole.
    static func check(_ app: URL, _ tag: String) throws {
        let plist = NSDictionary(contentsOf: app.appendingPathComponent("Contents/Info.plist"))
        if (plist?["CFBundleIdentifier"] as? String) != Bundle.main.bundleIdentifier { throw UpdateError("not Corner") }
        let v = UpdateInfo.parse(plist?["CFBundleShortVersionString"] as? String)
        if v == nil || v != UpdateInfo.parse(tag) { throw UpdateError("not version " + tag) }
        try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
    }

    /// Once this copy has quit: the old one's folder goes, and the new one is opened.
    static func openWhenGone(_ work: URL?) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        let script = "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; [ -n \"$3\" ] && rm -rf \"$3\"; exec /usr/bin/open \"$2\""
        p.arguments = ["-c", script, "corner-restart", String(ProcessInfo.processInfo.processIdentifier), Bundle.main.bundlePath, work?.path ?? ""]
        do { try p.run() } catch { Store.log(error) }
    }
}

/// What Settings shows, and the checks. Held by the panel (PanelController.updates).
final class UpdateWatch {
    weak var shell: PanelController?
    var timer: Timer? = nil
    var ready: String? = nil     // the version that is in place and waits for a restart
    var waiting: String? = nil   // a newer version Corner could not put in place itself
    var note: String? = nil      // what Settings shows after a check
    var busy = false
    var work: URL? = nil

    static var on: Bool { return Settings.get("AutoUpdate", 1) > 0.5 }

    func start(test: Bool) {
        if test && Updater.testURL == nil { return } // test copies never look for updates
        if !Updater.released { return }
        let was: Double = Settings.get("LastVersion", 0)
        let now: Double = UpdateInfo.number(Updater.version)
        // the one notice: after Corner put this version in place itself ("SelfUpdated", set in checked), not after a
        // copy put in place by hand. Checking and fetching are silent.
        if was > 0 && was < now && Settings.get("SelfUpdated", 0) > 0.5 {
            DispatchQueue.main.asyncAfter(deadline: .now() + 2.5) { [weak self] in
                self?.shell?.showToast("Corner updated itself to version " + Updater.version + ". What's new: Settings > Updates.")
            }
        }
        if was != now { Settings.set("LastVersion", now); Settings.set("SelfUpdated", 0) }
        next(test ? 3 : Updater.firstCheckSec)
    }

    func next(_ seconds: Double) {
        timer?.invalidate()
        let t = Timer(timeInterval: seconds, repeats: false) { [weak self] _ in
            guard let s = self else { return }
            if s.ready != nil {
                s.restartWhenFree()
                return
            }
            s.next(Double(Updater.everyHours) * 3600)
            // (one it could not put in place is not downloaded again and again: that waits for "Download")
            if UpdateWatch.on && s.waiting == nil { s.check(manual: false) }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    /// manual: from Settings' "Check now", which also reports "you have the latest".
    func check(manual: Bool) {
        if busy || ready != nil { return }
        if !Updater.released {
            note = "This copy is not a release"
            shell?.model.changed()
            return
        }
        busy = true
        if manual {
            note = "Checking…"
            shell?.model.changed()
        }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var say: String? = nil
            var got: String? = nil
            var stuck: String? = nil
            var old: URL? = nil
            do {
                if let r = try Updater.latest() {
                    if !UpdateInfo.isNewer(r.tag, than: Updater.version) {
                        say = "You have the latest version"
                    } else {
                        let v: String = r.tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV "))
                        if let why = Updater.blocked() {
                            stuck = v
                            say = "Version " + v + " is out. " + why + ", or download it"
                        } else {
                            do {
                                old = try Updater.install(r.tag, r.zip)
                                got = v
                            } catch {
                                Store.log("update: " + String(describing: error))
                                stuck = v
                                say = "Version " + v + " is out, but Corner couldn't put it in place. Download it"
                            }
                        }
                    }
                } else {
                    say = "Couldn't read the latest release"
                }
            } catch {
                Store.log("update: " + String(describing: error))
                say = "Couldn't check for updates (no connection?)"
            }
            let sayNow = say, gotNow = got, stuckNow = stuck, oldNow = old
            DispatchQueue.main.async { self?.checked(sayNow, gotNow, stuckNow, oldNow) }
        }
    }

    func checked(_ say: String?, _ got: String?, _ stuck: String?, _ old: URL?) {
        busy = false
        note = say
        if let v = got {
            ready = v
            waiting = nil
            work = old
            note = "Version " + v + " is ready; Corner restarts in a moment"
            Settings.set("SelfUpdated", 1) // no notice now: it comes after the restart (start)
            next(16)
        } else if let v = stuck {
            waiting = v
            // said once for each version; Settings keeps saying it
            if Settings.get("UpdateTold", 0) != UpdateInfo.number(v) {
                Settings.set("UpdateTold", UpdateInfo.number(v))
                shell?.showToast("Corner " + v + " is out. Get it from Settings, under Updates.")
            }
        }
        shell?.model.changed()
        shell?.contentChanged()
    }

    /// Restarts into the new copy when the panel is closed and no task timer is running; otherwise waits.
    func restartWhenFree() {
        guard ready != nil, let s = shell else { return }
        let m = s.model
        let timing: Bool = m.timerId != nil && (m.timerRunning || !AppModel.priorityMode)
        if m.shown || m.full || timing || s.onboard != nil {
            next(30)
            return
        }
        Updater.openWhenGone(work)
        NSApp.terminate(nil)
    }

    func openPage() {
        if let u = URL(string: Updater.page) { NSWorkspace.shared.open(u) }
    }
}
