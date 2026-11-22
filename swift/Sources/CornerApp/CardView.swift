// Corner for macOS - the card: its header, and what sits under it in the corner panel and in Full Mode; the trays
// above the list (the 1-10 check-in, the time's-up prompt, the next task) and the row of chips. Port of Build and
// UpdateHeader (Corner.cs), FocusLayout, DrawFocus and DrawActions (Focus.cs), ShowFeedback (Ui.cs).
import AppKit
import SwiftUI
import CornerCore

struct HeaderHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

struct CardView: View {
    @EnvironmentObject var model: AppModel
    @State private var headH: CGFloat = 0

    var body: some View {
        let m = model.m
        let showHeader = !model.promptOnly && !model.isBare
        let guess: CGFloat = model.full ? 72 : 48
        let headerH: CGFloat = showHeader ? (headH > 0 ? headH : guess) : 0
        let bodyMax = max(150, model.maxCardH - headerH)
        VStack(alignment: .leading, spacing: 0) {
            if showHeader {
                HeaderView()
                    .background(GeometryReader { g in Color.clear.preference(key: HeaderHeightKey.self, value: g.size.height) })
            }
            if model.settingsOpen {
                FitScroll(maxHeight: bodyMax - m.side) { SettingsPage() }.padding(.bottom, m.side)
            } else if model.full {
                FullBody(maxHeight: bodyMax)
            } else {
                CornerBody(maxHeight: bodyMax, alone: !showHeader)
            }
        }
        .frame(width: model.full ? fullW : cardW, alignment: .topLeading)
        .frame(minHeight: model.full ? min(fullMinH, model.maxCardH) : 0, alignment: .top)
        .background(PanelBackground(radius: m.sp(32, T.glass ? 28 : 26)))
        .environment(\.m, m)
        .onPreferenceChange(HeaderHeightKey.self) { v in if abs(v - headH) > 0.5 { headH = v } }
        .id(model.full ? "full" : "corner")
    }
}

/// The panel itself: pale sage with a darker edge, or (Glass) the frosted view of what is behind under a cool tint.
struct PanelBackground: View {
    let radius: CGFloat
    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        ZStack(alignment: .top) {
            if T.glass {
                Frost().clipShape(shape)
                shape.fill(T.glassTint)
                T.glassSheen.frame(height: 54).clipShape(shape)
                shape.stroke(T.glassRim, lineWidth: 1)
            } else {
                shape.fill(T.bg)
                shape.stroke(T.edge, lineWidth: 1)
            }
        }
        .compositingGroup()
        .shadow(color: T.shadow, radius: 16, x: 0, y: 10)
    }
}

/// Lets the header move the window: a press there starts the system's window drag.
struct DragArea: NSViewRepresentable {
    final class V: NSView {
        override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { return true }
    }
    func makeNSView(context: Context) -> V { return V() }
    func updateNSView(_ v: V, context: Context) {}
}

// ---------------------------------------------------------------- header

struct HeaderView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let cell = m.sp(34, 26)
        let now = Date()
        let open = model.openCount
        let all = model.data.tasks.count
        let count = open > 0 ? String(open) + " open" : (all > 0 ? "all done" : "")
        HStack(alignment: .center, spacing: 0) {
            if model.full {
                HeadIcon(d: Icons.back, th: 1.5, size: 10, tip: "Back to the corner", cell: cell) { model.shell?.exitFull() }
                    .padding(.trailing, 12)
                VStack(alignment: .leading, spacing: 0) {
                    Text(HeaderView.fmt("EEEE", now)).font(m.head(22)).foregroundColor(T.text)
                    Text(HeaderView.fmt("MMMM d", now) + (count.isEmpty ? "" : "  ·  " + count))
                        .font(m.font(12)).foregroundColor(T.muted).lineLimit(1)
                }
            } else {
                HStack(alignment: .firstTextBaseline, spacing: 0) {
                    Text(HeaderView.fmt("EEE d MMM", now)).font(m.font(13, true)).foregroundColor(T.text)
                    if !count.isEmpty { Text("   " + count).font(m.font(11)).foregroundColor(T.muted) }
                }
                .lineLimit(1)
                .padding(.leading, m.padX)
            }
            Spacer(minLength: 8)
            HStack(spacing: m.sp(6, 4)) {
                RingDisc(cell: cell)
                HeadIcon(d: Icons.gear, th: 1.2, size: 12, tip: "Settings", cell: cell) { model.toggleSettings() }
                if !model.full {
                    HeadIcon(d: Icons.full, th: 1.3, size: 10, tip: "Full Mode", cell: cell) { model.shell?.enterFull() }
                }
                HeadIcon(d: Icons.cross, th: 1.4, size: 10, tip: "Close", cell: cell) { model.closeButton() }
            }
        }
        .padding(.horizontal, m.side)
        .padding(.top, m.sp(16, 11))
        .padding(.bottom, m.sp(12, 9))
        .background(DragArea())
    }

    static func fmt(_ f: String, _ d: Date) -> String {
        let df = DateFormatter()
        df.locale = Locale(identifier: "en_US_POSIX")
        df.dateFormat = f
        return df.string(from: d)
    }
}

