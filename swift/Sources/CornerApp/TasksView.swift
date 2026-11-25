// Corner for macOS - the task list: its rows (circle, title, delete on hover, effort and date, the timer), the strip
// of quick dates under a row, the Done bar, dragging a row to reorder, and "Today's plan" upright beside the
// tasks a plan placed. Port of Rebuild, MakeRow, WhenStrip and the drag code in Corner.cs, RowSide in Focus.cs,
// EnergyTag in UiEnergy.cs and PlacePlanStrip in FullLayout.cs.
import AppKit
import SwiftUI
import CornerCore

struct RowHeightKey: PreferenceKey {
    static var defaultValue: [String: CGFloat] = [:]
    static func reduce(value: inout [String: CGFloat], nextValue: () -> [String: CGFloat]) {
        value.merge(nextValue(), uniquingKeysWith: { $1 })
    }
}

extension AppModel {
    /// A task that the plan applied today has placed, and that is still to do (InTodaysPlan in FullLayout.cs).
    func inTodaysPlan(_ t: TaskItem) -> Bool {
        if t.done { return false }
        let today: Date = Date().dateOnly
        if AppModel.priorityMode {
            // by priority: the tasks the order covers
            if !orderToday { return false }
            guard let d = t.dueAt else { return true }
            return d.dateOnly <= today
        }
        guard t.planned, t.hasTime, let d = t.dueAt else { return false }
        return d.dateOnly == today
    }
}

/// Where the "Today's plan" strip stands beside the rows: from the first task the plan placed to the last.
struct PlanSpan {
    let top: CGFloat
    let height: CGFloat
}

let planStripW: CGFloat = 18

