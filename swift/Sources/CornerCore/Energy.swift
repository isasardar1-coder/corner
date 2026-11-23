// Port of Corner.Core/src/Energy.cs: the alertness model (EnergyModel, EnergyDay, Zone) and the sleep log
// (SleepSpan, Night, SleepData, SleepLog; sleep.json beside tasks.json).
import Foundation

// ---------------------------------------------------------------- the model

/// Two-process model of alertness (Borbely): sleep pressure S that builds while awake and
/// clears during sleep, plus a circadian rhythm C with a 24 h and a 12 h component.
/// The 12 h component is what produces the post-lunch dip. Everything tunable is here.
public enum EnergyModel {
    // --- sleep need and debt
    public static let defaultNeed: Double = 8.0      // hours of sleep needed per night (overridden by "SleepNeed" in settings.json)
    public static let debtNights: Int = 14           // how many nights count towards sleep debt
    public static let debtEstimateMin: Int = 3       // nights logged in that window before the unlogged ones are counted as an average night
    public static let debtCap: Double = 12.0         // debt (hours) at which its effects stop growing
    public static let debtDrop: Double = 0.10        // how far full debt lowers the whole curve
    public static let debtDipGain: Double = 0.40     // how much full debt strengthens the 12 h rhythm (deepens the dip)
    public static let debtPressure: Double = 0.10    // extra sleep pressure at wake under full debt

    // --- process S: homeostatic sleep pressure, 0 (fully rested) to 1
    public static let riseTau: Double = 18.2         // hours; how fast pressure builds while awake
    public static let decayTau: Double = 4.2         // hours; how fast pressure clears while asleep
    public static let bedPressure: Double = 0.75     // assumed pressure when falling asleep
    public static let restedPressure: Double = 0.11  // pressure after a full night; the curve is not penalised below this
    public static let maxWakePressure: Double = 0.50 // cap on pressure at wake after a very short night
    public static let pressureWeight: Double = 0.55  // how strongly pressure pulls energy down

    // --- process C: circadian rhythm (clock hours, on a morning when you wake at your usual time)
    public static let base: Double = 0.62            // resting level of the curve
    public static let amp24: Double = 0.12           // size of the 24 h rhythm
    public static let peak24: Double = 17.0          // clock hour where the 24 h rhythm peaks
    public static let amp12: Double = 0.14           // size of the 12 h rhythm
    public static let peak12: Double = 8.75          // clock hour of its morning peak; with the other terms the dip bottoms out near 14:30
    public static let wakeShift: Double = 0.5        // waking an hour later than usual moves the rhythm (and the dip) this many hours later
    public static let shiftMax: Double = 2.0         // largest shift allowed, hours

    // --- sleep inertia (morning grogginess)
    public static let inertiaDepth: Double = 0.25    // how far energy is held down right at wake
    public static let inertiaTau: Double = 0.6       // hours; how fast grogginess clears
    public static let groggyDone: Double = 0.04      // grogginess counts as over below this

    // --- zones
    public static let peakBand: Double = 0.40        // a peak zone covers the top 40% of the peak-to-dip range
    public static let dipBand: Double = 0.10         // the dip zone covers the bottom 10% of it (about 1.5-2 h)
    public static let minProminence: Double = 0.03   // a dip shallower than this is ignored
    public static let minDay: Double = 10            // plausible hours awake; outside this, bedtime = wake + 24 - need
    public static let maxDay: Double = 20

    // --- naps
    public static let napShortMax: Double = 30          // minutes; a nap longer than this leaves a heavier groggy stretch
    public static let napInertiaShort: Double = 0.06    // how far energy is held down right after a short nap
    public static let napInertiaTauShort: Double = 0.15 // hours; how fast that clears
    public static let napInertiaLong: Double = 0.16     // the same after a long nap
    public static let napInertiaTauLong: Double = 0.40  // hours
    public static let napLift: Double = 0.06            // lift after a nap, on top of the sleep pressure it cleared
    public static let napLiftFull: Double = 20          // minutes of nap that give the full lift
    public static let napLiftTau: Double = 2.5          // hours; how long the lift lasts
    public static let lateNapHour: Double = 16.0        // nap time after this clock hour pushes tonight's wind-down later
    public static let lateNapFactor: Double = 1.5       // minutes later per minute napped after that hour
    public static let lateNapMax: Double = 90           // the most it can push, minutes

