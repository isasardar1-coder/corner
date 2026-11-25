// Corner for macOS - the card: its header, and what sits under it in the corner panel and in Full Mode; the trays
// above the list (the 1-10 check-in, the time's-up prompt, the next task) and the row of chips. Port of Build and
// UpdateHeader (Corner.cs), FocusLayout, DrawFocus and DrawActions (Focus.cs), ArrangeFull and FitStack
// (FullLayout.cs), ShowFeedback (Ui.cs).
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
                    .id(T.look) // made again when the skin changes, as what scrolls under it is (FitScroll)
                    .background(GeometryReader { g in Color.clear.preference(key: HeaderHeightKey.self, value: g.size.height) })
                    .modifier(model.grow(.header)) // grows in when the pointer comes onto the panel (Genie.swift)
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

/// The panel itself: pale sage with a darker edge; or (Glass) a sheet of clear glass over what is behind it,
/// light or dark (GlassSheet, Glass.swift); or (Metal, Ceramic, Paper) the skin's sheet: a brushed plate, a slab of stoneware, a card with a grain
/// (Material.swift). The notice and the welcome window are made of the same.
struct PanelBackground: View {
    let radius: CGFloat
    var skin: Int = T.skin // (kept, so that a change of skin draws it again)
    var dark: Bool = T.dark // (and a change from light glass to dark)
    var body: some View {
        if skin == 1 {
            GlassSheet(radius: radius, dark: dark)
        } else {
            sheet
        }
    }