/// The white card holding every task. In the corner panel the done tasks are under the open ones; Full Mode shows
/// them in the energy column (DoneCard), and there the whole card scrolls (FullTasks).
struct TaskListCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @State private var dragId: String? = nil
    @State private var dragDY: CGFloat = 0
    @State private var heights: [String: CGFloat] = [:]

    var body: some View {
        let active: [TaskItem] = model.data.tasks.filter { !$0.done }
        let done: [TaskItem] = model.full ? [] : model.data.tasks.filter { $0.done }.sorted { ($0.doneAt ?? "") > ($1.doneAt ?? "") }
        let anyDone: Bool = model.data.tasks.contains { $0.done }
        let hp: CGFloat = m.padX - rowOver
        rows(active, done, anyDone)
        .padding(.horizontal, hp)
        .padding(.vertical, m.sp(8, 6))
        .frame(maxWidth: .infinity, alignment: .leading)
        .plate(.field, radius: m.radius, rim: m.radius)
    }

    func rows(_ active: [TaskItem], _ done: [TaskItem], _ anyDone: Bool) -> some View {
        // Full Mode, once a plan has been applied: a lime strip runs down the left of the list beside the tasks it
        // placed. It stands mostly in the card's own edge, so the rows move over only a little for it.
        let span: PlanSpan? = model.full ? planSpan(active) : nil
        let stripX: CGFloat = 4 - (m.padX - rowOver) // 4 in from the card's edge
        let shift: CGFloat = span == nil ? 0 : max(0, stripX + planStripW + 3 - rowOver) // the circles start 3 after it
        return VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(active.enumerated()), id: \.element.id) { pair in
                TaskRow(t: pair.element, dragging: dragId != nil)
                    .background(GeometryReader { g in Color.clear.preference(key: RowHeightKey.self, value: [pair.element.id: g.size.height]) })
                    .offset(y: offset(pair.offset, active))
                    .zIndex(dragId == pair.element.id ? 5 : 0)
                    .gesture(drag(pair.offset, active))
            }
            if active.isEmpty {
                Text(anyDone ? "All done. Enjoy it." : "Nothing here yet.")
                    .font(m.font(12.5)).foregroundColor(T.faint)
                    .padding(.horizontal, rowOver).padding(.vertical, 6)
            }
            if !done.isEmpty {
                DoneHeader(count: done.count)
                if model.data.doneOpen {
                    ForEach(done, id: \.id) { t in TaskRow(t: t, dragging: false) }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.leading, shift)
        .overlay(strip(span, stripX), alignment: .topLeading)
        .onPreferenceChange(RowHeightKey.self) { heights = $0 }
    }

    @ViewBuilder func strip(_ span: PlanSpan?, _ x: CGFloat) -> some View {
        if let s = span, dragId == nil {
            PlanStripBar(height: s.height).offset(x: x, y: s.top)
        }
    }

    /// From the top of the first row the plan placed to the bottom of the last, 2 in at each end.
    func planSpan(_ active: [TaskItem]) -> PlanSpan? {
        var y: CGFloat = 0
        var top: CGFloat = 0
        var bottom: CGFloat = 0
        var any: Bool = false
        for t in active {
            let rowH: CGFloat = h(t)
            if model.inTodaysPlan(t) {
                if !any { top = y }
                any = true
                bottom = y + rowH
            }
            y += rowH
        }
        if !any || bottom - top < 12 { return nil }
        return PlanSpan(top: top + 2, height: bottom - top - 4)
    }

    func h(_ t: TaskItem) -> CGFloat { return heights[t.id] ?? 30 }

    /// Where a row sits while another is dragged: the dragged one follows the pointer, the ones it passes step aside.
    func offset(_ i: Int, _ active: [TaskItem]) -> CGFloat {
        guard let id = dragId, let d = active.firstIndex(where: { $0.id == id }) else { return 0 }
        if i == d { return clampDY(d, active) }
        var tops: [CGFloat] = []
        var y: CGFloat = 0
        for t in active { tops.append(y); y += h(t) }
        let dh = h(active[d])
        let top = tops[d] + clampDY(d, active)
        let bottom = top + dh
        let mid = tops[i] + h(active[i]) / 2
        if i < d && top < mid { return dh }
        if i > d && bottom > mid { return -dh }
        return 0
    }

    func clampDY(_ d: Int, _ active: [TaskItem]) -> CGFloat {
        var tops: [CGFloat] = []
        var y: CGFloat = 0
        for t in active { tops.append(y); y += h(t) }
        let lo = -tops[d]
        let hi = y - tops[d] - h(active[d])
        return max(lo, min(hi, dragDY))
    }

    func drag(_ i: Int, _ active: [TaskItem]) -> some Gesture {
        DragGesture(minimumDistance: 5, coordinateSpace: .global)
            .onChanged { v in
                guard active.count > 1, model.editId == nil else { return }
                if dragId == nil { dragId = active[i].id; model.dragging = true }
                withAnimation(.interactiveSpring()) { dragDY = v.translation.height }
            }
            .onEnded { _ in
                guard let id = dragId, let d = active.firstIndex(where: { $0.id == id }) else { return }
                var above = 0, below = 0
                for j in 0..<active.count where j != d {
                    let o = offset(j, active)
                    if o > 0 { above += 1 }
                    if o < 0 { below += 1 }
                }
                let to = d - above + below
                dragId = nil
                dragDY = 0
                model.dragging = false
                withAnimation(.easeOut(duration: 0.16)) { model.moveTask(from: d, to: to) }
            }
    }
}

/// "Today's plan", upright beside the tasks an applied plan placed. Too short for its name, it is a plain bar.
struct PlanStripBar: View {
    @Environment(\.m) var m
    let height: CGFloat

    var body: some View {
        let name: String = "TODAY'S PLAN"
        let nameW: CGFloat = Fonts.width(name, m.z(9), true) + 2
        ZStack {
            if height >= nameW + 12 {
                Text(name).font(m.font(9, true)).foregroundColor(T.onAccent).lineLimit(1).fixedSize()
                    .frame(width: nameW, height: 12)
                    .rotationEffect(.degrees(-90))
                    .frame(width: 12, height: nameW)
            }
        }
        .frame(width: planStripW, height: max(1, height))
        .plate(.accent, radius: 7)
        .allowsHitTesting(false)
    }
}

