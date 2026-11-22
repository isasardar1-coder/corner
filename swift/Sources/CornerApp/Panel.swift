// Corner for macOS - the shell: the menu-bar icon and its menu, the panel window (a borderless, non-activating
// NSPanel hosting the SwiftUI card), the hot corner, showing and hiding, Full Mode, moving the panel, Cmd+scroll,
// the timers, and the test renders (--demo). Port of Shell in Corner.cs, Checks.cs and ShellMac.cs.
import AppKit
import SwiftUI
import CornerCore

/// A borderless panel that can take the keyboard (for the add box) without making Corner the active app.
final class CornerPanel: NSPanel {
    override var canBecomeKey: Bool { return true }
    override var canBecomeMain: Bool { return false }
}

/// Clicks reach the card even while another app is in front.
final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { return true }
}

struct CardSizeKey: PreferenceKey {
    static var defaultValue: CGSize = .zero
    static func reduce(value: inout CGSize, nextValue: () -> CGSize) {
        let n = nextValue()
        if n != .zero { value = n }
    }
}

/// The window's content: the card, scaled to the text size, with room round it for its shadow.
struct RootView: View {
    @EnvironmentObject var model: AppModel
    let onSize: (CGSize) -> Void
    @State private var size: CGSize = .zero

    var body: some View {
        let s = CGFloat(model.currentScale)
        ZStack(alignment: .topLeading) {
            if model.demoRun { T.backdrop }
            CardView()
                .fixedSize()
                .background(GeometryReader { g in Color.clear.preference(key: CardSizeKey.self, value: g.size) })
                .onHover { inside in if inside && !model.demoRun { model.reveal() } }
                .scaleEffect(s, anchor: .topLeading)
                .frame(width: size.width * s, height: size.height * s, alignment: .topLeading)
                .padding(shadowM)
        }
        .onPreferenceChange(CardSizeKey.self) { v in
            if abs(v.width - size.width) > 0.5 || abs(v.height - size.height) > 0.5 {
                size = v
                onSize(v)
            }
        }
    }
}

final class PanelController: NSObject, NSMenuDelegate {
    let model: AppModel
    var window: CornerPanel!
    var hosting: NSView!
    var statusItem: NSStatusItem!
    var cardSize: CGSize = CGSize(width: cardW, height: 200)

    var gen = 0
    var cornerSince: Double = 0
    var awaySince: Double = 0
    var peekUntil: Double = 0
    var cornerBlocked = false
    var floating = false
    var programmaticMove = 0
    var moveSave: DispatchWorkItem? = nil
    var toast: ToastController? = nil
    var onboard: OnboardController? = nil
    var timers: [Timer] = []

    init(model: AppModel) {
        self.model = model
        super.init()
        model.shell = self
    }

    static func ms() -> Double { return ProcessInfo.processInfo.systemUptime * 1000 }

    // ------------------------------------------------------------ start

    func start(peek: Bool, test: Bool) {
        let firstRun = !Store.exists
        model.start()
        if !test {
            if firstRun {
                if Autostart.inApp { Autostart.set(true) }
                Store.save(model.data)
            } else if Autostart.enabled {
                Autostart.set(true) // keep the path current if the app was moved
            }
        }
        buildWindow()
        initStatusItem()

        let mouse = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.mouseTick() }
        let remind = Timer(timeInterval: 10, repeats: true) { [weak self] _ in
            guard let s = self else { return }
            s.model.logDays()
            s.model.checkReminders()
            s.model.checkInTick()
        }
        let second = Timer(timeInterval: 1, repeats: true) { [weak self] _ in self?.model.tick() }
        for t in [mouse, remind, second] {
            RunLoop.main.add(t, forMode: .common)
            timers.append(t)
        }