    var sheet: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: skin >= 2 ? .circular : .continuous)
        return ZStack(alignment: .top) {
            if skin >= 2 {
                shape.fill(T.bg)
                SheetGrain(skin: skin, radius: radius)
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
        // Full Mode: back, the day and date, then the ring, the gear and Close. The corner panel: the ring, Full Mode
        // and Close alone (settings are opened from Full Mode).
        HStack(alignment: .center, spacing: 0) {
            if model.full {
                HeadIcon(d: Icons.back, th: 1.5, size: 10, tip: "Back to the corner", cell: cell) { model.shell?.exitFull() }
                    .padding(.trailing, 12)
                VStack(alignment: .leading, spacing: 0) {
                    Text(HeaderView.fmt("EEEE", now)).font(m.head(22)).foregroundColor(T.text)
                    Text(HeaderView.fmt("MMMM d", now) + (count.isEmpty ? "" : "  ·  " + count))
                        .font(m.font(12)).foregroundColor(T.muted).lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            HStack(spacing: m.sp(6, 4)) {
                RingDisc(cell: cell)
                if model.full {
                    HeadIcon(d: Icons.gear, th: 1.2, size: 12, tip: "Settings", cell: cell) { model.toggleSettings() }
                } else {
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
            .plate(hot ? .lift : .field, rim: cell / 2, disc: true)
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
        .plate(.field, rim: cell / 2, disc: true)
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
        // At rest it is the next task alone (with a time's-up prompt over it, if one waits); the pointer brings in
        // the header, the check-ins and the chips. The chart slides down under the chips while the add box and the
        // list fold away, and back (Genie.swift): `open` is 0 on the tasks side, 1 on the energy side, and between
        // the two only while it slides.
        let open: CGFloat = model.energyOpen
        let tasksView = open < 1
        let rest = model.isBare
        let prompt = model.promptOnly
        // (the header growing in under the pointer takes the top margin's place)
        let lead: CGFloat = alone ? m.side : m.side * (1 - model.revealK)
        // the panel's bottom (and top, with no header) margin sits outside the scrolling part, so what scrolls
        // stops short of the rounded corners
        FitScroll(maxHeight: maxHeight - m.side - (alone ? m.side : 0)) {
            // (each part carries the gap that follows it, so the gap grows and folds with the part; the last one's is taken off)
            VStack(alignment: .leading, spacing: 0) {
                if model.checkInOpen && !rest && !prompt { CheckInView().modifier(model.grow(.hover)) }
                if model.feedbackOpen { FeedbackView().modifier(model.grow(.hover)) }
                if model.alertTask != nil { AlertView().padding(.bottom, m.gapY) }
                if !prompt { FocusView().modifier(model.grow(.focus)) } // (it stays while the chart is open under the chips)
                if !rest && !prompt { ActionRow().modifier(model.grow(.hover)) }
                if tasksView && model.addOpen && !rest && !prompt { AddBox().modifier(model.grow(.tasks)) }
                if tasksView && model.allOpen && !rest && !prompt { TaskListCard().modifier(model.grow(.tasks)) }
                if open > 0 && !rest && !prompt { EnergyView().modifier(model.grow(.energy)) } // the chart alone
            }
            .padding(.bottom, -m.gapY)
            .padding(.leading, m.side)
            .padding(.trailing, m.sideMid)
        }
        .padding(.top, lead)
        .padding(.bottom, m.side)
    }
}

// ---------------------------------------------------------------- Full Mode: two equal columns

/// The tasks column and the energy column, side by side ("Full Mode sides" in Settings swaps them).
struct FullBody: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat

    var body: some View {
        let swapped: Bool = model.swapped
        HStack(alignment: .top, spacing: 0) {
            if swapped {
                FullEnergy(maxHeight: maxHeight, swapped: true)
                FullTasks(maxHeight: maxHeight, swapped: true)
            } else {
                FullTasks(maxHeight: maxHeight, swapped: false)
                FullEnergy(maxHeight: maxHeight, swapped: false)
            }
        }
    }
}

struct UsedHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value += nextValue() }
}

/// The tasks column: the prompts, the open tasks, the add box, then the plan. The list takes the room the others
/// leave and its card scrolls when it needs more; the plan may take up to half (FitStack in FullLayout.cs).
struct FullTasks: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat
    let swapped: Bool
    @State private var used: CGFloat = 0

    var body: some View {
        let gap: CGFloat = m.gapY
        let avail: CGFloat = maxHeight - m.side
        let planMax: CGFloat = max(120, avail * 0.5)
        let listMax: CGFloat = max(60, avail - used)
        let side: CGFloat = T.skin >= 2 ? 8 : 0 // the list's card: room beside it for its shadow (a made skin)
        let over: CGFloat = 8 // room inside the plan's scrolling part for its card's shadow (a made skin)
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 0) {
                if model.checkInOpen { CheckInView().padding(.bottom, gap) }
                if model.feedbackOpen { FeedbackView().padding(.bottom, gap) }
                if model.alertTask != nil { AlertView().padding(.bottom, gap) }
            }
            .background(GeometryReader { g in Color.clear.preference(key: UsedHeightKey.self, value: g.size.height) })
            FitScroll(maxHeight: listMax, round: m.radius, inset: side) { TaskListCard().padding(.horizontal, side) }
                .padding(.horizontal, -side)
            VStack(alignment: .leading, spacing: 0) {
                AddBox().padding(.top, gap)
                FitScroll(maxHeight: planMax) {
                    PlanCard().padding(.top, gap).padding(.horizontal, over)
                }
                .padding(.horizontal, -over)
            }
            .background(GeometryReader { g in Color.clear.preference(key: UsedHeightKey.self, value: g.size.height) })
        }
        .onPreferenceChange(UsedHeightKey.self) { v in if abs(v - used) > 0.5 { used = v } }
        .padding(.leading, swapped ? m.sideMid : m.side)
        .padding(.trailing, swapped ? m.side : m.sideMid)
        .padding(.bottom, m.side)
        .frame(width: fullW / 2, alignment: .top)
    }
}

/// The energy column: the next task with its timer, the chart, slept / debt / need, nap and rate, routines, then
/// the done tasks (EnergyView.swift). It scrolls as one.
struct FullEnergy: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat
    let swapped: Bool

    var body: some View {
        FitScroll(maxHeight: maxHeight - m.side) {
            EnergyView()
                .padding(.leading, swapped ? m.side : m.sideMid)
                .padding(.trailing, swapped ? m.sideMid : m.side)
        }
        .padding(.bottom, m.side)
        .frame(width: fullW / 2, alignment: .top)
    }
}