    /// Sleep pressure at the final wake-up: it clears during every sleep period and builds again in every waking gap.
    static func wakePressure(_ night: Night) -> Double {
        var s = bedPressure
        let ps = night.sleep
        for i in 0..<ps.count {
            s *= exp(-ps[i].hours / decayTau)
            if i + 1 < ps.count {
                let gapHours = ps[i + 1].from.timeIntervalSince(ps[i].to) / 3600
                s = 1 - (1 - s) * exp(-gapHours / riseTau)
            }
        }
        return s
    }

    static func share(_ hours: Double) -> Double {
        return min(max(0, hours), debtCap) / debtCap // debt as a part of DebtCap, 0 to 1
    }

    /// The day's curve from the sleep log. Nil when no valid night ends on or before `day`.
    /// `correction` (from Tuning, by hours since waking) is read at each peak and at the dip, and eased between them (reshape).
    public static func build(_ nights: [Night]?, need: Double, day dayIn: Date, correction: ((Double) -> Double)? = nil) -> EnergyDay? {
        let day = dayIn.dateOnly
        let candidates = (nights ?? []).filter { $0.valid && $0.wokeAt.dateOnly <= day }
        let valid = stableSorted(candidates) { $0.wokeAt < $1.wokeAt }
        if valid.isEmpty { return nil }
        let last = valid[valid.count - 1]
        let cutoff = day.addingDays(-debtNights)
        let recent = valid.filter { $0.wokeAt.dateOnly > cutoff }

        var shortfall = 0.0 // every sleep period and nap of the day counts
        for x in recent { shortfall += need - x.totalHours }
        var loggedDays = Set<Date>()
        for x in recent { loggedDays.insert(x.wokeAt.dateOnly) }
        let logged = loggedDays.count

        // The rhythm is anchored to your usual wake time (the median of the earlier nights), so on a normal
        // night the dip sits near 14:30. Waking later or earlier than usual moves it part of the way.
        let earlier = recent.filter { $0 !== last }.map { $0.wokeAt.timeOfDay / 3600 }.sorted()
        let lastWakeHour = last.wokeAt.timeOfDay / 3600
        let usualWake = earlier.count > 0 ? earlier[earlier.count / 2] : lastWakeHour
        let shift = max(-shiftMax, min(shiftMax, (lastWakeHour - usualWake) * wakeShift))

        let e = EnergyDay()
        e.last = last
        e.need = need
        e.debtLogged = logged
        e.wake = day.atTimeOfDay(last.wokeAt.timeOfDay)
        e.bed = day.atTimeOfDay(last.asleepAt.timeOfDay)
        if e.bed <= e.wake { e.bed = e.bed.addingDays(1) }
        var span = e.bed.timeIntervalSince(e.wake) / 3600
        if span < minDay || span > maxDay { e.bed = e.wake.addingHours(24 - need); span = 24 - need }

        // today's naps (only when last night's record is today's)
        if last.wokeAt.dateOnly == day {
            let wake = e.wake
            e.naps = last.napSpans.filter { $0.to > wake }
        }
        // only the part of a nap that falls after LateNapHour counts, so a nap that just crosses it barely moves bedtime
        let lateFrom = day.addingHours(lateNapHour)
        var lateMinutes = 0.0
        for x in e.naps where x.to.dateOnly == day && x.to > lateFrom {
            let start = x.from > lateFrom ? x.from : lateFrom
            lateMinutes += x.to.timeIntervalSince(start) / 60
        }
        e.bedPush = min(lateNapMax, lateMinutes * lateNapFactor)
        if e.bedPush > 0 { e.bed = e.bed.addingMinutes(e.bedPush); span += e.bedPush / 60 }

        // Debt as it stood when you woke, before today's naps. A nap pays its minutes back from the moment it ends,
        // so logging one changes the rest of the day and never the hours before it.
        var napHours = 0.0
        for x in e.naps { napHours += x.hours }
        var beforeNaps = shortfall + napHours
        // Nights in the window that were never logged are counted as an average logged night (today's naps left out of
        // the average, so a nap pays back its own minutes and no more). With too few nights logged there is no average to trust.
        if logged >= debtEstimateMin && logged < debtNights {
            e.debtEstimate = Double(debtNights - logged) * beforeNaps / Double(logged)
        }
        beforeNaps += e.debtEstimate
        e.debtHours = max(0, beforeNaps - napHours)
        let d = share(beforeNaps)

        let s0 = min(maxWakePressure, wakePressure(last) + debtPressure * d)
        let wakeHour = e.wake.timeOfDay / 3600
        let dt = Double(EnergyDay.stepMin) / 60.0
        let n = Int(span * 60 / Double(EnergyDay.stepMin)) + 1
        e.samples = [Double](repeating: 0, count: n)
        e.raw = [Double](repeating: 0, count: n)
        let nAll = max(n, 24 * 60 / EnergyDay.stepMin + 1)
        e.all = [Double](repeating: 0, count: nAll) // the same curve carried on past bedtime to the next wake-up, for the chart only
        var plain = [Double](repeating: 0, count: n) // the same day without any nap: the peak and dip zones are read from this
        var s = s0
        var sPlain = s0
        let naps = e.naps
        for i in 0..<nAll {
            let t = Double(i) * dt
            let h = wakeHour + t
            let at = e.timeAt(i)
            if i > 0 {
                // pressure builds while awake and clears during a nap
                sPlain = 1 - (1 - sPlain) * exp(-dt / riseTau)
                var inNap = false
                for x in naps where at > x.from && at <= x.to { inNap = true; break }
                s = inNap ? s * exp(-dt / decayTau) : 1 - (1 - s) * exp(-dt / riseTau)
            }
            let c24: Double = amp24 * cos(2 * Double.pi * (h - peak24 - shift) / 24)
            let cos12: Double = cos(2 * Double.pi * (h - peak12 - shift) / 12)
            let inertia: Double = inertiaDepth * exp(-t / inertiaTau)
            func rhythmAt(_ dd: Double) -> Double {
                let r12: Double = amp12 * (1 + debtDipGain * dd) * cos12
                return base + c24 + r12 - inertia - debtDrop * dd
            }
            let rhythmPlain = rhythmAt(d)
            var rhythm = rhythmPlain
            if !naps.isEmpty {
                var paid = 0.0
                for x in naps where x.to <= at { paid += x.hours }
                rhythm = rhythmAt(share(beforeNaps - paid))
            }
            var nap = 0.0
            for x in naps {
                // after a nap: a short groggy stretch, then a lift that fades over a few hours
                if at < x.to { continue }
                let since = at.timeIntervalSince(x.to) / 3600
                let longNap = x.minutes > napShortMax
                let lift: Double = napLift * min(1, x.minutes / napLiftFull) * exp(-since / napLiftTau)
                let groggy: Double = (longNap ? napInertiaLong : napInertiaShort) * exp(-since / (longNap ? napInertiaTauLong : napInertiaTauShort))
                nap += lift - groggy
            }
            let raw: Double = max(0, min(1, rhythm - pressureWeight * (s - restedPressure) + nap))
            e.all[i] = raw
            if i >= n { continue }
            plain[i] = max(0, min(1, rhythmPlain - pressureWeight * (sPlain - restedPressure)))
            e.raw[i] = raw
        }
        // What the check-ins taught (Checks.swift) is added as one amount for each peak and for the dip, eased from one
        // to the next. So a hump is lifted or lowered whole: it keeps its place and its even shape, and no check-in
        // can put a bump of its own on a slope.
        let fit: [Double] = reshape(plain, correction, dt)
        for i in 0..<nAll {
            let f: Double = fit[min(i, n - 1)] // past bedtime: as at bedtime
            e.all[i] = max(0, min(1, e.all[i] + f))
            if i >= n { continue }
            plain[i] = max(0, min(1, plain[i] + f))
            e.samples[i] = e.all[i]
        }
        e.groggyUntil = e.wake.addingHours(-inertiaTau * log(groggyDone / inertiaDepth))
        findZones(e, plain)
        return e
    }