        // Cmd + scroll changes the text size, like Ctrl + wheel on Windows
        NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] e in
            guard let s = self, e.window === s.window, e.modifierFlags.contains(.command) else { return e }
            let d = e.scrollingDeltaY
            if abs(d) < 0.01 { return nil }
            s.model.setScale(s.model.currentScale + (d > 0 ? 0.05 : -0.05), save: true)
            return nil
        }

        NotificationCenter.default.addObserver(self, selector: #selector(windowMoved), name: NSWindow.didMoveNotification, object: window)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)

        if firstRun && (!test || ProcessInfo.processInfo.environment["CORNER_ONBOARD"] == "1") { showOnboarding() }
        else if peek { showPanel(peekMs: 6000) }
        model.checkReminders()
    }

    func buildWindow() {
        let w = CornerPanel(contentRect: NSRect(x: -10000, y: -10000, width: cardW + 2 * shadowM, height: 300),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.level = .statusBar
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        w.hidesOnDeactivate = false
        w.isFloatingPanel = true
        w.becomesKeyOnlyIfNeeded = true
        w.isMovableByWindowBackground = false
        w.isReleasedWhenClosed = false
        w.animationBehavior = .none
        let root = RootView(onSize: { [weak self] s in self?.cardSizeChanged(s) }).environmentObject(model)
        let h = FirstMouseHostingView(rootView: root)
        if #available(macOS 13.0, *) { h.sizingOptions = [] }
        h.autoresizingMask = [.width, .height]
        w.contentView = h
        hosting = h
        window = w
    }

    // ------------------------------------------------------------ the menu-bar icon

    func initStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let b = statusItem.button {
            b.image = Icon.menuBar()
            b.toolTip = "Corner"
        }
        let menu = NSMenu()
        menu.delegate = self
        menu.addItem(item("Show list", #selector(menuShow)))
        menu.addItem(item("Full Mode", #selector(menuFull)))
        menu.addItem(NSMenuItem.separator())
        let cornerItem = NSMenuItem(title: "Opens from", action: nil, keyEquivalent: "")
        let cm = NSMenu()
        for (i, n) in ["Top right", "Top left", "Bottom right", "Bottom left"].enumerated() {
            let it = item(n, #selector(menuCorner(_:)))
            it.tag = i
            cm.addItem(it)
        }
        cornerItem.submenu = cm
        menu.addItem(cornerItem)
        let sizeItem = NSMenuItem(title: "Text size", action: nil, keyEquivalent: "")
        let sm = NSMenu()
        for (i, v) in PanelController.sizes.enumerated() {
            let it = item(String(Int((v * 100).rounded())) + "%", #selector(menuSize(_:)))
            it.tag = i
            sm.addItem(it)
        }
        sizeItem.submenu = sm
        menu.addItem(sizeItem)
        menu.addItem(item("Open at login", #selector(menuLogin)))
        menu.addItem(item("Reset position", #selector(menuReset)))
        menu.addItem(NSMenuItem.separator())
        menu.addItem(item("Quit Corner", #selector(menuQuit)))
        statusItem.menu = menu
    }

    static let sizes: [Double] = [0.8, 0.9, 1.0, 1.15, 1.3]

    func item(_ title: String, _ sel: Selector) -> NSMenuItem {
        let it = NSMenuItem(title: title, action: sel, keyEquivalent: "")
        it.target = self
        return it
    }

    /// The same settings can be changed inside the panel, so the ticks are read fresh each time.
    func menuNeedsUpdate(_ menu: NSMenu) {
        for it in menu.items {
            if it.title == "Open at login" { it.state = Autostart.enabled ? .on : .off }
            if it.title == "Opens from", let sub = it.submenu {
                for s in sub.items { s.state = s.tag == model.corner ? .on : .off }
            }
            if it.title == "Text size", let sub = it.submenu {
                for s in sub.items { s.state = abs(PanelController.sizes[s.tag] - model.currentScale) < 0.001 ? .on : .off }
            }
        }
    }

    @objc func menuShow() { model.wake() }
    @objc func menuFull() { enterFull() }
    @objc func menuCorner(_ sender: NSMenuItem) { model.setCorner(sender.tag); model.wake() }
    @objc func menuSize(_ sender: NSMenuItem) { model.setScale(PanelController.sizes[sender.tag], save: true); model.wake() }
    @objc func menuLogin() { Autostart.set(!Autostart.enabled) }
    @objc func menuReset() { resetPosition(); model.wake() }
    @objc func menuQuit() { NSApp.terminate(nil) }

    func setTrayTip(_ text: String) { statusItem?.button?.toolTip = text }

    // ------------------------------------------------------------ placing the panel

    var screen: NSScreen? { return NSScreen.screens.first }

    func cardSizeChanged(_ s: CGSize) {
        cardSize = s
        if window.isVisible { place(animated: false) }
    }

    @objc func screensChanged() { if window.isVisible { place(animated: false) } }

    /// Where the window goes: beside its corner (or where it was dragged to), or centred in Full Mode.
    func targetFrame() -> NSRect {
        guard let scr = screen else { return window.frame }
        let vf = scr.visibleFrame
        let s = CGFloat(model.currentScale)
        let gap: CGFloat = 12
        let w = cardSize.width * s + 2 * shadowM
        let h = cardSize.height * s + 2 * shadowM
        var x: CGFloat
        var top: CGFloat
        if model.full {
            x = vf.midX - w / 2
            top = vf.maxY - 20 + shadowM
            if Settings.get("MacFullMoved", 0) > 0.5 {
                x = max(vf.minX - shadowM, min(vf.maxX - w + shadowM, CGFloat(Settings.get("MacFullX", Double(x)))))
                top = max(vf.minY + 260, min(vf.maxY + shadowM, CGFloat(Settings.get("MacFullTop", Double(top)))))
            }
            return NSRect(x: x, y: top - h, width: w, height: h)
        }
        floating = Settings.get("MacMoved", 0) > 0.5
        x = model.onRight ? vf.maxX - gap - cardSize.width * s - shadowM : vf.minX + gap - shadowM
        if floating {
            x = max(vf.minX - shadowM, min(vf.maxX - w + shadowM, CGFloat(Settings.get("MacPosX", Double(x)))))
        }
        if model.onTop {
            top = vf.maxY - gap + shadowM
            if floating { top = max(vf.minY + 240, min(vf.maxY + shadowM, CGFloat(Settings.get("MacPosY", Double(top))))) }
            return NSRect(x: x, y: top - h, width: w, height: h)
        }
        var bottom = vf.minY + gap - shadowM
        if floating { bottom = max(vf.minY - shadowM, min(vf.maxY - 240, CGFloat(Settings.get("MacPosY", Double(bottom))))) }
        return NSRect(x: x, y: bottom, width: w, height: h)
    }

    /// The tallest the card may be (before scaling) where it is: the scrolling parts stop there.
    func maxCardHeight() -> CGFloat {
        guard let scr = screen else { return 600 }
        let vf = scr.visibleFrame
        let s = CGFloat(model.currentScale)
        if model.full { return max(300, (vf.height - 40) / s) }
        let f = targetFrame()
        let room = model.onTop ? (f.maxY - shadowM) - vf.minY - 12 : vf.maxY - (f.minY + shadowM) - 12
        return max(200, room / s)
    }

    func place(animated: Bool) {
        let mh = maxCardHeight()
        if abs(mh - model.maxCardH) > 1 { model.maxCardH = mh; model.changed() }
        let f = targetFrame()
        if f == window.frame { return }
        programmaticMove += 1
        window.setFrame(f, display: true)
        programmaticMove -= 1
    }

    @objc func windowMoved() {
        if programmaticMove > 0 || !window.isVisible { return }
        // dragged by the header: remember where, once it has settled
        moveSave?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.saveMoved() }
        moveSave = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4, execute: work)
    }

    func saveMoved() {
        let f = window.frame
        if NSEvent.pressedMouseButtons & 1 != 0 { windowMoved(); return }
        if model.full {
            Settings.set("MacFullX", Double(f.minX).rounded())
            Settings.set("MacFullTop", Double(f.maxY).rounded())
            Settings.set("MacFullMoved", 1)
        } else {
            Settings.set("MacPosX", Double(f.minX).rounded())
            Settings.set("MacPosY", Double(model.onTop ? f.maxY : f.minY).rounded())
            Settings.set("MacMoved", 1)
        }
        place(animated: false)
    }

    func resetPosition() {
        Settings.set("MacMoved", 0)
        Settings.set("MacFullMoved", 0)
        if window.isVisible { place(animated: false) }
    }

    func skinChanged() {
        hosting.needsDisplay = true
    }

    func contentChanged() {
        DispatchQueue.main.async { [weak self] in
            guard let s = self, s.window.isVisible else { return }
            s.place(animated: false)
        }
    }

    // ------------------------------------------------------------ show / hide

    func showPanel(peekMs: Int) {
        let now = PanelController.ms()
        if peekMs > 0 { peekUntil = now + Double(peekMs) }
        else if !model.full && !model.shown && Settings.get("MacMoved", 0) > 0.5 { peekUntil = now + 3000 }
        if model.shown { return }
        gen += 1
        awaySince = 0
        model.prepareShow()
        hosting.layoutSubtreeIfNeeded()
        let mh = maxCardHeight()
        model.maxCardH = mh
        model.changed()
        hosting.layoutSubtreeIfNeeded()
        let target = targetFrame()
        var from = target
        if !model.full {
            from.origin.x += model.onRight ? 18 : -18
            from.origin.y += model.onTop ? 12 : -12
        } else {
            from.origin.y -= 10
        }
        programmaticMove += 1
        window.setFrame(from, display: false)
        window.alphaValue = 0
        if model.full {
            window.level = .normal
            window.collectionBehavior = [.fullScreenAuxiliary, .moveToActiveSpace]
            NSApp.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
        } else {
            window.level = .statusBar
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
            window.orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.32
            ctx.timingFunction = CAMediaTimingFunction(controlPoints: 0.22, 1, 0.36, 1)
            window.animator().setFrame(target, display: true)
            window.animator().alphaValue = 1
        }, completionHandler: { [weak self] in
            guard let s = self else { return }
            s.programmaticMove -= 1
            s.place(animated: false)
        })
    }

    func hidePanel() {
        if !model.shown { return }
        if model.full { exitFull(); return }
        model.shown = false
        gen += 1
        let g = gen
        model.whenOpenId = nil
        if let id = model.editId, let t = model.data.tasks.first(where: { $0.id == id }) { model.finishEdit(t, t.title, keep: false) }
        peekUntil = 0
        var to = window.frame
        to.origin.x += model.onRight ? 10 : -10
        to.origin.y += model.onTop ? 6 : -6
        programmaticMove += 1
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.14
            window.animator().setFrame(to, display: true)
            window.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            guard let s = self else { return }
            s.programmaticMove -= 1
            if s.gen == g { s.window.orderOut(nil) }
        })
    }

    // ------------------------------------------------------------ Full Mode

    func enterFull() {
        if model.full { activateFull(); return }
        window.orderOut(nil)
        model.shown = false
        gen += 1
        model.full = true
        model.paused = false
        model.promptOnly = false
        setTrayTip("Corner")
        showPanel(peekMs: 0)
    }

    func activateFull() {
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    func exitFull() {
        if !model.full { return }
        model.full = false
        model.shown = false
        gen += 1
        window.orderOut(nil)
        window.level = .statusBar
        model.changed()
    }

    // ------------------------------------------------------------ the hot corner

    /// About 60 times a second: rest the pointer in the corner for 140 ms to open the panel; it hides 260 ms after
    /// the pointer leaves the panel and the room round it. Not while a mouse button is down (a window's buttons sit there).
    func mouseTick() {
        if model.demoRun || model.full { return }
        guard let scr = screen else { return }
        let p = NSEvent.mouseLocation
        let sb = scr.frame
        let inX = model.onRight ? (p.x >= sb.maxX - 4 && p.x <= sb.maxX) : (p.x >= sb.minX && p.x <= sb.minX + 3)
        let inY = model.onTop ? (p.y >= sb.maxY - 4 && p.y <= sb.maxY) : (p.y >= sb.minY && p.y <= sb.minY + 3)
        let inCorner = inX && inY
        let btn = NSEvent.pressedMouseButtons & 1 != 0
        let now = PanelController.ms()

        if !model.shown {
            if !inCorner { cornerSince = 0; cornerBlocked = false; return }
            if model.paused { return }
            if btn { cornerBlocked = true }
            if cornerBlocked { return }
            if cornerSince == 0 { cornerSince = now }
            else if now - cornerSince >= 140 && !Platform.busy() { showPanel(peekMs: 0) }
            return
        }

        if model.promptOnly && inCorner {
            model.promptOnly = false
            model.bare = false
            model.applyPromptOnly()
            model.changed()
            contentChanged()
        }
        let keep = inKeepZone(p)
        if inCorner || keep {
            awaySince = 0
            peekUntil = floating && inCorner && !keep ? now + 2500 : 0
        } else if btn || model.dragging || now < peekUntil || model.alertHolds {
            awaySince = 0
        } else if awaySince == 0 {
            awaySince = now
        } else if now - awaySince > 260 {
            hidePanel()
        }
    }

    /// The card and a margin round it; generous on the two sides that face the corner.
    func inKeepZone(_ p: NSPoint) -> Bool {
        let f = window.frame.insetBy(dx: shadowM, dy: shadowM)
        let mm = 18 * CGFloat(model.currentScale)
        let left = f.minX - (model.onRight ? mm : 4 * mm)
        let right = f.maxX + (model.onRight ? 4 * mm : mm)
        let top = f.maxY + (model.onTop ? 4 * mm : mm)
        let bottom = f.minY - (model.onTop ? mm : 4 * mm)
        return p.x >= left && p.x <= right && p.y >= bottom && p.y <= top
    }

    // ------------------------------------------------------------ notices and the welcome window

    func showToast(_ line: String) {
        toast?.close(fade: false)
        let t = ToastController(model: model, line: line)
        toast = t
        t.show(near: self)
    }

    func showOnboarding(step: Int = 0) {
        let o = OnboardController(model: model, step: step) { [weak self] in
            self?.onboard = nil
            if !(self?.model.demoRun ?? true) { self?.showPanel(peekMs: 15000) }
        }
        onboard = o
        o.show()
    }

    // ------------------------------------------------------------ test renders

    /// Renders a view to a PNG at twice its size.
    static func savePNG(_ view: NSView, _ path: String) {
        view.layoutSubtreeIfNeeded()
        let b = view.bounds
        guard b.width > 0, b.height > 0,
              let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(b.width * 2), pixelsHigh: Int(b.height * 2),
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return }
        rep.size = b.size
        view.cacheDisplay(in: b, to: rep)
        if let data = rep.representation(using: .png, properties: [:]) {
            do { try data.write(to: URL(fileURLWithPath: path)) } catch { Store.log(error) }
        }
    }

    /// Test hook (only with CORNER_DATA set): fills a throwaway list and renders the panel to a PNG.
    func demo(_ path: String, _ modeIn: String) {
        var mode = modeIn
        model.demoRun = true
        if mode.hasPrefix("onboard") {
            let step = Int(String(mode.dropFirst(7))) ?? 0
            showOnboarding(step: step)
            onboard?.demoFill(step)
            DispatchQueue.main.asyncAfter(deadline: .now() + 2.6) { [weak self] in
                if let v = self?.onboard?.window.contentView { PanelController.savePNG(v, path) }
                exit(0)
            }
            return
        }
        if mode == "rise" || mode == "dip" {
            showToast(mode == "dip" ? Lines.dipLines[6] : Lines.riseLines[4])
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
                if let v = self?.toast?.window.contentView { PanelController.savePNG(v, path) }
                exit(0)
            }
            return
        }
        for s in ["Book flights", "Finish the quarterly report and send it to the whole team friday", "Buy milk", "Call Sam tomorrow 9am", "Water the plants today", "Stretch in 0 min"] {
            model.input = s
            model.addTask()
        }
        model.data.tasks[5].done = true
        model.data.tasks[5].doneAt = "x"
        model.data.doneOpen = true
        model.whenOpenId = model.data.tasks[2].id
        model.data.tasks[3].setDue(Date().dateOnly.addingDays(-1), hasTime: false)
        model.input = "Dentist nov 30 at 2pm"
        let full = mode.hasPrefix("full") || ProcessInfo.processInfo.environment["CORNER_FULL"] == "1"
        if mode.hasPrefix("full") { mode = String(mode.dropFirst(4)); if mode.isEmpty { mode = "tasks" } }
        let rest = mode.hasPrefix("rest")
        if rest && mode.count > 4 { mode = String(mode.dropFirst(4)) }
        if mode == "glass" { T.use(1); mode = "tasks" }
        demoSeed(mode)
        if full { enterFull() } else { showPanel(peekMs: 600000) }
        if !rest { model.reveal() }
        demoAfterShow(mode)
        model.changed()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak self] in
            guard let s = self else { exit(1) }
            s.place(animated: false)
            s.hosting.layoutSubtreeIfNeeded()
            PanelController.savePNG(s.hosting, path)
            exit(0)
        }
    }

    func demoSeed(_ mode: String) {
        if mode == "prompt" { model.promptOnly = true }
        if mode == "plan" || mode == "applied" {
            for s in ["Tidy desk", "Anki insertion !low 20m", "Review animation !med", "Emails !low", "Write concept 14 !high 45m"] {
                model.input = s
                model.addTask()
            }
            model.input = ""
        }
        if mode == "checkin" || mode == "real" { return }
        let day = SleepLog.today(AppModel.now())
        let short = [2, 3, 6, 9]
        for i in stride(from: 12, through: 0, by: -1) {
            let s = short.contains(i)
            let asleep = TimeInterval(23 * 3600 + (s ? 55 : 30) * 60)
            let woke = TimeInterval((s ? 5 : 6) * 3600 + (s ? 40 : 15) * 60)
            if let n = SleepLog.build(asleep: asleep, woke: woke, day: day.addingDays(-i)) { SleepLog.record(n) }
        }
        if mode == "split" || mode == "nap" {
            if let n = SleepLog.buildSplit(asleep: [23.5 * 3600, 5.25 * 3600], woke: [4.5 * 3600, 7 * 3600], day: day) { SleepLog.record(n) }
        }
        if mode == "nap" {
            SleepLog.addNap(day, from: day.addingHours(14.5), to: day.addingHours(14.5).addingMinutes(25))
            model.napOffer = true
        }
    }

    func demoAfterShow(_ mode: String) {
        switch mode {
        case "all": model.allOpen = true
        case "adding": model.addOpen = true
        case "energy", "split", "nap", "real": model.setTab(1)
        case "plan": model.setTab(1); model.makePlan()
        case "applied": model.makePlan(); model.applyPlan(); model.setTab(0); model.allOpen = true
        case "routines": model.routinesOpen = true; model.setTab(1)
        case "debt": model.debtOpen = true; model.setTab(1)
        case "settings": model.toggleSettings()
        case "feedback": model.rateNowPressed()
        case "timer":
            if let t = model.upcoming().first { model.startTimer(t); model.timerEnd = model.timerEnd.addingTimeInterval(-754) }
        case "timeup":
            if let t = model.upcoming().first { model.startTimer(t); model.timeUp() }
        case "nextup":
            if let t = model.upcoming().first { model.completeTask(t) }
        default: break
        }
        model.changed()
    }
}
