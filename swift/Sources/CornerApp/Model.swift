// Corner for macOS - the app's state and everything it does that is not drawing: tasks, timers and prompts, the
// morning check-in, the 1-10 check-ins, the energy curve, Plan, routines, the record, reminders and notices.
// The views (CardView.swift and the rest) only read this and call its methods. Port of the Shell logic spread over
// Corner.cs, Focus.cs, Checks.cs, Plan.cs, Stats.cs, Energy.cs, UiEnergy.cs and UiSleep.cs.
import AppKit
import SwiftUI
import CornerCore

final class AppModel: ObservableObject {
    @Published var rev = 0
    func changed() { rev &+= 1 }

    weak var shell: PanelController?

    var data = TaskData()

    // ---- what is showing
    var full = false
    var shown = false
    var bare = false        // the corner panel opened on the next task alone; the pointer has not been on it yet
    var revealK: CGFloat = 1   // how far the parts the pointer brings in have grown; 1 = in place (Genie.swift)
    var cycleK: CGFloat = -1   // the energy view sliding down or folding up: how far it is open; -1 = not sliding
    var countK: CGFloat = 1    // the energy % counting up; 1 = the whole number
    var promptOnly = false  // the panel shows nothing but a prompt that popped up by itself
    var rateNow = false     // "Rate now" was pressed
    var settingsOpen = false
    var tab = 0             // 0 = tasks, 1 = energy
    var allOpen = false     // the whole task list is open (corner panel)
    var addOpen = false     // the add box is open (corner panel)
    var whenOpenId: String? = nil
    var flashId: String? = nil
    var editId: String? = nil
    var demoRun = false
    var paused = false

    var corner = 0          // 0 top right, 1 top left, 2 bottom right, 3 bottom left
    var scale: Double = 1
    var fullScale: Double = 1
    var currentScale: Double { return full ? fullScale : scale }
    var onRight: Bool { return corner == 0 || corner == 2 }
    var onTop: Bool { return corner < 2 }

    var isBare: Bool { return bare && !full && !promptOnly && !settingsOpen }
    var m: M { return M(slim: !full) }
    /// Full Mode with "Energy left" (Settings): the two columns change places.
    var swapped: Bool { return Settings.get("SwapSides", 0) > 0.5 }

    // ---- add box
    var input = "" { didSet { changed() } }
    var focusInput = 0      // bumped to ask the add box to take the keyboard

    // ---- energy
    var energy: EnergyDay? = nil
    var needHint: NeedEstimate.Result? = nil
    var chartOffset: CGFloat = -1
    var napOpen = false
    var napOffer = false
    var debtOpen = false
    var statOpen: [Bool] = [false, false, false] // Full Mode: which of Slept, Debt and Need are showing their box
    var debtExtra = 30
    var napFrom = TimeEntryModel("from", waking: true, daytime: true)
    var napTo = TimeEntryModel("to", waking: true, daytime: true)
    var napJust = "25"
    var napJustBad = false

    // ---- morning check-in
    var checkInOpen = false
    var checkInEdit = false
    var checkRows: [[TimeEntryModel]] = []

    // ---- 1-10 check-in
    var feedbackOpen = false
    var feedbackShownAt = Date.distantPast

    // ---- plan and routines
    var proposal: [PlanItem]? = nil
    var planUndo: [PlanUndo]? = nil
    var planSplit = Set<String>()   // tasks the user chose to split across both peaks in the shown plan
    var planKeep = Set<String>()    // and those they chose to keep as one session (no more asking)
    var planKeepStarted = false
    var planOrder: [String]? = nil  // the list order the last applied plan replaced
    var routinesOpen = false
    var maxCardH: CGFloat = 600  // the tallest the card may be on this screen (set by the shell)
    var editRoutine: String? = nil
    var editRoutineText = ""
    var newRoutineText = ""
    var newRoutineEnergy: String? = nil
    var newRoutineMin = "30"
    var newRoutineBad = false

    // ---- timers and prompts
    var timerId: String? = nil
    var timerEnd = Date()
    var timerLeft: Double = 0
    var timerRunning = false
    var timerSecs: [String: Double] = [:]
    var lastTimerTick = Date()
    var alertKind = 0       // 0 none, 1 time's up, 2 ready for the next one?
    var alertTaskId: String? = nil

    // ---- the work log, the order by priority, habits and the typed energy % (WorkModel.swift)
    var workId: String? = nil       // the task whose stretch of work is open
    var workStart = Date()
    var workSeen = Date()           // the last second the open stretch was seen going on (workAwake)
    var workE0: Double = -1
    var workFelt: Double = -1
    var workWhy: String? = nil      // why the open stretch is about to end, when the code that ends it knows
    var rankWhy: [String: String] = [:] // from the last ordering: task id -> why
    var orderProposal: [Ranked]? = nil  // shown in the plan card, not yet applied
    var orderUndo: [String]? = nil      // the list order the applied one replaced
    var habitCache: [Habits.Habit]? = nil
    var habitRows = -1
    var habitDone = -1              // TaskLog.changes when the habits were last worked out
    var habitPart = -1
    var habitDay = Date.distantPast
    var feelEntries: [Calibration.Entry]? = nil // feel.csv, read once and kept
    var feelEditing = false         // the energy % is a box being typed into
    var feelAsk: FeelAsk? = nil     // a correction typed in, waiting for "Fine-tune the chart" or "Only the number"
    var feelAfter: FeelAfter? = nil // after a correction: re-plan the day? bring a high-effort task forward?

    // ---- time at the computer
    var activeSince = Date.distantPast
    var activeDay = Date.distantPast
    var activeToday: Double = 0
    var checkTicks = 0

    // ------------------------------------------------------------ helpers

    /// "Now", overridable for tests with CORNER_NOW=HH:mm.
    static func now() -> Date {
        if let s = ProcessInfo.processInfo.environment["CORNER_NOW"] {
            let p = s.split(separator: ":")
            if p.count >= 2, let h = Int(p[0]), let mi = Int(p[1]), h >= 0, h < 24, mi >= 0, mi < 60 {
                return Date().dateOnly.atTimeOfDay(TimeInterval(h * 3600 + mi * 60))
            }
        }
        return Date()
    }

    static func newId() -> String { return UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() }

    static func iso(_ d: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        f.timeZone = TimeZone.current
        return f.string(from: d)
    }

    static func durShort(_ hours: Double) -> String {
        let mins = Int((hours * 60).rounded())
        if mins < 60 { return String(mins) + "m" }
        return String(mins / 60) + "h" + (mins % 60 > 0 ? " " + String(mins % 60) + "m" : "")
    }

