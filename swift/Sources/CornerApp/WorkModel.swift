// Corner for macOS - working on a task, and what follows from it: "Plan by: Schedule / Priority" and the stopwatch,
// the work log (work.csv: one row for each stretch of work, kept in step with the timer), the pause when you have
// left, the order by priority and its plan, the habit chips under the add box, and the energy % you can type over.
// Port of the Shell half of Work.cs and of EnergyFeel.cs. The model itself is CornerCore (Work.swift, Feel.swift).
import AppKit
import SwiftUI
import CornerCore

/// A correction typed into the energy counter, waiting for "Fine-tune the chart" or "Only the number".
final class FeelAsk {
    let felt: Double    // 0..1
    let model: Double   // the curve's level then, 0..1
    let shown: Int      // what the counter said, 1 to 100
    let at: Date

    init(felt: Double, model: Double, shown: Int, at: Date) {
        self.felt = felt
        self.model = model
        self.shown = shown
        self.at = at
    }
}

/// After a correction: re-plan the day? bring a high-effort task forward?
final class FeelAfter {
    var replan: Bool
    var highId: String?
    let felt: Int

    init(replan: Bool, highId: String?, felt: Int) {
        self.replan = replan
        self.highId = highId
        self.felt = felt
    }
}

extension AppModel {
    static let awayIdleSec: Double = 600 // no key or mouse for this long pauses the task being worked on
    static let tickGapSec: Double = 90   // the once-a-second tick missing for this long: the Mac was asleep, and the work stopped when it was last seen

    static var priorityMode: Bool { return Settings.get("PlanBy", 0) > 0.5 }
    var showWatch: Bool { return Settings.get("Stopwatch", 1) > 0.5 }

    // ------------------------------------------------------------ the energy counter's number

    /// The counter's level (0..1) for the curve's level v, matched to your corrections (feel.csv, read once).
    func feelMap(_ v: Double, _ now: Date) -> Double {
        if feelEntries == nil { feelEntries = Calibration.read() }
        let all: [Calibration.Entry] = feelEntries ?? []
        return Calibration.map(v, all, now)
    }

    /// The energy counter's number now, as the panel shows it; -1 when there is no curve.
    func energyPct(_ now: Date) -> Double {
        guard let e = energy else { return -1 }
        let level: Double = feelMap(e.allAt(now), now)
        return max(1, min(100, NetFormat.round(level * 100)))
    }

    // ------------------------------------------------------------ work.csv

    /// Seconds worked on a task so far, the open stretch included.
    func elapsed(_ t: TaskItem) -> Double {
        var open: Double = 0
        if workId == t.id { open = max(0, Date().timeIntervalSince(workStart)) }
        return t.worked + open
    }

    /// Keeps the log in step with the timer: called after anything starts, pauses or stops it, and once a second.
    func workSync() {
        let want: String? = timerRunning ? timerId : nil
        if want == workId { workWhy = nil; return }
        if workId != nil {
            let fallback: String = want != nil ? "switched" : "paused"
            workEnd(workWhy ?? fallback, Date())
        }
        workWhy = nil
        if let w = want {
            workId = w
            workStart = Date()
            workSeen = workStart
            workE0 = energyPct(AppModel.now())
            workFelt = -1
        }
    }

    func rowFor(_ t: TaskItem, _ start: Date, _ end: Date, _ ended: String) -> WorkRow {
        let r = WorkRow(start: start, end: end, minutes: end.timeIntervalSince(start) / 60, id: t.id, title: t.title)
        r.effort = TaskTags.level(t.energy)
        r.kind = t.kind ?? "mental"
        r.routine = t.routine != nil
        r.planned = AppModel.durationMin(t)
        r.ended = ended
        return r
    }

    /// Closes the open stretch and writes its row. A stretch of a few seconds (a slip of the hand) is dropped.
    func workEnd(_ ended: String, _ endIn: Date) {
        guard let id = workId else { return }
        workId = nil
        guard let t = data.tasks.first(where: { $0.id == id }) else { return }
        var end: Date = endIn
        if end < workStart { end = workStart }
        let secs: Double = end.timeIntervalSince(workStart)
        if secs < 5 && ended != "completed" { return }
        t.worked += secs
        let r: WorkRow = rowFor(t, workStart, end, ended)
        let ago: Double = Date().timeIntervalSince(end)
        r.e0 = workE0
        r.e1 = energyPct(AppModel.now().addingTimeInterval(-ago))
        r.felt = workFelt
        WorkLog.add(r)
    }

