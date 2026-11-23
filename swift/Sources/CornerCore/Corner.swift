// Port of Corner.Core/src/Corner.cs: TaskItem, TaskData (C# "Data"), Store, Settings and When ("tomorrow 9am" parsing).
// Also holds the shared helpers that stand in for .NET: DateTime maths on Date (CornerTime), .NET number
// formatting (NetFormat), a small NSRegularExpression wrapper (Rx) and file helpers.
import Foundation

// ---------------------------------------------------------------- DateTime helpers

/// C# DateTime semantics on Foundation Date, in the current time zone with the Gregorian calendar.
/// `dateOnly` is DateTime.Date, `addingDays` keeps the wall-clock time like DateTime.AddDays,
/// `addingHours` / `addingMinutes` add elapsed time. Time-of-day values (C# TimeSpan) are TimeInterval seconds.
public enum CornerTime {
    public static let calendar: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone.current
        return c
    }()

    /// DateTime.MinValue / MaxValue.
    public static let minValue: Date = Date.distantPast
    public static let maxValue: Date = Date.distantFuture

    /// True when the user's locale shows times on a 12-hour clock (C#: ShortTimePattern contains 'h').
    public static var uses12HourClock: Bool {
        let f = DateFormatter.dateFormat(fromTemplate: "j", options: 0, locale: Locale.current) ?? ""
        return f.contains("a")
    }

    /// A date from its parts, or nil when they do not make a valid date (like `new DateTime(...)` throwing).
    public static func make(_ year: Int, _ month: Int, _ day: Int, _ hour: Int = 0, _ minute: Int = 0) -> Date? {
        if year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31 { return nil }
        if hour < 0 || hour > 23 || minute < 0 || minute > 59 { return nil }
        var c = DateComponents()
        c.year = year; c.month = month; c.day = day; c.hour = hour; c.minute = minute; c.second = 0
        guard let d = calendar.date(from: c) else { return nil }
        let back = calendar.dateComponents([.year, .month, .day], from: d)
        if back.year != year || back.month != month || back.day != day { return nil }
        return d
    }

    static func digits(_ u: [UInt8], _ from: Int, _ count: Int) -> Int? {
        var v = 0
        for i in from..<(from + count) {
            let b = u[i]
            if b < 48 || b > 57 { return nil }
            v = v * 10 + Int(b - 48)
        }
        return v
    }

    static var parseCache: [String: Date] = [:]

    /// Parses "yyyy-MM-ddTHH:mm" exactly (DateTime.TryParseExact with the invariant culture). Nil when it does not match.
    public static func parse(_ s: String?) -> Date? {
        guard let s = s else { return nil }
        if let hit = parseCache[s] { return hit }
        let u = Array(s.utf8)
        if u.count != 16 || u[4] != 45 || u[7] != 45 || u[10] != 84 || u[13] != 58 { return nil }
        guard let y = digits(u, 0, 4), let mo = digits(u, 5, 2), let d = digits(u, 8, 2),
              let h = digits(u, 11, 2), let mi = digits(u, 14, 2) else { return nil }
        guard let r = make(y, mo, d, h, mi) else { return nil }
        if parseCache.count > 50000 { parseCache.removeAll() }
        parseCache[s] = r
        return r
    }

    /// Parses "yyyy-MM-dd" exactly. Nil when it does not match.
    public static func parseDay(_ s: String?) -> Date? {
        guard let s = s else { return nil }
        let u = Array(s.utf8)
        if u.count != 10 || u[4] != 45 || u[7] != 45 { return nil }
        guard let y = digits(u, 0, 4), let mo = digits(u, 5, 2), let d = digits(u, 8, 2) else { return nil }
        return make(y, mo, d)
    }

    /// Parses "HH:mm" (TimeSpan "hh\:mm") into seconds since midnight.
    public static func parseHourMinute(_ s: String?) -> TimeInterval? {
        guard let s = s else { return nil }
        let u = Array(s.utf8)
        if u.count != 5 || u[2] != 58 { return nil }
        guard let h = digits(u, 0, 2), let m = digits(u, 3, 2) else { return nil }
        if h > 23 || m > 59 { return nil }
        return TimeInterval(h * 3600 + m * 60)
    }

    static func pad(_ v: Int, _ width: Int) -> String {
        var s = String(v < 0 ? -v : v)
        while s.count < width { s = "0" + s }
        return v < 0 ? "-" + s : s
    }

    /// "yyyy-MM-ddTHH:mm"
    public static func format(_ d: Date) -> String {
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute], from: d)
        let day: String = pad(c.year ?? 1, 4) + "-" + pad(c.month ?? 1, 2) + "-" + pad(c.day ?? 1, 2)
        let time: String = pad(c.hour ?? 0, 2) + ":" + pad(c.minute ?? 0, 2)
        return day + "T" + time
    }

    /// "yyyy-MM-dd"
    public static func formatDay(_ d: Date) -> String {
        let c = calendar.dateComponents([.year, .month, .day], from: d)
        return pad(c.year ?? 1, 4) + "-" + pad(c.month ?? 1, 2) + "-" + pad(c.day ?? 1, 2)
    }

    /// "HH:mm"
    public static func clock24(_ d: Date) -> String {
        let c = calendar.dateComponents([.hour, .minute], from: d)
        return pad(c.hour ?? 0, 2) + ":" + pad(c.minute ?? 0, 2)
    }

    /// "yyyy-MM-ddTHH:mm:ss" (C# ToString("s")).
    public static func sortable(_ d: Date) -> String {
        let c = calendar.dateComponents([.second], from: d)
        return format(d) + ":" + pad(c.second ?? 0, 2)
    }

    /// Whole days from the day of `a` to the day of `b` (C#: (b.Date - a.Date).Days).
    public static func days(from a: Date, to b: Date) -> Int {
        return calendar.dateComponents([.day], from: a.dateOnly, to: b.dateOnly).day ?? 0
    }

    /// The moment truncated to the whole minute.
    public static func truncateToMinute(_ d: Date) -> Date {
        return calendar.dateInterval(of: .minute, for: d)?.start ?? d
    }

    static let weekdayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]
    static let monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
}