// ---------------------------------------------------------------- the next task

/// The next task, one line in both modes: its circle, the task, large, and its timer as a pill at the end.
/// A small line above it says "Now" or "Paused" while it is being timed (with "Reset" at its far end), and by
/// priority why this one is next. At rest (the corner panel before the pointer comes) it is the one line alone.
struct FocusView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let up = model.upcoming()
        CardBox(surface: .tray) {
            if let t = up.first {
                if hasTop(t) { FocusTop(t: t) }
                FocusLine(t: t)
            } else {
                Text(model.data.tasks.isEmpty ? "Nothing here yet." : "All done. Enjoy it.")
                    .font(m.font(13)).foregroundColor(T.faint)
            }
        }
    }

    func hasTop(_ t: TaskItem) -> Bool {
        if model.isBare { return false }
        return model.timerId == t.id || model.nextReason(t) != nil
    }
}

struct FocusTop: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    @State private var resetHot = false

    var body: some View {
        let prio = AppModel.priorityMode
        let timed = model.timerId == t.id
        let state: String = stateText(timed)
        return HStack(alignment: .center, spacing: 8) {
            Caps(text: state, color: timed && model.timerRunning ? T.text : nil)
            Spacer(minLength: 0)
            if timed && !prio {
                // a timer that has been started can be put back to the task's full length
                Text("Reset").font(m.font(11)).foregroundColor(resetHot ? T.text : T.muted)
                    .padding(.leading, 10)
                    .contentShape(Rectangle())
                    .onHover { resetHot = $0 }
                    .onTapGesture { model.resetTimer() }
                    .help("Put the timer back to " + String(AppModel.durationMin(t)) + " min")
            }
        }
        .padding(.bottom, m.sp(9, 7))
    }

    /// "Now" or "Paused" while it is being timed; else, by priority, "Next" with why this one is next.
    func stateText(_ timed: Bool) -> String {
        if timed { return model.timerRunning ? "Now" : "Paused" }
        if let reason = model.nextReason(t) { return "Next · " + reason }
        return "Next"
    }
}

/// The circle, the task and, at the end of its own line, the timer. The circle and the pill are centred on the
/// title's first line.
struct FocusLine: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    @State private var hot = false

    var body: some View {
        let ck: CGFloat = m.sp(22, 20)
        let size: CGFloat = m.z(m.sp(22, 18))
        let first: CGFloat = max(size * 1.2, ck).rounded(.up)
        let pillH: CGFloat = m.sp(34, 30)
        let worked: Bool = model.workedOn(t)
        HStack(alignment: .top, spacing: 0) {
            Circle()
                .stroke(T.text, lineWidth: 1.5)
                .background(Circle().fill(hot ? T.soft : Color.clear))
                .overlay(tick(worked, ck))
                .frame(width: ck, height: ck)
                .frame(width: ck + m.sp(11, 9), height: first, alignment: .leading)
                .contentShape(Rectangle())
                .onHover { hot = $0 }
                .onTapGesture { model.completeTask(t) }
                .help(worked ? "Completed" : "Mark as done")
            Text(t.title)
                .font(Fonts.ui(size, true))
                .foregroundColor(T.text)
                .fixedSize(horizontal: false, vertical: true)
                .frame(minHeight: first, alignment: .topLeading)
                .offset(y: m.sp(2, 1.5)) // (Gilroy stands high in its line: this puts it level with the circle and the pill)
                .layoutPriority(1)
                .help(AppModel.infoText(t))
            Spacer(minLength: 0)
            TimerPill(t: t, big: true)
                .fixedSize()
                .padding(.leading, 10)
                .padding(.top, (first - pillH) / 2)
        }
    }

    /// The task being worked on: its circle carries a tick, so the one control says "completed".
    func tick(_ worked: Bool, _ ck: CGFloat) -> some View {
        Glyph(d: Icons.tick, color: T.text, th: 1.8, w: 18, h: 18)
            .scaleEffect(ck / 18)
            .opacity(worked ? 1 : 0)
            .allowsHitTesting(false)
    }
}