/// A round white button in the header.
struct HeadIcon: View {
    let d: String
    let th: CGFloat
    let size: CGFloat
    let tip: String
    let cell: CGFloat
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Glyph(d: d, color: T.text, th: th, w: size, h: size)
            .frame(width: cell, height: cell)
            .background(Circle().fill(hot ? T.lift : T.field))
            .overlay(Rim(radius: cell / 2))
            .contentShape(Circle())
            .onHover { hot = $0 }
            .onTapGesture { action() }
            .help(tip)
    }
}

/// How much of the list is done: a ring on a white disc.
struct RingDisc: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let cell: CGFloat
    var body: some View {
        let all = model.data.tasks.count
        let done = model.data.tasks.filter { $0.done }.count
        let f: CGFloat = all == 0 ? 0 : CGFloat(done) / CGFloat(all)
        let k = m.sp(0.75, 0.58)
        ZStack {
            Circle().stroke(T.ring, lineWidth: 3).frame(width: 22, height: 22)
            Circle().trim(from: 0, to: f)
                .stroke(T.strong, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                .frame(width: 19, height: 19)
                .rotationEffect(.degrees(-90))
        }
        .frame(width: 24, height: 24)
        .scaleEffect(k)
        .frame(width: cell, height: cell)
        .background(Circle().fill(T.field))
        .overlay(Rim(radius: cell / 2))
        .help(all == 0 ? "Progress: fills as tasks are ticked off" : "Progress: " + String(done) + " of " + String(all) + " tasks done")
    }
}

// ---------------------------------------------------------------- the corner panel

struct CornerBody: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat
    let alone: Bool

    var body: some View {
        let tasksView = model.tab == 0
        let rest = model.isBare
        let prompt = model.promptOnly
        // the panel's bottom (and top, with no header) margin sits outside the scrolling part, so what scrolls
        // stops short of the rounded corners
        FitScroll(maxHeight: maxHeight - m.side - (alone ? m.side : 0)) {
            VStack(alignment: .leading, spacing: m.gapY) {
                if model.checkInOpen && !rest && !prompt { CheckInView() }
                if model.feedbackOpen { FeedbackView() }
                if model.alertTask != nil { AlertView() }
                if tasksView && !prompt { FocusView() }
                if !rest && !prompt { ActionRow() }
                if tasksView && model.addOpen && !rest && !prompt { AddBox() }
                if tasksView && model.allOpen && !rest && !prompt { TaskListCard() }
                if !tasksView && !prompt { EnergyView() }
            }
            .padding(.leading, m.side)
            .padding(.trailing, m.sideMid)
        }
        .padding(.top, alone ? m.side : 0)
        .padding(.bottom, m.side)
    }
}

// ---------------------------------------------------------------- Full Mode: tasks on the left, energy on the right

struct FullBody: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            FitScroll(maxHeight: maxHeight - m.side) {
                VStack(alignment: .leading, spacing: m.gapY) {
                    if model.checkInOpen { CheckInView() }
                    if model.feedbackOpen { FeedbackView() }
                    if model.alertTask != nil { AlertView() }
                    FocusView()
                    AddBox()
                    TaskListCard()
                }
                .padding(.leading, m.side)
                .padding(.trailing, m.sideMid)
            }
            .padding(.bottom, m.side)
            .frame(width: fullW / 2)
            FitScroll(maxHeight: maxHeight - m.side) {
                EnergyView()
                    .padding(.leading, m.sideMid)
                    .padding(.trailing, m.side)
            }
            .padding(.bottom, m.side)
            .frame(width: fullW / 2)
        }
    }
}