    static func mins(_ minutes: Double) -> String {
        let mm = Int(minutes.rounded())
        return mm < 60 ? String(mm) + " min" : String(mm / 60) + "h" + (mm % 60 > 0 ? " " + String(mm % 60) + "m" : "")
    }

    func save() { Store.save(data) }

    var openCount: Int { return data.tasks.filter { !$0.done }.count }

    // ------------------------------------------------------------ start

    func start() {
        data = Store.load()
        scale = max(0.6, min(1.5, (Settings.loadScale() * 20).rounded() / 20))
        fullScale = max(0.7, min(1.6, Settings.get("FullScale", 1)))
        corner = max(0, min(3, Int(Settings.get("Corner", 0))))
        T.use(Int(Settings.get("Skin", GlassLook.test ? 0 : 1))) // Glass unless another skin was chosen, as on Windows (a test render: Sage)
        allOpen = Settings.get("AllOpen", 0) > 0.5
        EnergyDay.use12 = Settings.get("Clock12", EnergyDay.use12 ? 1 : 0) > 0.5
    }

    /// Picks up changes made to tasks.json outside the app.
    func syncFromDisk() {
        if !Store.changedOnDisk || editId != nil || dragging { return }
        guard let d = Store.reload() else { return }
        data = d
        whenOpenId = nil
        changed()
    }
    var dragging = false
    let secDrag = SectionDrag() // Full Mode's sections (Sections.swift)
    var showGrips = false       // test renders: every section's dots

    // ------------------------------------------------------------ text size

    func setScale(_ v: Double, save: Bool) {
        if full {
            fullScale = max(0.7, min(1.6, (v * 20).rounded() / 20))
            if save { Settings.set("FullScale", fullScale) }
        } else {
            scale = max(0.6, min(1.5, (v * 20).rounded() / 20))
            if save { Settings.saveScale(scale) }
        }
        changed()
        shell?.place(animated: false)
    }

    func setCorner(_ v: Int) {
        corner = v
        Settings.set("Corner", Double(v))
        Settings.set("MacMoved", 0)
        changed()
        if shown && !full { shell?.place(animated: false) }
    }

    func setSkin(_ i: Int) {
        Settings.set("Skin", Double(i))
        T.use(i)
        changed()
        shell?.skinChanged()
    }

    // ------------------------------------------------------------ opening and closing

    /// Called by the panel just before it shows.
    func prepareShow() {
        syncFromDisk()
        shown = true
        bare = !full
        refreshEnergy(opening: true)
        applyPromptOnly()
        changed()
    }

    func applyPromptOnly() {
        settingsOpen = false
        promptOnly = promptOnly && !full && (feedbackOpen || alertHolds)
        if promptOnly { checkInOpen = false }
        if isBare { checkInOpen = false }
    }

    /// The pointer came onto the panel: the header, the check-ins, the chips and the open list come in.
    func reveal() {
        if !bare { return }
        if let fx = shell?.fx {
            if !demoRun { fx.icon = false } // the pointer came onto the startup panel: it closes into its corner, without the icon
            if fx.flyingOut { fx.revealWaiting = true; return } // still coming out as one picture: the rest grows when it has landed
        }
        let was = isBare
        bare = false
        if !was || !shown { return }
        let now = AppModel.now()
        if SleepLog.needsCheckIn(now) { openCheckIn(edit: false) }
        showFeedback(now)
        // the parts grow in over 200 ms (Genie.swift); with "Reduce motion", and in a still test render, as before
        if shell?.growIn() != true { withAnimation(.easeOut(duration: 0.2)) { changed() } }
        shell?.contentChanged()
    }

    func hidePanel() { shell?.hidePanel() }

    func closeButton() {
        if full { shell?.exitFull(); return }
        paused = true
        hidePanel()
        shell?.setTrayTip("Corner is closed - click its menu-bar icon to reopen")
    }

    func wake() {
        if full { shell?.activateFull(); return }
        paused = false
        shell?.setTrayTip("Corner")
        shell?.showPanel(peekMs: 5000)
    }

    // ------------------------------------------------------------ tabs and settings

    func setTab(_ i: Int) {
        shell?.fx.endCycle() // (a slide of the energy view that is under way ends here: Genie.swift)
        tab = i
        if i == 1 { drawEnergyPrep() }
        changed()
        shell?.contentChanged()
    }

    func drawEnergyPrep() {
        if Routines.data == nil { Routines.load() }
    }

    func toggleSettings() {
        if settingsOpen { closeSettings(); return }
        promptOnly = false
        applyPromptOnly()
        settingsOpen = true
        changed()
        shell?.contentChanged()
    }

    func closeSettings() {
        settingsOpen = false
        changed()
        shell?.contentChanged()
    }

    // ------------------------------------------------------------ the energy curve

    /// Recomputes today's curve. `opening` = the panel is just being shown.
    func refreshEnergy(opening: Bool) {
        SleepLog.load()
        EnergyDay.use12 = Settings.get("Clock12", EnergyDay.use12 ? 1 : 0) > 0.5
        let now = AppModel.now()
        if opening { feelEntries = nil; feelEditing = false }
        energy = EnergyModel.build(SleepLog.data.nights, need: Settings.get("SleepNeed", EnergyModel.defaultNeed), day: SleepLog.today(now), correction: Tuning.active(now))
        needHint = NeedEstimate.build(SleepLog.data.nights, Feedback.read(), now)
        if opening {
            chartOffset = -1
            ensureRoutines(SleepLog.today(now))
            tab = 0
            if SleepLog.needsCheckIn(now) { openCheckIn(edit: false) } else { checkInOpen = false }
            showFeedback(now)
        }
        changed()
    }

    /// The colour that says where a moment falls on today's curve: lime on a peak, forest in the dip, grey between.
    func zoneColor(_ t: Date) -> Color {
        guard let e = energy else { return T.ctl }
        for z in [e.peak1, e.dip, e.peak2] {
            if let z = z, t >= z.from && t < z.to { return z.peak ? T.accent : T.strong }
        }
        return T.ctl
    }

    func zoneName(_ t: Date) -> String {
        guard let e = energy else { return "" }
        for z in [e.peak1, e.dip, e.peak2] {
            if let z = z, t >= z.from && t < z.to { return z.peak ? "peak" : "dip" }
        }
        return "between"
    }

    func stepNeed(_ dv: Double) {
        guard let e = energy else { return }
        Settings.set("SleepNeed", max(5, min(11, e.need + dv)))
        refreshEnergy(opening: false)
    }