    /// The correction for every sample of the day: the learned amount at the start of the day, at each peak, at the
    /// dip and at bedtime, eased between them (level at each, so the peaks and the dip stay where they are).
    static func reshape(_ plain: [Double], _ correction: ((Double) -> Double)?, _ dt: Double) -> [Double] {
        let n = plain.count
        var fit = [Double](repeating: 0, count: n)
        guard let corr = correction else { return fit }
        if n < 3 { return fit }
        let ex = extremes(plain)
        var at: [Int] = [0]
        let marks: [Int] = ex.dip >= 0 ? [ex.m1, ex.dip, ex.m2, n - 1] : [ex.m1, n - 1]
        for k in marks {
            if k > at[at.count - 1] { at.append(k) }
        }
        var amount: [Double] = []
        for k in at { amount.append(corr(Double(k) * dt)) }
        var j = 0
        while j + 1 < at.count {
            let a0: Int = at[j]
            let a1: Int = at[j + 1]
            let width = Double(a1 - a0)
            for i in a0...a1 {
                let x: Double = Double(i - a0) / width
                let ease: Double = (1 - cos(Double.pi * x)) / 2
                fit[i] = amount[j] + (amount[j + 1] - amount[j]) * ease
            }
            j += 1
        }
        return fit
    }

    /// The dip (-1 when there is none worth the name) and the highest point on each side of it; with no dip, m1 is the top.
    static func extremes(_ s: [Double]) -> (m1: Int, dip: Int, m2: Int) {
        let n = s.count
        if n < 3 { return (0, -1, -1) } // (the callers never pass fewer than three samples)
        // The dip is the low point that stands out most: the one lying deepest below the highest point on each side of it.
        // The peaks are those two highest points. (Taking "the first bump" as the morning peak is not safe: a curve
        // can have small bumps of its own, and the real dip was then lost.)
        var before = [Double](repeating: 0, count: n)
        var after = [Double](repeating: 0, count: n)
        before[0] = s[0]
        for i in 1..<n { before[i] = max(before[i - 1], s[i]) }
        after[n - 1] = s[n - 1]
        for i in stride(from: n - 2, through: 0, by: -1) { after[i] = max(after[i + 1], s[i]) }
        var dip = -1
        var m1 = 0
        var m2 = -1
        var depth = 0.0
        for i in 1..<(n - 1) {
            let p = min(before[i], after[i]) - s[i]
            if p > depth { depth = p; dip = i }
        }
        if depth < minProminence { dip = -1 }
        if dip >= 0 {
            for i in 0...dip { if s[i] > s[m1] { m1 = i } }
            m2 = dip
            for i in dip..<n { if s[i] > s[m2] { m2 = i } }
        } else {
            for i in 0..<n { if s[i] > s[m1] { m1 = i } }
        }
        return (m1, dip, m2)
    }