// ---------------------------------------------------------------- the next task

struct FocusView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let up = model.upcoming()
        CardBox(surface: T.tray) {
            if let t = up.first {
                FocusTop(t: t)
                FocusTitle(t: t)
                HStack(spacing: 6) {
                    TimerPill(t: t, big: true)
                    if up.count > 1 { ThenChip(n: up[1]) }
                    Spacer(minLength: 0)
                }
                .padding(.top, m.sp(11, 9))
            } else {
                Text(model.data.tasks.isEmpty ? "Nothing here yet." : "All done. Enjoy it.")
                    .font(m.font(13)).foregroundColor(T.faint)
            }
        }
    }
}

struct FocusTop: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    @State private var resetHot = false

    var body: some View {
        let timed = model.timerId == t.id
        let late = t.overdue && !timed
        let state = timed ? (model.timerRunning ? "Now" : "Paused") : "Next"
        var when: String? = nil
        if t.hasTime, let d = t.dueAt { when = (late ? "overdue " : "") + EnergyDay.clock(d) }
        return HStack(alignment: .center, spacing: 8) {
            Caps(text: state, color: timed && model.timerRunning ? T.text : nil)
            if let w = when {
                Text(w).font(late ? Fonts.lean(m.z(11)) : m.font(11)).foregroundColor(late ? T.danger : T.muted)
            }
            Spacer(minLength: 0)
            if timed {
                Text("Reset").font(m.font(11)).foregroundColor(resetHot ? T.text : T.muted)
                    .onHover { resetHot = $0 }
                    .onTapGesture { model.resetTimer() }
                    .help("Put the timer back to " + String(AppModel.durationMin(t)) + " min")
            }
        }
        .padding(.bottom, m.sp(9, 7))
    }
}

struct FocusTitle: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    @State private var hot = false

    var body: some View {
        let ck = m.sp(22, 20)
        let size = m.z(m.sp(22, 18))
        HStack(alignment: .top, spacing: 0) {
            Circle()
                .stroke(T.text, lineWidth: 1.5)
                .background(Circle().fill(hot ? T.soft : Color.clear))
                .frame(width: ck, height: ck)
                .frame(width: ck + m.sp(11, 9), height: max(size * 1.2, ck), alignment: .leading)
                .contentShape(Rectangle())
                .onHover { hot = $0 }
                .onTapGesture { model.completeTask(t) }
                .help("Mark as done")
            Text(t.title)
                .font(Fonts.ui(size, true))
                .foregroundColor(T.text)
                .fixedSize(horizontal: false, vertical: true)
                .help(AppModel.infoText(t))
            Spacer(minLength: 0)
        }
    }
}

struct ThenChip: View {
    @Environment(\.m) var m
    let n: TaskItem
    var body: some View {
        var text = "then " + n.title
        if n.hasTime, let d = n.dueAt, d.dateOnly <= Date().dateOnly { text += ", " + EnergyDay.clock(d) }
        return Text(text)
            .font(m.font(11.5)).foregroundColor(T.muted).lineLimit(1).truncationMode(.tail)
            .padding(.horizontal, m.sp(14, 12))
            .frame(height: m.sp(34, 30))
            .background(Capsule().fill(T.field))
            .overlay(Rim(radius: 15))
            .help("Then: " + n.title + "\n" + AppModel.infoText(n))
    }
}

/// The countdown beside a task. Click to start; click again to pause or carry on.
struct TimerPill: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    let big: Bool
    @State private var hot = false

    var body: some View {
        let mine = model.timerId == t.id
        let running = mine && model.timerRunning
        let ground: Color = big ? (running ? T.accent : T.strong) : (mine ? T.accent : Color.clear)
        let ink: Color = big ? (running ? T.onAccent : T.onStrong) : (mine ? T.onAccent : (hot ? T.text : T.muted))
        let size = m.z(big ? 13 : 11)
        let semi = big || mine
        HStack(spacing: big ? 7 : 5) {
            Glyph(d: running ? Icons.pause : Icons.play, color: ink, th: big ? 1.6 : 1.3, w: 9, h: 10, fill: big && !running ? ink : nil)
            Text(model.timerText(t))
                .font(Fonts.ui(size, semi))
                .foregroundColor(ink)
                .frame(minWidth: Fonts.width("00:00", size, true), alignment: .trailing)
        }
        .padding(.leading, big ? m.sp(13, 11) : 7)
        .padding(.trailing, big ? m.sp(16, 14) : 8)
        .padding(.vertical, big ? 0 : 2)
        .frame(height: big ? m.sp(34, 30) : nil)
        .background(Capsule().fill(ground))
        .opacity(big && hot ? 0.9 : 1)
        .contentShape(Capsule())
        .onHover { hot = $0 }
        .onTapGesture { model.timerClick(t) }
        .help(running ? "Pause the timer" : (mine ? "Carry on" : "Start a " + String(AppModel.durationMin(t)) + "-minute timer"))
    }
}