extension Date {
    /// DateTime.Date: midnight at the start of this day.
    public var dateOnly: Date { return CornerTime.calendar.startOfDay(for: self) }

    /// DateTime.AddDays with whole days: the same wall-clock time n days later.
    public func addingDays(_ n: Int) -> Date {
        return CornerTime.calendar.date(byAdding: .day, value: n, to: self) ?? addingTimeInterval(Double(n) * 86400)
    }

    /// DateTime.AddDays with a fraction: whole days by the calendar, the rest as elapsed time.
    public func addingDays(_ n: Double) -> Date {
        let whole = n.rounded(.towardZero)
        return addingDays(Int(whole)).addingTimeInterval((n - whole) * 86400)
    }

    public func addingYears(_ n: Int) -> Date {
        return CornerTime.calendar.date(byAdding: .year, value: n, to: self) ?? self
    }

    public func addingHours(_ h: Double) -> Date { return addingTimeInterval(h * 3600) }
    public func addingHours(_ h: Int) -> Date { return addingTimeInterval(Double(h) * 3600) }
    public func addingMinutes(_ m: Double) -> Date { return addingTimeInterval(m * 60) }
    public func addingMinutes(_ m: Int) -> Date { return addingTimeInterval(Double(m) * 60) }

    /// DateTime.TimeOfDay, in seconds since midnight (wall clock).
    public var timeOfDay: TimeInterval {
        let sod = dateOnly
        let diff = timeIntervalSince(sod)
        let c = CornerTime.calendar.dateComponents([.hour, .minute, .second], from: self)
        let hh: Int = c.hour ?? 0
        let mm: Int = c.minute ?? 0
        let ss: Int = c.second ?? 0
        let wallSeconds: Int = hh * 3600 + mm * 60 + ss
        let wall = Double(wallSeconds)
        if abs(diff.rounded(.down) - wall) < 1 { return diff }
        return wall + (diff - diff.rounded(.down))
    }

    /// C#: day.Date + timeOfDay (a wall-clock time on this day).
    public func atTimeOfDay(_ t: TimeInterval) -> Date {
        let sod = dateOnly
        let guess = sod.addingTimeInterval(t)
        if t >= 0 && t < 86400 {
            let whole = Int(t.rounded(.down))
            var c = CornerTime.calendar.dateComponents([.year, .month, .day], from: sod)
            c.hour = whole / 3600; c.minute = (whole % 3600) / 60; c.second = whole % 60
            if let d = CornerTime.calendar.date(from: c) {
                let r = d.addingTimeInterval(t - Double(whole))
                if abs(r.timeIntervalSince(guess)) < 0.5 { return guess }
                return r
            }
        }
        return guess
    }

    public var hour: Int { return CornerTime.calendar.component(.hour, from: self) }
    public var minute: Int { return CornerTime.calendar.component(.minute, from: self) }
    public var year: Int { return CornerTime.calendar.component(.year, from: self) }
    public var month: Int { return CornerTime.calendar.component(.month, from: self) }
    public var day: Int { return CornerTime.calendar.component(.day, from: self) }
    /// C# DayOfWeek: Sunday = 0 ... Saturday = 6.
    public var dayOfWeek: Int { return CornerTime.calendar.component(.weekday, from: self) - 1 }
}

