// Port of the model part of windows/src/Work.cs: working on a task, and what Corner learns from it.
//   work.csv   one row for each stretch of work on a task, from play to pause or done: when, how long, and the energy
//              % at its start and end. Kept beside tasks.json; it opens in a spreadsheet. (WorkRow, WorkLog)
//   Habits     what the log says you usually do in each part of the day (morning, noon, afternoon, evening).
//   Priority   "Plan by: Priority" (Settings): the open tasks in the order to do them for your energy now. No clock times.
// The list is put in that order when a plan is applied and each time a task is paused or completed, and at no other time
// (that rule, the stopwatch and the log's bookkeeping are the app's side: `partial class Shell` in Work.cs).
import Foundation

// ---------------------------------------------------------------- work.csv

public final class WorkRow {
    public var start: Date
    public var end: Date
    public var minutes: Double
    public var id: String
    public var title: String
    public var effort: String?     // "high", "medium", "low", or nil
    public var kind: String?       // "physical" or "mental"; nil is written as "mental"
    public var ended: String       // paused, completed, switched, away, timer ended, closed
    public var routine: Bool
    public var planned: Int        // the task's planned length, minutes
    public var e0: Double          // energy % at the start; -1 = not known
    public var e1: Double          // energy % at the end; -1 = not known
    public var felt: Double        // a % typed in meanwhile; -1 = not known
    public var ticked: Bool = false // not from work.csv: a task ticked off with no timer (done.csv). It marks the day, and has no minutes

    public init(start: Date = Date.distantPast, end: Date = Date.distantPast, minutes: Double = 0, id: String = "", title: String = "",
                effort: String? = nil, kind: String? = nil, ended: String = "", routine: Bool = false, planned: Int = 0,
                e0: Double = -1, e1: Double = -1, felt: Double = -1) {
        self.start = start
        self.end = end
        self.minutes = minutes
        self.id = id
        self.title = title
        self.effort = effort
        self.kind = kind
        self.ended = ended
        self.routine = routine
        self.planned = planned
        self.e0 = e0
        self.e1 = e1
        self.felt = felt
    }
}

public enum WorkLog {
    public static let head = "date,start,end,minutes,task_id,title,effort,kind,from_routine,minutes_planned,energy_start,energy_end,energy_typed,ended,part_of_day"
    public static var file: String { return Store.path("work.csv") }

    static var cache: [WorkRow]? = nil

    /// The whole log, read once and then kept up as rows are added.
    public static var rows: [WorkRow] {
        if let c = cache { return c }
        let list: [WorkRow] = read()
        cache = list
        return list
    }

    public static func forget() { cache = nil }

    static func csv(_ s: String?) -> String {
        let v: String = s ?? ""
        var special = false
        for u in v.unicodeScalars where u == "," || u == "\"" || u == "\n" || u == "\r" { special = true; break }
        return special ? "\"" + v.replacingOccurrences(of: "\"", with: "\"\"") + "\"" : v
    }

    static func pct(_ v: Double) -> String {
        return v < 0 ? "" : NetFormat.plain(NetFormat.round(v))
    }

    static func num(_ s: String) -> Double {
        if s.isEmpty { return -1 }
        return Double(s.trimmed) ?? -1
    }

    /// "HH:mm:ss" of a moment.
    static func clockSeconds(_ d: Date) -> String {
        return String(CornerTime.sortable(d).suffix(8))
    }

    /// Parses "HH:mm:ss" exactly (TimeSpan "hh\:mm\:ss") into seconds since midnight.
    static func parseClockSeconds(_ s: String) -> TimeInterval? {
        let u: [UInt8] = Array(s.utf8)
        if u.count != 8 || u[2] != 58 || u[5] != 58 { return nil }
        guard let h = CornerTime.digits(u, 0, 2), let m = CornerTime.digits(u, 3, 2), let sec = CornerTime.digits(u, 6, 2) else { return nil }
        if h > 23 || m > 59 || sec > 59 { return nil }
        let total: Int = h * 3600 + m * 60 + sec
        return TimeInterval(total)
    }