/// The pale-lime bar over the finished tasks: click to fold them; "Clear" removes them.
struct DoneHeader: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let count: Int
    @State private var clearHot = false

    var body: some View {
        HStack(spacing: 0) {
            Glyph(d: "M2.5,1 L5.5,4 L2.5,7", color: T.text, th: 1.5, w: 8, h: 8)
                .rotationEffect(.degrees(model.data.doneOpen ? 90 : 0))
            Text("DONE").font(m.font(10, true)).foregroundColor(T.text).padding(.leading, m.sp(9, 7)).padding(.trailing, 6)
            Text(String(count)).font(m.font(m.sp(10.5, 10))).foregroundColor(T.muted)
            Spacer(minLength: 0)
            if model.data.doneOpen {
                Text("Clear").font(m.font(11.5)).foregroundColor(clearHot ? T.danger : T.muted)
                    .padding(.leading, 8).padding(.vertical, 4)
                    .contentShape(Rectangle())
                    .onHover { clearHot = $0 }
                    .onTapGesture { model.clearDone() }
            }
        }
        .padding(.horizontal, rowOver + 2)
        .frame(height: m.sp(32, 28))
        .plate(.soft, radius: m.sp(12, 10))
        .contentShape(Rectangle())
        .onTapGesture { model.toggleDoneOpen() }
        .padding(.top, m.sp(6, 4))
    }
}

/// One task: circle | title | delete (on hover) | effort and date (Full Mode) and the timer.
struct TaskRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    let dragging: Bool
    @State private var hot = false
    @State private var boxHot = false
    @State private var delHot = false
    @State private var editText = ""
    @FocusState private var editing: Bool

    var body: some View {
        let rowH = m.sp(34, 26)
        let ck = m.sp(18, 15)
        let timed = model.timerId == t.id && !t.done
        let worked = model.workedOn(t)
        let showHover = (hot && !dragging) || (dragging && model.dragging && hot)
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .top, spacing: 0) {
                // the circle
                ZStack {
                    Circle().fill(boxHot && !t.done ? T.soft : Color.clear)
                    Circle().stroke(t.done ? T.ctl : T.text, lineWidth: 1.5)
                    if t.done {
                        Circle().fill(T.ctl)
                        Glyph(d: "M5,9.4 L7.9,12.2 L13.2,6.2", color: T.text, th: 1.8, w: 18, h: 18)
                    } else if worked {
                        // being worked on: the circle carries a tick, so the one control says "completed"
                        Glyph(d: "M5,9.4 L7.9,12.2 L13.2,6.2", color: T.text, th: 1.8, w: 18, h: 18)
                    }
                }
                .frame(width: 18, height: 18)
                .scaleEffect(ck / 18)
                .frame(width: ck, height: ck)
                .padding(.leading, rowOver)
                .frame(width: rowOver + ck + m.sp(10, 8), height: rowH, alignment: .leading)
                .contentShape(Rectangle())
                .onHover { boxHot = $0 }
                .onTapGesture { model.toggleDone(t) }
                .help(worked ? "Completed" : "")

                // the title, or the box it is edited in
                Group {
                    if model.editId == t.id {
                        TextField("", text: $editText, onCommit: { model.finishEdit(t, editText, keep: true) })
                            .textFieldStyle(.plain)
                            .font(m.font(13.5))
                            .foregroundColor(T.text)
                            .focused($editing)
                            .onExitCommand { model.finishEdit(t, editText, keep: false) }
                            .onAppear { editText = t.title; DispatchQueue.main.async { editing = true } }
                            .onChange(of: editing) { f in if !f && model.editId == t.id { model.finishEdit(t, editText, keep: true) } }
                    } else {
                        Text(t.title)
                            .font(m.font(13.5))
                            .foregroundColor(t.done ? T.muted : T.text)
                            .strikethrough(t.done, color: T.muted)
                            .lineLimit(m.slim ? 2 : nil)
                            .fixedSize(horizontal: false, vertical: true)
                            .onTapGesture {
                                if t.done { return }
                                model.editId = t.id
                                model.shell?.window.makeKey()
                                model.changed()
                            }
                    }
                }
                .padding(.top, m.sp(8, 5)).padding(.bottom, m.sp(9, 6)).padding(.trailing, m.sp(6, 4))
                .frame(maxWidth: .infinity, minHeight: rowH, alignment: .leading)

                // delete
                Glyph(d: Icons.cross, color: delHot ? T.danger : T.muted, th: 1.4, w: 10, h: 10)
                    .frame(width: m.sp(22, 18), height: rowH)
                    .contentShape(Rectangle())
                    .opacity(hot && !dragging ? 1 : 0)
                    .onHover { delHot = $0 }
                    .onTapGesture { model.deleteTask(t) }
                    .help("Delete")

                if !t.done {
                    HStack(spacing: 0) {
                        if model.full { DueChip(t: t, rowHot: hot).padding(.trailing, 4) }
                        TimerPill(t: t, big: false)
                    }
                    .fixedSize()
                    .frame(height: rowH)
                }
                Color.clear.frame(width: rowOver, height: 1)
            }
            .frame(minHeight: rowH)
            .background(
                ZStack {
                    if timed { RoundedRectangle(cornerRadius: m.sp(12, 10), style: .continuous).fill(T.soft) }
                    RoundedRectangle(cornerRadius: m.sp(12, 10), style: .continuous).fill(T.hover).opacity(showHover ? 1 : 0)
                    if model.flashId == t.id { FlashGlow(radius: m.sp(12, 10)) }
                }
            )
            .contentShape(Rectangle())
            .onHover { hot = $0 }
            .help(model.full || t.done ? "" : t.title + "\n" + AppModel.infoText(t))

            if model.whenOpenId == t.id && !t.done { WhenStrip(t: t) }
        }
    }
}