    // ------------------------------------------------------------ morning check-in

    func openCheckIn(edit: Bool) {
        let day = SleepLog.today(AppModel.now())
        checkInEdit = edit
        checkRows = []
        if edit, let tonight = SleepLog.forDay(day) {
            for p in tonight.sleep { addCheckRow(p.from, p.to) }
        } else if Settings.get("PrayerOn", 0) > 0.5 {
            let up = day.addingMinutes(Settings.get("PrayerAt", AppModel.prayerAtDefault))
            addCheckRow(nil, up)
            addCheckRow(up.addingMinutes(Settings.get("PrayerMin", AppModel.prayerMinDefault)), nil)
        } else {
            addCheckRow(nil, nil)
        }
        checkInOpen = true
        changed()
    }

    func addCheckRow(_ from: Date?, _ to: Date?) {
        let a = TimeEntryModel("asleep", waking: !checkRows.isEmpty) // a later period starts in the small hours
        let w = TimeEntryModel("woke", waking: true)
        if let f = from { a.fill(f) }
        if let t = to { w.fill(t) }
        checkRows.append([a, w])
        changed()
    }

    func removeCheckRow(_ i: Int) {
        if i > 0 && i < checkRows.count { checkRows.remove(at: i); changed() }
    }

    func saveCheckIn() {
        let day = SleepLog.today(AppModel.now())
        var a: [TimeInterval] = []
        var w: [TimeInterval] = []
        var ok = true
        for pair in checkRows {
            let ta = pair[0].get()
            let tw = pair[1].get()
            if ta == nil { pair[0].bad = true }
            if tw == nil { pair[1].bad = true }
            if let x = ta, let y = tw { a.append(x); w.append(y) } else { ok = false }
        }
        if !ok { return }
        guard let n = SleepLog.buildSplit(asleep: a, woke: w, day: day) else {
            for pair in checkRows { pair[0].bad = true; pair[1].bad = true }
            return
        }
        SleepLog.record(n)
        checkInOpen = false
        refreshEnergy(opening: false)
        setTab(1)
    }

    func skipCheckIn() {
        if !checkInEdit { SleepLog.skip(SleepLog.today(AppModel.now())) }
        checkInOpen = false
        refreshEnergy(opening: false)
        shell?.contentChanged()
    }

    static let prayerAtDefault: Double = 300
    static let prayerMinDefault: Double = 45

    // ------------------------------------------------------------ the 1-10 check-in

    /// Shows the check-in when one is due (or was asked for). Never together with the morning sleep check-in.
    func showFeedback(_ now: Date) {
        if isBare && !rateNow { feedbackOpen = false; return }
        if checkInOpen || energy == nil || (!rateNow && inTask) || !(rateNow || Feedback.due(now, energy)) {
            feedbackOpen = false
            return
        }
        if !feedbackOpen { feedbackShownAt = Date() }
        feedbackOpen = true
        changed()
    }

    func answerFeedback(_ a: String) {
        let at = AppModel.now()
        Feedback.log(at, a, energy, activeToday: activeToday, activeSession: activeSession(at))
        rateNow = false
        feedbackOpen = false
        if promptOnly && alertHolds { changed(); shell?.contentChanged() }
        else if promptOnly { hidePanel() }
        else { refreshEnergy(opening: false); shell?.contentChanged() }
    }

    func rateNowPressed() {
        rateNow = true
        showFeedback(AppModel.now())
        shell?.contentChanged()
    }

    func activeSession(_ now: Date) -> Double {
        return activeSince == Date.distantPast ? 0 : now.timeIntervalSince(activeSince) / 60
    }

    /// Every 10 seconds: counts time at the computer and brings up the check-in when one is due and you are there.
    func checkInTick() {
        let now = AppModel.now()
        let day = SleepLog.today(now)
        let dayKey = Double(day.year * 10000 + day.month * 100 + day.day)
        if activeDay != day {
            activeDay = day
            activeToday = Settings.get("ActiveDay", 0) == dayKey ? Settings.get("ActiveMin", 0) : 0
        }
        let here = Platform.idleSeconds() < Feedback.idleMax
        if here {
            if activeSince == Date.distantPast { activeSince = now }
            activeToday += 10.0 / 60.0
        } else {
            activeSince = Date.distantPast
        }
        checkTicks += 1
        if checkTicks % 30 == 0 && here { Settings.set("ActiveDay", dayKey); Settings.set("ActiveMin", activeToday.rounded()) }

        if checkTicks % 6 == 0 && here && !paused && !Platform.busy() { energyNudges(now) }
        if checkTicks % 6 != 0 || !here || paused || activeSession(now) < Feedback.settledMin { return }
        if inTask { return }
        if shown { if full && !feedbackOpen { showFeedback(now) }; return }
        SleepLog.load()
        if SleepLog.needsCheckIn(now) { return }
        let e = EnergyModel.build(SleepLog.data.nights, need: Settings.get("SleepNeed", EnergyModel.defaultNeed), day: day)
        if !Feedback.due(now, e) { return }
        promptOnly = true
        shell?.showPanel(peekMs: 15000)
    }

    // ------------------------------------------------------------ the task list

    /// Open tasks in the order to do them: the one being timed, then today's timed tasks by time,
    /// then the rest in list order, then anything dated for a later day.
    func upcoming() -> [TaskItem] {
        let today = Date().dateOnly
        let tomorrow = today.addingDays(1)
        let prio = AppModel.priorityMode // by priority the list itself is the order: a time does not move a task up
        func group(_ t: TaskItem) -> Int {
            let d = t.dueAt
            if t.id == timerId { return 0 }
            if !prio, t.hasTime, let dd = d, dd < tomorrow { return 1 }
            if let dd = d, dd.dateOnly > today { return 3 }
            return 2
        }
        var items: [(t: TaskItem, i: Int, g: Int, at: Double)] = []
        var i = 0
        for t in data.tasks where !t.done {
            let g = group(t)
            let at = g == 1 ? (t.dueAt?.timeIntervalSince1970 ?? 0) : 0
            items.append((t, i, g, at))
            i += 1
        }
        items.sort { a, b in
            if a.g != b.g { return a.g < b.g }
            if a.at != b.at { return a.at < b.at }
            return a.i < b.i
        }
        return items.map { $0.t }
    }

    func isFocus(_ t: TaskItem) -> Bool { return upcoming().first === t }

    static func durationMin(_ t: TaskItem) -> Int { return t.minutes > 0 ? t.minutes : Planner.defaultMinutes }