// ---------------------------------------------------------------- .NET number formatting

/// Number formatting that matches .NET (invariant culture): custom formats like "0.00" and "0.###"
/// (rounded half away from zero on the 15-significant-digit value, as .NET does), default ToString(),
/// and Math.Round (half to even).
public enum NetFormat {
    /// Digits of x (> 0) rounded to 15 significant digits, and how many of them come before the decimal point.
    static func digits15(_ x: Double) -> ([Int], Int) {
        let s = String(format: "%.14e", x)
        var digits: [Int] = []
        var exp = ""
        var inExp = false
        for ch in s {
            if inExp { exp.append(ch); continue }
            if ch == "e" || ch == "E" { inExp = true; continue }
            if let v = ch.wholeNumberValue { digits.append(v) }
        }
        let e = Int(exp) ?? 0
        return (digits, e + 1)
    }

    /// A custom format with `minDecimals` zeros and up to `maxDecimals` digits after the point ("0.00" = 2, 2; "0.###" = 0, 3).
    public static func custom(_ x: Double, minDecimals: Int, maxDecimals: Int) -> String {
        if x.isNaN { return "NaN" }
        if x.isInfinite { return x < 0 ? "-∞" : "∞" }
        let negative = x.sign == .minus
        let ax = Swift.abs(x)
        var digits: [Int] = []
        var point = 0
        if ax != 0 {
            let r = digits15(ax)
            digits = r.0
            point = r.1
        }
        let keep = point + maxDecimals
        if keep < 0 {
            digits = []
        } else if keep < digits.count {
            let up = digits[keep] >= 5
            digits = Array(digits[0..<keep])
            if up {
                var i = keep - 1
                var carry = true
                while carry && i >= 0 {
                    digits[i] += 1
                    if digits[i] == 10 { digits[i] = 0; i -= 1 } else { carry = false }
                }
                if carry { digits.insert(1, at: 0); point += 1 }
            }
        }
        var intPart = ""
        if point <= 0 {
            intPart = "0"
        } else {
            for i in 0..<point { intPart += i < digits.count ? String(digits[i]) : "0" }
        }
        var frac: [Int] = []
        if maxDecimals > 0 {
            for k in 0..<maxDecimals {
                let i = point + k
                frac.append(i >= 0 && i < digits.count ? digits[i] : 0)
            }
        }
        while frac.count > minDecimals, let lastDigit = frac.last, lastDigit == 0 { frac.removeLast() }
        var s = (negative ? "-" : "") + intPart
        if !frac.isEmpty { s += "." + frac.map { String($0) }.joined() }
        return s
    }

    /// x.ToString("0.000") with `decimals` zeros; decimals 0 is "0".
    public static func fixed(_ x: Double, _ decimals: Int) -> String {
        return custom(x, minDecimals: decimals, maxDecimals: decimals)
    }

    /// x.ToString("0.###") with up to `maxDecimals` digits.
    public static func optional(_ x: Double, _ maxDecimals: Int) -> String {
        return custom(x, minDecimals: 0, maxDecimals: maxDecimals)
    }

    /// x.ToString("+0.000"): a plus in front, and a minus before that for negative numbers.
    public static func plusPrefixed(_ x: Double, _ decimals: Int) -> String {
        return (x.sign == .minus ? "-" : "") + "+" + fixed(Swift.abs(x), decimals)
    }

    /// x.ToString("+0.000;-0.000"). A negative number that rounds to zero comes out as "-+0.000", as in .NET.
    public static func plusMinus(_ x: Double, _ decimals: Int) -> String {
        if x.sign != .minus { return "+" + fixed(x, decimals) }
        let r = fixed(-x, decimals)
        let allZero = !r.contains(where: { $0 >= "1" && $0 <= "9" })
        return allZero ? "-+" + r : "-" + r
    }

    /// Default double.ToString() (shortest round-trip form, "15" not "15.0", "1E-07").
    public static func plain(_ x: Double) -> String {
        if x.isNaN { return "NaN" }
        if x.isInfinite { return x < 0 ? "-∞" : "∞" }
        if x == x.rounded() && Swift.abs(x) < 1e15 {
            let i = Int64(x)
            if i == 0 && x.sign == .minus { return "-0" }
            return String(i)
        }
        var s = "\(x)"
        if let r = s.range(of: "e") {
            let mant = String(s[s.startIndex..<r.lowerBound])
            var ex = String(s[r.upperBound...])
            var sign = "+"
            if ex.hasPrefix("-") { sign = "-"; ex.removeFirst() } else if ex.hasPrefix("+") { ex.removeFirst() }
            while ex.count < 2 { ex = "0" + ex }
            s = mant + "E" + sign + ex
        }
        return s
    }