    static func findZones(_ e: EnergyDay, _ s: [Double]) {
        let n = s.count
        if n < 3 { return }
        let ex = extremes(s)
        let dip: Int = ex.dip
        let m1: Int = ex.m1
        let m2: Int = ex.m2
        if dip < 0 {
            // one hump only: a single peak zone measured against the end of the day
            let top = m1
            e.peak1 = run(e, s, top, s[top] - peakBand * (s[top] - s[n - 1]), 0, n - 1, true, true)
            return
        }
        let p1 = run(e, s, m1, s[m1] - peakBand * (s[m1] - s[dip]), 0, dip, true, true)
        if p1.from < e.groggyUntil && e.groggyUntil < p1.to { p1.from = e.groggyUntil } // not a peak while still groggy
        e.peak1 = p1
        e.peak2 = run(e, s, m2, s[m2] - peakBand * (s[m2] - s[dip]), dip, n - 1, true, true)
        e.dip = run(e, s, dip, s[dip] + dipBand * (min(s[m1], s[m2]) - s[dip]), m1, m2, false, false)
        // the moment the curve has fallen half way from the top of the first peak to the bottom of the dip
        let half = (s[m1] + s[dip]) / 2
        for i in m1...dip {
            if s[i] <= half { e.halfDown = e.timeAt(i); break }
        }
    }

    /// Grows a zone outwards from index c while the curve stays above (or below) the threshold.
    static func run(_ e: EnergyDay, _ s: [Double], _ c: Int, _ thr: Double, _ lo: Int, _ hi: Int, _ above: Bool, _ peak: Bool) -> Zone {
        var a = c
        var b = c
        while a > lo && (above ? s[a - 1] >= thr : s[a - 1] <= thr) { a -= 1 }
        while b < hi && (above ? s[b + 1] >= thr : s[b + 1] <= thr) { b += 1 }
        return Zone(from: e.timeAt(a), to: e.timeAt(b), peak: peak)
    }

    /// Test helper: the same night repeated, to see what the model does with it. Times are seconds since midnight.
    public static func sample(asleep: TimeInterval, woke: TimeInterval, nights: Int, need: Double, day: Date) -> EnergyDay? {
        var list: [Night] = []
        for i in 0..<nights {
            if let n = SleepLog.build(asleep: asleep, woke: woke, day: day.dateOnly.addingDays(-i)) { list.append(n) }
        }
        return build(list, need: need, day: day)
    }
}

public final class Zone {
    public var from: Date
    public var to: Date
    public var peak: Bool

