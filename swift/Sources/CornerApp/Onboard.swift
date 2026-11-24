// Corner for macOS - the welcome window a new user sees on the very first start: their name, their sleep need and
// the things they do most days (routines, each with an effort level and a length). Four short steps that slide in.
// Port of Onboard.cs.
import AppKit
import SwiftUI
import CornerCore

final class OnboardState: ObservableObject {
    @Published var step = 0
    @Published var dir = 1
    @Published var name = ""
    @Published var need = EnergyModel.defaultNeed
    @Published var routines: [Routine] = []
    @Published var effort = "medium"
    @Published var entry = ""
    @Published var minutes = "30"
    var finish: () -> Void = {}

    func next() {
        if step >= 3 { finish(); return }
        go(step + 1)
    }

    func go(_ to: Int) {
        if to < 0 || to > 3 || to == step { return }
        dir = to > step ? 1 : -1
        withAnimation(.easeOut(duration: 0.38)) { step = to }
    }

    func add(_ title: String, _ effort: String, _ minutes: Int) {
        let r = Routine(id: "r" + String(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(8)),
                        title: title, energy: effort, minutes: minutes, days: "daily")
        withAnimation(.easeOut(duration: 0.3)) { routines.append(r) }
    }

    func addTyped() {
        let t = entry.trimmed
        if t.isEmpty { return }
        var mm = Int(minutes) ?? 0
        if mm <= 0 { mm = Planner.defaultMinutes }
        add(t, effort, min(mm, 600))
        entry = ""
    }
}

final class OnboardController {
    let model: AppModel
    let state = OnboardState()
    var window: NSWindow!
    let onDone: () -> Void
    var finished = false

    init(model: AppModel, step: Int, onDone: @escaping () -> Void) {
        self.model = model
        self.onDone = onDone
        state.step = step
        state.dir = 0
        state.finish = { [weak self] in self?.finish() }
    }

    func show() {
        let root = OnboardView(state: state, corner: model.corner, demo: model.demoRun) { [weak self] in self?.finish() }
        let host = NSHostingView(rootView: root)
        let w = OnboardWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 650), styleMask: [.borderless], backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.isMovableByWindowBackground = true
        w.level = .floating
        w.isReleasedWhenClosed = false
        w.title = "Welcome to Corner"
        host.frame = NSRect(x: 0, y: 0, width: 520, height: 650)
        w.contentView = host
        w.center()
        w.onEscape = { [weak self] in self?.finish() }
        window = w
        if GlassLook.adapt(w.frame.insetBy(dx: 30, dy: 30)) { model.changed() } // Glass: light or dark
        w.makeKeyAndOrderFront(nil)
        if !model.demoRun { NSApp.activate(ignoringOtherApps: true) }
    }

    /// Test renders: a name, a sleep need and three routines, as on Windows.
    func demoFill(_ step: Int) {
        state.name = "Sam"
        if step >= 2 {
            state.routines = [Routine(id: "d1", title: "Exercise", energy: "high", minutes: 30, days: "daily"),
                              Routine(id: "d2", title: "Reading", energy: "medium", minutes: 30, days: "daily"),
                              Routine(id: "d3", title: "Emails", energy: "low", minutes: 20, days: "daily")]
        }
        if step >= 1 { state.need = 7.5 }
    }

    /// Keeps what was entered (also when the window is closed early) and opens the corner panel.
    func finish() {
        if finished { return }
        finished = true
        let n = state.name.trimmed
        if !n.isEmpty { Profile.name = n }
        Settings.set("SleepNeed", state.need)
        Routines.load()
        if let d = Routines.data {
            d.routines.append(contentsOf: state.routines)
            Routines.save()
        }
        Settings.set("Onboarded", 1)
        model.refreshEnergy(opening: false)
        let w = window
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.22
            w?.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            w?.orderOut(nil)
            self?.onDone()
        })
    }
}

final class OnboardWindow: NSWindow {
    var onEscape: () -> Void = {}
    override var canBecomeKey: Bool { return true }
    override var canBecomeMain: Bool { return true }
    override func cancelOperation(_ sender: Any?) { onEscape() }
}