    /// Math.Round(x): half to even.
    public static func round(_ x: Double) -> Double { return x.rounded(.toNearestOrEven) }

    /// Math.Round(x, digits), as .NET does it: scale, round half to even, scale back.
    public static func round(_ x: Double, _ digits: Int) -> Double {
        var p = 1.0
        for _ in 0..<max(0, digits) { p *= 10 }
        return (x * p).rounded(.toNearestOrEven) / p
    }
}

// ---------------------------------------------------------------- regular expressions

/// NSRegularExpression with the few operations Corner uses. Patterns are fixed, so a bad one is a programming error.
final class Rx {
    let re: NSRegularExpression

    init(_ pattern: String, ignoreCase: Bool = false) {
        do {
            re = try NSRegularExpression(pattern: pattern, options: ignoreCase ? [.caseInsensitive] : [])
        } catch {
            fatalError("Corner: bad regular expression " + pattern)
        }
    }

    func match(_ s: String) -> RxMatch? {
        let ns = NSString(string: s)
        guard let r = re.firstMatch(in: s, options: [], range: NSRange(location: 0, length: ns.length)) else { return nil }
        return RxMatch(r: r, ns: ns)
    }

    func matches(_ s: String) -> [RxMatch] {
        let ns = NSString(string: s)
        return re.matches(in: s, options: [], range: NSRange(location: 0, length: ns.length)).map { RxMatch(r: $0, ns: ns) }
    }

    /// Regex.Replace(s, " ") for every match.
    func replaceAll(_ s: String, _ with: String) -> String {
        let ns = NSString(string: s)
        return re.stringByReplacingMatches(in: s, options: [], range: NSRange(location: 0, length: ns.length),
                                           withTemplate: NSRegularExpression.escapedTemplate(for: with))
    }

    /// Regex.Replace(s, with, 1): the first match only.
    func replaceFirst(_ s: String, _ with: String) -> String {
        guard let m = match(s) else { return s }
        return m.ns.replacingCharacters(in: m.range, with: with)
    }

    /// Regex.Match(s, pattern) with a pattern made on the spot.
    static func first(_ s: String, _ pattern: String, ignoreCase: Bool = false) -> RxMatch? {
        return Rx(pattern, ignoreCase: ignoreCase).match(s)
    }
}

struct RxMatch {
    let r: NSTextCheckingResult
    let ns: NSString

    var range: NSRange { return r.range }

    func has(_ i: Int) -> Bool { return i < r.numberOfRanges && r.range(at: i).location != NSNotFound }
    func group(_ i: Int) -> String { return has(i) ? ns.substring(with: r.range(at: i)) : "" }
    func has(_ name: String) -> Bool { return r.range(withName: name).location != NSNotFound }
    func group(_ name: String) -> String { return has(name) ? ns.substring(with: r.range(withName: name)) : "" }
}

extension String {
    /// C# Trim().
    var trimmed: String { return trimmingCharacters(in: .whitespacesAndNewlines) }
}

/// A stable sort (LINQ OrderBy keeps the order of equal items; Swift's sort does not promise to).
func stableSorted<T>(_ items: [T], _ less: (T, T) -> Bool) -> [T] {
    let indexed = Array(items.enumerated())
    let sorted = indexed.sorted { a, b in
        if less(a.element, b.element) { return true }
        if less(b.element, a.element) { return false }
        return a.offset < b.offset
    }
    return sorted.map { $0.element }
}

// ---------------------------------------------------------------- model

/// One task. JSON keys are the C# member names; the energy fields are left out while unset (EmitDefaultValue = false).
public final class TaskItem: Codable {
    public var id: String
    public var title: String
    public var done: Bool
    public var doneAt: String?
    public var due: String?
    public var hasTime: Bool
    public var notified: Bool
    // Added for the energy features. Left out of the file while unset, so older tasks are written exactly as before.
    public var energy: String?     // "high", "medium", "low" (or the older "deep" / "light"), or nil
    public var minutes: Int        // length in minutes; 0 = default
    public var planned: Bool       // due was set by Plan, so Plan may move it again
    public var kind: String?       // "physical", or nil for mental
    public var routine: String?    // id of the routine this task came from
    public var routineDay: String? // the day it was added for
    public var part: Int           // 1..n for a routine's check-ins
    public var worked: Double      // seconds worked on it so far, from its timer (Work.swift)

    public static let fmt = "yyyy-MM-ddTHH:mm"