    public init(from: Date, to: Date, peak: Bool) {
        self.from = from
        self.to = to
        self.peak = peak
    }
}

public final class EnergyDay {
    public static let stepMin = 5
    public var wake: Date = Date.distantPast
    public var bed: Date = Date.distantPast
    public var groggyUntil: Date = Date.distantPast
    public var debtHours: Double = 0
    public var need: Double = 0
    public var debtLogged: Int = 0      // nights logged in the debt window
    public var debtEstimate: Double = 0 // hours of debtHours that stand for the nights not logged (0 when none are estimated)
    public var last: Night? = nil
    public var samples: [Double] = []
    public var raw: [Double] = []       // the same curve before any tuning from check-ins
    public var all: [Double] = []       // samples carried on to 24 hours after waking; drawn on the chart
    public var halfDown: Date? = nil    // half way down from the first peak to the dip: time to leave high-effort work
    public var peak1: Zone? = nil
    public var dip: Zone? = nil
    public var peak2: Zone? = nil
    public var naps: [SleepSpan] = []   // today's logged naps
    public var bedPush: Double = 0      // minutes a late nap pushes tonight's wind-down

    public init() {}

    public var nextWake: Date { return wake.addingMinutes((all.count - 1) * EnergyDay.stepMin) }

    public func allAt(_ t: Date) -> Double {
        let x = t.timeIntervalSince(wake) / 60 / Double(EnergyDay.stepMin)
        if x <= 0 { return all[0] }
        if x >= Double(all.count - 1) { return all[all.count - 1] }
        let i = Int(x)
        return all[i] + (all[i + 1] - all[i]) * (x - Double(i))
    }

    public func timeAt(_ i: Int) -> Date { return wake.addingMinutes(i * EnergyDay.stepMin) }

    public func at(_ t: Date) -> Double {
        let x = t.timeIntervalSince(wake) / 60 / Double(EnergyDay.stepMin)
        if x <= 0 { return samples[0] }
        if x >= Double(samples.count - 1) { return samples[samples.count - 1] }
        let i = Int(x)
        return samples[i] + (samples[i + 1] - samples[i]) * (x - Double(i))
    }

    /// The untuned model's value at a moment, for the check-in log.
    public func rawAt(_ t: Date) -> Double {
        let x = NetFormat.round(t.timeIntervalSince(wake) / 60 / Double(EnergyDay.stepMin))
        let i = Int(max(0, min(Double(raw.count - 1), x)))
        return raw[i]
    }

    /// am/pm or 24-hour display. Starts from the system setting; "Clock12" in settings.json overrides it (UI side).
    public static var use12: Bool = CornerTime.uses12HourClock

    public static func clock(_ t: Date) -> String {
        if use12 {
            var h = t.hour % 12
            if h == 0 { h = 12 }
            return String(h) + ":" + CornerTime.pad(t.minute, 2) + (t.hour < 12 ? "am" : "pm")
        }
        return CornerTime.clock24(t)
    }

    /// Short label for a whole hour on the chart axis: "14" or "2pm".
    public static func hourLabel(_ t: Date) -> String {
        if use12 {
            var h = t.hour % 12
            if h == 0 { h = 12 }
            return String(h) + (t.hour < 12 ? "am" : "pm")
        }
        return String(t.hour)
    }

    static func span(_ z: Zone) -> String { return clock(z.from) + "–" + clock(z.to) }

    /// One line for right now, e.g. "Peak until 11:40 · dip 14:10–15:30".
    public func summary(_ now: Date) -> String {
        let note = bedPush >= 5 ? " · late nap: wind-down about " + String(Int(NetFormat.round(bedPush))) + " min later" : ""
        return zoneLine(now) + note
    }

    /// The short form, without the late-nap note (used beside the tabs, where there is little room).
    public func zoneLine(_ now: Date) -> String {
        guard let p1 = peak1 else { return "Bed around " + EnergyDay.clock(bed) }
        if now < p1.to {
            let head = now < p1.from ? "Peak from " + EnergyDay.clock(p1.from) : "Peak until " + EnergyDay.clock(p1.to)
            if let d = dip { return head + " · dip " + EnergyDay.span(d) }
            return head
        }
        if let d = dip, now < d.from {
            if let p2 = peak2 { return "Dip " + EnergyDay.span(d) + " · peak " + EnergyDay.span(p2) }
            return "Dip " + EnergyDay.span(d)
        }
        if let d = dip, now < d.to {
            if let p2 = peak2 { return "Dip until " + EnergyDay.clock(d.to) + " · next peak " + EnergyDay.clock(p2.from) }
            return "Dip until " + EnergyDay.clock(d.to)
        }
        if let p2 = peak2, now < p2.from { return "Second peak " + EnergyDay.span(p2) }
        if let p2 = peak2, now < p2.to { return "Second peak until " + EnergyDay.clock(p2.to) }
        return now < bed ? "Winding down · bed around " + EnergyDay.clock(bed) : "Past bedtime"
    }
}