struct OnboardView: View {
    @ObservedObject var state: OnboardState
    let corner: Int
    let demo: Bool
    let close: () -> Void
    @State private var shown = false
    @ObservedObject private var watch = SkinWatch.shared // the card is made again when the skin changes

    static func fk(_ s: CGFloat) -> CGFloat { return (s * Fonts.k * 2).rounded() / 2 }

    var body: some View {
        ZStack {
            if demo { T.backdrop }
            card
                .id(watch.skin)
                .padding(30)
                .scaleEffect(shown || demo ? 1 : 0.94)
                .opacity(shown || demo ? 1 : 0)
        }
        .frame(width: 520, height: 650)
        .onAppear { withAnimation(.spring(response: 0.45, dampingFraction: 0.7)) { shown = true } }
        .environment(\.m, M(slim: false))
    }

    var card: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                HStack(spacing: 6) {
                    ForEach(0..<4, id: \.self) { i in
                        Capsule().fill(i <= state.step ? T.strong : T.ctl).frame(width: i == state.step ? 24 : 8, height: 8)
                    }
                }
                Spacer()
                IconHit(d: Icons.cross, color: T.muted, hotColor: T.text, size: 10, box: 28, th: 1.5) { close() }.help("Skip setup")
            }
            .padding(.bottom, 18)
            ZStack(alignment: .topLeading) {
                page(state.step)
                    .id(state.step)
                    .transition(.asymmetric(insertion: .offset(x: CGFloat(state.dir) * 46).combined(with: .opacity),
                                            removal: .offset(x: CGFloat(-state.dir) * 40).combined(with: .opacity)))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            HStack {
                ObButton(text: "Back", main: false) { state.go(state.step - 1) }.opacity(state.step == 0 ? 0 : 1).disabled(state.step == 0)
                Spacer()
                ObButton(text: state.step == 3 ? "Start" : (state.step == 2 && state.routines.isEmpty ? "Skip for now" : "Continue"), main: true) { state.next() }
            }
            .padding(.top, 18)
        }
        .padding(.horizontal, 34).padding(.top, 26).padding(.bottom, 28)
        .background(PanelBackground(radius: 26))
    }

    @ViewBuilder func page(_ i: Int) -> some View {
        switch i {
        case 0: welcome
        case 1: sleep
        case 2: routinesPage
        default: done
        }
    }

    func title(_ s: String) -> some View {
        Text(s).font(Fonts.ui(OnboardView.fk(29), true)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
            .lineSpacing(OnboardView.fk(29) * 0.1).padding(.bottom, 12)
    }

    func bodyText(_ s: String) -> some View {
        Text(s).font(Fonts.ui(OnboardView.fk(14.5))).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
            .lineSpacing(OnboardView.fk(14.5) * 0.4).padding(.bottom, 6)
    }

    var welcome: some View {
        VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .topLeading) {
                Glyph(d: "M0,46 C18,46 26,6 52,6 C78,6 84,40 104,40 C124,40 132,14 154,14 C176,14 182,48 200,52", color: T.strong, th: 3.5, w: 200, h: 56)
                Circle().fill(T.accent).overlay(Circle().stroke(T.strong, lineWidth: 2.5)).frame(width: 12, height: 12).padding(.leading, 46)
            }
            .padding(.leading, 2).padding(.top, 6).padding(.bottom, 22)
            title("Hi, I'm Corner.")
            bodyText("I plan your day around your energy: the hard tasks on your peaks, the easy ones in your afternoon dip, and a reminder before each.")
            Text("What should I call you?").font(Fonts.ui(OnboardView.fk(13.5), true)).foregroundColor(T.text).padding(.leading, 2).padding(.top, 26).padding(.bottom, 10)
            ObInput(hint: "Your name", text: $state.name, autofocus: !demo) { state.next() }
        }
    }

    var sleep: some View {
        VStack(alignment: .leading, spacing: 0) {
            title(state.name.isEmpty ? "How much sleep do you need?" : "How much sleep do you need, " + state.name + "?")
            bodyText("Most adults need 7 to 9 hours a night. Corner uses this to work out your sleep debt and the shape of each day's energy.")
            HStack(spacing: 0) {
                ObRound(d: "M1,5 H9") { state.need = max(5, state.need - 0.25) }
                Text(AppModel.durShort(state.need)).font(Fonts.ui(OnboardView.fk(56), true)).foregroundColor(T.text).frame(minWidth: 210)
                ObRound(d: "M1,5 H9 M5,1 V9") { state.need = min(11, state.need + 0.25) }
            }
            .frame(maxWidth: .infinity)
            .padding(.top, 34).padding(.bottom, 10)
            Text("Not sure? Keep 8 hours. Once Corner knows you, it suggests a figure from how fresh you feel.")
                .font(Fonts.ui(OnboardView.fk(12.5))).foregroundColor(T.muted).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity)
                .padding(.horizontal, 20).padding(.top, 12)
        }
    }

    var routinesPage: some View {
        VStack(alignment: .leading, spacing: 0) {
            title("What do you do most days?")
            bodyText("Add your daily routines, how much effort each takes and how long. Each morning, send them to your tasks and Plan my day fits them to your energy.")
            ObInput(hint: "e.g. Exercise, Reading, Emails", text: $state.entry, autofocus: !demo) {
                if state.entry.trimmed.isEmpty { state.next() } else { state.addTyped() }
            }
            .padding(.top, 20)
            HStack(spacing: 0) {
                HStack(spacing: 5) {
                    ForEach(0..<3, id: \.self) { i in
                        let lv = ["low", "medium", "high"][i]
                        let on = state.effort == lv
                        HStack(spacing: 6) {
                            Bars(level: i + 1, onColor: on ? T.onStrong : nil)
                            Text(["Low", "Medium", "High"][i]).font(Fonts.ui(OnboardView.fk(12))).foregroundColor(on ? T.onStrong : T.text)
                        }
                        .padding(.leading, 11).padding(.trailing, 12).padding(.top, 6).padding(.bottom, 7)
                        .plate(on ? .strong : .field)
                        .contentShape(Capsule())
                        .onTapGesture { state.effort = lv }
                        .help(["Low", "Medium", "High"][i] + " effort")
                    }
                }
                HStack(spacing: 6) {
                    MiniBox(text: $state.minutes, width: 26, surface: .field) { state.addTyped() }
                    Text("min").font(Fonts.ui(OnboardView.fk(12.5))).foregroundColor(T.muted)
                }
                .padding(.leading, 10)
                Spacer(minLength: 0)
                ObButton(text: "Add", main: false) { state.addTyped() }
            }
            .padding(.top, 10)
            FlowRow(items: picks(), hs: 6, vs: 6).padding(.top, 14)
            VStack(alignment: .leading, spacing: 6) {
                if state.routines.isEmpty {
                    Text("Nothing yet. You can also add routines later.").font(Fonts.ui(OnboardView.fk(12.5))).foregroundColor(T.faint).padding(.leading, 2).padding(.top, 8)
                }
                FitScroll(maxHeight: 150) {
                    VStack(spacing: 6) {
                        ForEach(state.routines, id: \.id) { r in ObRoutineRow(r: r) { withAnimation { state.routines.removeAll { $0 === r } } } }
                    }
                }
            }
            .padding(.top, 6)
        }
    }

    func picks() -> [AnyView] {
        var v: [AnyView] = [Text("Try").font(Fonts.ui(OnboardView.fk(12))).foregroundColor(T.faint).padding(.leading, 2).padding(.trailing, 2).padding(.top, 5).any()]
        for (t, e, mm) in [("Exercise", "high", 30), ("Reading", "medium", 30), ("Emails", "low", 20), ("Deep work", "high", 90)] {
            v.append(QuickPick(text: "+ " + t) { if !state.routines.contains(where: { $0.title == t }) { state.add(t, e, mm) } }.any())
        }
        return v
    }

    var done: some View {
        let wheres = ["top-right", "top-left", "bottom-right", "bottom-left"]
        let n = state.routines.count
        let count: String = n == 0 ? "no routines yet" : (n == 1 ? "1 routine" : String(n) + " routines")
        let summary: String = AppModel.durShort(state.need) + " of sleep  ·  " + count
        return VStack(alignment: .leading, spacing: 0) {
            DoneBadge(demo: demo).padding(.top, 10).padding(.bottom, 24)
            title(state.name.isEmpty ? "You're all set." : "You're all set, " + state.name + ".")
            bodyText("Corner lives in the " + wheres[max(0, min(3, corner))] + " corner of your screen. Move your pointer there any time to see your tasks.")
            bodyText("Next, tell it how you slept last night, and it will draw today's energy curve.")
            Text(summary).font(Fonts.ui(OnboardView.fk(12.5), true)).foregroundColor(T.text)
                .padding(.horizontal, 14).padding(.top, 6).padding(.bottom, 7)
                .plate(.soft)
                .padding(.top, 20)
        }
    }
}