    /// From taskCompleted, before the timer is stopped: the row that ends here says "completed". A task finished
    /// while paused gets a row of no length, so the log still has the moment it was completed.
    func workCompleted(_ t: TaskItem) {
        if workId == t.id { workWhy = "completed"; return }
        if t.worked <= 0 { return }
        let now = Date()
        let r: WorkRow = rowFor(t, now, now, "completed")
        let pct: Double = energyPct(AppModel.now())
        r.e0 = pct
        r.e1 = pct
        WorkLog.add(r)
    }

    /// The Mac locked or went to sleep, or sat untouched for ten minutes: the task being worked on is paused, and the
    /// stretch ends when you left, not when Corner noticed. Never in a test render.
    func autoPause(_ endIn: Date) {
        if workId == nil || demoRun { return }
        let end: Date = endIn < workStart ? workStart : endIn
        workEnd("away", end)
        if AppModel.priorityMode {
            // (the task stays in hand, paused: timerClick)
            timerRunning = false
            timerLeft = 0
            resortData()
        } else {
            timerLeft = max(0, timerEnd.timeIntervalSince(end)) // the countdown stops where it stood when you left
            timerRunning = false
        }
        save()
        if shown {
            changed()
            shell?.contentChanged()
        }
    }

    /// Once a second, before the timer is looked at (tick). The Mac can go to sleep without Corner being told in
    /// time; the ticks then simply stop. A long gap between two ticks means the work ended at the last one, so a
    /// night's sleep is never logged as work and never runs a countdown out.
    func workAwake() {
        if workId == nil { return }
        let now = Date()
        if now.timeIntervalSince(workSeen) > AppModel.tickGapSec && workSeen >= workStart {
            autoPause(workSeen)
            return
        }
        workSeen = now
    }

    /// Once a second (tick).
    func workTick() {
        workSync()
        if workId == nil || demoRun { return }
        let idle: Double = Platform.idleSeconds()
        if idle >= AppModel.awayIdleSec { autoPause(Date().addingTimeInterval(-idle)) }
    }

    /// Corner is quitting: the open stretch ends "closed".
    func closeWork() {
        if workId == nil { return }
        workEnd("closed", Date())
        save()
    }

    // ------------------------------------------------------------ the order

    static func dayKey(_ d: Date) -> Double { return Double(d.year * 10000 + d.month * 100 + d.day) }

    /// A priority plan was applied today.
    var orderToday: Bool {
        if !AppModel.priorityMode { return false }
        return Settings.get("OrderDay", 0) == AppModel.dayKey(SleepLog.today(AppModel.now()))
    }

    func rankNow() -> [Ranked] {
        let now = AppModel.now()
        let pct: Double = energyPct(now)
        let level: Double = pct < 0 ? -1 : pct / 100
        // the task in hand stays first, running or paused
        return CornerCore.Priority.rank(data.tasks, energy: energy, now: now, level: level, rows: Habits.log, working: timerId)
    }

    /// Priority mode: puts the open tasks in the order to do them now. Only the data; the caller redraws.
    /// Called when a plan is applied and when a task is paused or completed, and at no other time.
    func resortData() {
        if !AppModel.priorityMode { return }
        let ranked: [Ranked] = rankNow()
        var why: [String: String] = [:]
        var open: [TaskItem] = []
        for r in ranked {
            if let w = r.why { why[r.task.id] = w }
            open.append(r.task)
        }
        rankWhy = why
        let done: [TaskItem] = data.tasks.filter { $0.done }
        data.tasks = open + done
    }

    func makeOrder() {
        orderProposal = rankNow().filter { !$0.later }
        changed()
        shell?.contentChanged()
    }

    func discardOrder() {
        orderProposal = nil
        changed()
        shell?.contentChanged()
    }

    func applyOrder() {
        if orderProposal == nil { return }
        orderUndo = data.tasks.map { $0.id }
        orderProposal = nil
        resortData()
        Settings.set("OrderDay", AppModel.dayKey(SleepLog.today(AppModel.now())))
        save()
        changed()
        shell?.contentChanged()
    }

    func undoOrder() {
        Settings.set("OrderDay", 0)
        if let was = orderUndo {
            orderUndo = nil
            restoreOrder(was)
        }
        rankWhy = [:]
        save()
        changed()
        shell?.contentChanged()
    }

    /// Settings: plan by schedule (0) or by priority (1). A running timer is stopped first, and logged.
    func setPlanBy(_ v: Int) {
        stopTimer()
        if alertKind != 0 { clearAlert() }
        Settings.set("PlanBy", Double(v))
        proposal = nil
        orderProposal = nil
        rankWhy = [:]
        changed()
        shell?.contentChanged()
    }

    func setShowWatch(_ on: Bool) {
        Settings.set("Stopwatch", on ? 1 : 0)
        changed()
        shell?.contentChanged()
    }