    /// One row as a line of work.csv.
    public static func line(_ r: WorkRow) -> String {
        var f: [String] = []
        f.append(CornerTime.formatDay(r.start))
        f.append(clockSeconds(r.start))
        f.append(clockSeconds(r.end))
        f.append(NetFormat.plain(NetFormat.round(r.minutes, 1)))
        f.append(r.id)
        f.append(csv(r.title))
        f.append(r.effort ?? "")
        f.append(r.kind ?? "mental")
        f.append(r.routine ? "yes" : "no")
        f.append(String(r.planned))
        f.append(pct(r.e0))
        f.append(pct(r.e1))
        f.append(pct(r.felt))
        f.append(r.ended)
        f.append(Habits.partNames[Habits.part(r.start)])
        return f.joined(separator: ",")
    }

    /// Reads one line of work.csv. Nil when it is not a row (C#: bool Parse(line, out row)).
    public static func parse(_ line: String) -> WorkRow? {
        let f: [String] = TaskLog.split(line)
        if f.count < 15 { return nil }
        guard let day = CornerTime.parseDay(f[0]) else { return nil }
        guard let a = parseClockSeconds(f[1]), let b = parseClockSeconds(f[2]) else { return nil }
        let mins: Double = Double(f[3].trimmed) ?? 0
        let planned: Int = Int(f[9].trimmed) ?? 0
        let start: Date = day.atTimeOfDay(a)
        let endDay: Date = b < a ? day.addingDays(1) : day // a stretch across midnight ends the next day
        let end: Date = endDay.atTimeOfDay(b)
        let r = WorkRow(start: start, end: end, minutes: mins, id: f[4], title: f[5])
        r.effort = f[6].isEmpty ? nil : f[6]
        r.kind = f[7]
        r.routine = f[8] == "yes"
        r.planned = planned
        r.e0 = num(f[10])
        r.e1 = num(f[11])
        r.felt = num(f[12])
        r.ended = f[13]
        return r
    }

    public static func read() -> [WorkRow] {
        var list: [WorkRow] = []
        guard let lines = Store.readLines(file) else { return list }
        for line in lines.dropFirst() {
            if let r = parse(line) { list.append(r) }
        }
        return list
    }

    public static func add(_ r: WorkRow) {
        var list: [WorkRow] = rows
        list.append(r)
        cache = list
        let fresh: Bool = !FileManager.default.fileExists(atPath: file)
        Store.appendText(file, (fresh ? head + Store.newline : "") + line(r) + Store.newline, bom: fresh)
    }
}

// ---------------------------------------------------------------- what you usually do, and when

public enum Habits {
    public static let partNames: [String] = ["morning", "noon", "afternoon", "evening"]
    public static let minDays = 3                 // done in the same part of the day on at least this many days...
    public static let windowDays = 28             // ...of the last this-many, to count as usual
    public static let minMinutes: Double = 1      // a stretch shorter than this is not counted