struct DoneBadge: View {
    let demo: Bool
    @State private var popped = false
    var body: some View {
        ZStack {
            Circle().fill(T.accent)
            Glyph(d: "M2,13 L11,22 L28,3", color: T.onAccent, th: 4.5, w: 30, h: 25)
        }
        .frame(width: 78, height: 78)
        .scaleEffect(popped || demo ? 1 : 0.4)
        .onAppear { withAnimation(.spring(response: 0.5, dampingFraction: 0.55).delay(0.25)) { popped = true } }
    }
}

struct ObRoutineRow: View {
    let r: Routine
    let remove: () -> Void
    @State private var hot = false
    var body: some View {
        let lvl = max(1, [nil, "low", "medium", "high"].firstIndex(where: { $0 == r.energy }) ?? 1)
        return HStack(spacing: 0) {
            Bars(level: lvl).padding(.trailing, 12)
            Text(r.title).font(Fonts.ui(OnboardView.fk(13.5))).foregroundColor(T.text).lineLimit(1)
            Spacer(minLength: 0)
            Text((TaskTags.short(r.energy) ?? "") + " · " + TaskTags.len(r.minutes)).font(Fonts.ui(OnboardView.fk(12))).foregroundColor(T.muted)
                .padding(.leading, 10).padding(.trailing, 8)
            Glyph(d: Icons.cross, color: hot ? T.danger : T.faint, th: 1.3, w: 10, h: 10)
                .frame(width: 22, height: 22)
                .contentShape(Rectangle())
                .onHover { hot = $0 }
                .onTapGesture { remove() }
                .help("Remove")
        }
        .padding(.leading, 14).padding(.trailing, 10).padding(.top, 9).padding(.bottom, 10)
        .plate(.field, radius: 14, rim: 14)
    }
}

