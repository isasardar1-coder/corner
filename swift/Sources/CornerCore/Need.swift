// Port of Corner.Core/src/Need.cs: estimating your sleep need from the sleep log and the 1-10 check-ins (NeedEstimate).
import Foundation

/// Works out how much sleep you seem to need: the amount beyond which more sleep stops raising your check-in scores.
/// It only ever suggests a figure; the Need setting changes when you accept it. Everything tunable is here.
public enum NeedEstimate {
    public static let windowDays = 60       // only days this recent are used
    public static let minDays = 14          // days needed before anything is suggested...
    public static let minPerDay = 2         // ...each with an entered night and at least this many check-ins
    public static let minEachSide = 3       // days needed both below the estimate and at or above it
    public static let sigma: Double = 1.5   // hours; how widely scores are pooled to find what is normal for a time of day
    public static let nightWeights: [Double] = [0.5, 0.3, 0.2] // last night, the night before and the one before that
    public static let from: Double = 6.0    // the amounts of sleep that are tried
    public static let to: Double = 10.0
    public static let step: Double = 0.25
    public static let floor: Double = 7.0   // never suggests less
    public static let maxRange: Double = 1.5 // hours; if the likely range is wider than this, nothing is suggested
    public static let minFit: Double = 0.15  // share of the day-to-day differences in your scores that sleep must explain
    // How wide the likely range is drawn (3.84 = about 19 chances in 20; 6.63 = 99 in 100).
    public static let rangeChiLow: Double = 3.84
    public static let rangeChiHigh: Double = 6.63

    public final class Result {
        public var ready: Bool = false    // there is a figure to suggest
        public var need: Double = 0       // the figure and its likely range, hours
        public var low: Double = 0
        public var high: Double = 0
        public var days: Int = 0          // days that could be used
        public var note: String = ""      // one line saying where things stand
        public init() {}
    }

    static func dur(_ hours: Double) -> String {
        let m = Int(NetFormat.round(hours * 60))
        return String(m / 60) + "h" + (m % 60 > 0 ? " " + String(m % 60) + "m" : "")
    }

    /// One point per usable day: recent sleep (hours) and how fresh you felt that day compared with your usual
    /// for the same hours since waking (points on the 1-10 scale).
    static func dayPoints(_ nights: [Night]?, _ checks: [Feedback.Entry]?, _ now: Date) -> [[Double]] {
        var byDay: [Date: Night] = [:]
        let ordered = stableSorted((nights ?? []).filter { $0.valid }) { $0.wokeAt < $1.wokeAt }
        for n in ordered { byDay[n.wokeAt.dateOnly] = n }
        let window = Double(windowDays)
        let rated = (checks ?? []).filter { $0.score > 0 && $0.at <= now && now.timeIntervalSince($0.at) / 86400 <= window }

        func usual(_ t: Double) -> Double {
            var w = 0.0
            var sum = 0.0
            for x in rated {
                let k = exp(-(t - x.sinceWake) * (t - x.sinceWake) / (2 * sigma * sigma))
                w += k
                sum += k * Double(x.score)
            }
            return sum / w
        }

        // group by the day as Corner counts it, in order of first appearance (LINQ GroupBy)
        var keys: [Date] = []
        var groups: [Date: [Feedback.Entry]] = [:]
        for x in rated {
            let k = SleepLog.today(x.at)
            if groups[k] == nil { keys.append(k); groups[k] = [] }
            groups[k]?.append(x)
        }

        var days: [[Double]] = []
        for key in keys {
            let g = groups[key] ?? []
            guard g.count >= minPerDay, let last = byDay[key], !last.assumed else { continue } // an assumed night is not a measurement
            var w = 0.0
            var sleep = 0.0
            for k in 0..<nightWeights.count {
                guard let n = byDay[key.addingDays(-k)], !n.assumed else { continue }
                sleep += nightWeights[k] * (k == 0 ? n.hours : n.totalHours) // earlier days count their naps; today's come after the fact
                w += nightWeights[k]
            }
            var diff = 0.0
            for x in g { diff += Double(x.score) - usual(x.sinceWake) }
            days.append([sleep / w, diff / Double(g.count)])
        }
        return days
    }

    public static func build(_ nights: [Night]?, _ checks: [Feedback.Entry]?, _ now: Date) -> Result {
        let r = Result()
        let days = dayPoints(nights, checks, now)
        let n = days.count
        r.days = n
        if n < minDays {
            r.note = "Corner can suggest your sleep need once " + String(minDays) + " days have an entered night and " + String(minPerDay) + " check-ins. " + String(n) + " so far."
            return r
        }

        var my = 0.0
        for d in days { my += d[1] }
        my /= Double(n)
        var total = 0.0
        for d in days { total += (d[1] - my) * (d[1] - my) }
        if total < 1e-9 { r.note = "Your check-in scores are all alike, so they say nothing about your sleep need yet."; return r }

        // For each candidate: scores rise with sleep up to it and are level beyond it. The best candidate leaves the least unexplained.
        var tried: [[Double]] = [] // [candidate, what is left unexplained]
        var c = from
        while c <= to + 1e-9 {
            let x = days.map { Swift.min($0[0], c) }
            var mx = 0.0
            for v in x { mx += v }
            mx /= Double(x.count)
            var sxx = 0.0
            var sxy = 0.0
            for i in 0..<n {
                sxx += (x[i] - mx) * (x[i] - mx)
                sxy += (x[i] - mx) * (days[i][1] - my)
            }
            let left = sxx < 1e-9 || sxy <= 0 ? total : total - sxy * sxy / sxx // more sleep never counts as making you less fresh
            tried.append([c, left])
            c += step
        }
        var least = tried[0][1]
        for t in tried where t[1] < least { least = t[1] }
        let bestRun = tried.filter { $0[1] <= least * (1 + 1e-9) + 1e-12 }.map { $0[0] }
        let best = NetFormat.round((bestRun[0] + bestRun[bestRun.count - 1]) / 2 / step) * step

        if 1 - least / total < minFit {
            r.note = "Over " + String(n) + " days your check-in scores do not yet follow how much you slept, so no sleep need is suggested."
            return r
        }
        let below = days.filter { $0[0] < best - 1e-9 }.count
        let atOrAbove = n - below
        if atOrAbove < minEachSide {
            r.note = "You feel fresher the more you sleep, and Corner has not yet seen where that stops: you may need more than you have been getting. A few longer nights would show it."
            return r
        }
        if below < minEachSide {
            r.note = "You have rarely slept little enough for Corner to see where your sleep need lies. Nothing is suggested."
            return r
        }

        let lowLimit = least * exp(rangeChiLow / Double(n))
        let highLimit = least * exp(rangeChiHigh / Double(n))
        var low = Double.infinity
        var high = -Double.infinity
        for t in tried {
            if t[1] <= lowLimit && t[0] < low { low = t[0] }
            if t[1] <= highLimit && t[0] > high { high = t[0] }
        }
        if high - low > maxRange {
            r.note = "Your check-ins point to a sleep need somewhere between " + dur(low) + " and " + dur(high) + ": not narrow enough to suggest a figure yet."
            return r
        }
        r.need = Swift.max(floor, best)
        r.low = Swift.max(floor, low)
        r.high = Swift.max(r.need, high)
        r.ready = true
        let range = r.high - r.low > 0.01 ? " (likely " + dur(r.low) + " to " + dur(r.high) + ")" : ""
        r.note = "From " + String(n) + " days of check-ins: a sleep need of about " + dur(r.need) + range + "."
        return r
    }
}
