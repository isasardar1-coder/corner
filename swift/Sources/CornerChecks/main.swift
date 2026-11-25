// Port of corner/tests/Tests.cs: checks for Corner's sleep and energy model and for Plan.
// Prints PASS or FAIL for each check, in the same order and wording as the C# checks, then "ALL PASS" or "<n> FAILED".
// Exit code 1 when anything failed. "CornerChecks sim" runs the sleep-need simulation, "CornerChecks seed" fills
// CORNER_DATA with 40 days of a made-up sleeper.
import Foundation
import CornerCore

/// System.Random(seed) as .NET has it (the subtractive generator every .NET version uses for a seeded Random),
/// so the simulated sleepers are the same people as in the C# checks.
final class NetRandom {
    private var seedArray = [Int](repeating: 0, count: 56)
    private var inext = 0
    private var inextp = 21
    private let mbig = 2147483647
    private let mseed = 161803398

    init(_ seed: Int) {
        let subtraction = seed == -2147483648 ? 2147483647 : abs(seed)
        var mj = mseed - subtraction
        seedArray[55] = mj
        var mk = 1
        for i in 1..<55 {
            let ii = (21 * i) % 55
            seedArray[ii] = mk
            mk = mj - mk
            if mk < 0 { mk += mbig }
            mj = seedArray[ii]
        }
        for _ in 1..<5 {
            for i in 1..<56 {
                seedArray[i] -= seedArray[1 + (i + 30) % 55]
                if seedArray[i] < 0 { seedArray[i] += mbig }
            }
        }
        inext = 0
        inextp = 21
    }

    private func internalSample() -> Int {
        var locINext = inext + 1
        if locINext >= 56 { locINext = 1 }
        var locINextp = inextp + 1
        if locINextp >= 56 { locINextp = 1 }
        var retVal = seedArray[locINext] - seedArray[locINextp]
        if retVal == mbig { retVal -= 1 }
        if retVal < 0 { retVal += mbig }
        seedArray[locINext] = retVal
        inext = locINext
        inextp = locINextp
        return retVal
    }

    func nextDouble() -> Double { return Double(internalSample()) * (1.0 / 2147483647.0) }
    func next(_ maxValue: Int) -> Int { return Int(nextDouble() * Double(maxValue)) }
}

struct Sleepers {
    var nights: [Night]
    var checks: [Feedback.Entry]
}

enum Program {
    static var fails = 0

    static func check(_ what: String, _ ok: Bool, _ detail: String = "") {
        if !ok { fails += 1 }
        print((ok ? "  PASS  " : "  FAIL  ") + what + (detail != "" ? "   [" + detail + "]" : ""))
    }

    static func c(_ t: Date) -> String { return CornerTime.clock24(t) }
    static func z(_ zone: Zone?) -> String {
        guard let zz = zone else { return "none" }
        return c(zz.from) + "-" + c(zz.to)
    }
    static func T(_ h: Int, _ m: Int) -> TimeInterval { return TimeInterval(h * 3600 + m * 60) }
    static func f(_ x: Double, _ decimals: Int) -> String { return NetFormat.fixed(x, decimals) }
    static func b(_ x: Bool) -> String { return x ? "True" : "False" }
    static func padRight(_ s: String, _ width: Int) -> String {
        var r = s
        while r.utf16.count < width { r += " " }
        return r
    }
    static func padLeft(_ s: String, _ width: Int) -> String {
        var r = s
        while r.utf16.count < width { r = " " + r }
        return r
    }
    static func average(_ xs: [Double]) -> Double {
        var s = 0.0
        for x in xs { s += x }
        return s / Double(xs.count)
    }

    static func describe(_ name: String, _ e: EnergyDay) {
        let d0 = e.wake.dateOnly
        var line = "  " + name + ": wake " + c(e.wake) + " bed " + c(e.bed) + " groggy<" + c(e.groggyUntil)
        line += " peak1 " + z(e.peak1) + " dip " + z(e.dip) + " peak2 " + z(e.peak2)
        line += " debt " + f(e.debtHours, 2) + "h  E@10=" + f(e.at(d0.addingHours(10)), 3)
        line += " E@14.5=" + f(e.at(d0.addingHours(14.5)), 3) + " E@19=" + f(e.at(d0.addingHours(19)), 3)
        print(line)
    }

    static func repeatNights(_ asleep: TimeInterval, _ woke: TimeInterval, _ nights: Int, _ day: Date) -> [Night] {
        var list: [Night] = []
        var i = nights - 1
        while i >= 0 {
            if let n = SleepLog.build(asleep: asleep, woke: woke, day: day.addingDays(-i)) { list.append(n) }
            i -= 1
        }
        return list
    }

    static func newId() -> String {
        return UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    }

    static func task(_ title: String, _ energy: String?, _ minutes: Int, _ kind: String? = nil) -> TaskItem {
        return TaskItem(id: newId(), title: title, energy: energy, minutes: minutes, kind: kind)
    }

    static func minDate(_ a: Date, _ b: Date) -> Date { return a < b ? a : b }
    static func maxDate(_ a: Date, _ b: Date) -> Date { return a > b ? a : b }

    static func overlap(_ zone: Zone?, _ a: Date, _ minutes: Int) -> Double {
        guard let zz = zone else { return 0 }
        let end = a.addingMinutes(minutes)
        let m = minDate(zz.to, end).timeIntervalSince(maxDate(zz.from, a)) / 60
        return max(0, m) / Double(minutes)
    }

    static func peakShare(_ e: EnergyDay, _ a: Date, _ minutes: Int) -> Double {
        return overlap(e.peak1, a, minutes) + overlap(e.peak2, a, minutes)
    }

    static func zoneOf(_ e: EnergyDay, _ a: Date, _ minutes: Int) -> String {
        let p = peakShare(e, a, minutes)
        let d = overlap(e.dip, a, minutes)
        var s: String
        if p >= 0.5 { s = "peak" }
        else if d >= 0.5 { s = "dip" }
        else if a.addingMinutes(minutes) <= e.groggyUntil.addingMinutes(4) { s = "groggy" }
        else { s = "between" }
        return s + " (" + String(Int(NetFormat.round(p * 100))) + "% peak, " + String(Int(NetFormat.round(d * 100))) + "% dip)"
    }

    static func printPlan(_ e: EnergyDay, _ plan: [PlanItem]) {
        let indexed = Array(plan.enumerated())
        let sorted = indexed.sorted { x, y in
            let kx = x.element.placed ? 0 : 1
            let ky = y.element.placed ? 0 : 1
            if kx != ky { return kx < ky }
            if x.element.start != y.element.start { return x.element.start < y.element.start }
            return x.offset < y.offset
        }
        for entry in sorted {
            let it = entry.element
            var line = "    " + (it.placed ? c(it.start) + "-" + c(it.start.addingMinutes(it.minutes)) : "  -- no room") + "  "
            line += padRight(it.level ?? "unset", 7) + (it.physical ? "phys " : "     ") + padRight(it.title, 28)
            if it.placed { line += zoneOf(e, it.start, it.minutes) + "  avgE=" + f(avgE(e, it), 3) }
            print(line)
        }
    }

    static func avgE(_ e: EnergyDay, _ it: PlanItem) -> Double {
        var s = 0.0
        var n = 0
        var t = it.start
        let end = it.start.addingMinutes(it.minutes)
        while t < end {
            s += e.at(t)
            n += 1
            t = t.addingMinutes(5)
        }
        return s / Double(n)
    }

    // ------------------------------------------------------------------ simulated sleepers, for the sleep-need estimate

    static func gauss(_ r: NetRandom) -> Double {
        let u1 = r.nextDouble()
        let u2 = r.nextDouble()
        return sqrt(-2 * log(1 - u1)) * cos(2 * Double.pi * u2)
    }

    /// A made-up person whose true need is known: each day's scores drop by `perHour` points for every hour their recent
    /// sleep falls short of it. `lastNightOnly`: the drop follows last night alone instead of the last three nights.
    static func sleeper(_ rng: NetRandom, _ day: Date, _ trueNeed: Double, _ days: Int, _ sleepMean: Double, _ sleepSd: Double,
                        _ perHour: Double, _ noise: Double, _ lastNightOnly: Bool, perDay: Int = 0) -> Sleepers {
        var nights: [Night] = []
        var checks: [Feedback.Entry] = []
        var lens: [Double] = []
        var i = days - 1
        while i >= 0 {
            let len = max(4.5, min(10.5, sleepMean + sleepSd * gauss(rng)))
            let wake = day.addingDays(-i).addingHours(7).addingMinutes(NetFormat.round(20 * gauss(rng)))
            nights.append(Night.make(wake.addingMinutes(-NetFormat.round(len * 60)), wake, assumed: false))
            lens.append(nights[nights.count - 1].hours)
            let k = lens.count - 1
            let recent: Double = (lastNightOnly || k < 2) ? lens[k] : 0.5 * lens[k] + 0.3 * lens[k - 1] + 0.2 * lens[k - 2]
            let drop = perHour * max(0, trueNeed - recent)
            let count = perDay > 0 ? perDay : 3 + rng.next(3)
            for _ in 0..<count {
                let t = 1 + rng.nextDouble() * 13
                let usual: Double = 6.8 + 1.3 * cos(2 * Double.pi * (t - 3) / 12) - 0.08 * t // a morning peak, an afternoon dip, an evening rise
                let g = gauss(rng)
                let score = Int(max(1, min(10, NetFormat.round(usual - drop + noise * g))))
                checks.append(Feedback.Entry(at: wake.addingHours(t), score: score, predicted: 0.5, sinceWake: t))
            }
            i -= 1
        }
        return Sleepers(nights: nights, checks: checks)
    }