struct QuickPick: View {
    let text: String
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Text(text).font(Fonts.ui(OnboardView.fk(12))).foregroundColor(T.text)
            .padding(.horizontal, 12).padding(.top, 5).padding(.bottom, 6)
            .plate(hot ? .field : .lift)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

struct ObButton: View {
    let text: String
    let main: Bool
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Text(text).font(Fonts.ui(OnboardView.fk(14), main)).foregroundColor(main ? T.onMain : T.text)
            .padding(.horizontal, main ? 26 : 18).padding(.top, 11).padding(.bottom, 12)
            .plate(main ? .main : (hot ? .field : .lift))
            .opacity(main && hot ? 0.9 : 1)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

struct ObRound: View {
    let d: String
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Glyph(d: d, color: T.text, th: 1.8, w: 14, h: 14)
            .frame(width: 48, height: 48)
            .background(Circle().fill(hot ? T.lift : T.field))
            .contentShape(Circle())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}

struct ObInput: View {
    let hint: String
    @Binding var text: String
    let autofocus: Bool
    let onSubmit: () -> Void
    @FocusState private var focused: Bool
    var body: some View {
        HintField(hint: hint, text: Binding(get: { text }, set: { text = String($0.prefix(60)) }), size: 16, focus: $focused, onSubmit: onSubmit)
            .padding(.horizontal, 18).padding(.top, 11).padding(.bottom, 12)
            .plate(.field)
            .overlay(Capsule().strokeBorder(focused ? T.strong : Color.clear, lineWidth: 1.5))
            .onAppear { if autofocus { DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { focused = true } } }
    }
}