    static let partTailRx = Rx(#"\s*\(\d+/\d+\)\s*$"#)
    static let spaceRx = Rx(#"\s+"#)

    /// 0 morning (to 12:00), 1 noon (to 14:00), 2 afternoon (to 18:00), 3 evening (and the small hours).
    public static func part(_ t: Date) -> Int {
        let h: Double = t.timeOfDay / 3600
        if h < 4 { return 3 }
        if h < 12 { return 0 }
        if h < 14 { return 1 }
        if h < 18 { return 2 }
        return 3
    }

    /// What makes two tasks "the same thing": the title, without case, spare spaces or a "(1/2)" at its end.
    public static func key(_ title: String?) -> String {
        let s: String = partTailRx.replaceAll(title ?? "", "")
        return spaceRx.replaceAll(s, " ").trimmed.lowercased()
    }

    public final class Habit {
        public var title: String
        public var effort: String?
        public var kind: String?
        public var minutes: Int
        public var days: Int
        public var routine: Bool

        public init(title: String = "", effort: String? = nil, kind: String? = nil, minutes: Int = 0, days: Int = 0, routine: Bool = false) {
            self.title = title
            self.effort = effort
            self.kind = kind
            self.minutes = minutes
            self.days = days
            self.routine = routine
        }
    }

    /// The work log and, from done.csv, the tasks ticked off without a timer: ticking a task off is doing it too.
    /// A tick is left out when the same task has timed work on the same day (that work already marks the day).
    /// A tick from before work.csv existed that has timer minutes of its own keeps them.
    public static func withDone(_ work: [WorkRow]?, _ done: [TaskLog.Row]?) -> [WorkRow] {
        var all: [WorkRow] = work ?? []
        var timed = Set<String>()
        var inWork = Set<String>()
        for r in all {
            inWork.insert(r.id)
            if r.minutes >= minMinutes { timed.insert(r.id + "|" + CornerTime.formatDay(SleepLog.today(r.start))) }
        }
        for d in done ?? [] {
            if timed.contains(d.id + "|" + CornerTime.formatDay(d.day)) { continue }
            // the small hours belong to the day before (done.csv)
            let small: Bool = d.at.timeOfDay / 3600 < Double(SleepLog.dayStartsAt)
            let at: Date = small ? d.at.addingDays(1) : d.at
            // (a task in work.csv has its minutes there, on the days they were worked)
            let had: Bool = d.timerMinutes >= minMinutes && !inWork.contains(d.id)
            let start: Date = had ? at.addingMinutes(-d.timerMinutes) : at
            let r = WorkRow(start: start, end: at, minutes: had ? d.timerMinutes : 0, id: d.id, title: d.title)
            r.effort = d.effort.isEmpty ? nil : d.effort
            r.kind = d.kind
            r.routine = d.routine
            r.planned = d.minutes
            r.ended = "completed"
            r.ticked = !had
            all.append(r)
        }
        return all
    }

    static var logCache: [WorkRow]? = nil
    static var logWork = -1
    static var logDone = -1

    /// What was worked on and what was ticked off, together. Made again only when one of the two logs has changed.
    public static var log: [WorkRow] {
        let work: [WorkRow] = WorkLog.rows
        if let c = logCache, logWork == work.count, logDone == TaskLog.changes { return c }
        logWork = work.count
        logDone = TaskLog.changes
        let made: [WorkRow] = withDone(work, TaskLog.readDone())
        logCache = made
        return made
    }

    /// The minutes a row stands for: its own, or for a tick with no timer the length the task was given.
    static func weight(_ r: WorkRow) -> Double {
        if r.ticked { return Double(r.planned > 0 ? r.planned : Planner.defaultMinutes) }
        return r.minutes
    }

    static func window(_ rows: [WorkRow], _ part: Int, _ today: Date) -> [WorkRow] {
        let last: Date = today.dateOnly
        let first: Date = last.addingDays(-windowDays)
        return rows.filter { r in
            if r.minutes < minMinutes && !r.ticked { return false }
            if Habits.part(r.start) != part { return false }
            let d: Date = r.start.dateOnly
            return d < last && d >= first
        }
    }

    /// The things usually done in this part of the day, most regular first. A habit's length is the middle one of
    /// the minutes it really took on the days it was timed, to the nearest five; never timed, it is the length it was given.
    public static func usual(_ rows: [WorkRow], _ part: Int, _ today: Date) -> [Habit] {
        // grouped by key, in the order the keys first appear
        var keys: [String] = []
        var groups: [String: [WorkRow]] = [:]
        for r in window(rows, part, today) {
            let k: String = key(r.title)
            if groups[k] == nil {
                keys.append(k)
                groups[k] = [r]
            } else {
                groups[k]?.append(r)
            }
        }
        var list: [Habit] = []
        for k in keys {
            let g: [WorkRow] = groups[k] ?? []
            if k.isEmpty || g.isEmpty { continue }
            var perDay: [Date: Double] = [:]
            for r in g {
                let d: Date = r.start.dateOnly
                perDay[d] = (perDay[d] ?? 0) + r.minutes
            }
            let days: [Double] = perDay.values.sorted()
            if days.count < minDays { continue }
            var last: WorkRow = g[0]
            for r in g where r.start >= last.start { last = r }
            let timedDays: [Double] = days.filter { $0 >= minMinutes }
            let middle: Double = timedDays.isEmpty ? weight(last) : timedDays[timedDays.count / 2]
            let mins: Int = Int(max(5, NetFormat.round(middle / 5.0) * 5))
            list.append(Habit(title: partTailRx.replaceAll(last.title, ""), effort: last.effort, kind: last.kind,
                              minutes: mins, days: days.count, routine: last.routine))
        }
        return stableSorted(list) { a, b in
            if a.days != b.days { return a.days > b.days }
            return titleBefore(a.title, b.title)
        }
    }

    /// Titles in dictionary order, case set aside (C# orders them by the current culture).
    static func titleBefore(_ a: String, _ b: String) -> Bool {
        let c: ComparisonResult = a.caseInsensitiveCompare(b)
        if c != .orderedSame { return c == .orderedAscending }
        return a > b
    }

    /// How much of this part of the day's logged minutes went to work of this effort and kind, 0..1.
    public static func typeShare(_ rows: [WorkRow], _ part: Int, _ today: Date, effort: String?, kind: String?) -> Double {
        let w: [WorkRow] = window(rows, part, today)
        var all = 0.0
        for r in w { all += weight(r) }
        if all < 30 { return 0 } // too little to say
        let phys: Bool = kind == "physical"
        var same = 0.0
        for r in w where r.effort == effort && (r.kind == "physical") == phys { same += weight(r) }
        return same / all
    }
}

// ---------------------------------------------------------------- the order to do things in, for your energy now

public final class Ranked {
    public var task: TaskItem
    public var score: Double = 0
    public var why: String? = nil   // a few words on why it stands where it does, or nil
    public var later: Bool = false  // dated for a later day: after everything else, as it was

    public init(task: TaskItem) {
        self.task = task
    }
}

public enum Priority {
    public static let lowLevel: Double = 0.35   // the energy counter, 0..1: under this it has a say too
    public static let highLevel: Double = 0.70  // and over this
    public static let soonMin = 60              // a time you set yourself counts as "due" this long before it

    public static func zoneAt(_ e: EnergyDay?, _ now: Date) -> Zone? {
        guard let e = e else { return nil }
        let zones: [Zone?] = [e.peak1, e.dip, e.peak2]
        for item in zones {
            if let z = item, now >= z.from, now < z.to { return z }
        }
        return nil
    }

    /// The open tasks, best first for this moment. `level` is the energy counter (0..1, or below 0 when not known);
    /// `rows` is the work log; `working` is the task being worked on, which stays first.
    public static func rank(_ tasks: [TaskItem], energy e: EnergyDay?, now: Date, level: Double, rows: [WorkRow]?, working: String?) -> [Ranked] {
        let part: Int = Habits.part(now)
        let log: [WorkRow] = rows ?? []
        var usual = Set<String>()
        for h in Habits.usual(log, part, now) { usual.insert(Habits.key(h.title)) }
        let zone: Zone? = zoneAt(e, now)
        // the groggy stretch after waking: Plan keeps it for light work, and so does this
        var waking = false
        if let day = e { waking = now >= day.wake && now < day.groggyUntil }
        var list: [Ranked] = []
        for t in tasks {
            if t.done { continue }
            let r: Ranked = one(t, zone, waking, now, level, log, part, usual)
            if let w = working, t.id == w {
                r.score = 1000
                r.why = nil
            }
            list.append(r)
        }
        // not dated for later first, the higher score first; ties keep the list's order
        return stableSorted(list) { a, b in
            if a.later != b.later { return !a.later }
            let sa: Double = a.later ? 0 : a.score
            let sb: Double = b.later ? 0 : b.score
            return sa > sb
        }
    }

    static func one(_ t: TaskItem, _ zone: Zone?, _ waking: Bool, _ now: Date, _ level: Double, _ rows: [WorkRow], _ part: Int, _ usual: Set<String>) -> Ranked {
        let r = Ranked(task: t)
        let due: Date? = t.dueAt
        if let d = due, d.dateOnly > now.dateOnly {
            r.later = true
            return r
        }
        var score = 0.0
        var best = 0.0
        var why: String? = nil
        func add(_ v: Double, _ text: String?) {
            score += v
            if let tx = text, v > best {
                best = v
                why = tx
            }
        }

        let effort: String? = TaskTags.level(t.energy)
        let mins: Int = t.minutes > 0 ? t.minutes : Planner.defaultMinutes
        var peak = false
        var dip = false
        if let z = zone {
            peak = z.peak
            dip = !z.peak
        }
        if t.kind == "physical" {
            // physical work has its own time of day, whatever the curve is doing
            let h: Double = now.timeOfDay / 3600
            if h >= Planner.physicalFrom && h < Planner.physicalTo { add(2.5, "good time for physical work") }
        } else if waking {
            // still waking up: something light first, and the hard thing waits for the peak (as Plan places them)
            if effort == "low" {
                add(2, "easy start while you wake up")
            } else if effort == "medium" {
                add(0.5, nil)
            } else if effort == "high" {
                add(-1, nil)
            }
        } else {
            var fit = 0.0
            if effort == "high" {
                fit = peak ? 3 : (dip ? -2 : 0.5)
                add(fit, peak ? "fits your peak" : nil)
            } else if effort == "medium" {
                fit = dip ? 0 : 1.5
                add(fit, (peak || dip) ? nil : "fits your energy now")
            } else if effort == "low" {
                fit = dip ? 3 : (peak ? -1 : 1)
                add(fit, dip ? "light work for the dip" : nil)
            }
            if let z = zone, fit >= 1.5 {
                // and does it fit in what is left of the peak or the dip?
                let left: Double = z.to.timeIntervalSince(now) / 60
                if Double(mins) <= left + 5 {
                    add(1, nil)
                } else if Double(mins) > left + 15 {
                    // it runs well past the end: still the best time for it, but the reason says how long the peak lasts
                    add(-1, nil)
                    if peak && why == "fits your peak" { why = "your peak lasts until " + EnergyDay.clock(z.to) }
                }
            }
            if level >= 0 && level < lowLevel {
                if effort == "low" {
                    add(1.5, "low energy now")
                } else if effort == "high" {
                    add(-1.5, nil)
                }
            }
            if level > highLevel && effort == "high" { add(1, "high energy now") }
        }

        if let d = due {
            if t.hasTime && !t.planned {
                // a time you set yourself (a time an old schedule gave it does not count)
                let dueAtText: String = "due at " + EnergyDay.clock(d)
                if d <= now {
                    let sameDay: Bool = d.dateOnly == now.dateOnly
                    add(6, sameDay ? dueAtText : "due")
                } else if d <= now.addingMinutes(soonMin) {
                    add(5, dueAtText)
                }
            } else if !t.hasTime {
                if d.dateOnly < now.dateOnly {
                    add(3, "due")
                } else {
                    add(1, "due today")
                }
            }
        }

        if usual.contains(Habits.key(t.title)) {
            add(1.5, "usual for your " + Habits.partNames[part])
        } else {
            add(Habits.typeShare(rows, part, now, effort: effort, kind: t.kind), nil)
        }

        r.score = score
        r.why = why
        return r
    }
}
