// Port of Corner.Core/src/Plan.cs: task tags typed into a task or routine (TaskTags), daily routines
// (Routine, RoutineData, Routines; routines.json beside tasks.json) and the Plan action (PlanItem, Planner).
import Foundation

// ---------------------------------------------------------------- tags typed into a task or routine

public enum TaskTags {
    static let energyRx = Rx(#"(?:^|\s)!(high|medium|med|low|deep|light|h|m|l)(?=\s|$)"#, ignoreCase: true)
    static let kindRx = Rx(#"(?:^|\s)!(physical|phys|mental)(?=\s|$)"#, ignoreCase: true)
    // "45m", "20min", "2h" as a word of its own; not after "in" ("in 20m" is still a reminder time)
    static let lenRx = Rx(#"(?<!\bin)(?:^|\s)(\d{1,3})(m|min|mins|h|hr|hrs)(?=\s|$)"#, ignoreCase: true)
    static let spaceRx = Rx(#"\s+"#)

    /// "high" / "medium" / "low" / nil. Also understands the short forms and the older deep/light.
    public static func level(_ s: String?) -> String? {
        switch (s ?? "").lowercased() {
        case "high", "h", "deep": return "high"
        case "medium", "med", "m": return "medium"
        case "low", "l", "light": return "low"
        default: return nil
        }
    }

    public static func short(_ level: String?) -> String? { return level == "medium" ? "med" : level }

    public static func len(_ minutes: Int) -> String {
        return minutes % 60 == 0 ? String(minutes / 60) + "h" : String(minutes) + "m"
    }

    /// Pulls "!high" / "!med" / "!low", "!phys" / "!mental" and a length like "45m" out of the text; returns the rest.
    /// Text that is nothing but tags is returned as plain text, with no tags.
    public static func extract(_ text: String?) -> (text: String, energy: String?, minutes: Int, kind: String?) {
        var s = text ?? ""
        var en: String? = nil
        var kd: String? = nil
        var mins = 0
        if let m = energyRx.match(s) {
            en = level(m.group(1))
            s = energyRx.replaceAll(s, " ")
        }
        if let m = kindRx.match(s) {
            kd = m.group(1).lowercased().hasPrefix("p") ? "physical" : "mental"
            s = kindRx.replaceAll(s, " ")
        }
        if let m = lenRx.match(s) {
            let n = Int(m.group(1)) ?? 0
            mins = m.group(2).lowercased().hasPrefix("h") ? n * 60 : n
            if mins < 1 || mins > 600 { mins = 0 } else { s = lenRx.replaceFirst(s, " ") }
        }
        s = spaceRx.replaceAll(s, " ").trimmed
        if s.isEmpty { return ((text ?? "").trimmed, nil, 0, nil) } // nothing but tags: treat it as plain text
        return (s, en, mins, kd)
    }
}

// ---------------------------------------------------------------- daily routines (routines.json, beside tasks.json)

public final class Routine: Codable {
    public var id: String
    public var title: String
    public var energy: String?   // "low", "medium", "high" or nil
    public var kind: String?     // "physical", or nil for mental
    public var minutes: Int
    public var days: String?     // "daily", "weekdays", "weekends" or "mon,wed,fri"
    public var after: String?    // id of a routine that must come earlier the same day
    public var checkIns: Int     // more than 1 = that many short check-ins spread over the day
    public var prefer: String?   // "early" (first thing) or "dip" (the afternoon dip); nil = by energy level
    public var off: Bool         // switched off: kept in the list but not added to the day (left out of the file when false)

    public init(id: String = "", title: String = "", energy: String? = nil, kind: String? = nil, minutes: Int = 0,
                days: String? = nil, after: String? = nil, checkIns: Int = 0, prefer: String? = nil, off: Bool = false) {
        self.id = id; self.title = title; self.energy = energy; self.kind = kind; self.minutes = minutes
        self.days = days; self.after = after; self.checkIns = checkIns; self.prefer = prefer; self.off = off
    }

    enum CodingKeys: String, CodingKey { case Id, Title, Energy, Kind, Minutes, Days, After, CheckIns, Prefer, Off }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .Id) ?? ""
        title = try c.decodeIfPresent(String.self, forKey: .Title) ?? ""
        energy = try c.decodeIfPresent(String.self, forKey: .Energy)
        kind = try c.decodeIfPresent(String.self, forKey: .Kind)
        minutes = try c.decodeIfPresent(Int.self, forKey: .Minutes) ?? 0
        days = try c.decodeIfPresent(String.self, forKey: .Days)
        after = try c.decodeIfPresent(String.self, forKey: .After)
        checkIns = try c.decodeIfPresent(Int.self, forKey: .CheckIns) ?? 0
        prefer = try c.decodeIfPresent(String.self, forKey: .Prefer)
        off = try c.decodeIfPresent(Bool.self, forKey: .Off) ?? false
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try encodeOrNull(&c, after, .After)
        try c.encode(checkIns, forKey: .CheckIns)
        try encodeOrNull(&c, days, .Days)
        try encodeOrNull(&c, energy, .Energy)
        try c.encode(id, forKey: .Id)
        try encodeOrNull(&c, kind, .Kind)
        try c.encode(minutes, forKey: .Minutes)
        if off { try c.encode(off, forKey: .Off) }
        try encodeOrNull(&c, prefer, .Prefer)
        try c.encode(title, forKey: .Title)
    }
}

public final class RoutineData: Codable {
    public var addedOn: String?   // the day today's routines were last added to the task list
    public var routines: [Routine]

    public init(addedOn: String? = nil, routines: [Routine] = []) {
        self.addedOn = addedOn
        self.routines = routines
    }

    enum CodingKeys: String, CodingKey { case AddedOn, Routines }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        addedOn = try c.decodeIfPresent(String.self, forKey: .AddedOn)
        let list = try c.decodeIfPresent([Routine?].self, forKey: .Routines) ?? []
        routines = list.compactMap { $0 }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try encodeOrNull(&c, addedOn, .AddedOn)
        try c.encode(routines, forKey: .Routines)
    }
}

public enum Routines {
    public static var data: RoutineData? = nil
    static var file: String { return Store.path("routines.json") }