    public init(id: String = "", title: String = "", done: Bool = false, doneAt: String? = nil, due: String? = nil,
                hasTime: Bool = false, notified: Bool = false, energy: String? = nil, minutes: Int = 0, planned: Bool = false,
                kind: String? = nil, routine: String? = nil, routineDay: String? = nil, part: Int = 0, worked: Double = 0) {
        self.id = id; self.title = title; self.done = done; self.doneAt = doneAt; self.due = due
        self.hasTime = hasTime; self.notified = notified; self.energy = energy; self.minutes = minutes; self.planned = planned
        self.kind = kind; self.routine = routine; self.routineDay = routineDay; self.part = part
        self.worked = worked
    }

    enum CodingKeys: String, CodingKey {
        case Id, Title, Done, DoneAt, Due, HasTime, Notified, Energy, Minutes, Planned, Kind, Routine, RoutineDay, Part, Worked
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .Id) ?? ""
        title = try c.decodeIfPresent(String.self, forKey: .Title) ?? ""
        done = try c.decodeIfPresent(Bool.self, forKey: .Done) ?? false
        doneAt = try c.decodeIfPresent(String.self, forKey: .DoneAt)
        due = try c.decodeIfPresent(String.self, forKey: .Due)
        hasTime = try c.decodeIfPresent(Bool.self, forKey: .HasTime) ?? false
        notified = try c.decodeIfPresent(Bool.self, forKey: .Notified) ?? false
        energy = try c.decodeIfPresent(String.self, forKey: .Energy)
        minutes = try c.decodeIfPresent(Int.self, forKey: .Minutes) ?? 0
        planned = try c.decodeIfPresent(Bool.self, forKey: .Planned) ?? false
        kind = try c.decodeIfPresent(String.self, forKey: .Kind)
        routine = try c.decodeIfPresent(String.self, forKey: .Routine)
        routineDay = try c.decodeIfPresent(String.self, forKey: .RoutineDay)
        part = try c.decodeIfPresent(Int.self, forKey: .Part) ?? 0
        worked = try c.decodeIfPresent(Double.self, forKey: .Worked) ?? 0
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .Id)
        try c.encode(title, forKey: .Title)
        try c.encode(done, forKey: .Done)
        try encodeOrNull(&c, doneAt, .DoneAt)
        try encodeOrNull(&c, due, .Due)
        try c.encode(hasTime, forKey: .HasTime)
        try c.encode(notified, forKey: .Notified)
        if let v = energy { try c.encode(v, forKey: .Energy) }
        if minutes != 0 { try c.encode(minutes, forKey: .Minutes) }
        if planned { try c.encode(planned, forKey: .Planned) }
        if let v = kind { try c.encode(v, forKey: .Kind) }
        if let v = routine { try c.encode(v, forKey: .Routine) }
        if let v = routineDay { try c.encode(v, forKey: .RoutineDay) }
        if part != 0 { try c.encode(part, forKey: .Part) }
        if worked != 0 { try c.encode(worked, forKey: .Worked) }
    }

    public var dueAt: Date? {
        guard let d = due, !d.isEmpty else { return nil }
        return CornerTime.parse(d)
    }

    public func setDue(_ d: Date?, hasTime: Bool) {
        if let v = d { due = CornerTime.format(v) } else { due = nil }
        self.hasTime = d != nil && hasTime
        planned = false
        notified = false
    }

    public var overdue: Bool {
        guard let d = dueAt else { return false }
        let now = Date()
        return hasTime ? d < now : d.dateOnly < now.dateOnly
    }
}

/// Writes the value, or null when it is nil (DataContract members with EmitDefaultValue left on).
func encodeOrNull<K: CodingKey>(_ c: inout KeyedEncodingContainer<K>, _ value: String?, _ key: K) throws {
    if let v = value { try c.encode(v, forKey: key) } else { try c.encodeNil(forKey: key) }
}

/// tasks.json: the C# class `Data` (renamed, because `Data` would clash with Foundation.Data).
public final class TaskData: Codable {
    public var tasks: [TaskItem]
    public var doneOpen: Bool

    public init(tasks: [TaskItem] = [], doneOpen: Bool = false) {
        self.tasks = tasks
        self.doneOpen = doneOpen
    }

    enum CodingKeys: String, CodingKey { case Tasks, DoneOpen }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let list = try c.decodeIfPresent([TaskItem?].self, forKey: .Tasks) ?? []
        tasks = list.compactMap { $0 }
        doneOpen = try c.decodeIfPresent(Bool.self, forKey: .DoneOpen) ?? false
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(tasks, forKey: .Tasks)
        try c.encode(doneOpen, forKey: .DoneOpen)
    }
}

// ---------------------------------------------------------------- files

