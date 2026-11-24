// Port of Corner.Core/src/Stats.cs: the record of what gets done (TaskLog), kept on this computer only,
// as two spreadsheets beside tasks.json:
//   done.csv  one row each time a task is ticked off (and removed again if it is unticked the same day)
//   days.csv  one row per day Corner was running, written when the next day starts
// Both are written as the Windows app writes them: UTF-8 with a byte order mark, \r\n line ends.
import Foundation

public enum TaskLog {
    public final class Row {
        public var day: Date = Date.distantPast
        public var at: Date = Date.distantPast
        public var id: String = ""
        public var title: String = ""
        public var effort: String = ""
        public var kind: String = ""
        public var zone: String = ""
        public var routine: Bool = false
        public var planned: Bool = false
        public var minutes: Int = 0
        public var timerMinutes: Double = 0
        public init() {}
    }

    static var doneFile: String { return Store.path("done.csv") }
    static var daysFile: String { return Store.path("days.csv") }
    public static var folder: String { return Store.dir }

    /// Goes up each time done.csv is written, so what is worked out from it (Habits.log in Work.swift) knows to read it again.
    public static var changes = 0

    public static let doneHead = "date,time,task_id,title,effort,kind,from_routine,minutes_planned,placed_by_plan,planned_start,minutes_from_planned_start,timer_minutes,energy_zone"
    public static let daysHead = "date,tasks,done,open,done_pct,high_done,high_total,medium_done,medium_total,low_done,low_total,untagged_done,untagged_total,routines_sent,routines_done,placed_by_plan,placed_done,high_done_on_peak,sleep_hours,sleep_debt_hours"

    static func csv(_ s: String?) -> String {
        let v = s ?? ""
        var special = false
        for u in v.unicodeScalars where u == "," || u == "\"" || u == "\n" || u == "\r" { special = true; break }
        return special ? "\"" + v.replacingOccurrences(of: "\"", with: "\"\"") + "\"" : v
    }