// ---------------------------------------------------------------- sleep log (sleep.json, beside tasks.json)

/// One stretch of sleep: a part of the night, or a nap.
public final class SleepSpan: Codable {
    public var asleep: String
    public var woke: String

    public init(asleep: String, woke: String) {
        self.asleep = asleep
        self.woke = woke
    }

    enum CodingKeys: String, CodingKey { case Asleep, Woke }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        asleep = try c.decodeIfPresent(String.self, forKey: .Asleep) ?? ""
        woke = try c.decodeIfPresent(String.self, forKey: .Woke) ?? ""
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(asleep, forKey: .Asleep)
        try c.encode(woke, forKey: .Woke)
    }

    public var from: Date { return Night.p(asleep) }
    public var to: Date { return Night.p(woke) }
    public var hours: Double { return to.timeIntervalSince(from) / 3600 }
    public var minutes: Double { return to.timeIntervalSince(from) / 60 }
    public var valid: Bool { return from > Date.distantPast && to > from }

    public static func make(_ from: Date, _ to: Date) -> SleepSpan {
        return SleepSpan(asleep: CornerTime.format(from), woke: CornerTime.format(to))
    }
}

public final class Night: Codable {
    public var asleep: String          // start of the first sleep period
    public var woke: String            // the final wake-up of the main sleep
    public var assumed: Bool           // true when the check-in was skipped and the previous night was reused
    // Both left out of the file when not used, so a night with one period is written exactly as before.
    public var periods: [SleepSpan]?   // every period, only when the night was split
    public var naps: [SleepSpan]?      // naps taken during the day this night leads into

    public init(asleep: String, woke: String, assumed: Bool = false, periods: [SleepSpan]? = nil, naps: [SleepSpan]? = nil) {
        self.asleep = asleep
        self.woke = woke
        self.assumed = assumed
        self.periods = periods
        self.naps = naps
    }

    enum CodingKeys: String, CodingKey { case Asleep, Woke, Assumed, Periods, Naps }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        asleep = try c.decodeIfPresent(String.self, forKey: .Asleep) ?? ""
        woke = try c.decodeIfPresent(String.self, forKey: .Woke) ?? ""
        assumed = try c.decodeIfPresent(Bool.self, forKey: .Assumed) ?? false
        if let ps = try c.decodeIfPresent([SleepSpan?].self, forKey: .Periods) { periods = ps.compactMap { $0 } } else { periods = nil }
        if let ns = try c.decodeIfPresent([SleepSpan?].self, forKey: .Naps) { naps = ns.compactMap { $0 } } else { naps = nil }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(asleep, forKey: .Asleep)
        try c.encode(assumed, forKey: .Assumed)
        if let ps = periods { try c.encode(ps, forKey: .Periods) }
        if let ns = naps { try c.encode(ns, forKey: .Naps) }
        try c.encode(woke, forKey: .Woke)
    }

    public static let fmt = "yyyy-MM-ddTHH:mm"

    /// Parses a stored time; Date.distantPast (DateTime.MinValue) when it is missing or malformed.
    public static func p(_ s: String?) -> Date {
        return CornerTime.parse(s ?? "") ?? Date.distantPast
    }

    public var asleepAt: Date { return Night.p(asleep) }
    public var wokeAt: Date { return Night.p(woke) }

    /// The periods of the main sleep, in order. One period unless the night was split.
    public var sleep: [SleepSpan] {
        if let ps0 = periods, ps0.count > 1, ps0.allSatisfy({ $0.valid }) {
            let ps = stableSorted(ps0) { $0.from < $1.from }
            var ordered = true
            for i in 1..<ps.count { if ps[i].from < ps[i - 1].to { ordered = false } }
            if ordered && ps[0].from >= asleepAt && ps[ps.count - 1].to <= wokeAt { return ps }
        }
        return [SleepSpan.make(asleepAt, wokeAt)]
    }

    /// The day's naps in order. Naps that overlap (the same one entered twice) are joined, so no minute is counted twice.
    public var napSpans: [SleepSpan] {
        var list: [SleepSpan] = []
        let src = stableSorted((naps ?? []).filter { $0.valid }) { $0.from < $1.from }
        for x in src {
            if let prev = list.last {
                if x.from > prev.to { list.append(x) }
                else if x.to > prev.to { list[list.count - 1] = SleepSpan.make(prev.from, x.to) }
            } else {
                list.append(x)
            }
        }
        return list
    }

    /// Main sleep, waking gaps left out.
    public var hours: Double {
        var sum = 0.0
        for p in sleep { sum += p.hours }
        return sum
    }

    public var napHours: Double {
        var sum = 0.0
        for p in napSpans { sum += p.hours }
        return sum
    }

    public var totalHours: Double { return hours + napHours }

    public var valid: Bool {
        let a = asleepAt
        let w = wokeAt
        return a > Date.distantPast && w > Date.distantPast && hours >= 1 && w.timeIntervalSince(a) / 3600 <= 16
    }

    public static func make(_ asleep: Date, _ woke: Date, assumed: Bool) -> Night {
        return Night(asleep: CornerTime.format(asleep), woke: CornerTime.format(woke), assumed: assumed)
    }
}