    static func sim() {
        let day = CornerTime.make(2026, 11, 21)!
        let now = day.addingHours(23)
        print("Sleep-need estimate on simulated sleepers (300 each). ready = a figure was suggested; of those: average error, share within 30 min, share off by more than 45 min.")
        func run(_ name: String, _ trueNeed: Double, _ days: Int, _ mean: Double, _ sd: Double, _ perHour: Double, _ noise: Double, _ lastOnly: Bool) {
            var ready = 0
            var close = 0
            var far = 0
            var err = 0.0
            var noteKeys: [String] = []
            var notes: [String: Int] = [:]
            for seed in 1...300 {
                let sl = sleeper(NetRandom(seed), day, trueNeed, days, mean, sd, perHour, noise, lastOnly)
                let r = NeedEstimate.build(sl.nights, sl.checks, now)
                if !r.ready {
                    let key = String(r.note.prefix(34))
                    if notes[key] == nil { noteKeys.append(key); notes[key] = 0 }
                    notes[key] = (notes[key] ?? 0) + 1
                    continue
                }
                ready += 1
                let e = abs(r.need - max(NeedEstimate.floor, trueNeed))
                err += e
                if e <= 0.5 + 1e-9 { close += 1 }
                if e > 0.75 + 1e-9 { far += 1 }
            }
            var line = "  " + padRight(name, 46) + " ready " + padLeft(String(ready * 100 / 300), 3) + "%"
            if ready > 0 {
                line += "  avg error " + f(err / Double(ready) * 60, 0) + " min, within 30 min " + String(close * 100 / ready) + "%, off >45 min " + String(far * 100 / ready) + "%"
            }
            print(line)
            let ordered = Array(noteKeys.enumerated()).sorted { x, y in
                let vx = notes[x.element] ?? 0
                let vy = notes[y.element] ?? 0
                if vx != vy { return vx > vy }
                return x.offset < y.offset
            }
            for entry in ordered {
                let v = notes[entry.element] ?? 0
                print("        not ready " + padLeft(String(v * 100 / 300), 3) + "%: " + entry.element + "...")
            }
        }
        run("need 7h30, 14 days, varied nights", 7.5, 14, 7.3, 0.9, 1.2, 1.0, false)
        run("need 7h30, 28 days, varied nights", 7.5, 28, 7.3, 0.9, 1.2, 1.0, false)
        run("need 7h30, 56 days, varied nights", 7.5, 56, 7.3, 0.9, 1.2, 1.0, false)
        run("need 7h30, 28 days, follows last night only", 7.5, 28, 7.3, 0.9, 1.2, 1.0, true)
        run("need 7h30, 28 days, noisier scores", 7.5, 28, 7.3, 0.9, 1.2, 1.6, false)
        run("need 7h30, 28 days, weak effect", 7.5, 28, 7.3, 0.9, 0.6, 1.0, false)
        run("need 8h30, 28 days, sleeps 7h20 on average", 8.5, 28, 7.3, 0.9, 1.2, 1.0, false)
        run("need 7h30, 28 days, nights all alike", 7.5, 28, 7.3, 0.3, 1.2, 1.0, false)
        run("need 6h15 (below the floor), 28 days", 6.25, 28, 7.0, 0.9, 1.2, 1.0, false)
        run("sleep makes no difference, 28 days", 7.5, 28, 7.3, 0.9, 0.0, 1.0, false)
        run("sleep makes no difference, 56 days", 7.5, 56, 7.3, 0.9, 0.0, 1.0, false)
    }

    /// Fills the data folder (CORNER_DATA) with 40 days of a made-up sleeper who needs 7h30, to render the suggestion.
    static func seed() {
        let today = Date().dateOnly
        let sl = sleeper(NetRandom(7), today, 7.5, 40, 7.3, 0.9, 1.5, 0.4, false, perDay: 5)
        for x in sl.nights { SleepLog.record(x) }
        var text = "time,score,predicted,hours_since_wake,active_min_today,active_min_session\r\n"
        let cutoff = today.addingHours(15)
        for x in sl.checks where x.at <= cutoff {
            text += CornerTime.format(x.at) + "," + String(x.score) + ",0.500," + f(x.sinceWake, 2) + ",0,0\r\n"
        }
        let path = NSString(string: Store.dir).appendingPathComponent("checkins.csv")
        try? text.write(toFile: path, atomically: true, encoding: .utf8)
        print(NeedEstimate.build(sl.nights, sl.checks, cutoff).note)
    }