    /// Effort, length and date in words.
    static func infoText(_ t: TaskItem) -> String {
        var p: [String] = []
        if let level = TaskTags.level(t.energy) { p.append(level.prefix(1).uppercased() + level.dropFirst() + " effort") }
        if t.kind == "physical" { p.append("physical") }
        p.append(String(durationMin(t)) + " min")
        let prio = AppModel.priorityMode // by priority nothing is "overdue", and a time an old schedule gave is not shown
        if let d = t.dueAt, !(prio && t.planned) {
            let late: Bool = t.overdue && !prio
            p.append((late ? "overdue · " : "") + When.label(d, hasTime: t.hasTime))
        }
        if t.routine != nil { p.append("routine") }
        return p.joined(separator: " · ")
    }

    /// What the typed line will become ("Nov 30 14:00", "low · 45m").
    var inputPreview: String {
        let x = TaskTags.extract(input)
        var parts: [String] = []
        if let en = x.energy, let s = TaskTags.short(en) { parts.append(s) }
        if x.kind == "physical" { parts.append("phys") }
        if x.minutes > 0 { parts.append(TaskTags.len(x.minutes)) }
        if let w = When.parse(x.text) { parts.append(When.label(w.due, hasTime: w.hasTime)) }
        return parts.joined(separator: " · ")
    }

    func addTask() {
        let text = input.trimmed
        if text.isEmpty { return }
        let t = TaskItem(id: AppModel.newId())
        let x = TaskTags.extract(text)
        t.energy = x.energy
        t.minutes = x.minutes
        t.kind = x.kind == "physical" ? x.kind : nil
        var title = x.text.trimmed
        if let w = When.parse(x.text) { t.setDue(w.due, hasTime: w.hasTime); title = w.title }
        t.title = title
        data.tasks.insert(t, at: 0)
        save()
        input = ""
        changed()
        shell?.contentChanged()
    }

    func toggleDone(_ t: TaskItem) {
        if t.done {
            Click.play() // (Sound.swift)
            t.done = false
            t.doneAt = nil
            logUndone(t)
            data.tasks.removeAll { $0 === t }
            data.tasks.append(t)
            save()
            changed()
            return
        }
        completeTask(t)
    }

    /// Marks a task done from its row, the next-task box or a prompt.
    func completeTask(_ t: TaskItem) {
        let wasNext = isFocus(t)
        Click.play() // (Sound.swift)
        t.done = true
        t.doneAt = AppModel.iso(Date())
        logDone(t)
        if whenOpenId == t.id { whenOpenId = nil }
        taskCompleted(t, wasNext: wasNext)
        save()
        withAnimation(.easeOut(duration: 0.22)) { changed() }
        shell?.contentChanged()
    }

    func deleteTask(_ t: TaskItem) {
        if timerId == t.id { stopTimer() } // (its stretch of work is logged first)
        data.tasks.removeAll { $0 === t }
        if whenOpenId == t.id { whenOpenId = nil }
        save()
        withAnimation(.easeOut(duration: 0.18)) { changed() }
        shell?.contentChanged()
    }

    func clearDone() {
        data.tasks.removeAll { $0.done }
        save()
        changed()
        shell?.contentChanged()
    }

    func toggleDoneOpen() {
        data.doneOpen.toggle()
        save()
        changed()
        shell?.contentChanged()
    }

    func toggleWhen(_ t: TaskItem) {
        whenOpenId = whenOpenId == t.id ? nil : t.id
        changed()
        shell?.contentChanged()
    }

    /// Energy on a row: one click cycles unset -> low -> med -> high -> unset.
    func cycleEnergy(_ t: TaskItem) {
        let level = TaskTags.level(t.energy)
        t.energy = level == nil ? "low" : level == "low" ? "medium" : level == "medium" ? "high" : nil
        save()
        changed()
    }

    func setDate(_ t: TaskItem, _ d: Date) {
        var planned = false // picking the same day again keeps a planned time movable by Plan
        if t.planned, t.hasTime, let was = t.dueAt { planned = was.dateOnly == d.dateOnly }
        if t.hasTime, let old = t.dueAt { t.setDue(d.atTimeOfDay(old.timeOfDay), hasTime: true) } else { t.setDue(d, hasTime: false) }
        t.planned = planned
        whenOpenId = nil
        save()
        changed()
        shell?.contentChanged()
    }

    /// "fri 3pm" typed under a task. False when it is not a date.
    func setWhenText(_ t: TaskItem, _ text: String) -> Bool {
        guard let w = When.parse("x " + text), w.title == "x" else { return false }
        t.setDue(w.due, hasTime: w.hasTime)
        whenOpenId = nil
        save()
        changed()
        shell?.contentChanged()
        return true
    }

    func clearDate(_ t: TaskItem) {
        t.setDue(nil, hasTime: false)
        whenOpenId = nil
        save()
        changed()
        shell?.contentChanged()
    }

    /// The title edited in place; tags and a date at its end are read as when adding.
    func finishEdit(_ t: TaskItem, _ textIn: String, keep: Bool) {
        editId = nil
        var text = textIn.trimmed
        if keep && !text.isEmpty {
            let x = TaskTags.extract(text)
            if let k = x.kind { t.kind = k == "physical" ? k : nil }
            if x.energy != nil { t.energy = x.energy }
            if x.minutes > 0 { t.minutes = x.minutes }
            text = x.text.trimmed
            if !text.isEmpty {
                var title = text
                if let w = When.parse(text) { t.setDue(w.due, hasTime: w.hasTime); title = w.title }
                t.title = title
                save()
            }
        }
        changed()
        shell?.contentChanged()
    }

    /// Drag to reorder: the open task at `from` moves to `to` among the open tasks.
    func moveTask(from: Int, to: Int) {
        var active = data.tasks.filter { !$0.done }
        if from < 0 || from >= active.count || to < 0 || to >= active.count || from == to { changed(); return }
        let t = active.remove(at: from)
        active.insert(t, at: to)
        data.tasks = active + data.tasks.filter { $0.done }
        save()
        changed()
    }

    // ------------------------------------------------------------ timers

    var inTask: Bool { return timerId != nil && timerRunning }

    func timerSeconds() -> Double { return timerRunning ? timerEnd.timeIntervalSinceNow : timerLeft }

    static func clockText(_ seconds: Int) -> String {
        let mm: Int = seconds / 60
        let ss: Int = seconds % 60
        return String(mm) + ":" + (ss < 10 ? "0" : "") + String(ss)
    }