/// How a timer pill looks at this moment.
struct PillLook {
    var ground: Tone = .clear
    var ink: Color = Color.clear
    var text: String = ""
    var showText: Bool = true
    var running: Bool = false
    var semi: Bool = false
    var tip: String = ""
}

/// The countdown beside a task (by priority: a stopwatch, counting up). Click to start; click again to pause or carry on.
struct TimerPill: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    let big: Bool
    @State private var hot = false

    var body: some View {
        let k: PillLook = look()
        let size: CGFloat = m.z(big ? 13 : 11)
        let minW: CGFloat = Fonts.width("00:00", size, true)
        let fill: Color? = big && !k.running ? k.ink : nil
        HStack(spacing: big ? 7 : 5) {
            Glyph(d: k.running ? Icons.pause : Icons.play, color: k.ink, th: big ? 1.6 : 1.3, w: 9, h: 10, fill: fill)
            if k.showText {
                Text(k.text)
                    .font(Fonts.ui(size, k.semi))
                    .foregroundColor(k.ink)
                    .frame(minWidth: minW, alignment: .trailing)
            }
        }
        .padding(.leading, big ? m.sp(13, 11) : 7)
        .padding(.trailing, big ? m.sp(16, 14) : 8)
        .padding(.vertical, big ? 0 : 2)
        .frame(height: big ? m.sp(34, 30) : nil)
        .plate(k.ground)
        .opacity(big && hot ? 0.9 : 1)
        .contentShape(Capsule())
        .onHover { hot = $0 }
        .onTapGesture { model.timerClick(t) }
        .help(k.tip)
    }

    func look() -> PillLook {
        var k = PillLook()
        let mine: Bool = model.timerId == t.id
        let running: Bool = mine && model.timerRunning
        let prio: Bool = AppModel.priorityMode
        let begun: Bool = prio && (mine || t.worked > 0)
        let mins: Int = AppModel.durationMin(t)
        k.running = running
        k.semi = big || mine
        if big {
            // waiting: the colour of the main button; running: the accent
            k.ground = running ? .accent : .main
            k.ink = running ? T.onAccent : T.onMain
        } else if mine {
            k.ground = .accent
            k.ink = T.onAccent
        } else {
            k.ground = .clear
            k.ink = hot ? T.text : T.muted
        }
        if begun && model.elapsed(t) > Double(mins * 60) {
            // past its planned length: a quiet change of colour, no prompt
            if big || mine { k.ground = .dangerSoft }
            k.ink = T.danger
        }
        // (a Full Mode row already shows its length beside its effort)
        let blank: Bool = prio && !begun && !big && model.full
        k.text = blank ? "" : model.timerText(t)
        k.showText = !(begun && !model.showWatch) // "Show stopwatch" off (Settings): the icon alone
        if prio {
            k.tip = running ? "Pause" : (begun ? "Carry on with this" : "Start working on this")
        } else {
            k.tip = running ? "Pause the timer" : (mine ? "Carry on" : "Start a " + String(mins) + "-minute timer")
        }
        return k
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
                if !tasks {
                    // the list takes the chart's place
                    model.allOpen = true
                    Settings.set("AllOpen", 1)
                    model.setTab(0)
                    return
                }
                model.allOpen.toggle()
                Settings.set("AllOpen", model.allOpen ? 1 : 0)
                model.changed()
                model.shell?.contentChanged()
            }) {
                HStack(spacing: 0) {
                    Glyph(d: Icons.lines, color: ink, th: 1.5, w: 14, h: 14)
                    Text("All tasks").font(m.font(12)).foregroundColor(ink).padding(.leading, 7).padding(.trailing, 6)
                    Text(String(model.openCount)).font(m.font(11)).foregroundColor(listOn ? T.onStrongSoft : T.muted)
                    Spacer(minLength: 0)
                    // an arrow at the far end: across while the list is closed, down while it is open
                    Glyph(d: listOn ? Icons.down : Icons.right, color: ink, th: 1.5, w: 14, h: 14)
                }
                .padding(.horizontal, 11)
            }
            RoundChip(d: Icons.plus, on: tasks && model.addOpen, tip: "Add a task") {
                if !tasks { model.addOpen = true; model.setTab(0) } else { model.addOpen.toggle() }
                if model.addOpen { model.focusInput += 1 }
                model.changed()
                model.shell?.contentChanged()
            }
            RoundChip(d: Icons.wave, on: !tasks, tip: "Energy") { model.cycle(to: tasks ? 1 : 0) } // drops down under the chips (Genie.swift)
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

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            AddPill()
            HabitRow()
        }
    }
}