/// The new row glows lime three times.
struct FlashGlow: View {
    let radius: CGFloat
    @State private var on = false
    var body: some View {
        RoundedRectangle(cornerRadius: radius, style: .continuous).fill(T.accent)
            .opacity(on ? 0.28 : 0)
            .onAppear { withAnimation(.easeInOut(duration: 0.52).repeatCount(6, autoreverses: true)) { on = true } }
    }
}

/// Full Mode: the routine mark, effort bars and length, and the date; click the date to change it.
struct DueChip: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    let rowHot: Bool

    var body: some View {
        // by priority nothing is "overdue", and a time an old schedule gave a task is not shown
        let prio: Bool = AppModel.priorityMode
        let overdue: Bool = t.overdue && !prio
        let shown: Date? = prio && t.planned ? nil : t.dueAt
        HStack(spacing: 0) {
            if t.routine != nil {
                Text("↻").font(m.font(11.5)).foregroundColor(T.faint).padding(.trailing, 3).padding(.bottom, 2)
            }
            EnergyTag(t: t, rowHot: rowHot)
            if let due = shown {
                let today = due.dateOnly <= Date().dateOnly
                let b: Color = overdue ? T.danger : (today ? T.text : T.muted)
                if t.hasTime { Glyph(d: Icons.bell, color: b, th: 1, w: 10, h: 10.5).padding(.trailing, 4).padding(.top, 1) }
                let label = whenLabel(due, overdue)
                let txt = Text(label).font(overdue ? Fonts.lean(m.z(11.5)) : m.font(11.5)).foregroundColor(b)
                if overdue || today {
                    txt.padding(.horizontal, 8).padding(.top, 2).padding(.bottom, 3)
                        .plate(overdue ? .dangerSoft : .soft)
                } else {
                    txt.padding(.trailing, 2)
                }
            } else {
                Glyph(d: Icons.clock, color: T.muted, th: 1.1, w: 12, h: 12)
                    .padding(.trailing, 8)
                    .opacity(rowHot || model.whenOpenId == t.id ? 1 : 0)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { model.toggleWhen(t) }
    }

    func whenLabel(_ due: Date, _ overdue: Bool) -> String {
        var when = When.label(due, hasTime: t.hasTime)
        if overdue {
            if due.dateOnly == Date().dateOnly { when = EnergyDay.clock(due) }
            else if when.hasPrefix("Yesterday") { when = "y" + when.dropFirst() }
            when = "Overdue · " + when
        }
        return when
    }
}

/// Effort as one to three bars, with "phys" and the length beside them; one click cycles the effort.
struct EnergyTag: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    let rowHot: Bool

    var body: some View {
        let level = TaskTags.level(t.energy)
        let bars = level == "high" ? 3 : level == "medium" ? 2 : level == "low" ? 1 : 0
        var parts: [String] = []
        if t.kind == "physical" { parts.append("phys") }
        if t.minutes > 0 { parts.append(TaskTags.len(t.minutes)) }
        let set = bars > 0 || !parts.isEmpty
        return HStack(spacing: 0) {
            Bars(level: bars).padding(.leading, 2).padding(.trailing, parts.isEmpty ? 0 : 5)
            if !parts.isEmpty { Text(parts.joined(separator: " ")).font(m.font(10.5)).foregroundColor(T.muted) }
        }
        .opacity(set || rowHot ? 1 : 0)
        .padding(.trailing, 6)
        .contentShape(Rectangle())
        .onTapGesture { model.cycleEnergy(t) }
        .help(level == nil ? "Energy: not set" : "Energy: " + (level ?? ""))
    }
}