public final class SleepData: Codable {
    public var nights: [Night]
    public var askedOn: String? // the day the check-in was last answered or skipped

    public init(nights: [Night] = [], askedOn: String? = nil) {
        self.nights = nights
        self.askedOn = askedOn
    }

    enum CodingKeys: String, CodingKey { case Nights, AskedOn }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let list = try c.decodeIfPresent([Night?].self, forKey: .Nights) ?? []
        nights = list.compactMap { $0 }
        askedOn = try c.decodeIfPresent(String.self, forKey: .AskedOn)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try encodeOrNull(&c, askedOn, .AskedOn)
        try c.encode(nights, forKey: .Nights)
    }
}

public enum SleepLog {
    public static let keep = 400       // nights kept
    public static let dayStartsAt = 4  // 04:00: before this, "today" is still yesterday

    public static var data = SleepData()
    static var file: String { return Store.path("sleep.json") }

    /// The day as Corner counts it: before 04:00 it is still yesterday.
    public static func today(_ now: Date) -> Date { return now.addingHours(-dayStartsAt).dateOnly }

    public static func load() {
        if !FileManager.default.fileExists(atPath: file) { return }
        do {
            let bytes = try Data(contentsOf: URL(fileURLWithPath: file))
            let d = try JSONDecoder().decode(SleepData.self, from: bytes)
            d.nights.removeAll { !$0.valid }
            data = d
        } catch {
            Store.log(error)
        }
    }

    static func save() {
        do {
            let bak = Store.path("sleep.before-split.json") // one-off copy before the first write by the split-sleep version
            if FileManager.default.fileExists(atPath: file) && !FileManager.default.fileExists(atPath: bak) {
                try FileManager.default.copyItem(atPath: file, toPath: bak)
            }
            try Store.writeAtomically(try Store.encodeJSON(data), file)
        } catch {
            Store.log(error)
        }
    }

    public static func forDay(_ day: Date) -> Night? {
        let d = day.dateOnly
        return data.nights.last { $0.wokeAt.dateOnly == d }
    }

    public static var latest: Night? {
        return stableSorted(data.nights) { $0.wokeAt < $1.wokeAt }.last
    }

    public static func needsCheckIn(_ now: Date) -> Bool {
        let day = today(now)
        return forDay(day) == nil && data.askedOn != CornerTime.formatDay(day)
    }

    /// A night that ends on `day` at clock time `woke`, having started at clock time `asleep` (seconds since midnight). Nil if implausible.
    public static func build(asleep: TimeInterval, woke: TimeInterval, day: Date) -> Night? {
        let w = day.dateOnly.atTimeOfDay(woke)
        var a = day.dateOnly.atTimeOfDay(asleep)
        if a >= w { a = a.addingDays(-1) }
        let n = Night.make(a, w, assumed: false)
        return n.valid ? n : nil
    }