// ---------------------------------------------------------------- the row of chips

struct ActionRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let tasks = model.tab == 0
        let listOn = tasks && model.allOpen
        let ink: Color = listOn ? T.onStrong : T.text
        HStack(spacing: 6) {
            Chip(on: listOn, action: {
                if !tasks { model.setTab(0); return }
                model.allOpen.toggle()
                Settings.set("AllOpen", model.allOpen ? 1 : 0)
                model.changed()
                model.shell?.contentChanged()
            }) {
                HStack(spacing: 0) {
                    Glyph(d: tasks ? Icons.lines : Icons.left, color: ink, th: 1.5, w: 14, h: 14)
                    Text(tasks ? "All tasks" : "Tasks").font(m.font(12)).foregroundColor(ink).padding(.leading, 7).padding(.trailing, 6)
                    if tasks { Text(String(model.openCount)).font(m.font(11)).foregroundColor(listOn ? T.onStrongSoft : T.muted) }
                    Spacer(minLength: 0)
                    if tasks { Glyph(d: listOn ? Icons.down : Icons.right, color: ink, th: 1.5, w: 14, h: 14) }
                }
                .padding(.horizontal, 11)
            }
            RoundChip(d: Icons.plus, on: tasks && model.addOpen, tip: "Add a task") {
                if !tasks { model.addOpen = true; model.setTab(0) } else { model.addOpen.toggle() }
                if model.addOpen { model.focusInput += 1 }
                model.changed()
                model.shell?.contentChanged()
            }
            RoundChip(d: Icons.wave, on: !tasks, tip: "Energy") { model.setTab(tasks ? 1 : 0) }
        }
        .frame(height: 30)
    }
}

struct RoundChip: View {
    let d: String
    let on: Bool
    let tip: String
    let action: () -> Void
    var body: some View {
        Chip(on: on, width: 30, action: action) {
            Glyph(d: d, color: on ? T.onStrong : T.text, th: 1.5, w: 14, h: 14)
        }
        .help(tip)
    }
}

// ---------------------------------------------------------------- the add box

struct AddBox: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @FocusState private var focused: Bool

    var body: some View {
        let preview = model.inputPreview
        HStack(spacing: 0) {
            Glyph(d: Icons.plus, color: T.muted, th: 1.5, w: 14, h: 14)
                .padding(.leading, m.padX - 2)
                .padding(.trailing, m.sp(9, 7))
            HintField(hint: "Add a task  ·  try “gym tomorrow 7am”", text: Binding(get: { model.input }, set: { model.input = $0 }),
                      size: 13.5, focus: $focused,
                      onSubmit: { model.addTask(); focused = true },
                      onEscape: {
                          if !model.input.isEmpty { model.input = "" }
                          else if model.addOpen && !model.full { model.addOpen = false; model.changed(); model.shell?.contentChanged() }
                          else { model.hidePanel() }
                      })
            if !preview.isEmpty {
                Text(preview).font(m.font(11)).foregroundColor(T.text).lineLimit(1).fixedSize()
                    .padding(.horizontal, 7).padding(.top, 2).padding(.bottom, 3)
                    .background(Capsule().fill(T.soft))
                    .padding(.leading, 8).padding(.trailing, m.sp(7, 5))
            }
        }
        .frame(height: m.sp(40, 32))
        .background(Capsule().fill(T.field))
        .overlay(Capsule().strokeBorder(focused ? T.strong : Color.clear, lineWidth: 1.5))
        .overlay(Rim(radius: 20))
        .onAppear { if model.addOpen && !model.full && !model.demoRun { focused = true } }
        .onChange(of: model.focusInput) { _ in focused = true }
    }
}

// ---------------------------------------------------------------- prompts