    func timerText(_ t: TaskItem) -> String {
        if AppModel.priorityMode {
            // a stopwatch, counting up; a task not started yet shows its planned length ("45m")
            let w: Double = elapsed(t)
            if w <= 0 && t.id != timerId { return TaskTags.len(AppModel.durationMin(t)) }
            return AppModel.clockText(Int(w.rounded(.down)))
        }
        // always minutes and seconds ("90:00", "120:00"), so every timer has the same shape
        let s: Int = t.id == timerId ? Int(ceil(max(0, timerSeconds()))) : AppModel.durationMin(t) * 60
        return AppModel.clockText(s)
    }

    func holdFeedback() { if !rateNow { feedbackOpen = false } }

    func startTimer(_ t: TaskItem) {
        // by priority it is a stopwatch: it counts up and never runs out
        timerId = t.id
        timerEnd = AppModel.priorityMode ? Date.distantFuture : Date().addingMinutes(AppModel.durationMin(t))
        timerRunning = true
        timerLeft = 0
        holdFeedback()
        workSync() // a stretch of work starts: work.csv
    }

    func stopTimer() { timerId = nil; timerRunning = false; timerLeft = 0; workSync() }

    func timerClick(_ t: TaskItem) {
        if AppModel.priorityMode {
            // play starts the stopwatch on this task (pausing any other); pause ends the stretch and puts the rest of the
            // list in order again. The paused task is still the one in hand: it stays first until another is started.
            let pause: Bool = timerId == t.id && timerRunning
            if pause {
                timerRunning = false
                timerLeft = 0
                workSync()
                resortData()
                save()
            } else {
                startTimer(t)
            }
            if alertKind != 0 {
                clearAlert()
                if !shown { return }
            }
            changed()
            shell?.contentChanged()
            return
        }
        if timerId != t.id { startTimer(t) }
        else if timerRunning { timerLeft = max(0, timerSeconds()); timerRunning = false }
        else if timerLeft > 0 { timerEnd = Date().addingTimeInterval(timerLeft); timerRunning = true; holdFeedback() }
        else { startTimer(t) }
        workSync()
        if alertKind == 1 { clearAlert() }
        changed()
        shell?.contentChanged()
    }

    func resetTimer() { stopTimer(); changed() }

    /// Every second.
    func tick() {
        workAwake() // the Mac slept meanwhile? Before the timer is looked at (WorkModel.swift)
        let now = Date()
        let dt = min(5, now.timeIntervalSince(lastTimerTick))
        lastTimerTick = now
        if let id = timerId, timerRunning, dt > 0 { timerSecs[id, default: 0] += dt }
        timerTick(now)
        workTick() // work.csv, and the pause when you have left (WorkModel.swift)
    }

    func timerTick(_ now: Date) {
        if timerId == nil { return }
        let prio: Bool = AppModel.priorityMode
        if timerRunning && !prio && now >= timerEnd { timeUp(); return }
        if shown && timerRunning && (!prio || showWatch) { changed() }
    }

    func timeUp() {
        timerRunning = false
        timerLeft = 0
        workWhy = "timer ended"
        workSync()
        guard let t = data.tasks.first(where: { $0.id == timerId && !$0.done }) else { stopTimer(); return }
        alertKind = 1
        alertTaskId = t.id
        if !demoRun { Platform.notify("Time's up", t.title) }
        if shown { showFeedback(AppModel.now()) }
        showAlert()
    }

    var alertTask: TaskItem? {
        guard alertKind != 0, let id = alertTaskId else { return nil }
        return data.tasks.first { $0.id == id && !$0.done }
    }

    var alertHolds: Bool { return alertTask != nil }

    /// Makes sure the prompt is seen: alone in the corner if the panel was closed, on top of it otherwise.
    func showAlert() {
        if alertTask == nil { clearAlert(); return }
        changed()
        if full { shell?.activateFull(); return }
        if !shown { promptOnly = true; shell?.showPanel(peekMs: 0) } else { shell?.contentChanged() }
    }

    func extend(_ t: TaskItem, _ minutes: Int) {
        timerId = t.id
        timerEnd = Date().addingMinutes(minutes)
        timerRunning = true
        holdFeedback()
        workSync()
        clearAlert()
    }

    func clearAlert() {
        alertKind = 0
        alertTaskId = nil
        if promptOnly && shown && !full && !feedbackOpen { hidePanel(); return }
        changed()
        shell?.contentChanged()
    }

    /// After a task is ticked off: stop its timer, and if it was the current one, offer the next.
    func taskCompleted(_ t: TaskItem, wasNext wasNextIn: Bool) {
        var wasNext = wasNextIn
        workCompleted(t) // work.csv: the stretch that ends here ends "completed"
        if timerId == t.id {
            stopTimer()
            wasNext = true
            if shown { showFeedback(AppModel.now()) }
        }
        resortData() // by priority: the rest are put in order for the energy you have now
        if alertTaskId == t.id { alertKind = 0 }
        if !wasNext { return }
        guard let next = upcoming().first(where: { $0 !== t }) else { alertKind = 0; alertTaskId = nil; return }
        alertKind = 2
        alertTaskId = next.id
        showAlert()
    }

    // ------------------------------------------------------------ the record (done.csv, days.csv)

    func logDone(_ t: TaskItem) {
        let now = AppModel.now()
        var secs: Double = timerSecs[t.id] ?? 0
        let worked: Double = elapsed(t) // the time worked on it, kept in tasks.json: it outlasts a restart, which the count above does not
        if worked > 0 { secs = worked }
        TaskLog.done(t, at: now, day: SleepLog.today(now), timerMinutes: secs / 60, zone: zoneName(now))
    }

    func logUndone(_ t: TaskItem) { TaskLog.undone(t.id, day: SleepLog.today(AppModel.now())) }

    /// When a new day starts, writes the day before to days.csv.
    func logDays() {
        let today = SleepLog.today(AppModel.now())
        let key = Double(today.year * 10000 + today.month * 100 + today.day)
        let seen = Settings.get("StatsDay", 0)
        if seen == key { return }
        Settings.set("StatsDay", key)
        if seen <= 0 { return }
        let s = Int(seen)
        guard let day = CornerTime.make(s / 10000, s / 100 % 100, s % 100), day < today else { return }
        SleepLog.load()
        let night = SleepLog.forDay(day)
        let e = EnergyModel.build(SleepLog.data.nights, need: Settings.get("SleepNeed", EnergyModel.defaultNeed), day: day)
        TaskLog.writeDay(TaskLog.summarise(day, data.tasks, TaskLog.readDone(), sleep: night?.totalHours, debt: e?.debtHours))
    }