    /// A night in one to three periods, given as clock times in order; the last one ends on `day`. Nil if implausible.
    public static func buildSplit(asleep: [TimeInterval], woke: [TimeInterval], day: Date) -> Night? {
        let n = asleep.count
        if n == 0 || woke.count != n { return nil }
        var spans: [SleepSpan] = []
        var next = Date.distantFuture // start of the period that follows
        for i in stride(from: n - 1, through: 0, by: -1) {
            var w = day.dateOnly.atTimeOfDay(woke[i])
            while w > next { w = w.addingDays(-1) }
            var a = w.dateOnly.atTimeOfDay(asleep[i])
            if a >= w { a = a.addingDays(-1) }
            if w.timeIntervalSince(a) / 3600 > 16 { return nil }
            spans.insert(SleepSpan.make(a, w), at: 0)
            next = a
        }
        let night = Night.make(spans[0].from, spans[n - 1].to, assumed: false)
        if n > 1 { night.periods = spans }
        return night.valid && night.sleep.count == n ? night : nil
    }

    /// Adds a nap to the given day (creating that day's night from the most recent one if it was never entered).
    @discardableResult
    public static func addNap(_ day: Date, from: Date, to: Date) -> Bool {
        if forDay(day) == nil { skip(day) }
        guard let night = forDay(day), to > from else { return false }
        if from < night.wokeAt { return false }                                  // inside the night's sleep: already counted there
        if night.napSpans.contains(where: { from < $0.to && $0.from < to }) { return false } // overlaps a nap already logged
        if night.naps == nil { night.naps = [] }
        night.naps?.append(SleepSpan.make(from, to))
        night.naps = night.napSpans
        save()
        return true
    }

    public static func removeNap(_ day: Date, _ nap: SleepSpan) {
        guard let night = forDay(day), let list = night.naps else { return }
        let napFrom = nap.from
        let napTo = nap.to
        let kept = list.filter { x in !(!x.valid || (x.from < napTo && napFrom < x.to)) } // everything inside the nap shown goes
        night.naps = kept.isEmpty ? nil : kept
        save()
    }

    // IMPORT HOOK: a future import (phone, watch, a CSV) only needs to turn each night into
    // Night.make(asleep, woke, assumed: false) (or buildSplit for a night in parts) and pass it to record.
    public static func record(_ n: Night) {
        let day = n.wokeAt.dateOnly
        if let old = forDay(day), n.naps == nil { n.naps = old.naps } // correcting the night keeps the day's naps
        data.nights.removeAll { $0.wokeAt.dateOnly == day }
        data.nights.append(n)
        data.nights = stableSorted(data.nights) { $0.wokeAt < $1.wokeAt }
        if data.nights.count > keep { data.nights.removeFirst(data.nights.count - keep) }
        data.askedOn = CornerTime.formatDay(day)
        save()
    }

    /// Check-in skipped: reuse the most recent night's times for today.
    public static func skip(_ day: Date) {
        var n: Night? = nil
        if let last = latest {
            // the same periods, moved to today (a split night keeps its split; naps are not copied)
            let days = CornerTime.days(from: last.wokeAt, to: day)
            let ps = last.sleep.map { SleepSpan.make($0.from.addingDays(days), $0.to.addingDays(days)) }
            let made = Night.make(ps[0].from, ps[ps.count - 1].to, assumed: true)
            if ps.count > 1 { made.periods = ps }
            if made.valid { n = made }
        }
        if let night = n { record(night) }
        data.askedOn = CornerTime.formatDay(day)
        save()
    }

    static let clockRx = Rx(#"^\s*(?:at\s+)?(\d{1,2})(?:\s*[:.]?\s*(\d{2}))?\s*(am|pm|a|p)?\s*$"#, ignoreCase: true)

    /// Loose clock times: "11:30pm", "23:30", "6:15", "615", "6". Returns seconds since midnight.
    /// Without am/pm, a bedtime of 6-11 means the evening and a wake time means the morning.
    public static func tryParseClock(_ text: String?, waking: Bool) -> TimeInterval? {
        let s = (text ?? "").trimmed.lowercased()
        if s == "midnight" { return 0 }
        if s == "noon" { return 12 * 3600 }
        guard let m = clockRx.match(s), var h = Int(m.group(1)) else { return nil }
        let min = m.has(2) ? (Int(m.group(2)) ?? 0) : 0
        if min > 59 { return nil }
        if m.has(3) {
            if h < 1 || h > 12 { return nil }
            if m.group(3).hasPrefix("p") { if h != 12 { h += 12 } } else if h == 12 { h = 0 }
        } else {
            if h > 23 { return nil }
            if !waking { if h == 12 { h = 0 } else if h >= 6 && h <= 11 { h += 12 } }
        }
        return TimeInterval(h * 3600 + min * 60)
    }
}