/// "How fresh do you feel?": ten cells; the one under the mouse turns lime.
struct FeedbackView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                Text("How fresh do you feel?").font(m.font(12.5, true)).foregroundColor(T.text).lineLimit(1).fixedSize()
                if m.slim {
                    Text("1 drained · 10 fresh").font(m.font(10)).foregroundColor(T.muted).lineLimit(1)
                        .padding(.leading, 8).padding(.trailing, 4)
                }
                Spacer(minLength: 0)
                IconHit(d: Icons.cross) { model.answerFeedback("skipped") }.help("Skip")
            }
            .padding(.trailing, 2)
            .padding(.bottom, m.sp(9, 7))
            HStack(spacing: 3) {
                ForEach(1...10, id: \.self) { i in ScoreCell(n: i) }
            }
            if !m.slim {
                HStack {
                    Text("drained").font(m.font(10.5)).foregroundColor(T.muted)
                    Spacer()
                    Text("fresh").font(m.font(10.5)).foregroundColor(T.muted)
                }
                .padding(.top, 6).padding(.horizontal, 2)
            }
            if model.promptOnly && !model.alertHolds {
                // popped up by itself: a thin line runs out over the 15 seconds it stays
                TimelineView(.periodic(from: Date(), by: 0.1)) { ctx in
                    let f = max(0, 1 - ctx.date.timeIntervalSince(model.feedbackShownAt) / 15)
                    GeometryReader { g in
                        Capsule().fill(T.strong).frame(width: g.size.width * CGFloat(f), height: 3)
                    }
                    .frame(height: 3)
                }
                .padding(.top, m.sp(10, 8))
            }
        }
        .padding(.leading, m.padX).padding(.trailing, m.padX - 2)
        .padding(.top, m.padY - 1).padding(.bottom, m.padY)
        .background(RoundedRectangle(cornerRadius: m.radius, style: .continuous).fill(T.tray))
        .overlay(Rim(radius: m.radius))
    }
}

struct ScoreCell: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let n: Int
    @State private var hot = false
    var body: some View {
        Text(String(n))
            .font(m.font(11.5, hot))
            .foregroundColor(T.text)
            .frame(maxWidth: .infinity)
            .frame(height: m.sp(34, 28))
            .background(RoundedRectangle(cornerRadius: m.sp(11, 9), style: .continuous).fill(hot ? T.accent : T.lift))
            .contentShape(Rectangle())
            .onHover { hot = $0 }
            .onTapGesture { model.answerFeedback(String(n)) }
    }
}

/// Time's up, or ready for the next one: the one dark box, with the answer that moves things on in lime.
struct AlertView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        if let t = model.alertTask {
            VStack(alignment: .leading, spacing: 0) {
                Caps(text: model.alertKind == 1 ? "Time's up" : "Ready for the next one?", color: T.accent).padding(.bottom, 4)
                Text(t.title).font(m.head(15)).foregroundColor(T.onStrong).fixedSize(horizontal: false, vertical: true)
                    .padding(.trailing, 4).padding(.bottom, m.sp(11, 9))
                FlowRow(items: buttons(t), hs: m.slim ? 3 : 5, vs: 5)
            }
            .padding(.leading, m.padX + 2).padding(.trailing, m.padX)
            .padding(.top, m.padY + 2).padding(.bottom, m.padY + 2 - m.sp(6, 5))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: m.radius, style: .continuous).fill(T.strong))
        }
    }

    func buttons(_ t: TaskItem) -> [AnyView] {
        if model.alertKind == 1 {
            return [
                Btn(text: "Done", kind: 0, on: 2) { model.alertKind = 0; model.completeTask(t) }.any(),
                Btn(text: m.slim ? "+5m" : "+5 min", kind: 1, on: 2) { model.extend(t, 5) }.any(),
                Btn(text: m.slim ? "+15m" : "+15 min", kind: 1, on: 2) { model.extend(t, 15) }.any(),
                Btn(text: "Restart", kind: 2, on: 2) { model.startTimer(t); model.clearAlert() }.any(),
                Btn(text: "Pause", kind: 2, on: 2) { model.stopTimer(); model.clearAlert() }.any(),
            ]
        }
        return [
            Btn(text: "Start " + String(AppModel.durationMin(t)) + " min", kind: 0, on: 2) { model.startTimer(t); model.clearAlert() }.any(),
            Btn(text: "Not yet", kind: 2, on: 2) { model.clearAlert() }.any(),
        ]
    }
}