    // ------------------------------------------------------------ routines

    /// First open of the day: drop yesterday's unfinished routine tasks.
    func ensureRoutines(_ day: Date) {
        logDays()
        Routines.load()
        Routines.offerNap()
        let key = CornerTime.formatDay(day)
        let before = data.tasks.count
        data.tasks.removeAll { $0.routine != nil && !$0.done && $0.routineDay != key }
        if data.tasks.count != before { save() }
    }

    func routineInToday(_ r: Routine) -> Bool {
        let key = CornerTime.formatDay(SleepLog.today(AppModel.now()))
        return data.tasks.contains { $0.routine == r.id && $0.routineDay == key }
    }

    @discardableResult
    func addRoutinesToToday(_ rs: [Routine]) -> Int {
        let key = CornerTime.formatDay(SleepLog.today(AppModel.now()))
        var added = 0
        for r in rs {
            if routineInToday(r) { continue }
            let n = max(1, r.checkIns)
            for k in 1...n {
                let t = TaskItem(id: AppModel.newId(), title: r.title + (n > 1 ? " (" + String(k) + "/" + String(n) + ")" : ""))
                t.energy = TaskTags.level(r.energy)
                t.kind = r.kind == "physical" ? "physical" : nil
                t.minutes = r.minutes
                t.routine = r.id
                t.routineDay = key
                t.part = n > 1 ? k : 0
                data.tasks.append(t)
                added += 1
            }
        }
        if added > 0 { proposal = nil; save(); changed(); shell?.contentChanged() }
        return added
    }

    var routines: [Routine] {
        if Routines.data == nil { Routines.load() }
        return Routines.data?.routines ?? []
    }

    func routinesWaiting() -> [Routine] {
        let today = SleepLog.today(AppModel.now())
        return routines.filter { !$0.off && Routines.appliesOn($0, today) && !routineInToday($0) }
    }

    func deleteRoutine(_ r: Routine) {
        guard let d = Routines.data else { return }
        d.routines.removeAll { $0 === r }
        for o in d.routines where o.after == r.id { o.after = nil }
        Routines.save()
        changed()
    }

    func beginEditRoutine(_ r: Routine) {
        editRoutine = r.id
        editRoutineText = Routines.format(r, routines)
        changed()
    }

    func commitEditRoutine(_ r: Routine, keep: Bool) {
        if keep && !editRoutineText.trimmed.isEmpty {
            Routines.apply(r, editRoutineText, routines)
            Routines.save()
        }
        editRoutine = nil
        changed()
    }

    func cycleNewRoutineEnergy() {
        let efforts: [String?] = [nil, "low", "medium", "high"]
        let i = efforts.firstIndex(where: { $0 == newRoutineEnergy }) ?? 0
        newRoutineEnergy = efforts[(i + 1) % efforts.count]
        changed()
    }