public enum Store {
    /// The data folder: CORNER_DATA if set, else ~/Library/Application Support/Corner (created when missing).
    public static let dir: String = {
        if let env = ProcessInfo.processInfo.environment["CORNER_DATA"], !env.isEmpty { return env }
        let base: String
        if let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first {
            base = url.path
        } else {
            base = NSString(string: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        }
        let d = NSString(string: base).appendingPathComponent("Corner")
        try? FileManager.default.createDirectory(atPath: d, withIntermediateDirectories: true, attributes: nil)
        return d
    }()

    public static var file: String { return path("tasks.json") }

    /// Line ending used in the CSV files and the error log (the Windows app's Environment.NewLine).
    public static let newline = "\r\n"

    public static func path(_ name: String) -> String {
        return NSString(string: dir).appendingPathComponent(name)
    }

    public static var exists: Bool { return FileManager.default.fileExists(atPath: file) }

    static var stamp: Date = Date.distantPast

    static func diskStamp() -> Date {
        if !FileManager.default.fileExists(atPath: file) { return Date.distantPast }
        if let attrs = try? FileManager.default.attributesOfItem(atPath: file), let d = attrs[.modificationDate] as? Date { return d }
        return stamp
    }

    public static var changedOnDisk: Bool { return diskStamp() != stamp }

    /// Re-reads the file after someone else edited it. Nil when it is missing or unreadable (e.g. half written).
    public static func reload() -> TaskData? {
        if !exists { return nil }
        let st = diskStamp()
        guard let data = FileManager.default.contents(atPath: file),
              let d = try? JSONDecoder().decode(TaskData.self, from: data) else { return nil }
        d.tasks.removeAll { $0.id.isEmpty }
        stamp = st
        return d
    }

    public static func load() -> TaskData {
        if exists {
            do {
                stamp = diskStamp()
                let data = try Data(contentsOf: URL(fileURLWithPath: file))
                let d = try JSONDecoder().decode(TaskData.self, from: data)
                d.tasks.removeAll { $0.id.isEmpty }
                return d
            } catch {
                log(error)
                let bad = file + ".bad"
                try? FileManager.default.removeItem(atPath: bad)
                try? FileManager.default.copyItem(atPath: file, toPath: bad)
            }
        }
        return TaskData()
    }

    public static func save(_ d: TaskData) {
        do {
            try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: nil)
            let bak = path("tasks.before-energy.json") // one-off copy before the first write by the energy version
            if FileManager.default.fileExists(atPath: file) && !FileManager.default.fileExists(atPath: bak) {
                try FileManager.default.copyItem(atPath: file, toPath: bak)
            }
            try writeAtomically(try encodeJSON(d), file)
            stamp = diskStamp()
        } catch {
            log(error)
        }
    }

    /// The same JSON the Windows app writes (DataContractJsonSerializer): compact, keys in name order, "/" escaped.
    static func encodeJSON<T: Encodable>(_ value: T) throws -> Data {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys]
        return try enc.encode(value)
    }

    /// Writes to a temporary file and moves it over the old one.
    static func writeAtomically(_ data: Data, _ path: String) throws {
        try data.write(to: URL(fileURLWithPath: path), options: .atomic)
    }

    public static func log(_ error: Error) {
        log(String(describing: error))
    }

    public static func log(_ message: String) {
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: nil)
        appendText(path("error.log"), CornerTime.sortable(Date()) + " " + message + newline)
    }

    /// File.AppendAllText; `bom` writes a UTF-8 byte order mark first when the file is new.
    static func appendText(_ path: String, _ text: String, bom: Bool = false) {
        var bytes = Data()
        let fresh = !FileManager.default.fileExists(atPath: path)
        if fresh && bom { bytes.append(contentsOf: [0xEF, 0xBB, 0xBF]) }
        bytes.append(contentsOf: Array(text.utf8))
        if fresh {
            _ = FileManager.default.createFile(atPath: path, contents: bytes, attributes: nil)
            return
        }
        if let h = FileHandle(forWritingAtPath: path) {
            _ = h.seekToEndOfFile()
            h.write(bytes)
            h.closeFile()
        }
    }

    /// File.ReadAllLines: UTF-8 with or without a byte order mark; \r\n, \n and \r all end a line.
    static func readLines(_ path: String) -> [String]? {
        guard let data = FileManager.default.contents(atPath: path) else { return nil }
        var scalars = Array(String(decoding: data, as: UTF8.self).unicodeScalars)
        if let f = scalars.first, f == "\u{FEFF}" { scalars.removeFirst() }
        var lines: [String] = []
        var cur = String.UnicodeScalarView()
        var i = 0
        while i < scalars.count {
            let u = scalars[i]
            if u == "\r" || u == "\n" {
                lines.append(String(cur))
                cur = String.UnicodeScalarView()
                if u == "\r" && i + 1 < scalars.count && scalars[i + 1] == "\n" { i += 1 }
            } else {
                cur.append(u)
            }
            i += 1
        }
        if !cur.isEmpty { lines.append(String(cur)) }
        return lines
    }

    /// File.WriteAllText / WriteAllLines.
    static func writeText(_ path: String, _ text: String, bom: Bool = false) throws {
        var bytes = Data()
        if bom { bytes.append(contentsOf: [0xEF, 0xBB, 0xBF]) }
        bytes.append(contentsOf: Array(text.utf8))
        try bytes.write(to: URL(fileURLWithPath: path))
    }
}