    /// Reads routines.json; on the very first run writes the starting set.
    public static func load() {
        if FileManager.default.fileExists(atPath: file) {
            do {
                let bytes = try Data(contentsOf: URL(fileURLWithPath: file))
                let d = try JSONDecoder().decode(RoutineData.self, from: bytes)
                d.routines.removeAll { $0.id.isEmpty || $0.title.isEmpty }
                data = d
                return
            } catch {
                Store.log(error)
                if data != nil { return }
            }
        }
        if data == nil {
            data = RoutineData(addedOn: nil, routines: seed())
            if !FileManager.default.fileExists(atPath: file) { save() }
        }
    }

    public static func save() {
        guard let d = data else { return }
        do {
            try Store.writeAtomically(try Store.encodeJSON(d), file)
        } catch {
            Store.log(error)
        }
    }

    static func seed() -> [Routine] {
        return []
    }

    /// Adds the optional "Nap" routine once, switched off. Deleting it afterwards is respected.
    public static func offerNap() {
        guard let d = data else { return }
        if Settings.get("NapRoutine", 0) > 0.5 { return }
        if !d.routines.contains(where: { $0.id == "nap" }) {
            d.routines.append(Routine(id: "nap", title: "Nap", energy: "low", minutes: 20, days: "daily", prefer: "dip", off: true))
            save()
        }
        Settings.set("NapRoutine", 1)
    }

    static let dayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"]

    public static func appliesOn(_ r: Routine, _ day: Date) -> Bool {
        let d = (r.days ?? "daily").lowercased()
        let dow = day.dayOfWeek
        let weekend = dow == 6 || dow == 0
        if d == "daily" || d == "" { return true }
        if d == "weekdays" { return !weekend }
        if d == "weekends" { return weekend }
        return d.components(separatedBy: ",").contains(dayNames[dow])
    }