/// The box you type a task into.
struct AddPill: View {
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
                    .plate(.soft)
                    .padding(.leading, 8).padding(.trailing, m.sp(7, 5))
            }
        }
        .frame(height: m.sp(40, 32))
        .overlay(Capsule().strokeBorder(focused ? T.strong : Color.clear, lineWidth: 1.5))
        .plate(.field, rim: 20)
        .onAppear { if model.addOpen && !model.full && !model.demoRun { focused = true } }
        .onChange(of: model.focusInput) { _ in focused = true }
    }
}

/// Under the add box: what the work log says you usually do at this time of day, one click each.
struct HabitRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let offer: [Habits.Habit] = model.habitOffer()
        if !offer.isEmpty {
            FlowRow(items: items(offer), hs: 4, vs: 4)
                .padding(.leading, 2)
                .padding(.bottom, -4)
        }
    }

    func items(_ offer: [Habits.Habit]) -> [AnyView] {
        let part: Int = Habits.part(AppModel.now())
        var list: [AnyView] = []
        let label = Text("Usually this " + Habits.partNames[part] + ":")
            .font(m.font(11)).foregroundColor(T.muted).lineLimit(1).fixedSize()
            .padding(.top, 2).padding(.bottom, 3).padding(.trailing, 2)
        list.append(label.any())
        for h in offer { list.append(HabitChip(h: h).any()) }
        return list
    }
}

struct HabitChip: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let h: Habits.Habit
    @State private var hot = false

    var body: some View {
        // no wider than 150: a long title is cut short with an ellipsis
        let w: CGFloat = min(150, Fonts.width(h.title, m.z(11)) + 2)
        Text(h.title)
            .font(m.font(11)).foregroundColor(T.text).lineLimit(1).truncationMode(.tail)
            .frame(width: w, alignment: .leading)
            .padding(.horizontal, 9).padding(.top, 2).padding(.bottom, 3)
            .plate(hot ? .lift : .field)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { model.addHabit(h) }
            .help(AppModel.habitTip(h))
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
        .plate(.tray, radius: m.radius, rim: m.radius)
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
            .plate(hot ? .accent : .lift, radius: m.sp(11, 9))
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
                Caps(text: model.alertKind == 1 ? "Time's up" : "Ready for the next one?", color: T.accentOn).padding(.bottom, 4)
                Text(t.title).font(m.head(15)).foregroundColor(T.onStrong).fixedSize(horizontal: false, vertical: true)
                    .padding(.trailing, 4).padding(.bottom, m.sp(11, 9))
                FlowRow(items: buttons(t), hs: m.slim ? 3 : 5, vs: 5)
            }
            .padding(.leading, m.padX + 2).padding(.trailing, m.padX)
            .padding(.top, m.padY + 2).padding(.bottom, m.padY + 2 - m.sp(6, 5))
            .frame(maxWidth: .infinity, alignment: .leading)
            .plate(.strong, radius: m.radius)
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
            Btn(text: startText(t), kind: 0, on: 2) { model.startTimer(t); model.clearAlert() }.any(),
            Btn(text: "Not yet", kind: 2, on: 2) { model.clearAlert() }.any(),
        ]
    }

    /// By priority the next task has a stopwatch, not a countdown of so many minutes.
    func startText(_ t: TaskItem) -> String {
        if AppModel.priorityMode { return "Start" }
        return "Start " + String(AppModel.durationMin(t)) + " min"
    }
}