    /// By priority: why the next task is next ("fits your peak"), or nil.
    func nextReason(_ t: TaskItem) -> String? {
        if !AppModel.priorityMode || t.id == timerId { return nil }
        return rankWhy[t.id]
    }

    // ------------------------------------------------------------ "Usually this morning": chips under the add box

    /// The things the log says you usually do in this part of the day and that are not in the list yet.
    func habitOffer() -> [Habits.Habit] {
        let now = AppModel.now()
        let part: Int = Habits.part(now)
        let rows: [WorkRow] = Habits.log // timed work and ticked-off tasks
        if habitCache == nil || habitRows != rows.count || habitDone != TaskLog.changes || habitPart != part || habitDay != now.dateOnly {
            habitCache = Habits.usual(rows, part, now)
            habitRows = rows.count
            habitDone = TaskLog.changes
            habitPart = part
            habitDay = now.dateOnly
        }
        var have = Set<String>()
        for t in data.tasks { have.insert(Habits.key(t.title)) }
        let most: Int = full ? 4 : 3
        var offer: [Habits.Habit] = []
        for h in habitCache ?? [] {
            if have.contains(Habits.key(h.title)) { continue }
            offer.append(h)
            if offer.count >= most { break }
        }
        return offer
    }

    static func habitTip(_ h: Habits.Habit) -> String {
        var s: String = "Add to today · " + TaskTags.len(h.minutes)
        if let e = h.effort { s += " · " + (TaskTags.short(e) ?? e) }
        s += " · on " + String(h.days) + " days lately"
        return s
    }

    func addHabit(_ h: Habits.Habit) {
        let key: String = Habits.key(h.title)
        let all: [Routine] = Routines.data?.routines ?? []
        if let rt = all.first(where: { Habits.key($0.title) == key }), !routineInToday(rt) {
            addRoutinesToToday([rt])
            return
        }
        let t = TaskItem(id: AppModel.newId(), title: h.title)
        t.energy = TaskTags.level(h.effort)
        t.kind = h.kind == "physical" ? "physical" : nil
        t.minutes = h.minutes
        flashId = t.id
        data.tasks.insert(t, at: 0)
        save()
        changed()
        shell?.contentChanged()
    }

    // ------------------------------------------------------------ "I feel 63%, not 30%"

    /// The counter's number was clicked: it turns into a box to type in.
    func beginFeel() {
        if energy == nil { return }
        feelEditing = true
        shell?.window.makeKey()
        changed()
    }

    func cancelFeel() {
        if !feelEditing { return }
        feelEditing = false
        changed()
        shell?.contentChanged()
    }

    /// Enter in the box, or the box left: a number from 1 to 100 that differs from the counter's is asked about.
    func commitFeel(_ textIn: String, shown: Int) {
        if !feelEditing { return }
        feelEditing = false
        var s: String = textIn.trimmed
        while s.hasSuffix("%") { s.removeLast() }
        s = s.trimmed
        if let v = Int(s), v >= 1, v <= 100, v != shown, let e = energy {
            let now = AppModel.now()
            feelAsk = FeelAsk(felt: Double(v) / 100.0, model: e.allAt(now), shown: shown, at: now)
        }
        changed()
        shell?.contentChanged()
    }

    func afterFeel(_ before: EnergyDay?, _ a: FeelAsk, tuned: Bool) {
        let feltPct: Double = NetFormat.round(a.felt * 100)
        if workId != nil { workFelt = feltPct } // typed while working on a task: into its row of work.csv
        // (by priority there are no times to re-plan; and the plan it leads to is only shown in Full Mode)
        let anyOpen: Bool = data.tasks.contains { !$0.done }
        let moved: Bool = tuned && full && !AppModel.priorityMode && FeelPlan.zonesMoved(before, energy) && anyOpen
        let hi: TaskItem? = FeelPlan.highNow(data.tasks, felt: a.felt, shown: a.shown, now: AppModel.now(), inTask: inTask)
        if moved || hi != nil {
            feelAfter = FeelAfter(replan: moved, highId: hi?.id, felt: Int(feltPct))
        } else {
            feelAfter = nil
        }
    }

    /// "Fine-tune the chart": the counter is matched to you, and the correction also counts as a 1-10 check-in.
    func feelFineTune() {
        guard let a = feelAsk else { return }
        feelAsk = nil
        let before: EnergyDay? = energy
        Calibration.add(a.at, model: a.model, felt: a.felt)
        feelEntries = nil
        let raw: Double = NetFormat.round(1 + 9 * a.felt)
        let score: Int = Int(max(1, min(10, raw)))
        Feedback.log(a.at, String(score), energy, activeToday: activeToday, activeSession: activeSession(a.at))
        refreshEnergy(opening: false)
        afterFeel(before, a, tuned: true) // re-plan? a high-effort task now?
        changed()
        shell?.contentChanged()
    }