    func addRoutine() {
        let typed = newRoutineText.trimmed
        if typed.isEmpty { return }
        guard let mins = Int(newRoutineMin.trimmed), mins >= 1, mins <= 600 else { newRoutineBad = true; changed(); return }
        guard let d = Routines.data else { return }
        let r = Routine(id: "r" + String(AppModel.newId().prefix(8)))
        Routines.apply(r, typed, d.routines)
        let lower = typed.lowercased()
        if lower.range(of: #"(^|\s)!(low|med|medium|high)\b"#, options: .regularExpression) == nil { r.energy = newRoutineEnergy }
        if lower.range(of: #"(^|\s)\d+(\.\d+)?\s?(m|min|h)\b"#, options: .regularExpression) == nil { r.minutes = mins }
        newRoutineText = ""
        newRoutineEnergy = nil
        newRoutineMin = "30"
        newRoutineBad = false
        d.routines.append(r)
        Routines.save()
        changed()
    }

    // ------------------------------------------------------------ Plan

    /// "Plan my day". By priority it is an order, not times (WorkModel.swift).
    func makePlan(keepStarted: Bool = false) {
        if !keepStarted {
            if AppModel.priorityMode { makeOrder(); return }
            planSplit.removeAll()
            planKeep.removeAll()
        }
        proposePlan(keepStarted)
    }

    func proposePlan(_ keepStarted: Bool) {
        guard let e = energy else { return }
        planKeepStarted = keepStarted
        proposal = Planner.propose(data.tasks, energy: e, routines: Routines.data?.routines, now: AppModel.now(),
                                   keepStarted: keepStarted, split: planSplit)
        changed()
        shell?.contentChanged()
    }

    /// The answer to "split it, or keep it as one session?" on a row of the shown plan.
    func splitInPlan(_ id: String, _ split: Bool) {
        if split {
            planSplit.insert(id)
            planKeep.remove(id)
        } else {
            planSplit.remove(id)
            planKeep.insert(id)
        }
        proposePlan(planKeepStarted)
    }

    func discardPlan() {
        proposal = nil
        planSplit.removeAll()
        planKeep.removeAll()
        changed()
        shell?.contentChanged()
    }

    static func splitTitle(_ title: String, _ part: Int) -> String { return title + " (" + String(part) + "/2)" }

    func applyPlan() {
        guard let p = proposal else { return }
        var undo: [PlanUndo] = []
        planOrder = data.tasks.map { $0.id }
        // the first sessions before the second ones (C#: OrderBy(SplitOf))
        for pass in 0...2 {
            for it in p where it.placed && it.splitOf == pass {
                guard let t = data.tasks.first(where: { $0.id == it.id }), !t.done else { continue }
                if pass == 2 {
                    // the second session becomes a task of its own, right after the first
                    let t2 = TaskItem(id: AppModel.newId(), title: AppModel.splitTitle(it.title, 2))
                    t2.energy = t.energy
                    t2.kind = t.kind
                    t2.minutes = it.minutes
                    t2.routine = t.routine
                    t2.routineDay = t.routineDay
                    t2.setDue(it.start, hasTime: true)
                    t2.planned = true
                    let at: Int = data.tasks.firstIndex(where: { $0 === t }) ?? (data.tasks.count - 1)
                    data.tasks.insert(t2, at: at + 1)
                    var made = PlanUndo(id: t2.id)
                    made.created = true
                    undo.append(made)
                    continue
                }
                var u = PlanUndo(id: t.id)
                u.due = t.due
                u.hasTime = t.hasTime
                u.notified = t.notified
                u.planned = t.planned
                u.title = t.title
                u.minutes = t.minutes
                undo.append(u)
                t.setDue(it.start, hasTime: true)
                t.planned = true
                if pass == 1 {
                    t.title = AppModel.splitTitle(it.title, 1)
                    t.minutes = it.minutes
                }
            }
        }
        planUndo = undo
        proposal = nil
        planSplit.removeAll()
        planKeep.removeAll()
        sortByTime()
        save()
        changed()
        shell?.contentChanged()
    }

    /// After a plan is applied: today's timed tasks first, in time order, then the rest as they were.
    /// Dragging a row still reorders the list by hand afterwards.
    func sortByTime() {
        let today = Date().dateOnly
        let tomorrow = today.addingDays(1)
        func group(_ t: TaskItem) -> Int {
            let d = t.dueAt
            if t.done { return 4 }
            if t.hasTime, let dd = d, dd < tomorrow { return 1 }
            if let dd = d, dd.dateOnly > today { return 3 }
            return 2
        }
        var items: [(t: TaskItem, i: Int, g: Int, at: Double)] = []
        var i = 0
        for t in data.tasks {
            let g = group(t)
            let at: Double = g == 1 ? (t.dueAt?.timeIntervalSince1970 ?? 0) : 0
            items.append((t, i, g, at))
            i += 1
        }
        items.sort { a, b in
            if a.g != b.g { return a.g < b.g }
            if a.at != b.at { return a.at < b.at }
            return a.i < b.i
        }
        data.tasks = items.map { $0.t }
    }

    /// Puts the list back in a remembered order; tasks added since keep their place at the end.
    func restoreOrder(_ was: [String]) {
        var place: [String: Int] = [:]
        for (k, id) in was.enumerated() where place[id] == nil { place[id] = k }
        var items: [(t: TaskItem, i: Int, k: Int)] = []
        var i = 0
        for t in data.tasks {
            items.append((t, i, place[t.id] ?? Int.max))
            i += 1
        }
        items.sort { a, b in
            if a.k != b.k { return a.k < b.k }
            return a.i < b.i
        }
        data.tasks = items.map { $0.t }
    }

    func undoPlan() {
        guard let u = planUndo else { return }
        for x in u {
            guard let t = data.tasks.first(where: { $0.id == x.id }) else { continue }
            if x.created {
                if !t.done { data.tasks.removeAll { $0 === t } }
                continue
            }
            t.due = x.due; t.hasTime = x.hasTime; t.notified = x.notified; t.planned = x.planned
            if let title = x.title { t.title = title; t.minutes = x.minutes }
        }
        planUndo = nil
        if let was = planOrder {
            planOrder = nil
            restoreOrder(was)
        }
        save()
        changed()
        shell?.contentChanged()
    }

    // ------------------------------------------------------------ naps

    func logNap(_ from: Date, _ to: Date) -> Bool {
        guard let e = energy else { return false }
        let now = AppModel.now()
        let day = SleepLog.today(now)
        let mins = to.timeIntervalSince(from) / 60
        if mins < 5 || mins > 240 || to > now.addingMinutes(5) || from < e.wake { return false }
        if !SleepLog.addNap(day, from: from, to: to) { return false }
        napOpen = false
        napOffer = data.tasks.contains { Planner.plannable($0, day) }
        napFrom = TimeEntryModel("from", waking: true, daytime: true)
        napTo = TimeEntryModel("to", waking: true, daytime: true)
        refreshEnergy(opening: false)
        return true
    }

    func addNapFromBoxes() {
        let day = SleepLog.today(AppModel.now())
        let a = napFrom.get()
        let b = napTo.get()
        if a == nil { napFrom.bad = true }
        if b == nil { napTo.bad = true }
        guard let x = a, let y = b else { return }
        if !logNap(day.atTimeOfDay(x), day.atTimeOfDay(y)) { napFrom.bad = true; napTo.bad = true }
    }

    func addNapJustWoke() {
        guard let mins = Int(napJust) else { napJustBad = true; changed(); return }
        let end = CornerTime.truncateToMinute(AppModel.now())
        if !logNap(end.addingMinutes(-mins), end) { napJustBad = true; changed() }
    }

    func removeNap(_ nap: SleepSpan) {
        SleepLog.removeNap(SleepLog.today(AppModel.now()), nap)
        napOffer = false
        refreshEnergy(opening: false)
    }

    // ------------------------------------------------------------ reminders

    func checkReminders() {
        syncFromDisk()
        let now = Date()
        var hit: TaskItem? = nil
        var n = 0
        for t in data.tasks {
            if t.done || !t.hasTime || t.notified { continue }
            if let d = t.dueAt, d <= now {
                t.notified = true
                if hit == nil { hit = t }
                n += 1
            }
        }
        guard let h = hit else { return }
        save()
        Platform.notify(n == 1 ? "Reminder" : String(n) + " reminders", h.title)
        if paused { return }
        flashId = h.id
        if shown { changed() } else { shell?.showPanel(peekMs: 9000) }
    }

    // ------------------------------------------------------------ energy notices

    static let riseLeadMin: Double = 10
    static let dipLeadMin: Double = 20

    /// Once a minute while you are at the computer: a card 20 minutes before the dip and 10 before each peak.
    func energyNudges(_ now: Date) {
        let day = SleepLog.today(now)
        let key = Double(day.year * 10000 + day.month * 100 + day.day)
        SleepLog.load()
        if SleepLog.needsCheckIn(now) { return }
        guard let e = EnergyModel.build(SleepLog.data.nights, need: Settings.get("SleepNeed", EnergyModel.defaultNeed), day: day, correction: Tuning.active(now)) else { return }
        if let d = e.dip, now >= d.from.addingMinutes(-AppModel.dipLeadMin), now < d.from, Settings.get("DipNudge", 0) != key {
            Settings.set("DipNudge", key)
            shell?.showToast(Lines.dip())
        }
        let rises = [e.peak1, e.peak2]
        for i in 0..<rises.count {
            guard let z = rises[i] else { continue }
            let k = key * 10 + Double(i + 1)
            if now < z.from.addingMinutes(-AppModel.riseLeadMin) || now >= z.from || Settings.get("RiseNudge", 0) == k { continue }
            Settings.set("RiseNudge", k)
            shell?.showToast(Lines.rise())
        }
    }

    // ------------------------------------------------------------ the record, for Settings

    struct Record {
        var done = 0, total = 0, days = 1, hiDone = 0, hiTotal = 0, hiPeak = 0, rSent = 0, rDone = 0
        var timed: Double = 0
    }

    func record() -> Record {
        let now = AppModel.now()
        let today = SleepLog.today(now)
        let rows = TaskLog.readDone()
        var byDate: [Date: TaskLog.Day] = [:]
        for d in TaskLog.readDays() where d.date > today.addingDays(-7) && d.date < today { byDate[d.date] = d }
        var days = Array(byDate.values)
        days.append(TaskLog.summarise(today, data.tasks, rows, sleep: nil, debt: nil))
        var r = Record()
        r.days = days.count
        for d in days {
            r.done += d.done; r.total += d.tasks
            r.hiDone += d.doneBy[0]; r.hiTotal += d.totalBy[0]; r.hiPeak += d.highDoneOnPeak
            r.rSent += d.routinesSent; r.rDone += d.routinesDone
        }
        for x in rows where x.day > today.addingDays(-7) { r.timed += x.timerMinutes }
        return r
    }
}

/// What an applied plan replaced on one task (or, `created`, the second session it added).
struct PlanUndo {
    var id: String
    var due: String? = nil
    var title: String? = nil
    var hasTime = false
    var notified = false
    var planned = false
    var created = false
    var minutes = 0

    init(id: String) { self.id = id }
}

/// The lines on the energy notices: one at random, before the dip and before each peak.
enum Lines {
    static func dip() -> String { return dipLines.randomElement() ?? "" }
    static func rise() -> String { return riseLines.randomElement() ?? "" }

    static let dipLines = [
        "Your biological battery is about to hit that sad little red bar—step away before you start making decisions you’ll have to apologize for.",
        "The afternoon lobotomy is coming soon; do yourself a favor and stop pretending to be useful before it arrives.",
        "Incoming: a scheduled collapse of your higher brain functions. Park the ambition and go stare at a wall.",
        "Your neurons are about to file a union grievance—give them a break before they go on strike permanently.",
        "The great daily cognitive recession is almost here; liquidate your focus and go do nothing with dignity.",
        "Brace yourself—the human equivalent of a \"404 Not Found\" error is rolling in. Step away from the keyboard.",
        "Your energy is about to fall off a cliff; don't still be holding a heavy thought when it happens.",
        "A mandatory software crash is approaching—save your work and go become a piece of furniture for twenty minutes.",
        "The neurochemical tide is about to go out, leaving nothing behind but wet sand and bad ideas. Take a break.",
        "Coming soon: the part of the day where your brain turns into lukewarm oatmeal. Stop trying to sculpt it.",
        "Your willpower is about to pack its bags and leave without a forwarding address—go grab a coffee and let it.",
        "The daily devolution from \"functioning adult\" to \"houseplant\" is inbound. Embrace the photosynthesis.",
        "A wave of pure, uncut mediocrity is about to hit your bloodstream—step off the stage before you embarrass yourself.",
        "Your prefrontal cortex is about to clock out for the afternoon; don't leave the lizard brain in charge of your emails.",
        "The cognitive fog is rolling in fast—pull over before you drive your productivity straight into a ditch.",
        "Incoming: your scheduled window of zero-calorie existence. Stop hustling and just breathe for a minute.",
        "Your brain is about to switch from \"high-speed fiber\" to \"dial-up modem\"—stop trying to download anything important.",
        "A brief, mercy-killing dip in your energy is coming soon; accept it like a grown-up and go take a walk.",
        "The biological curfew on competence is almost here—close the tabs before you start clicking things you shouldn't.",
        "Your mental engine is about to start knocking—kill the ignition before you blow a gasket over a spreadsheet.",
        "Coming soon to a nervous system near you: complete and total apathy. Might as well beat it to the punch and relax.",
        "The afternoon slump is creeping up behind you with a baseball bat—sit down voluntarily before it makes you.",
        "Your daily lease on alertness is about to expire; vacate the premises before you get evicted.",
        "A glorious, guilt-free void of ambition is approaching—lean into it and stop trying to optimize your pulse.",
        "The energy dip is on the horizon; drop the rope, walk away, and let the world spin without your supervision for a while.",
    ]

    static let riseLines = [
        "A brief, miraculous surge of competence is coming soon—try not to blow it reorganizing your sock drawer.",
        "Your daily ration of functioning gray matter is about to drop; aim it at the ugly stuff before it spoils.",
        "The dopamine tide is coming in soon—use it to move a mountain, not to argue with a stranger on the internet.",
        "Incoming: a rare, temporary truce between your brain and your willpower. Don't waste it on fonts.",
        "Your cognitive high tide is rolling in; get the heavy lifting done before you wash back out to sea.",
        "Brace yourself—a suspiciously functional version of you is about to show up. Hand them the hardest job immediately.",
        "The biological lightning strike is coming soon; point it at the monster, not the mosquitos.",
        "You’re about to experience a brief outbreak of actual intelligence—quarantine it on your worst task.",
        "A window of non-idiocy is opening shortly; shove your biggest problem through it before it slams shut.",
        "Your nervous system is winding up for its one daily miracle—don't insult it with busywork.",
        "The mental caffeine is about to kick in, whether you drank any or not—go pick a fight with something difficult.",
        "Coming soon to a skull near you: actual focus. Use it on the stuff that makes you sweat, not the stuff that just makes you feel busy.",
        "Your brain’s daily lease on genius is about to start; don't sublet it to your inbox.",
        "A rare alignment of neurons is approaching—sacrifice your hardest task to it before the eclipse ends.",
        "The energy surge is on the horizon; prepare to do something that actually justifies your opposable thumbs.",
        "Your daily allotment of give-a-damn is arriving shortly—spend it all in one place, on the heaviest thing you own.",
        "Brace for impact: a wave of sheer, unadulterated competence is inbound. Put the hardest work in its path.",
        "The great biological clock is about to strike \"useful\"—be ready to wrestle something that fights back.",
        "A brief, terrifying burst of clarity is coming soon; use it to murder the task you’ve been politely ignoring.",
        "Your brain is about to stop buffering—don't waste the bandwidth on low-resolution chores.",
        "The daily surge of human potential is almost here; try to do something a golden retriever couldn't accidentally accomplish.",
        "Incoming: your scheduled window of peak cognitive violence. Point it at the problem that’s been mocking you all week.",
        "The neurochemical cavalry is riding over the hill—point them at the fortress, not the weeds.",
        "You’re about to hit your brief daily phase of evolutionary superiority; do the heavy thinking before you devolve back into a sofa cushion.",
        "A suspicious amount of energy is about to hit your bloodstream—channel it into something hard before it turns into anxiety.",
    ]
}