/// settings.json beside tasks.json: a flat list of named numbers.
public enum Settings {
    static var file: String { return Store.path("settings.json") }
    static var values: [String: Double]? = nil
    static var order: [String] = [] // keys in the order they were added (a C# Dictionary keeps that order)
    static let pairRx = Rx(#""(\w+)"\s*:\s*(-?[0-9.]+)"#)

    static func load() {
        var v: [String: Double] = [:]
        order = []
        if let text = try? String(contentsOfFile: file, encoding: .utf8) {
            for m in pairRx.matches(text) {
                if let d = Double(m.group(2)) {
                    let k = m.group(1)
                    if v[k] == nil { order.append(k) }
                    v[k] = d
                }
            }
        }
        values = v
    }

    public static func get(_ key: String, _ fallback: Double) -> Double {
        if values == nil { load() }
        return values?[key] ?? fallback
    }

    public static func set(_ key: String, _ value: Double) {
        if values == nil { load() }
        if values?[key] == nil { order.append(key) }
        values?[key] = value
        let parts = order.map { k -> String in "\"" + k + "\": " + NetFormat.optional(values?[k] ?? 0, 3) }
        do { try Store.writeText(file, "{" + parts.joined(separator: ", ") + "}") } catch { Store.log(error) }
    }

    public static func loadScale() -> Double { return get("Scale", 1) }
    public static func saveScale(_ v: Double) { set("Scale", v) }
}

// ---------------------------------------------------------------- "tomorrow 9am" parsing

public enum When {
    static let mon = "jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec"
    static let dPart1 = #"today|tonight|tomorrow|tmrw|tmr|next\s+week|(?:next\s+)?(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tues|tue|wed|thurs|thur|thu|fri|sat|sun)"#
    static let dPart2 = "|(?:" + mon + #")[a-z]*\.?\s+\d{1,2}|\d{1,2}\s+(?:"# + mon + ")[a-z]*"
    static let dPart3 = #"|\d{4}-\d{1,2}-\d{1,2}|in\s+\d+\s*(?:days?|d|weeks?|w)"#
    static let d = dPart1 + dPart2 + dPart3
    static let tm = #"(?:at\s+|@\s*)?\d{1,2}(?::\d{2})?\s*(?:am|pm)|(?:at\s+|@\s*)?\d{1,2}:\d{2}|(?:at\s+|@\s*)\d{1,2}|noon|midnight"#
    static let r = #"in\s+\d+\s*(?:minutes?|mins?|m|hours?|hrs?|h)"#

    // .NET allows the same group name twice; NSRegularExpression does not, so the two halves use d1/t1 and t2/d2.
    static let rx: Rx = {
        let head = #"^(?<title>.*?)(?:^|\s+)(?:(?:by|on|due)\s+)?(?:(?<rel>"# + r + ")"
        let mid = "|(?<d1>" + d + #")(?:\s+(?<t1>"# + tm + "))?"
        let tail = "|(?<t2>" + tm + #")(?:\s+(?:on\s+)?(?<d2>"# + d + #"))?)\s*$"#
        return Rx(head + mid + tail, ignoreCase: true)
    }()