    /// Splits one CSV line, with quoted fields.
    public static func split(_ line: String) -> [String] {
        var f: [String] = []
        var sb = String.UnicodeScalarView()
        var q = false
        let chars = Array(line.unicodeScalars)
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if q {
                if c == "\"" {
                    if i + 1 < chars.count && chars[i + 1] == "\"" { sb.append("\""); i += 1 } else { q = false }
                } else {
                    sb.append(c)
                }
            } else if c == "\"" {
                q = true
            } else if c == "," {
                f.append(String(sb))
                sb = String.UnicodeScalarView()
            } else {
                sb.append(c)
            }
            i += 1
        }
        f.append(String(sb))
        return f
    }

    static func append(_ file: String, _ head: String, _ line: String) {
        let fresh = !FileManager.default.fileExists(atPath: file)
        Store.appendText(file, (fresh ? head + Store.newline : "") + line + Store.newline, bom: fresh)
    }

    /// A task was ticked off at `at`, on the day `day` (the day as Corner counts it, which starts in the early morning).
    public static func done(_ t: TaskItem, at: Date, day: Date, timerMinutes: Double, zone: String?) {
        let due = t.dueAt
        let level = TaskTags.level(t.energy)
        var p: [String] = []
        p.append(CornerTime.formatDay(day))
        p.append(CornerTime.clock24(at))
        p.append(t.id)
        p.append(csv(t.title))
        p.append(level ?? "")
        p.append(t.kind ?? "mental")
        p.append(t.routine != nil ? "yes" : "no")
        p.append(String(t.minutes > 0 ? t.minutes : Planner.defaultMinutes))
        p.append(t.planned ? "yes" : "no")
        if t.planned, let d = due {
            p.append(CornerTime.clock24(d))
            p.append(NetFormat.plain(NetFormat.round(at.timeIntervalSince(d) / 60)))
        } else {
            p.append("")
            p.append("")
        }
        p.append(timerMinutes > 0 ? NetFormat.plain(NetFormat.round(timerMinutes, 1)) : "")
        p.append(zone ?? "")
        append(doneFile, doneHead, p.joined(separator: ","))
        changes += 1
    }

    /// A task was unticked: its row for `day` comes out again, so the record only holds what stayed done.
    public static func undone(_ id: String, day: Date) {
        guard var lines = Store.readLines(doneFile) else { return }
        let d = CornerTime.formatDay(day)
        var at = -1
        for i in stride(from: lines.count - 1, through: 0, by: -1) {
            let f = split(lines[i])
            if f.count > 2 && f[0] == d && f[2] == id { at = i; break }
        }
        if at <= 0 { return }
        lines.remove(at: at)
        var text = ""
        for l in lines { text += l + Store.newline }
        do { try Store.writeText(doneFile, text, bom: true) } catch { Store.log(error) }
        changes += 1
    }

    public static func readDone() -> [Row] {
        var list: [Row] = []
        guard let lines = Store.readLines(doneFile) else { return list }
        for line in lines.dropFirst() {
            let f = split(line)
            guard f.count >= 13, let day = CornerTime.parseDay(f[0]) else { continue }
            let r = Row()
            r.day = day
            if let tm = CornerTime.parseHourMinute(f[1]) { r.at = day.atTimeOfDay(tm) } else { r.at = day }
            r.id = f[2]
            r.title = f[3]
            r.effort = f[4]
            r.kind = f[5]
            r.routine = f[6] == "yes"
            r.minutes = Int(f[7].trimmed) ?? 0
            r.planned = f[8] == "yes"
            r.timerMinutes = Double(f[11].trimmed) ?? 0
            r.zone = f[12]
            list.append(r)
        }
        return list
    }

    public final class Day {
        public var date: Date = Date.distantPast
        public var tasks = 0
        public var done = 0
        public var open = 0
        public var routinesSent = 0
        public var routinesDone = 0
        public var placed = 0
        public var placedDone = 0
        public var highDoneOnPeak = 0
        public var doneBy: [Int] = [0, 0, 0, 0]   // high, medium, low, untagged
        public var totalBy: [Int] = [0, 0, 0, 0]
        public var sleep: Double? = nil
        public var debt: Double? = nil
        public init() {}
    }

    static let levels: [String?] = ["high", "medium", "low", nil]

    /// The day's figures: what was done (from done.csv) and what was still open at its end (from the task list).
    public static func summarise(_ day: Date, _ tasks: [TaskItem], _ doneRows: [Row], sleep: Double?, debt: Double?) -> Day {
        let key = CornerTime.formatDay(day)
        let dayD = day.dateOnly
        // rows of the day, one per task (the last one), in order of first appearance
        var ids: [String] = []
        var lastRow: [String: Row] = [:]
        for r in doneRows where r.day == dayD {
            if lastRow[r.id] == nil { ids.append(r.id) }
            lastRow[r.id] = r
        }
        var done: [Row] = []
        for id in ids { if let r = lastRow[id] { done.append(r) } }
        let doneIds = Set(done.map { $0.id })
        let open = tasks.filter { t in
            if t.done || doneIds.contains(t.id) { return false }
            if t.routine == nil {
                guard let d = t.dueAt else { return true }
                return d.dateOnly <= dayD
            }
            return t.routineDay == key
        }
        let d = Day()
        d.date = dayD
        d.done = done.count
        d.open = open.count
        d.sleep = sleep
        d.debt = debt
        d.tasks = d.done + d.open
        for i in 0..<4 {
            let lv = levels[i]
            d.doneBy[i] = done.filter { r -> Bool in
                let effort: String? = r.effort.isEmpty ? nil : r.effort
                return effort == lv
            }.count
            d.totalBy[i] = d.doneBy[i] + open.filter { TaskTags.level($0.energy) == lv }.count
        }
        d.routinesDone = done.filter { $0.routine }.count
        d.routinesSent = d.routinesDone + open.filter { $0.routine != nil }.count
        d.placedDone = done.filter { $0.planned }.count
        d.placed = d.placedDone + open.filter { t in
            guard t.planned, let due = t.dueAt else { return false }
            return due.dateOnly == dayD
        }.count
        d.highDoneOnPeak = done.filter { $0.effort == "high" && $0.zone == "peak" }.count
        return d
    }

    public static func writeDay(_ d: Day) {
        func num(_ v: Double?) -> String {
            guard let x = v else { return "" }
            return NetFormat.plain(NetFormat.round(x, 2))
        }
        var p: [String] = [CornerTime.formatDay(d.date), String(d.tasks), String(d.done), String(d.open),
                           d.tasks > 0 ? NetFormat.plain(NetFormat.round(100.0 * Double(d.done) / Double(d.tasks))) : ""]
        for i in 0..<4 { p.append(String(d.doneBy[i])); p.append(String(d.totalBy[i])) }
        p.append(String(d.routinesSent))
        p.append(String(d.routinesDone))
        p.append(String(d.placed))
        p.append(String(d.placedDone))
        p.append(String(d.highDoneOnPeak))
        p.append(num(d.sleep))
        p.append(num(d.debt))
        append(daysFile, daysHead, p.joined(separator: ","))
    }

    public static func readDays() -> [Day] {
        var list: [Day] = []
        guard let lines = Store.readLines(daysFile) else { return list }
        for line in lines.dropFirst() {
            let f = split(line)
            guard f.count >= 20, let day = CornerTime.parseDay(f[0]) else { continue }
            func I(_ i: Int) -> Int { return Int(f[i].trimmed) ?? 0 }
            let d = Day()
            d.date = day
            d.tasks = I(1); d.done = I(2); d.open = I(3)
            d.routinesSent = I(13); d.routinesDone = I(14); d.placed = I(15); d.placedDone = I(16); d.highDoneOnPeak = I(17)
            for i in 0..<4 { d.doneBy[i] = I(5 + 2 * i); d.totalBy[i] = I(6 + 2 * i) }
            list.append(d)
        }
        return list
    }
}