    /// "Only the number": the counter is matched to you; the chart stays as it is.
    func feelOnlyNumber() {
        guard let a = feelAsk else { return }
        feelAsk = nil
        Calibration.add(a.at, model: a.model, felt: a.felt)
        feelEntries = nil
        afterFeel(energy, a, tuned: false)
        changed()
        shell?.contentChanged()
    }

    func feelCancelAsk() {
        feelAsk = nil
        changed()
        shell?.contentChanged()
    }

    func feelTidy() {
        if let f = feelAfter, !f.replan, f.highId == nil { feelAfter = nil }
    }

    /// The high-effort task the offer is about, while it is still open.
    func feelHighTask() -> TaskItem? {
        guard let f = feelAfter, let id = f.highId else { return nil }
        return data.tasks.first { $0.id == id && !$0.done }
    }

    func feelReplan(_ yes: Bool) {
        feelAfter?.replan = false
        feelTidy()
        if yes {
            planSplit.removeAll()
            planKeep.removeAll()
            proposePlan(true)
            return
        }
        changed()
        shell?.contentChanged()
    }

    func feelMoveHigh(_ yes: Bool) {
        let t: TaskItem? = feelHighTask()
        feelAfter?.highId = nil
        feelTidy()
        if yes, let task = t {
            var at: Date = CornerTime.truncateToMinute(AppModel.now())
            let over: Int = at.minute % 5
            if over != 0 { at = at.addingMinutes(5 - over) } // the next 5 minutes
            task.setDue(at, hasTime: true)
            task.planned = true // Plan may move it again
            sortByTime()
            save()
        }
        changed()
        shell?.contentChanged()
    }

    /// Settings: forget the corrections; the counter shows the curve's own number again.
    func resetFeel() {
        Calibration.clear()
        feelEntries = nil
        feelAsk = nil
        feelAfter = nil
        changed()
    }
}

// ---------------------------------------------------------------- Done: today's, and the days before
// (Work.cs: DoneToday, DoneNote, PastDays, DoneStep)

enum DoneHistory {
    static var rows: [TaskLog.Row] = []
    static var seen = -1

    /// done.csv, read again only when it has been written.
    static func doneRows() -> [TaskLog.Row] {
        if seen != TaskLog.changes {
            seen = TaskLog.changes
            rows = TaskLog.readDone()
        }
        return rows
    }

    static let withFraction: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    static let plain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()
}

extension AppModel {
    /// When a task was ticked off; nil when that cannot be read.
    static func doneTime(_ t: TaskItem) -> Date? {
        guard let s = t.doneAt else { return nil }
        if let d = DoneHistory.withFraction.date(from: s) { return d }
        return DoneHistory.plain.date(from: s)
    }

    /// The tasks completed today, latest first. Earlier days' are not shown: done.csv and work.csv keep them.
    /// (A task whose time of completion cannot be read is shown, so nothing done is lost from view by a bad date.)
    func doneToday() -> [TaskItem] {
        let today: Date = SleepLog.today(Date())
        let list: [TaskItem] = data.tasks.filter { t in
            if !t.done { return false }
            guard let at = AppModel.doneTime(t) else { return true }
            return SleepLog.today(at) == today
        }
        return list.sorted { ($0.doneAt ?? "") > ($1.doneAt ?? "") }
    }

    /// Beside a done task: how long it was worked on and from when to when, or the time it was ticked off.
    func doneNote(_ t: TaskItem) -> String? {
        guard let at = AppModel.doneTime(t) else { return nil }
        return WorkLog.note(WorkLog.spanOf(WorkLog.rows, t.id, SleepLog.today(at)), at)
    }

    /// The days before today with something to show, oldest first.
    func pastDays() -> [Date] {
        let today: Date = SleepLog.today(Date())
        return History.days(WorkLog.rows, DoneHistory.doneRows()).filter { $0 < today }
    }

    /// One day back (-1) or forward (+1), skipping days with nothing to show. Forward from the last one is today.
    func doneStep(_ by: Int) {
        let days: [Date] = pastDays()
        var cur: Date? = doneDay
        if let c = cur, !days.contains(c) { cur = nil }
        if by < 0 {
            var earlier: [Date] = days
            if let c = cur { earlier = days.filter { $0 < c } }
            if let last = earlier.last { cur = last }
        } else if let c = cur {
            cur = days.first { $0 > c }
        }
        doneDay = cur
        changed()
        shell?.contentChanged()
    }
}