    static let spaceRx = Rx(#"\s+"#)
    static let relRx = Rx(#"(\d+)\s*([a-z])"#, ignoreCase: true)
    static let inDaysRx = Rx(#"^in (\d+) ?([dw])"#)
    static let isoRx = Rx(#"^(\d{4})-(\d{1,2})-(\d{1,2})$"#)
    static let monDayRx = Rx("^(" + mon + #")[a-z]*\.? (\d{1,2})$"#)
    static let dayMonRx = Rx(#"^(?<dd>\d{1,2}) (?<mm>"# + mon + ")")
    static let timeRx = Rx(#"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?"#)

    /// Finds a date/time phrase at the END of the text. Nil when there is none.
    public static func parse(_ text: String?) -> (title: String, due: Date, hasTime: Bool)? {
        let title = (text ?? "").trimmed
        guard let m = rx.match(title) else { return nil }
        let tt = m.group("title").trimmed
        if tt.isEmpty { return nil }
        let now = Date()

        if m.has("rel") {
            guard let rm = relRx.match(m.group("rel")) else { return nil }
            guard let n = Int(rm.group(1)), n <= 100000 else { return nil }
            let minutes = rm.group(2).lowercased().hasPrefix("m") ? Double(n) : Double(n) * 60.0
            let due = CornerTime.truncateToMinute(now.addingMinutes(minutes))
            return (tt, due, true)
        }

        var date = now.dateOnly
        var haveDate = false
        var time: TimeInterval = 0
        var haveTime = false
        let dName: String? = m.has("d1") ? "d1" : (m.has("d2") ? "d2" : nil)
        let tName: String? = m.has("t1") ? "t1" : (m.has("t2") ? "t2" : nil)
        if let dn = dName {
            let ds = spaceRx.replaceAll(m.group(dn).lowercased(), " ")
            guard let resolved = resolveDate(ds, now.dateOnly) else { return nil }
            date = resolved
            haveDate = true
            if ds == "tonight" { time = 20 * 3600; haveTime = true }
        }
        if let tn = tName {
            guard let t = resolveTime(m.group(tn).lowercased()) else { return nil }
            time = t
            haveTime = true
        }
        var due = date.atTimeOfDay(time)
        if !haveDate && due <= now { due = due.addingDays(1) }
        return (tt, due, haveTime)
    }

    static func resolveDate(_ s: String, _ today: Date) -> Date? {
        if s == "today" || s == "tonight" { return today }
        if s == "tomorrow" || s == "tmrw" || s == "tmr" { return today.addingDays(1) }
        if s == "next week" {
            let d = (1 - today.dayOfWeek + 7) % 7
            return today.addingDays(d == 0 ? 7 : d)
        }
        if let m = inDaysRx.match(s) {
            guard let n = Int(m.group(1)), n <= 3650 else { return nil }
            return today.addingDays(m.group(2) == "w" ? n * 7 : n)
        }
        if let m = isoRx.match(s) {
            guard let y = Int(m.group(1)), let mo = Int(m.group(2)), let d = Int(m.group(3)) else { return nil }
            return CornerTime.make(y, mo, d)
        }
        var monText: String? = nil
        var dayText: String? = nil
        if let m = monDayRx.match(s) {
            monText = m.group(1); dayText = m.group(2)
        } else if let m = dayMonRx.match(s) {
            monText = m.group("mm"); dayText = m.group("dd")
        }
        if let mt = monText, let dt = dayText {
            let names = mon.components(separatedBy: "|")
            guard let mi = names.firstIndex(of: mt), let dd = Int(dt) else { return nil }
            guard var date = CornerTime.make(today.year, mi + 1, dd) else { return nil }
            if date < today { date = date.addingYears(1) }
            return date
        }
        let w = s.hasPrefix("next ") ? String(s.dropFirst(5)) : s
        if w.count < 3 { return nil }
        guard let wi = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"].firstIndex(of: String(w.prefix(3))) else { return nil }
        let delta = (wi - today.dayOfWeek + 7) % 7
        return today.addingDays(delta == 0 ? 7 : delta)
    }

    static func resolveTime(_ s: String) -> TimeInterval? {
        if s == "noon" { return 12 * 3600 }
        if s == "midnight" { return 0 }
        guard let m = timeRx.match(s), var h = Int(m.group(1)) else { return nil }
        let min = m.has(2) ? (Int(m.group(2)) ?? 0) : 0
        if min > 59 { return nil }
        if m.has(3) {
            if h < 1 || h > 12 { return nil }
            if m.group(3) == "pm" { if h != 12 { h += 12 } } else if h == 12 { h = 0 }
        } else {
            if h > 23 { return nil }
            let clock12 = CornerTime.uses12HourClock
            // "at 5" almost always means the afternoon
            if h >= 1 && h <= 7 && (!m.has(2) || clock12) { h += 12 }
        }
        return TimeInterval(h * 3600 + min * 60)
    }

    static let shortTime: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale.current
        f.timeZone = CornerTime.calendar.timeZone
        f.dateStyle = .none
        f.timeStyle = .short
        return f
    }()

    /// "Today", "Tomorrow 9:00", "Friday", "Nov 21", "Nov 21, 2027".
    public static func label(_ due: Date, hasTime: Bool) -> String {
        let today = Date().dateOnly
        let diff = CornerTime.days(from: today, to: due)
        var s: String
        if diff == 0 { s = "Today" }
        else if diff == 1 { s = "Tomorrow" }
        else if diff == -1 { s = "Yesterday" }
        else if diff > 1 && diff < 7 { s = CornerTime.weekdayNames[due.dayOfWeek] }
        else if due.year == today.year { s = CornerTime.monthNames[due.month - 1] + " " + String(due.day) }
        else { s = CornerTime.monthNames[due.month - 1] + " " + String(due.day) + ", " + String(due.year) }
        if hasTime { s += " " + shortTime.string(from: due) }
        return s
    }
}