    /// Finds the pattern and replaces what it matched with one space (C#: Take(ref s, pattern, out m)).
    static func take(_ s: inout String, _ pattern: String) -> RxMatch? {
        guard let m = Rx.first(s, pattern) else { return nil }
        s = m.ns.replacingCharacters(in: m.range, with: " ")
        return m
    }

    /// Fills a routine from one typed line, e.g. "Workout 20m !high !phys daily".
    /// Anything not mentioned goes back to its default.
    public static func apply(_ r: Routine, _ text: String?, _ all: [Routine]) {
        var s = " " + (text ?? "") + " "
        _ = take(&s, #"\s!noclinic(?=\s)"#) // an old tag: dropped
        r.off = take(&s, #"\s!off(?=\s)"#) != nil
        if take(&s, #"\s!early(?=\s)"#) != nil { r.prefer = "early" }
        else if take(&s, #"\s!dip(?=\s)"#) != nil { r.prefer = "dip" }
        else { r.prefer = nil }
        r.after = nil
        if let m = take(&s, #"\safter[\s:]*#?(\d{1,2})(?=\s)"#) {
            let i = (Int(m.group(1)) ?? 0) - 1
            if i >= 0 && i < all.count && all[i] !== r { r.after = all[i].id }
        }
        if let m = take(&s, #"\sx(\d)(?=\s)"#) { r.checkIns = Int(m.group(1)) ?? 0 } else { r.checkIns = 0 }
        if let m = take(&s, #"\s(daily|weekdays|weekends|(?:mon|tue|wed|thu|fri|sat|sun)(?:,(?:mon|tue|wed|thu|fri|sat|sun))*)(?=\s)"#) {
            r.days = m.group(1)
        } else {
            r.days = "daily"
        }
        let x = TaskTags.extract(s)
        r.title = x.text
        r.energy = x.energy
        r.minutes = x.minutes > 0 ? x.minutes : Planner.defaultMinutes
        r.kind = x.kind == "physical" ? x.kind : nil
    }

    /// The typed line that apply would turn back into this routine.
    public static func format(_ r: Routine, _ all: [Routine]) -> String {
        var p: [String] = [r.title, TaskTags.len(r.minutes > 0 ? r.minutes : Planner.defaultMinutes)]
        if let lv = TaskTags.level(r.energy) { p.append("!" + (TaskTags.short(lv) ?? lv)) }
        if r.kind == "physical" { p.append("!phys") }
        p.append((r.days ?? "").isEmpty ? "daily" : (r.days ?? "daily"))
        let a = indexOf(all, r.after)
        if a >= 0 { p.append("after " + String(a + 1)) }
        if r.checkIns > 1 { p.append("x" + String(r.checkIns)) }
        if let pr = r.prefer { p.append("!" + pr) }
        if r.off { p.append("!off") }
        return p.joined(separator: " ")
    }

    public static func summary(_ r: Routine, _ all: [Routine]) -> String {
        var p: [String] = [TaskTags.len(r.minutes > 0 ? r.minutes : Planner.defaultMinutes)]
        if let lv = TaskTags.level(r.energy) { p.append(TaskTags.short(lv) ?? lv) }
        if r.kind == "physical" { p.append("physical") }
        p.append((r.days ?? "").isEmpty ? "daily" : (r.days ?? "daily"))
        let a = indexOf(all, r.after)
        if a >= 0 { p.append("after " + String(a + 1)) }
        if r.checkIns > 1 { p.append(String(r.checkIns) + " check-ins") }
        if r.prefer == "early" { p.append("first thing") }
        if r.prefer == "dip" { p.append("in the dip") }
        if r.off { p.append("off") }
        return p.joined(separator: " · ")
    }

    static func indexOf(_ all: [Routine], _ id: String?) -> Int {
        guard let id = id else { return -1 }
        for i in 0..<all.count where all[i].id == id { return i }
        return -1
    }
}

// ---------------------------------------------------------------- Plan my day

public final class PlanItem {
    public var id: String
    public var title: String
    public var level: String?
    public var routine: String?
    public var after: String?
    public var prefer: String?
    public var minutes: Int
    public var part: Int
    public var physical: Bool
    public var checkIn: Bool
    public var placed: Bool
    public var start: Date
    var end: Int = 0
    var tried: Bool = false

    public init(id: String = "", title: String = "", level: String? = nil, routine: String? = nil, after: String? = nil,
                prefer: String? = nil, minutes: Int = 0, part: Int = 0, physical: Bool = false, checkIn: Bool = false,
                placed: Bool = false, start: Date = Date.distantPast) {
        self.id = id; self.title = title; self.level = level; self.routine = routine; self.after = after
        self.prefer = prefer; self.minutes = minutes; self.part = part; self.physical = physical; self.checkIn = checkIn
        self.placed = placed; self.start = start
    }
}

/// Proposes a start time for each open task that has no fixed time.
public enum Planner {
    public static let defaultMinutes = 30   // length of a task with no length given
    public static let gapMinutes = 5        // breathing room after a task (none inside an "after" chain)
    public static let stopBeforeBed = 60    // nothing is planned in the last hour before bed
    public static let physicalFrom: Double = 16  // physical tasks: preferred window, clock hours
    public static let physicalTo: Double = 20
    public static let checkInGap = 60       // check-ins are at least this many minutes apart
    public static let checkInSpread = 120   // and at most about this far apart when the day has room
    public static let peakFit = 80          // percent of a task that must lie on a peak to count as "mostly on the peak"

    /// The end of the time Plan fills when planning at `now`: an hour before bed, or bedtime itself once
    /// less than half an hour is left before that hour, so a plan made late in the evening still has room.
    public static func until(_ e: EnergyDay, _ now: Date) -> Date {
        let wind = e.bed.addingMinutes(-stopBeforeBed)
        return now > wind.addingMinutes(-30) ? e.bed : wind
    }

    public static func plannable(_ t: TaskItem, _ day: Date) -> Bool {
        if t.done { return false }
        guard let d = t.dueAt else { return true }
        if d.dateOnly > day { return false }   // belongs to a later day
        return !t.hasTime || t.planned         // a time you set yourself stays where it is
    }

    /// keepStarted: planned tasks whose start time has already passed stay where they are (used when re-planning after a nap).
    public static func plan(_ tasks: [TaskItem], energy e: EnergyDay, routines: [Routine]?, now: Date, keepStarted: Bool = false) -> [PlanItem] {
        func started(_ t: TaskItem) -> Bool {
            if !(keepStarted && t.planned && t.hasTime) { return false }
            guard let d = t.dueAt else { return false }
            return d <= now
        }
        let step = EnergyDay.stepMin
        let n = e.samples.count
        let gap = gapMinutes / step
        let day = e.wake.dateOnly
        let key = CornerTime.formatDay(day)
        var taken = [Bool](repeating: false, count: n)
        var zone = [Int](repeating: 0, count: n)   // 1 = peak, 2 = dip
        let wake = e.wake
        func idx(_ t: Date) -> Int {
            let x = NetFormat.round(t.timeIntervalSince(wake) / 60 / Double(step))
            return max(0, min(n, Int(max(-1e9, min(1e9, x)))))
        }
        func fillZone(_ z: Zone?, _ v: Int) {
            guard let z = z else { return }
            for i in stride(from: idx(z.from), to: idx(z.to), by: 1) { zone[i] = v }
        }
        fillZone(e.peak1, 1)
        fillZone(e.peak2, 1)
        fillZone(e.dip, 2)
        for nap in e.naps {
            for i in stride(from: idx(nap.from), to: idx(nap.to), by: 1) { taken[i] = true } // never inside a logged sleep period
        }
        for t in tasks {
            // tasks with a time you set yourself keep their slot
            if t.done || !t.hasTime { continue }
            guard let d = t.dueAt else { continue }
            if (t.planned && !started(t)) || d < e.wake || d > e.bed { continue }
            let len = (t.minutes > 0 ? t.minutes : defaultMinutes) + gapMinutes
            for i in stride(from: idx(d), to: idx(d.addingMinutes(len)), by: 1) { taken[i] = true }
        }

        let firstRaw = (now.timeIntervalSince(wake) / 60 / Double(step)).rounded(.up)
        let first = max(0, min(n, Int(max(-1e9, min(1e9, firstRaw)))))
        let last = idx(until(e, now))
        let groggy = idx(e.groggyUntil)

        var items: [PlanItem] = []
        for t in tasks where plannable(t, day) && !started(t) {
            var r: Routine? = nil
            if let rid = t.routine, let rs = routines { r = rs.first { $0.id == rid } }
            items.append(PlanItem(id: t.id, title: t.title, level: TaskTags.level(t.energy), routine: t.routine,
                                  after: r?.after, prefer: r?.prefer, minutes: t.minutes > 0 ? t.minutes : defaultMinutes,
                                  part: t.part, physical: t.kind == "physical", checkIn: t.part > 0))
        }

        func lenOf(_ it: PlanItem) -> Int {
            return max(1, Int((Double(it.minutes) / Double(step)).rounded(.up)))
        }
        func fits(_ it: PlanItem, _ i: Int) -> Bool {
            let len = lenOf(it)
            if i < first || i + len > last { return false }
            for k in i..<(i + len) { if taken[k] { return false } }
            return true
        }
        func find(_ it: PlanItem, _ from: Int, _ ok: (Int) -> Bool) -> Int {
            let len = lenOf(it)
            var i = max(first, from)
            while i + len <= last {
                if fits(it, i) && ok(i) { return i }
                i += 1
            }
            return -1
        }
        func countZone(_ it: PlanItem, _ i: Int, _ z: Int) -> Int {
            var c = 0
            for k in i..<(i + lenOf(it)) { if zone[k] == z { c += 1 } }
            return c
        }
        func mostEnergy(_ it: PlanItem, _ from: Int, _ ok: (Int) -> Bool) -> Int {
            let len = lenOf(it)
            var at = -1
            var best = -1.0
            var i = max(first, from)
            while i + len <= last {
                if fits(it, i) && ok(i) {
                    var sum = 0.0
                    for k in i..<(i + len) { sum += e.samples[k] }
                    if sum > best { best = sum; at = i }
                }
                i += 1
            }
            return at
        }

        // an "after" link whose earlier routine is itself being planned: the follower is placed straight after it
        func plannedPred(_ it: PlanItem) -> PlanItem? {
            guard let after = it.after else { return nil }
            return items.first { $0 !== it && $0.routine == after && $0.part <= 1 }
        }
        func notBefore(_ it: PlanItem) -> Int {
            guard let after = it.after else { return first }
            guard let pt = tasks.first(where: { $0.routine == after && $0.routineDay == key }) else { return first }
            if pt.done || !pt.hasTime { return first }
            guard let d = pt.dueAt else { return first }
            return max(first, idx(d.addingMinutes(pt.minutes > 0 ? pt.minutes : defaultMinutes)))
        }

        func place(_ it: PlanItem, _ at: Int) {
            let len = lenOf(it)
            var followers: [PlanItem] = []
            if let rid = it.routine, it.part <= 1 {
                followers = items.filter { $0 !== it && $0.after == rid && !$0.checkIn && !$0.placed }
            }
            let upper = min(n, at + len + (followers.count > 0 ? 0 : gap))
            for k in stride(from: at, to: upper, by: 1) { taken[k] = true }
            it.placed = true
            it.tried = true
            it.start = e.timeAt(at)
            it.end = at + len
            for f in followers {
                if f.placed { continue }
                let j = find(f, it.end, { _ in true })
                if j >= 0 { place(f, j) }
            }
        }

        func choose(_ it: PlanItem, _ from: Int) -> Int {
            let len = lenOf(it)
            var at = -1
            if it.prefer == "early" { return find(it, from, { _ in true }) }
            if it.physical {
                // late afternoon or early evening if there is room, else anywhere
                let a = idx(day.addingHours(physicalFrom))
                let b = idx(day.addingHours(physicalTo))
                at = find(it, max(from, a), { i in i + len <= b })
                if at < 0 { at = find(it, max(from, a), { _ in true }) }
                if at < 0 { at = find(it, from, { _ in true }) }
                return at
            }
            if it.prefer == "dip" || it.level == "low" {
                // the groggy stretch after waking (low only), then the dip, then anywhere off the peaks
                if it.prefer != "dip" { at = find(it, from, { i in i + len <= groggy }) }
                if at < 0 { at = find(it, from, { i in countZone(it, i, 2) == len }) }
                if at < 0 { at = find(it, from, { i in countZone(it, i, 2) * 2 >= len }) }
                if at < 0 { at = find(it, from, { i in countZone(it, i, 1) == 0 }) }
                if at < 0 { at = find(it, from, { _ in true }) }
                return at
            }
            if it.level == "high" {
                // the earliest slot entirely on a peak. If none is left (or the task is longer than the peak): the
                // highest-energy slot that is mostly on a peak, then half on one, then the highest-energy slot of the day.
                at = find(it, from, { i in countZone(it, i, 1) == len })
                if at < 0 { at = mostEnergy(it, from, { i in countZone(it, i, 1) * 100 >= len * peakFit }) }
                if at < 0 { at = mostEnergy(it, from, { i in countZone(it, i, 1) * 2 >= len }) }
                return at >= 0 ? at : mostEnergy(it, from, { _ in true })
            }
            if it.level == "medium" {
                at = find(it, from, { i in countZone(it, i, 1) == len })
                if at < 0 { at = mostEnergy(it, from, { i in countZone(it, i, 1) * 100 >= len * peakFit }) }
                return at >= 0 ? at : mostEnergy(it, from, { _ in true })
            }
            at = find(it, from, { i in countZone(it, i, 1) == 0 })
            return at >= 0 ? at : find(it, from, { _ in true })
        }

        // Order of choosing; within each group, the order in the task list.
        let groups: [(PlanItem) -> Bool] = [
            { x in x.prefer == "early" },                       // the early block, with everything chained after it
            { x in !x.physical && x.level == "high" },          // high mental work takes the peaks
            { x in x.physical },
            { x in x.level == "medium" && x.prefer != "dip" },  // medium gets what is left of the peaks
            { x in x.prefer == "dip" || x.level == "low" },
            { _ in true },
        ]
        func runGroups(_ fromGroup: Int, _ toGroup: Int) {
            for gi in fromGroup...toGroup {
                let members = items.filter(groups[gi])
                for it in members {
                    if it.placed || it.tried || it.checkIn || plannedPred(it) != nil { continue }
                    it.tried = true
                    let at = choose(it, notBefore(it))
                    if at >= 0 { place(it, at) }
                }
            }
        }
        runGroups(0, 3)

        // Check-ins: spread through the day after the routine they follow, in the gaps between the blocks above.
        // They go in before the low and untagged tasks, which then fill in around them.
        var groupKeys: [String?] = []
        var groupLists: [[PlanItem]] = []
        for x in items where x.checkIn {
            if let gi = groupKeys.firstIndex(where: { $0 == x.routine }) {
                groupLists[gi].append(x)
            } else {
                groupKeys.append(x.routine)
                groupLists.append([x])
            }
        }
        for grp in groupLists {
            let list = stableSorted(grp) { $0.part < $1.part }
            var pred = plannedPred(list[0])
            if let p = pred, p.checkIn { pred = nil }
            if let p = pred, !p.placed { continue }
            let start = pred != nil ? pred!.end : notBefore(list[0])
            let every = max(checkInGap / step, min(checkInSpread / step, (last - start) / (list.count + 1)))
            var prev = -10000
            for k in 0..<list.count {
                let at = find(list[k], max(start + (k + 1) * every, prev + checkInGap / step), { _ in true })
                if at < 0 { break }
                place(list[k], at)
                prev = at
            }
        }

        runGroups(4, 5)
        return items
    }
}