/// Under a task: Today, Tomorrow, Next week, a box for "fri 3pm", and Clear.
struct WhenStrip: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let t: TaskItem
    @State private var text = ""
    @State private var bad = false

    var body: some View {
        let today = Date().dateOnly
        let wd = CornerTime.calendar.component(.weekday, from: today) // 1 = Sunday
        let toMon = (2 - wd + 7) % 7
        var items: [AnyView] = [
            Pill(text: "Today") { model.setDate(t, today) }.any(),
            Pill(text: "Tomorrow") { model.setDate(t, today.addingDays(1)) }.any(),
            Pill(text: "Next week") { model.setDate(t, today.addingDays(toMon == 0 ? 7 : toMon)) }.any(),
            HintField(hint: "or “fri 3pm”", text: Binding(get: { text }, set: { text = $0; bad = false }), size: 11.5,
                      color: bad ? T.danger : nil,
                      onSubmit: { if !model.setWhenText(t, text) { bad = true } },
                      onEscape: { model.toggleWhen(t) })
                .frame(width: 76)
                .padding(.horizontal, 8).padding(.top, 3).padding(.bottom, 4)
                .plate(.field)
                .overlay(Capsule().stroke(T.ctl, lineWidth: 1))
                .any(),
        ]
        if t.dueAt != nil { items.append(Pill(text: "Clear") { model.clearDate(t) }.any()) }
        return FlowRow(items: items, hs: 6, vs: 6)
            .padding(.leading, rowOver + m.sp(18, 15) + m.sp(10, 8))
            .padding(.trailing, rowOver)
            .padding(.bottom, m.sp(8, 4))
    }
}

/// A small rounded choice: sand, darker under the mouse.
struct Pill: View {
    @Environment(\.m) var m
    let text: String
    let action: () -> Void
    @State private var hot = false
    var body: some View {
        Text(text).font(m.font(11.5)).foregroundColor(T.text)
            .padding(.leading, m.sp(11, 9)).padding(.trailing, m.sp(10, 8))
            .padding(.top, m.sp(4, 3)).padding(.bottom, m.sp(5, 4))
            .plate(hot ? .ctl : .raised)
            .contentShape(Capsule())
            .onHover { hot = $0 }
            .onTapGesture { action() }
    }
}