    static func main() {
        let args = Array(CommandLine.arguments.dropFirst())
        if args.count > 0 && args[0] == "sim" { sim(); return }
        if args.count > 0 && args[0] == "seed" { seed(); return }
        let day = CornerTime.make(2026, 11, 21)!
        let need = 8.0

        // ------------------------------------------------------------------ 1. curve from last night
        print("== 1. Energy curve from last night's sleep")
        let normal = EnergyModel.build(repeatNights(T(23, 0), T(7, 0), 14, day), need: need, day: day)!
        describe("8h x14        ", normal)
        var dipMid = 0.0
        if let dp = normal.dip { dipMid = (dp.from.timeOfDay / 3600 + dp.to.timeOfDay / 3600) / 2 }
        check("normal night: dip centred near 14:30", normal.dip != nil && abs(dipMid - 14.5) <= 0.5, "centre " + f(dipMid, 2))
        check("normal night: no debt", normal.debtHours < 0.01)
        var ordered = false
        if let p1 = normal.peak1, let p2 = normal.peak2, let dp = normal.dip {
            ordered = normal.groggyUntil <= p1.from && p1.to < dp.from && dp.to < p2.from
        }
        check("normal night: groggy -> peak1 -> dip -> peak2 in order", ordered)

        var ns = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        ns.append(SleepLog.build(asleep: T(2, 0), woke: T(7, 0), day: day)!)
        let shortNight = EnergyModel.build(ns, need: need, day: day)!
        describe("5h last night ", shortNight)
        var lower = true
        for i in 0..<min(normal.samples.count, shortNight.samples.count) where !(shortNight.samples[i] <= normal.samples[i] + 1e-9) { lower = false }
        check("short night lowers the whole curve", lower)
        check("short night: debt = 3h", abs(shortNight.debtHours - 3) < 0.01, f(shortNight.debtHours, 2))

        var nl = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        nl.append(SleepLog.build(asleep: T(22, 0), woke: T(8, 0), day: day)!)
        describe("10h last night", EnergyModel.build(nl, need: need, day: day)!)

        var nv = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        nv.append(SleepLog.build(asleep: T(4, 0), woke: T(7, 0), day: day)!)
        let veryShort = EnergyModel.build(nv, need: need, day: day)!
        describe("3h last night ", veryShort)
        check("3h night is lower than 5h night at 10:00", veryShort.at(day.addingHours(10)) < shortNight.at(day.addingHours(10)) - 0.005,
              f(veryShort.at(day.addingHours(10)), 3) + " vs " + f(shortNight.at(day.addingHours(10)), 3))

        var sp = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        let split = SleepLog.buildSplit(asleep: [T(23, 30), T(5, 15)], woke: [T(4, 30), T(7, 0)], day: day)!
        sp.append(split)
        let es = EnergyModel.build(sp, need: need, day: day)!
        describe("split 6h45    ", es)
        check("split night totals 6h45", abs(split.hours - 6.75) < 1e-9, f(split.hours, 3))
        check("split night: curve starts at 07:00", es.wake == day.addingHours(7))
        var one = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        one.append(SleepLog.build(asleep: T(0, 15), woke: T(7, 0), day: day)!)
        let eo = EnergyModel.build(one, need: need, day: day)!
        let at10 = day.addingHours(10)
        check("split costs a little vs. same sleep in one piece", es.at(at10) < eo.at(at10) && eo.at(at10) - es.at(at10) < 0.03, f(eo.at(at10) - es.at(at10), 4))
        let three = SleepLog.buildSplit(asleep: [T(22, 0), T(0, 15), T(5, 10)], woke: [T(23, 30), T(4, 30), T(7, 30)], day: day)
        var threeOk = false
        if let th = three { threeOk = abs(th.hours - (1.5 + 4.25 + 2 + 20 / 60.0)) < 1e-9 && th.asleepAt == day.addingDays(-1).addingHours(22) }
        check("three periods, first ends before midnight: 1h30 + 4h15 + 2h20", threeOk, three == nil ? "null" : f(three!.hours, 3))

        var lw = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
        lw.append(SleepLog.build(asleep: T(1, 0), woke: T(9, 0), day: day)!)
        let late = EnergyModel.build(lw, need: need, day: day)!
        describe("woke 09:00    ", late)
        var lateOk = false
        var lateDetail = "no dip"
        if let ld = late.dip, let nd = normal.dip {
            let moved = ld.from.timeIntervalSince(nd.from) / 3600
            lateOk = abs(moved - 1) < 0.5
            lateDetail = f(moved, 2)
        }
        check("waking 2h late moves the dip about 1h later", lateOk, lateDetail)

        print("  -- sweep of single-night patterns (14 nights each)")
        let patterns: [[Int]] = [[21, 0, 5, 0], [22, 0, 6, 0], [0, 0, 8, 0], [1, 0, 9, 0], [2, 0, 10, 0], [3, 0, 11, 0], [0, 30, 6, 30], [1, 0, 6, 0], [23, 0, 9, 0]]
        for p in patterns {
            let e = EnergyModel.build(repeatNights(T(p[0], p[1]), T(p[2], p[3]), 14, day), need: need, day: day)!
            let name = CornerTimePad.two(p[0]) + ":" + CornerTimePad.two(p[1]) + "-" + CornerTimePad.two(p[2]) + ":" + CornerTimePad.two(p[3]) + "   "
            describe(name, e)
            var ok = false
            if let p1 = e.peak1, let dp = e.dip, let p2 = e.peak2 {
                ok = p1.to <= dp.from && dp.to <= p2.from && p1.from >= e.wake
            }
            check("   zones present and ordered", ok)
        }

        // zones once the check-in tuning is active: corrections with bumps must not make the zones vanish
        print("  -- with a tuning correction added to the curve")
        let bumps: [(String, (Double) -> Double)] = [
            ("early lift, then low (0.5h +, 1.6h -)", { t in 0.08 * exp(-(t - 0.5) * (t - 0.5) / 1.1) - 0.10 * exp(-(t - 1.6) * (t - 1.6) / 1.1) }),
            ("sharper morning, flatter evening    ", { t in 0.12 * exp(-(t - 2.2) * (t - 2.2) / 1.1) - 0.08 * exp(-(t - 12) * (t - 12) / 1.1) }),
            ("late-evening bump (15h +)           ", { t in 0.10 * exp(-(t - 15) * (t - 15) / 1.1) - 0.06 * exp(-(t - 13.5) * (t - 13.5) / 1.1) }),
            ("ripple                              ", { t in 0.05 * sin(t * 2.4) }),
        ]
        for bump in bumps {
            let e = EnergyModel.build(repeatNights(T(23, 0), T(7, 0), 14, day), need: need, day: day, correction: bump.1)!
            describe(bump.0, e)
            var ok = false
            if let p1 = e.peak1, let dp = e.dip, let p2 = e.peak2 {
                ok = dp.from.timeOfDay / 3600 >= 12.5 && dp.to.timeOfDay / 3600 <= 16.5 && p1.to <= dp.from && dp.to <= p2.from
            }
            check("   tuned curve keeps peak, dip (12:30-16:30) and second peak", ok)
        }

        // the check-ins may also move a peak or the dip in time (EnergyModel.reshape), by 90 minutes at the most
        print("  -- check-ins that put the afternoon top earlier")
        do {
            let tmNights: [Night] = repeatNights(T(23, 0), T(7, 0), 14, day)
            func tmTop(_ e: EnergyDay, _ a: Int, _ b: Int) -> Int {
                var k: Int = a
                var i: Int = a
                while i <= b {
                    if e.samples[i] > e.samples[k] { k = i }
                    i += 1
                }
                return k
            }
            func tmLow(_ e: EnergyDay) -> Int {
                var k = 24
                var i = 24
                while i < e.samples.count - 24 {
                    if e.samples[i] < e.samples[k] { k = i }
                    i += 1
                }
                return k
            }
            func tmHumps(_ e: EnergyDay, _ a: Int, _ b: Int) -> Int {
                var n = 0
                var i: Int = a + 1
                while i < b {
                    if e.samples[i] > e.samples[i - 1] + 1e-12 && e.samples[i] >= e.samples[i + 1] { n += 1 }
                    i += 1
                }
                return n
            }
            func tmStep(_ e: EnergyDay) -> Double {
                var m = 0.0
                for i in 1..<e.samples.count { m = max(m, abs(e.samples[i] - e.samples[i - 1])) }
                return m
            }
            let tmEnd: Int = normal.samples.count - 1
            let tmDip0: Int = tmLow(normal)
            let tmTop0: Int = tmTop(normal, tmDip0, tmEnd)

            let tmEarly: (Double) -> Double = { t in 0.08 * exp(-(t - 9.5) * (t - 9.5) / 3) - 0.10 * exp(-(t - 13) * (t - 13) / 3) }
            let tmE1: EnergyDay = EnergyModel.build(tmNights, need: need, day: day, correction: tmEarly)!
            describe("fresher at 16:30, flatter at 20:00   ", tmE1)
            let tmDip1: Int = tmLow(tmE1)
            let tmTop1: Int = tmTop(tmE1, tmDip1, tmEnd)
            let tmMoved: Int = (tmTop1 - tmTop0) * 5
            check("the afternoon top moves earlier, by 90 minutes at the most", tmMoved <= -15 && tmMoved >= -90, c(normal.timeAt(tmTop0)) + " -> " + c(tmE1.timeAt(tmTop1)))
            var tmZoneEarlier = false
            if let tmA = tmE1.peak2, let tmB = normal.peak2 { tmZoneEarlier = tmA.from < tmB.from }
            check("...and its peak zone starts earlier with it", tmZoneEarlier, z(normal.peak2) + " -> " + z(tmE1.peak2))
            let tmWant: Double = normal.samples[tmTop1] + tmEarly(Double(tmTop1 * 5) / 60.0)
            check("...its height is what the check-ins say for that moment", abs(tmE1.samples[tmTop1] - tmWant) < 1e-9, f(tmE1.samples[tmTop1], 3))
            let tmH1: Int = tmHumps(tmE1, 0, tmDip1)
            let tmH2: Int = tmHumps(tmE1, tmDip1, tmEnd)
            let tmSmooth: Bool = tmStep(tmE1) <= tmStep(normal) * 1.5 + 0.003
            check("the curve stays one even hump on each side of the dip, with no jump", tmH1 == 1 && tmH2 == 1 && tmSmooth,
                  String(tmH1) + " + " + String(tmH2) + " humps, largest step " + f(tmStep(tmE1), 3) + " (plain " + f(tmStep(normal), 3) + ")")
            var tmOrdered = false
            if let tmP1 = tmE1.peak1, let tmD = tmE1.dip, let tmP2 = tmE1.peak2 { tmOrdered = tmP1.to <= tmD.from && tmD.to <= tmP2.from }
            check("zones stay in order: peak, dip, peak", tmOrdered)

            let tmE2: EnergyDay = EnergyModel.build(tmNights, need: need, day: day, correction: { _ in 0.05 })!
            let tmSameZones: Bool = z(tmE2.peak1) == z(normal.peak1) && z(tmE2.dip) == z(normal.dip) && z(tmE2.peak2) == z(normal.peak2)
            check("check-ins that only say \"higher all day\" move nothing in time", tmSameZones && tmTop(tmE2, tmDip0, tmEnd) == tmTop0,
                  z(tmE2.peak1) + " / " + z(tmE2.dip) + " / " + z(tmE2.peak2))
            var tmLifted = true
            for i in 0...tmEnd where abs(tmE2.samples[i] - normal.samples[i] - 0.05) >= 1e-9 { tmLifted = false }
            check("...and lift the whole curve by that much", tmLifted)

            // scores that tilt the afternoon hump hard towards its start (the dip is left alone): read moment by moment,
            // the best of the afternoon is more than three hours before the model's top
            let tmTilt: (Double) -> Double = { t in t < 8 ? 0 : max(-0.15, min(0.15, -0.06 * (t - 10.5))) }
            let tmE3: EnergyDay = EnergyModel.build(tmNights, need: need, day: day, correction: tmTilt)!
            var tmDip3: Int = tmDip0 // (the evening ends lower than the dip here, so the dip is taken from its zone)
            if let tmD3 = tmE3.dip { tmDip3 = Int(NetFormat.round(tmD3.from.timeIntervalSince(tmE3.wake) / 60 / 5)) }
            let tmTop3: Int = tmTop(tmE3, tmDip3, tmEnd)
            var tmZones3 = false
            if let tmD3 = tmE3.dip, let tmP3 = tmE3.peak2 { tmZones3 = tmD3.to <= tmP3.from }
            check("check-ins asking for a move of hours: the top goes 90 minutes and no further", (tmTop0 - tmTop3) * 5 == 90 && tmZones3 && tmHumps(tmE3, tmDip3, tmEnd) == 1,
                  c(normal.timeAt(tmTop0)) + " -> " + c(tmE3.timeAt(tmTop3)) + ", zones " + z(tmE3.dip) + " / " + z(tmE3.peak2))
            var tmRawSame = true
            for i in 0...tmEnd where tmE1.raw[i] != normal.raw[i] { tmRawSame = false }
            check("the untuned figure kept with each check-in does not move with the tuning", tmRawSame)
        }

        // ------------------------------------------------------------------ 2. debt
        print("== 2. Sleep debt")
        SleepLog.load()
        // your own log (a copy of sleep.json), on the day of its latest night; the debt is added up again here, straight from the file's times
        let realNights = SleepLog.data.nights // kept: the nap checks further down write their own nights over the copy
        let realDay = SleepLog.latest != nil ? SleepLog.latest!.wokeAt.dateOnly : day
        let real = EnergyModel.build(SleepLog.data.nights, need: need, day: realDay)
        if let real = real {
            describe("real sleep.json", real)
            let window = SleepLog.data.nights.filter { $0.wokeAt.dateOnly > realDay.addingDays(-14) && $0.wokeAt.dateOnly <= realDay }
            var measured = 0.0
            for x in window {
                var mainH = 0.0
                if let ps = x.periods, ps.count > 1 {
                    for p in ps { mainH += Night.p(p.woke).timeIntervalSince(Night.p(p.asleep)) / 3600 }
                } else {
                    mainH = x.wokeAt.timeIntervalSince(x.asleepAt) / 3600
                }
                var napSum = 0.0
                for p in x.naps ?? [] { napSum += Night.p(p.woke).timeIntervalSince(Night.p(p.asleep)) / 3600 }
                measured += need - mainH - napSum
            }
            // unlogged nights count as an average logged night (today's naps left out of the average), once 3 nights are logged
            var todayNaps = 0.0
            for x in real.naps { todayNaps += x.hours }
            let guessed = window.count >= 3 && window.count < 14 ? Double(14 - window.count) * (measured + todayNaps) / Double(window.count) : 0
            let hand = max(0, measured + guessed)
            check("real data: debt matches an independent sum of the file", abs(real.debtHours - hand) < 0.01, f(real.debtHours, 3) + " vs " + f(hand, 3))
            print("  nights logged in window: " + String(window.count) + " of 14; measured " + f(measured, 2) + "h + estimated " + f(guessed, 2) + "h")
        }
        let d14 = EnergyModel.build(repeatNights(T(0, 0), T(7, 0), 20, day), need: need, day: day)!
        check("14 x 7h = 14h debt (older nights ignored)", abs(d14.debtHours - 14) < 0.01, f(d14.debtHours, 2))
        var mix = repeatNights(T(0, 0), T(7, 0), 7, day.addingDays(-7))
        mix.append(contentsOf: repeatNights(T(22, 0), T(7, 0), 7, day))
        let dm = EnergyModel.build(mix, need: need, day: day)!
        check("7 x 7h then 7 x 9h = 0 debt", abs(dm.debtHours) < 0.01, f(dm.debtHours, 2))
        let d7 = EnergyModel.build(repeatNights(T(23, 30), T(7, 0), 14, day), need: need, day: day)! // 14 x 7h30 = 7h debt
        let at145 = day.addingHours(14.5)
        check("more debt -> lower energy at 10:00 and deeper dip", abs(d7.debtHours - 7) < 0.01 && d14.at(at10) < d7.at(at10) && d14.at(at145) < d7.at(at145),
              f(d7.at(at10), 3) + " -> " + f(d14.at(at10), 3))
        let d9 = EnergyModel.build(repeatNights(T(23, 0), T(7, 0), 14, day), need: 9, day: day)!
        check("need 9h with 8h nights x14 = 14h debt", abs(d9.debtHours - 14) < 0.01, f(d9.debtHours, 2))

        // nights that were never logged count as an average logged night
        let half = EnergyModel.build(repeatNights(T(0, 0), T(7, 0), 7, day), need: need, day: day)!
        check("7 nights of 7h logged, 7 not logged = 14h (7h measured + 7h estimated)", abs(half.debtHours - 14) < 0.01 && abs(half.debtEstimate - 7) < 0.01 && half.debtLogged == 7,
              f(half.debtHours, 2) + ", estimate " + f(half.debtEstimate, 2))
        check("...and the curve is the same as with all 14 logged", abs(half.at(at10) - d14.at(at10)) < 1e-9)
        let uneven: [Night] = [SleepLog.build(asleep: T(2, 0), woke: T(7, 0), day: day.addingDays(-6))!, SleepLog.build(asleep: T(23, 0), woke: T(7, 0), day: day.addingDays(-3))!,
                               SleepLog.build(asleep: T(0, 0), woke: T(7, 0), day: day.addingDays(-1))!, SleepLog.build(asleep: T(23, 0), woke: T(7, 0), day: day)!]
        let un = EnergyModel.build(uneven, need: need, day: day)! // shortfalls 3, 0, 1, 0: average 1h a night
        check("4 logged (3h, 0, 1h, 0 short), 10 not logged = 4h + 10 x 1h = 14h", abs(un.debtHours - 14) < 0.01, f(un.debtHours, 2))
        let gaps: [Night] = [SleepLog.build(asleep: T(1, 0), woke: T(7, 0), day: day.addingDays(-5))!, SleepLog.build(asleep: T(1, 0), woke: T(7, 0), day: day)!]
        let two = EnergyModel.build(gaps, need: need, day: day)!
        check("only 2 nights logged: too few to estimate, debt = 4h measured", abs(two.debtHours - 4) < 0.01 && two.debtEstimate == 0, f(two.debtHours, 2))
        let over = EnergyModel.build(repeatNights(T(22, 0), T(7, 0), 5, day), need: need, day: day)!
        check("5 nights of 9h logged, 9 not logged: no debt, never below zero", over.debtHours == 0, f(over.debtHours, 2))
        let napGap = repeatNights(T(0, 0), T(7, 0), 7, day)
        let napGap2 = repeatNights(T(0, 0), T(7, 0), 7, day)
        napGap2[6].naps = [SleepSpan.make(day.addingHours(14.5), day.addingHours(14.5).addingMinutes(30))]
        let dropGap = EnergyModel.build(napGap, need: need, day: day)!.debtHours - EnergyModel.build(napGap2, need: need, day: day)!.debtHours
        check("with unlogged nights, a 30-minute nap still pays back exactly 30 minutes", abs(dropGap - 0.5) < 1e-6, f(dropGap * 60, 1) + " min")
        let notLogged = EnergyModel.build(repeatNights(T(1, 0), T(7, 0), 3, day.addingDays(-1)), need: need, day: day)!
        print("  today not logged, 3 x 6h before: debt = " + f(notLogged.debtHours, 2) + "h (6h measured + 11 nights estimated at 2h)")

        // ------------------------------------------------------------------ 3. naps
        print("== 3. Naps")
        func baseNights() -> [Night] {
            var l = repeatNights(T(23, 0), T(7, 0), 13, day.addingDays(-1))
            l.append(SleepLog.buildSplit(asleep: [T(23, 30), T(5, 15)], woke: [T(4, 30), T(7, 0)], day: day)!)
            return l
        }
        let b0 = baseNights()
        let e0 = EnergyModel.build(b0, need: need, day: day)!
        let b1 = baseNights()
        b1[b1.count - 1].naps = [SleepSpan.make(day.addingHours(14.5), day.addingHours(14.5).addingMinutes(25))]
        let e1 = EnergyModel.build(b1, need: need, day: day)!
        describe("no nap        ", e0)
        describe("nap 14:30 25m ", e1)
        check("nap: debt drops by exactly the nap length", abs((e0.debtHours - e1.debtHours) - 25 / 60.0) < 1e-6, f(e0.debtHours - e1.debtHours, 3))
        var unchanged = true
        var maxChange = 0.0
        for i in 0..<Int(7.5 * 12) {
            let ch = abs(e0.samples[i] - e1.samples[i])
            if !(ch < 1e-12) { unchanged = false }
            if ch > maxChange { maxChange = ch }
        }
        check("nap: curve before the nap is unchanged", unchanged, "max change " + f(maxChange, 4))
        let lifts = [15.5, 16.5, 18.0].allSatisfy { h in e1.at(day.addingHours(h)) > e0.at(day.addingHours(h)) + 0.01 }
        let liftDetail = [15.0, 15.5, 16.5, 18, 21].map { h in NetFormat.plain(h) + ":" + NetFormat.plusMinus(e1.at(day.addingHours(h)) - e0.at(day.addingHours(h)), 3) }.joined(separator: " ")
        check("nap: afternoon lifts afterwards (15:30, 16:30, 18:00)", lifts, liftDetail)
        let w25 = day.addingHours(14.5).addingMinutes(25)
        let w45 = day.addingHours(14.5).addingMinutes(45)
        check("nap: short groggy stretch right at waking, gone in 20 min", e1.at(w25) - e0.at(w25) < e1.at(w45) - e0.at(w45))
        check("nap before 16:00: bedtime not pushed", e1.bedPush == 0 && e1.bed == e0.bed)
        check("nap: peak and dip zones stay where they were", z(e0.peak1) == z(e1.peak1) && z(e0.dip) == z(e1.dip) && z(e0.peak2) == z(e1.peak2), z(e1.peak1) + " / " + z(e1.dip) + " / " + z(e1.peak2))

        let b2 = baseNights()
        b2[b2.count - 1].naps = [SleepSpan.make(day.addingHours(17), day.addingHours(17).addingMinutes(40))]
        let e2 = EnergyModel.build(b2, need: need, day: day)!
        describe("nap 17:00 40m ", e2)
        let summary18 = e2.summary(day.addingHours(18))
        check("late nap pushes bedtime 60 min and says so", abs(e2.bedPush - 60) < 1e-6 && e2.bed.timeIntervalSince(e0.bed) / 60 == 60 && summary18.contains("late nap"), summary18)
        let w1740 = day.addingHours(17).addingMinutes(40)
        check("long nap: heavier grogginess than a short one", (e2.at(w1740) - e0.at(w1740)) < (e1.at(w25) - e0.at(w25)))

        func push(_ fromH: Double, _ mins: Double) -> Double {
            let bn = baseNights()
            bn[bn.count - 1].naps = [SleepSpan.make(day.addingHours(fromH), day.addingHours(fromH).addingMinutes(mins))]
            return EnergyModel.build(bn, need: need, day: day)!.bedPush
        }
        var pushLine = "  bed push: nap 15:30-15:55 " + NetFormat.plain(push(15.5, 25))
        pushLine += " min, 15:30-16:05 " + NetFormat.plain(push(15.5, 35))
        pushLine += " min, 15:45-16:15 " + NetFormat.plain(push(15.75, 30))
        pushLine += " min, 16:00-16:30 " + NetFormat.plain(push(16, 30)) + " min"
        print(pushLine)
        check("a nap that only just crosses 16:00 pushes bedtime only a little", push(15.5, 35) <= 10, NetFormat.plain(push(15.5, 35)) + " min")

        let b4 = baseNights()
        b4[b4.count - 1].naps = [SleepSpan.make(day.addingHours(14.5), day.addingHours(14.5).addingMinutes(25)), SleepSpan.make(day.addingHours(14.5), day.addingHours(14.5).addingMinutes(25)),
                                 SleepSpan.make(day.addingHours(14.6), day.addingHours(14.5).addingMinutes(25))]
        let e4 = EnergyModel.build(b4, need: need, day: day)!
        check("a nap already in the file twice is counted once in debt", abs((e0.debtHours - e4.debtHours) - 25 / 60.0) < 1e-6, "debt drop " + f((e0.debtHours - e4.debtHours) * 60, 0) + " min")
        let at16 = day.addingHours(16)
        check("a nap already in the file twice lifts once", abs(e4.at(at16) - e1.at(at16)) < 1e-9, NetFormat.plusPrefixed(e4.at(at16) - e1.at(at16), 3))

        SleepLog.data = SleepData(nights: baseNights())
        let a1 = SleepLog.addNap(day, from: day.addingHours(14.5), to: day.addingHours(14.5).addingMinutes(25))
        let a2 = SleepLog.addNap(day, from: day.addingHours(14.6), to: day.addingHours(14.6).addingMinutes(25))
        check("AddNap refuses a nap that overlaps one already logged", a1 && !a2, "first " + b(a1) + ", overlapping " + b(a2))
        let a3 = SleepLog.addNap(day, from: day.addingHours(6.5), to: day.addingHours(6.9))
        check("AddNap refuses a nap inside the main sleep", !a3, b(a3))
        let a6 = SleepLog.addNap(day, from: day.addingHours(16.5), to: day.addingHours(16.5).addingMinutes(20))
        check("AddNap accepts a second, separate nap", a6 && SleepLog.forDay(day)!.napSpans.count == 2)

        SleepLog.data = SleepData(nights: repeatNights(T(23, 0), T(7, 0), 3, day.addingDays(-1)))
        let a5 = SleepLog.addNap(day, from: day.addingHours(13), to: day.addingHours(13).addingMinutes(20))
        let e5 = EnergyModel.build(SleepLog.data.nights, need: need, day: day)!
        check("nap on a skipped morning still counts today", a5 && e5.naps.count == 1)

        // ------------------------------------------------------------------ 4. planner
        print("== 4. Plan by effort")
        func mk() -> [TaskItem] {
            return [task("Tidy desk", nil, 0), task("Anki insertion", "low", 20), task("Review animation", "medium", 30), task("Emails", "low", 30),
                    task("Write concept 14", "high", 45), task("Deep reading", "high", 60), task("Workout", "high", 20, "physical"), task("Admin forms", "low", 30), task("Slides", "medium", 45)]
        }
        let en = normal
        let plan = Planner.plan(mk(), energy: en, routines: nil, now: en.wake)
        var freeLine = "  free day, planned at wake (" + c(en.wake) + "); zones " + z(en.peak1)
        freeLine += " / " + z(en.dip) + " / " + z(en.peak2) + ", groggy until " + c(en.groggyUntil)
        print(freeLine)
        printPlan(en, plan)
        check("everything placed", plan.allSatisfy { $0.placed })
        let placedItems = plan.filter { $0.placed }
        var overlapFound = false
        for x in placedItems {
            for y in placedItems where y !== x {
                if x.start < y.start.addingMinutes(y.minutes) && y.start < x.start.addingMinutes(x.minutes) { overlapFound = true }
            }
        }
        check("no two tasks overlap", !overlapFound)
        // the planner works in 5-minute steps, so a zone edge can be up to a minute or two off the step
        let highMental = plan.filter { $0.level == "high" && !$0.physical }
        check("high mental tasks are fully on a peak", highMental.allSatisfy { peakShare(en, $0.start, $0.minutes) > 0.97 })
        check("no high task inside the groggy stretch", highMental.allSatisfy { $0.start >= en.groggyUntil.addingMinutes(-2.5) })
        check("low tasks are in the groggy stretch or the dip, never on a peak", plan.filter { $0.level == "low" }.allSatisfy { !zoneOf(en, $0.start, $0.minutes).hasPrefix("peak") })
        check("physical task 16:00-20:00", plan.filter { $0.physical }.allSatisfy { $0.start.hour >= 16 && $0.start.addingMinutes($0.minutes) <= day.addingHours(20) })
        let hi = average(highMental.map { avgE(en, $0) })
        let me = average(plan.filter { $0.level == "medium" }.map { avgE(en, $0) })
        let lo = average(plan.filter { $0.level == "low" }.map { avgE(en, $0) })
        check("average energy under tasks: high > medium > low", hi > me && me > lo, f(hi, 3) + " / " + f(me, 3) + " / " + f(lo, 3))

        // planning later in the day: from now on, never before now or past bedtime
        for at in [day.addingHours(14), day.addingHours(18.5), en.bed.addingMinutes(-80)] {
            let p2 = Planner.plan(mk(), energy: en, routines: nil, now: at)
            print("  planned at " + c(at) + " (bed " + c(en.bed) + ")")
            printPlan(en, p2)
            let ok = p2.contains { $0.placed } && p2.filter { $0.placed }.allSatisfy { $0.start >= at && $0.start.addingMinutes($0.minutes) <= en.bed }
            check("planned at " + c(at) + ": something placed, nothing before now or past bed", ok)
        }

        if let real = real {
            let mine = [task("Writing", "high", 90), task("VO in 11 Labs", "medium", 30), task("Emails", "low", 20), task("Tidy desk", nil, 0)]
            let p3 = Planner.plan(mine, energy: real, routines: nil, now: real.wake.addingMinutes(20))
            print("  real night, planned 20 min after waking; zones " + z(real.peak1) + " / " + z(real.dip) + " / " + z(real.peak2) + ", groggy until " + c(real.groggyUntil))
            printPlan(real, p3)
            var peakLen = 0
            if let p1 = real.peak1 { peakLen = Int(p1.to.timeIntervalSince(p1.from) / 60) }
            check("Writing (90m high) sits fully on the morning peak", peakShare(real, p3[0].start, 90) > 0.97, zoneOf(real, p3[0].start, 90) + ", peak is " + String(peakLen) + " min")
        }

        // the seeded routine chain from the brief: voice-over -> transcript -> studio first, then Writing on the peak
        let rs: [Routine] = [
            Routine(id: "r1", title: "Voice-over", energy: "low", minutes: 20, prefer: "early"), Routine(id: "r2", title: "Transcript", energy: "low", minutes: 2, after: "r1"),
            Routine(id: "r3", title: "Studio start", energy: "medium", minutes: 15, after: "r2"), Routine(id: "r5", title: "Writing", energy: "high", minutes: 90),
            Routine(id: "r6", title: "Workout", energy: "high", kind: "physical", minutes: 20), Routine(id: "r7", title: "YouTube Watch Later", energy: "medium", minutes: 20, prefer: "dip"),
        ]
        let rt = rs.map { r in TaskItem(id: newId(), title: r.title, energy: r.energy, minutes: r.minutes, kind: r.kind, routine: r.id, routineDay: "2026-11-21") }
        let p4 = Planner.plan(rt, energy: en, routines: rs, now: en.wake)
        print("  seeded routines, free day, planned at wake")
        printPlan(en, p4)
        func by(_ id: String) -> PlanItem { return p4.first(where: { $0.routine == id })! }
        let chainOk = by("r1").start < by("r2").start && by("r2").start < by("r3").start && by("r3").start < by("r5").start
            && peakShare(en, by("r5").start, 90) > 0.97 && zoneOf(en, by("r7").start, 20).hasPrefix("dip")
        check("routine chain first, in order, then Writing fully on the peak, YouTube in the dip", chainOk)

        let tasks = mk()
        let firstPlan = Planner.plan(tasks, energy: e0, routines: nil, now: e0.wake)
        for it in firstPlan where it.placed {
            let t = tasks.first(where: { $0.id == it.id })!
            t.setDue(it.start, hasTime: true)
            t.planned = true
        }
        let nowNap = day.addingHours(13)
        let b6 = baseNights()
        b6[b6.count - 1].naps = [SleepSpan.make(day.addingHours(12.5), day.addingHours(12.5).addingMinutes(25))]
        let e6 = EnergyModel.build(b6, need: need, day: day)!
        let re = Planner.plan(tasks, energy: e6, routines: nil, now: nowNap, keepStarted: true)
        print("  re-plan at 13:00 after a 12:30 nap (tasks already started are kept)")
        printPlan(e6, re)
        check("re-plan: nothing before now", re.filter { $0.placed }.allSatisfy { $0.start >= nowNap })
        let onlyUnstarted = re.allSatisfy { x in
            let fp = firstPlan.first(where: { $0.id == x.id })!
            return fp.start > nowNap || !fp.placed
        }
        check("re-plan: only tasks that had not started are moved", onlyUnstarted, String(re.count) + " re-planned of " + String(firstPlan.count))

        // a nap logged in the future part of an old plan: plan must avoid it
        let napLater = baseNights()
        napLater[napLater.count - 1].naps = [SleepSpan.make(day.addingHours(8.5), day.addingHours(9))]
        let e7 = EnergyModel.build(napLater, need: need, day: day)!
        let p7 = Planner.plan(mk(), energy: e7, routines: nil, now: e7.wake)
        check("plan never puts a task inside a logged nap", p7.filter { $0.placed }.allSatisfy { !($0.start < day.addingHours(9) && day.addingHours(8.5) < $0.start.addingMinutes($0.minutes)) })

        let p5 = Planner.plan([task("Thesis", "high", 240)], energy: en, routines: nil, now: en.wake)
        print("  a high task longer than the peak")
        printPlan(en, p5)
        // every 4-hour slot that is at least 80% on the peak; the one chosen must be the one with the most energy under it
        var bestE = -Double.infinity
        for k in 0..<40 {
            let t = en.wake.addingMinutes(5 * k)
            if peakShare(en, t, 240) >= 0.8 {
                let v = avgE(en, PlanItem(minutes: 240, start: t))
                if v > bestE { bestE = v }
            }
        }
        check("a high task longer than the peak takes the highest-energy slot that is mostly on it",
              p5[0].placed && peakShare(en, p5[0].start, 240) >= 0.8 && avgE(en, p5[0]) >= bestE - 1e-9, f(avgE(en, p5[0]), 4) + " vs best " + f(bestE, 4))

        // a 90-minute high task planned when only part of the first peak is left: offered as two sessions, one per peak
        do {
            let spAt = en.peak1!.to.addingMinutes(-60) // an hour of the first peak left
            let wr = task("Writing", "high", 90)
            let spOne = Planner.propose([wr], energy: en, routines: nil, now: spAt, keepStarted: false, split: nil)
            var spLine = "  90m high task with 60m of the first peak left: "
            spLine += spOne.map { x in x.placed ? EnergyDay.clock(x.start) : "--" }.joined(separator: ", ")
            if spOne[0].canSplit {
                spLine += " (offer " + String(spOne[0].splitA) + "m " + EnergyDay.clock(spOne[0].splitAt1)
                spLine += " + " + String(spOne[0].splitB) + "m " + EnergyDay.clock(spOne[0].splitAt2) + ")"
            }
            print(spLine)
            let spOffer: Bool = spOne.count == 1 && spOne[0].canSplit && spOne[0].splitA + spOne[0].splitB == 90
            check("split: offered when one session misses the first peak", spOffer && spOne[0].splitA >= Planner.minSession && spOne[0].splitB >= Planner.minSession)
            let spTwo = Planner.propose([wr], energy: en, routines: nil, now: spAt, keepStarted: false, split: Set<String>([wr.id]))
            let s1 = spTwo.first(where: { $0.splitOf == 1 })
            let s2 = spTwo.first(where: { $0.splitOf == 2 })
            var spTwoOk = false
            if let x1 = s1, let x2 = s2, let pk1 = en.peak1, let pk2 = en.peak2 {
                let firstOk: Bool = x1.placed && x1.start >= spAt && x1.start >= pk1.from && x1.start.addingMinutes(x1.minutes) <= pk1.to
                let secondOk: Bool = x2.placed && x2.start >= pk2.from && x2.start.addingMinutes(x2.minutes) <= pk2.to
                spTwoOk = firstOk && secondOk && x1.minutes + x2.minutes == 90
            }
            check("split: two sessions, first wholly on peak 1, second wholly on peak 2", spTwoOk)
            var spEven = false
            if let x1 = s1, let x2 = s2 { spEven = x1.minutes == 45 && x2.minutes == 45 }
            check("split: as even as the peaks allow (45 + 45 here)", spEven)
            let spEarly = Planner.propose([task("Writing", "high", 90)], energy: en, routines: nil, now: en.wake, keepStarted: false, split: nil)
            check("split: not offered when the task fits wholly on the first peak", spEarly[0].placed && !spEarly[0].canSplit)
            let spLate = Planner.propose([task("Writing", "high", 90)], energy: en, routines: nil, now: en.peak1!.to.addingMinutes(10), keepStarted: false, split: nil)
            check("split: not offered once the first peak is over", !spLate[0].canSplit)
            let shortT = Planner.propose([task("Short", "high", 50)], energy: en, routines: nil, now: spAt.addingMinutes(30), keepStarted: false, split: nil)
            check("split: not offered for a task under two sessions' worth (" + String(2 * Planner.minSession) + "m)", !shortT[0].canSplit)
        }

        // ------------------------------------------------------------------ 4b2. the energy counter, matched to you
        print("== 4b2. Energy counter corrections")
        do {
            let cNow = CornerTime.make(2026, 11, 23, 12, 0)!
            let calnone: [Calibration.Entry] = []
            check("no corrections: the counter is the curve", abs(Calibration.map(0.30, calnone, cNow) - 0.30) < 1e-9)
            let calone: [Calibration.Entry] = [Calibration.Entry(at: cNow, model: 0.30, felt: 0.63)]
            check("one correction: 30% read as 63%", abs(Calibration.map(0.30, calone, cNow) - 0.63) < 1e-9)
            check("one correction shifts the rest of the day too", abs(Calibration.map(0.40, calone, cNow) - 0.73) < 1e-9)
            let calspread: [Calibration.Entry] = [
                Calibration.Entry(at: cNow.addingHours(-5), model: 0.30, felt: 0.40),
                Calibration.Entry(at: cNow.addingHours(-3), model: 0.40, felt: 0.60),
                Calibration.Entry(at: cNow.addingHours(-1), model: 0.50, felt: 0.80),
            ]
            check("several apart on the curve: the scale stretches", abs(Calibration.map(0.45, calspread, cNow) - 0.70) < 1e-9)
            let calold: [Calibration.Entry] = [Calibration.Entry(at: cNow.addingDays(-90), model: 0.30, felt: 0.90)]
            check("corrections older than 60 days are left out", abs(Calibration.map(0.30, calold, cNow) - 0.30) < 1e-9)
            check("never above 100%", Calibration.map(0.95, calone, cNow) <= 1)

            // what a correction may lead to: a re-plan, or a high-effort task now
            func zn(_ fromMin: Int, _ toMin: Int) -> Zone {
                return Zone(from: cNow.dateOnly.addingMinutes(fromMin), to: cNow.dateOnly.addingMinutes(toMin), peak: false)
            }
            func zoneDay(_ p1: Zone?, _ dp: Zone?, _ p2: Zone?) -> EnergyDay {
                let d = EnergyDay()
                d.peak1 = p1
                d.dip = dp
                d.peak2 = p2
                return d
            }
            let dayA = zoneDay(zn(540, 690), zn(810, 930), zn(1080, 1260))
            let dayB = zoneDay(zn(545, 695), zn(810, 930), zn(1080, 1260))
            let dayC = zoneDay(zn(540, 720), zn(810, 930), zn(1080, 1260))
            let dayD = zoneDay(zn(540, 690), nil, zn(1080, 1260))
            check("re-plan: a 5-minute shift is not a move", !FeelPlan.zonesMoved(dayA, dayB))
            check("re-plan: the peak ending 30 min later is a move", FeelPlan.zonesMoved(dayA, dayC))
            check("re-plan: a dip that disappears is a move", FeelPlan.zonesMoved(dayA, dayD))
            func tk(_ title: String, _ lvl: String, _ due: Date?) -> TaskItem {
                let t = TaskItem(id: title, title: title, energy: lvl)
                if let d = due { t.setDue(d, hasTime: true) }
                return t
            }
            var hiTasks: [TaskItem] = [tk("Email", "low", nil), tk("Writing", "high", cNow.addingHours(6)), tk("Report", "high", nil)]
            let hiPick: TaskItem? = FeelPlan.highNow(hiTasks, felt: 0.63, shown: 30, now: cNow, inTask: false)
            var hiPickOk = false
            if let picked = hiPick { hiPickOk = picked.title == "Writing" }
            check("energy now: offers the high-effort task timed later today", hiPickOk)
            check("energy now: no offer when you feel only a little above the counter", FeelPlan.highNow(hiTasks, felt: 0.63, shown: 55, now: cNow, inTask: false) == nil)
            check("energy now: no offer when you feel low", FeelPlan.highNow(hiTasks, felt: 0.40, shown: 10, now: cNow, inTask: false) == nil)
            check("energy now: no offer while a task timer runs", FeelPlan.highNow(hiTasks, felt: 0.63, shown: 30, now: cNow, inTask: true) == nil)
            hiTasks.append(tk("Deep read", "high", cNow.addingMinutes(30)))
            check("energy now: no offer when one is coming up within the hour", FeelPlan.highNow(hiTasks, felt: 0.63, shown: 30, now: cNow, inTask: false) == nil)
        }

        // 4c. Updates: not ported. The C# checks here test the Windows self-updater (Update.cs), which has no Swift counterpart.

        // ------------------------------------------------------------------ 4b. completion log (done.csv, days.csv)
        print("== 4b. Completion log")
        do {
            for name in ["done.csv", "days.csv"] {
                let fp = NSString(string: Store.dir).appendingPathComponent(name)
                if FileManager.default.fileExists(atPath: fp) { try? FileManager.default.removeItem(atPath: fp) }
            }
            let d0 = day
            let at = d0.addingHours(10)
            let lgHi = task("Write, the \"big\" one", "high", 60)
            lgHi.planned = true
            lgHi.setDue(d0.addingHours(9.5), hasTime: true)
            lgHi.planned = true
            let med = task("Review", "medium", 30)
            let low = task("Emails", "low", 20)
            let low2 = task("Tidy", "low", 15)
            let lgRt = task("Reading", "medium", 30)
            lgRt.routine = "r1"
            lgRt.routineDay = CornerTime.formatDay(d0)
            let later = task("Next week", "high", 30)
            later.setDue(d0.addingDays(7), hasTime: false)
            TaskLog.done(lgHi, at: at, day: d0, timerMinutes: 52.5, zone: "peak"); lgHi.done = true
            TaskLog.done(med, at: at.addingHours(1), day: d0, timerMinutes: 0, zone: "between"); med.done = true
            TaskLog.done(low, at: at.addingHours(2), day: d0, timerMinutes: 0, zone: "dip"); low.done = true
            TaskLog.undone(low.id, day: d0); low.done = false // unticked again: no longer counts as done
            let rows = TaskLog.readDone()
            let rowsOk = rows.count == 2 && rows[0].title == "Write, the \"big\" one" && rows[0].timerMinutes == 52.5 && rows[0].planned && rows[0].zone == "peak"
            check("done.csv: one row per task that stayed done, title with comma and quotes intact", rowsOk, String(rows.count) + " rows")
            let all = [lgHi, med, low, low2, lgRt, later]
            let sum = TaskLog.summarise(d0, all, rows, sleep: 7.25, debt: 1.5)
            var sumLine = "  " + String(sum.done) + " done of " + String(sum.tasks)
            sumLine += ", high " + String(sum.doneBy[0]) + "/" + String(sum.totalBy[0])
            sumLine += ", routines " + String(sum.routinesDone) + "/" + String(sum.routinesSent)
            sumLine += ", on peak " + String(sum.highDoneOnPeak)
            print(sumLine)
            check("day summary: 2 done of 5 (a task due next week is not counted)", sum.done == 2 && sum.open == 3 && sum.tasks == 5)
            let byOk = sum.doneBy[0] == 1 && sum.totalBy[0] == 1 && sum.totalBy[2] == 2 && sum.routinesSent == 1 && sum.routinesDone == 0 && sum.placedDone == 1 && sum.highDoneOnPeak == 1
            check("day summary: by effort, routines, placed by Plan, high on a peak", byOk)
            TaskLog.writeDay(sum)
            let back = TaskLog.readDays()
            check("days.csv reads back what was written", back.count == 1 && back[0].done == 2 && back[0].tasks == 5 && back[0].totalBy[2] == 2 && back[0].highDoneOnPeak == 1)
        }

        // ------------------------------------------------------------------ 5. sleep-need suggestion
        print("== 5. Sleep-need suggestion (simulated sleepers with a known need; \"run.ps1 sim\" runs 300 of each)")
        let late23 = day.addingHours(23)
        func sleeperResult(_ trueNeed: Double, _ days: Int, _ mean: Double, _ sd: Double, _ perHour: Double, _ noise: Double, _ seed: Int) -> NeedEstimate.Result {
            let sl = sleeper(NetRandom(seed), day, trueNeed, days, mean, sd, perHour, noise, false, perDay: 5)
            return NeedEstimate.build(sl.nights, sl.checks, late23)
        }
        let clear = sleeperResult(7.5, 56, 7.3, 0.9, 1.5, 0.4, 7)
        check("clear case (need 7h30, 56 days): suggests within 15 min, range contains 7h30", clear.ready && abs(clear.need - 7.5) <= 0.25 + 1e-9 && clear.low <= 7.5 && clear.high >= 7.5, clear.note)
        var hit = 0
        var ready5 = 0
        for seed in 1...40 {
            let r = sleeperResult(7.5, 56, 7.3, 0.9, 1.5, 0.4, seed)
            if r.ready {
                ready5 += 1
                if abs(r.need - 7.5) <= 0.5 + 1e-9 { hit += 1 }
            }
        }
        check("40 clear cases: most get a figure, and every figure is within 30 min", ready5 >= 30 && hit == ready5, String(ready5) + " of 40 suggested, " + String(hit) + " within 30 min")
        let few = sleeperResult(7.5, 10, 7.3, 0.9, 1.5, 0.4, 7)
        check("10 days: nothing suggested, and it says how many days it has", !few.ready && few.days == 10 && few.note.contains("10 so far"), few.note)
        let flat = sleeperResult(7.5, 56, 7.3, 0.9, 0.0, 0.8, 7)
        check("scores that do not follow sleep: nothing suggested", !flat.ready, flat.note)
        let under = sleeperResult(9.25, 56, 7.2, 0.6, 1.5, 0.4, 7)
        check("someone who never gets enough: no figure, told they may need more", !under.ready && under.note.contains("may need more"), under.note)
        var lowBall = 0
        for seed in 1...40 {
            let r = sleeperResult(6.25, 56, 7.0, 0.9, 1.5, 0.4, seed)
            if r.ready && r.need < NeedEstimate.floor - 1e-9 { lowBall += 1 }
        }
        check("a true need of 6h15: never suggests under the 7h floor", lowBall == 0)
        do {
            let sl = sleeper(NetRandom(7), day, 7.5, 56, 7.3, 0.9, 1.5, 0.4, false, perDay: 5)
            for x in sl.nights { x.assumed = true }
            check("assumed nights (skipped mornings) are not used", NeedEstimate.build(sl.nights, sl.checks, late23).days == 0)
        }
        let mine2 = NeedEstimate.build(realNights, Feedback.read(), Date())
        print("  your own data: " + mine2.note)

        // ------------------------------------------------------------------ work log, habits and the order by priority (Work.swift)
        print("== Work log, habits, priority")
        let pwPartsDay: Bool = Habits.part(day.addingHours(8)) == 0 && Habits.part(day.addingHours(12.5)) == 1 && Habits.part(day.addingHours(15)) == 2
        let pwPartsNight: Bool = Habits.part(day.addingHours(19)) == 3 && Habits.part(day.addingHours(2)) == 3
        check("parts of the day: 08:00 morning, 12:30 noon, 15:00 afternoon, 19:00 and 02:00 evening", pwPartsDay && pwPartsNight)
        let pwRow = WorkRow(start: day.addingHours(9).addingTimeInterval(5), end: day.addingHours(9.75), minutes: 44.9, id: "a1", title: "Write, \"concept\" 14",
                            effort: "high", kind: "mental", ended: "paused", planned: 45, e0: 48, e1: 61)
        var pwOk = false
        if let pwBack = WorkLog.parse(WorkLog.line(pwRow)) {
            let pwSame: Bool = pwBack.start == pwRow.start && pwBack.end == pwRow.end && pwBack.title == pwRow.title && pwBack.effort == "high"
            let pwNums: Bool = pwBack.e0 == 48 && pwBack.e1 == 61 && pwBack.felt < 0 && pwBack.ended == "paused" && pwBack.planned == 45
            pwOk = pwSame && pwNums
        }
        check("a row of work.csv reads back as written (title with a comma and quotes, energy 48 -> 61, no typed %)", pwOk, WorkLog.line(pwRow))
        if FileManager.default.fileExists(atPath: WorkLog.file) { try? FileManager.default.removeItem(atPath: WorkLog.file) }
        WorkLog.forget()
        WorkLog.add(pwRow)
        WorkLog.forget()
        let pwText: String = (try? String(contentsOfFile: WorkLog.file, encoding: .utf8)) ?? ""
        let pwLines: [String] = pwText.components(separatedBy: "\r\n").filter { !$0.isEmpty }
        let pwRows: [WorkRow] = WorkLog.rows
        check("work.csv: a heading, then one row per stretch", pwLines.count == 2 && pwLines[0].hasSuffix(WorkLog.head) && pwRows.count == 1 && pwRows[0].id == "a1")
        var pwMidnight = false
        if let pwLate = WorkLog.parse("2026-11-20,23:50:00,00:10:00,20,x,Late,low,mental,no,30,40,35,,paused,evening") {
            pwMidnight = pwLate.end == CornerTime.make(2026, 11, 21, 0, 10)!
        }
        check("a stretch across midnight ends the next day", pwMidnight)

        func pwW(_ title: String, _ daysAgo: Int, _ hour: Double, _ mins: Double, _ effort: String) -> WorkRow {
            let st: Date = day.addingDays(-daysAgo).addingHours(hour)
            return WorkRow(start: st, end: st.addingMinutes(mins), minutes: mins, id: title + String(daysAgo), title: title,
                           effort: effort, kind: "mental", ended: "completed")
        }
        let pwLog: [WorkRow] = [
            pwW("Emails", 1, 8, 22, "low"), pwW("Emails", 2, 9, 31, "low"), pwW("emails ", 4, 8.5, 18, "low"), pwW("Emails", 0, 8, 60, "low"),
            pwW("Gym", 1, 9, 50, "high"), pwW("Gym", 3, 9, 50, "high"),
            pwW("Reading", 1, 20, 40, "low"), pwW("Reading", 2, 20, 40, "low"), pwW("Reading", 3, 21, 40, "low"),
            pwW("Old habit", 30, 8, 30, "low"), pwW("Old habit", 31, 8, 30, "low"), pwW("Old habit", 32, 8, 30, "low"),
        ]
        let pwUsual: [Habits.Habit] = Habits.usual(pwLog, 0, day.addingHours(9))
        let pwUsualNames: String = pwUsual.map { h in h.title + " x" + String(h.days) }.joined(separator: ", ")
        check("usual in the morning: only what was done on 3 of the last 28 days (not twice, not a month ago, not in the evening)",
              pwUsual.count == 1 && pwUsual[0].title == "Emails" && pwUsual[0].days == 3, pwUsualNames)
        check("its length is the middle of the real minutes, to the nearest five; today is not counted", pwUsual.count == 1 && pwUsual[0].minutes == 20,
              pwUsual.count == 1 ? String(pwUsual[0].minutes) + " min" : "")
        let pwEvening: [String] = Habits.usual(pwLog, 3, day.addingHours(20)).map { h in h.title }
        check("usual in the evening: Reading", pwEvening == ["Reading"])

        let pwPeak: Date = normal.peak1!.from.addingMinutes(10)
        let pwDip: Date = normal.dip!.from.addingMinutes(10)
        func pwT(_ title: String, _ effort: String?, _ mins: Int) -> TaskItem {
            return TaskItem(id: title, title: title, energy: effort, minutes: mins)
        }
        func pwNames(_ ranked: [Ranked]) -> String {
            return ranked.map { r in r.task.title }.joined(separator: " > ")
        }
        let pwTasks: [TaskItem] = [pwT("Tidy desk", "low", 15), pwT("Review", "medium", 30), pwT("Write concept", "high", 45), pwT("Plain", nil, 0)]
        let pwOnPeak: [Ranked] = Priority.rank(pwTasks, energy: normal, now: pwPeak, level: 0.6, rows: nil, working: nil)
        let pwOnPeakOk: Bool = pwOnPeak[0].task.title == "Write concept" && pwOnPeak[0].why == "fits your peak" && pwOnPeak[pwOnPeak.count - 1].task.title == "Tidy desk"
        check("on a peak: the high-effort task first, the low-effort one last, and it says why", pwOnPeakOk, pwNames(pwOnPeak))
        let pwInDip: [Ranked] = Priority.rank(pwTasks, energy: normal, now: pwDip, level: 0.5, rows: nil, working: nil)
        let pwInDipOk: Bool = pwInDip[0].task.title == "Tidy desk" && pwInDip[0].why == "light work for the dip" && pwInDip[pwInDip.count - 1].task.title == "Write concept"
        check("in the dip: the low-effort task first, the high-effort one last", pwInDipOk, pwNames(pwInDip))
        let pwDue = pwT("Call the bank", "low", 10)
        pwDue.setDue(pwPeak.addingMinutes(-30), hasTime: true)
        let pwLater = pwT("Next week's thing", "high", 45)
        pwLater.setDue(pwPeak.dateOnly.addingDays(3), hasTime: false)
        var pwMore: [TaskItem] = [pwLater, pwDue]
        pwMore.append(contentsOf: pwTasks)
        let pwMixed: [Ranked] = Priority.rank(pwMore, energy: normal, now: pwPeak, level: 0.6, rows: nil, working: nil)
        let pwDueWhy: String = "due at " + EnergyDay.clock(pwPeak.addingMinutes(-30))
        let pwMixedFirst: Bool = pwMixed[0].task === pwDue && pwMixed[0].why == pwDueWhy
        let pwMixedLast: Bool = pwMixed[pwMixed.count - 1].task === pwLater && pwMixed[pwMixed.count - 1].later
        check("a time you set that has passed comes first, with no word of \"overdue\"; a task dated for a later day comes last",
              pwMixedFirst && pwMixedLast, pwNames(pwMixed))
        let pwWorking: [Ranked] = Priority.rank(pwTasks, energy: normal, now: pwPeak, level: 0.6, rows: nil, working: "Tidy desk")
        check("the task being worked on stays first", pwWorking[0].task.title == "Tidy desk")
        let pwPlanned = pwT("Placed by an old schedule", "low", 10)
        pwPlanned.setDue(pwPeak.addingMinutes(-30), hasTime: true)
        pwPlanned.planned = true
        let pwOld: [Ranked] = Priority.rank([pwPlanned, pwTasks[2]], energy: normal, now: pwPeak, level: 0.6, rows: nil, working: nil)
        check("a time an old schedule gave a task does not count as due", pwOld[0].task === pwTasks[2])
        let pwTwo: [TaskItem] = [pwT("Notes", "low", 20), pwT("Emails", "low", 20)]
        let pwHabit: [Ranked] = Priority.rank(pwTwo, energy: normal, now: day.addingHours(9), level: 0.5, rows: pwLog, working: nil)
        check("of two like tasks, the one you usually do at this time of day goes first", pwHabit[0].task.title == "Emails" && pwHabit[0].why == "usual for your morning",
              pwNames(pwHabit) + " (" + (pwHabit[0].why ?? "") + ")")
        let pwGym = pwT("Gym", "high", 45)
        pwGym.kind = "physical"
        let pwAt17: [Ranked] = Priority.rank([pwT("Notes", nil, 20), pwGym], energy: normal, now: day.addingHours(17), level: 0.5, rows: nil, working: nil)
        check("physical work goes first in the late afternoon", pwAt17[0].task === pwGym && pwAt17[0].why == "good time for physical work", pwNames(pwAt17))
        let pwTired: [Ranked] = Priority.rank([pwT("Hard", "high", 30), pwT("Easy", "low", 30)], energy: nil, now: day.addingHours(22), level: 0.2, rows: nil, working: nil)
        check("no curve and a low counter: the low-effort task first", pwTired[0].task.title == "Easy" && pwTired[0].why == "low energy now", pwNames(pwTired))
        let pwAgain: String = pwNames(Priority.rank(pwTasks, energy: normal, now: pwPeak, level: 0.6, rows: nil, working: nil))
        let pwPlain: String = pwNames(Priority.rank([pwT("A", nil, 0), pwT("B", nil, 0), pwT("C", nil, 0)], energy: normal, now: pwPeak, level: 0.6, rows: nil, working: nil))
        check("same input, same order (ties keep the list's order)", pwAgain == pwNames(pwOnPeak) && pwPlain == "A > B > C")

        // ticking a task off counts as doing it, timer or not (Habits.withDone); and the groggy stretch after waking
        func hcD(_ title: String, _ daysAgo: Int, _ hour: Double, _ timer: Double) -> TaskLog.Row {
            let moment: Date = day.addingDays(-daysAgo).addingHours(hour)
            let r = TaskLog.Row()
            r.day = SleepLog.today(moment)
            r.at = r.day.atTimeOfDay(moment.timeOfDay) // (as readDone gives it: Corner's day and the clock time)
            r.id = title + String(daysAgo)
            r.title = title
            r.effort = "medium"
            r.kind = "mental"
            r.minutes = 30
            r.timerMinutes = timer
            return r
        }
        func hcHas(_ list: [Habits.Habit], _ title: String, _ days: Int, _ minutes: Int) -> Bool {
            return list.contains { h in h.title == title && h.days == days && h.minutes == minutes }
        }
        let hcDone: [TaskLog.Row] = [hcD("Voice-over", 1, 10, 0), hcD("Voice-over", 2, 11, 0), hcD("Voice-over", 3, 9.5, 0), hcD("Emails", 1, 8.4, 0), hcD("Gym", 2, 9, 0)]
        let hcLog: [WorkRow] = Habits.withDone(pwLog, hcDone)
        let hcUsual: [Habits.Habit] = Habits.usual(hcLog, 0, day.addingHours(9))
        let hcUsualNames: String = hcUsual.map { h in h.title + " x" + String(h.days) + " " + String(h.minutes) + "m" }.joined(separator: ", ")
        check("ticked off on 3 mornings with no timer: usual for the morning, at the length it was given", hcHas(hcUsual, "Voice-over", 3, 30), hcUsualNames)
        check("a tick on a day the task was also timed is not counted twice (Emails stays at 3 days, 20 min)", hcHas(hcUsual, "Emails", 3, 20) && hcLog.count == pwLog.count + 4,
              String(hcLog.count) + " rows from " + String(pwLog.count) + " + " + String(hcDone.count))
        check("timed on two mornings and ticked on a third: usual, at the length it really took (Gym, 50 min)", hcHas(hcUsual, "Gym", 3, 50))
        let hcOld: [WorkRow] = Habits.withDone([], [hcD("Sketch", 1, 10, 18), hcD("Sketch", 2, 10, 22), hcD("Sketch", 3, 10, 41)])
        let hcOldUsual: [Habits.Habit] = Habits.usual(hcOld, 0, day.addingHours(9))
        check("ticks from before work.csv keep their own timer minutes (18, 22, 41 -> 20)", hcOldUsual.contains { h in h.title == "Sketch" && h.minutes == 20 })
        let hcNight: [WorkRow] = Habits.withDone([], [hcD("Late thing", 1, 25.5, 0)])
        let hcNightAt: Date = hcNight[0].start
        check("a tick at 01:30 stands on the night it happened, in the evening part", hcNightAt == day.addingHours(1.5) && Habits.part(hcNightAt) == 3,
              CornerTime.formatDay(hcNightAt) + "T" + CornerTime.clock24(hcNightAt) + ":00")
        let hcShare: Double = Habits.typeShare(hcLog, 0, day.addingHours(9), effort: "medium", kind: "mental")
        check("ticked-off work counts in the share of the morning by effort", hcShare > 0.3, f(hcShare, 2))

        let hcWake: Date = normal.wake.addingMinutes(20)
        let hcGroggy: [Ranked] = Priority.rank(pwTasks, energy: normal, now: hcWake, level: 0.9, rows: nil, working: nil)
        let hcGroggyOk: Bool = hcGroggy[0].task.title == "Tidy desk" && hcGroggy[0].why == "easy start while you wake up" && hcGroggy[hcGroggy.count - 1].task.title == "Write concept"
        check("still waking up (" + c(hcWake) + ", groggy until " + c(normal.groggyUntil) + "): the low-effort task first, the high-effort one last, even with a high counter",
              hcGroggyOk, pwNames(hcGroggy))
        let hcAfter: [Ranked] = Priority.rank(pwTasks, energy: normal, now: normal.groggyUntil.addingMinutes(10), level: 0.9, rows: nil, working: nil)
        check("once the groggy stretch is over: the high-effort task first again", hcAfter[0].task.title == "Write concept", pwNames(hcAfter))
        let hcPeakEnd: Date = normal.peak1!.to
        let hcEnd: [Ranked] = Priority.rank([pwT("Long write", "high", 90), pwT("Tidy desk", "low", 15)], energy: normal, now: hcPeakEnd.addingMinutes(-5), level: 0.6, rows: nil, working: nil)
        check("a 90-minute high task 5 minutes before the peak ends: still first, and the reason says when the peak ends",
              hcEnd[0].task.title == "Long write" && hcEnd[0].why == "your peak lasts until " + EnergyDay.clock(hcPeakEnd), hcEnd[0].why ?? "")

        // updates (the checks of Update.cs, for the Mac zip)
        check("update: v9.0.0 is newer than 1.1.2", UpdateInfo.isNewer("v9.0.0", than: "1.1.2"))
        check("update: a build's own tag is not newer", !UpdateInfo.isNewer("v1.1.2", than: "1.1.2"))
        check("update: v1.0.0 is not newer; 1.1.10 is newer than 1.1.9", !UpdateInfo.isNewer("v1.0.0", than: "1.1.2") && UpdateInfo.isNewer("v1.1.10", than: "1.1.9"))
        check("update: a tag that is not a version is ignored", !UpdateInfo.isNewer("nightly", than: "1.1.2") && !UpdateInfo.isNewer(nil, than: "1.1.2") && !UpdateInfo.isNewer("v9.x.0", than: "1.1.2"))
        check("update: version as a number", UpdateInfo.number("1.2.3") == 10203 && UpdateInfo.number("v1.1") == 10100)
        let upJson: String = "{\"tag_name\": \"v1.2.0\", \"assets\": [{\"browser_download_url\": \"https://x/v1.2.0/Corner-Windows.zip\"}, {\"browser_download_url\": \"https://x/v1.2.0/Corner-Mac-Swift.zip\"}]}"
        let up = UpdateInfo.parseRelease(upJson)
        check("update: reads the tag and the Mac zip from a release", up?.tag == "v1.2.0" && up?.zip == "https://x/v1.2.0/Corner-Mac-Swift.zip")
        check("update: a release without the Mac zip is skipped", UpdateInfo.parseRelease("{\"tag_name\": \"v2.0.0\", \"assets\": []}") == nil)

        print(fails == 0 ? "ALL PASS" : String(fails) + " FAILED")
    }
}

/// Two-digit zero padding for the sweep names (C#: ToString("00")).
enum CornerTimePad {
    static func two(_ v: Int) -> String { return v < 10 ? "0" + String(v) : String(v) }
}

Program.main()
exit(Program.fails == 0 ? 0 : 1)
