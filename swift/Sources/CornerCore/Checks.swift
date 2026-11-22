// Port of Corner.Core/src/Checks.cs: energy check-ins 1 to 10 (Feedback; checkins.csv beside tasks.json)
// and tuning the curve from them (Tuning).
import Foundation

// ---------------------------------------------------------------- check-in log (checkins.csv, beside tasks.json)

/// "How fresh do you feel?" answers, 1 (drained) to 10 (fresh), and when to ask.
public enum Feedback {
    public static let intensiveDays = 14              // days of frequent asking, counted from the first check-in
    public static let intensiveEvery: Double = 1.5    // hours between prompts during those days
    public static let intensiveMax = 8                // prompts per day during those days
    public static let normalEvery: Double = 3.0       // afterwards: hours between prompts
    public static let normalMax = 4                   // afterwards: prompts per day
    public static let firstAfterWake: Double = 0.25   // hours after waking before the first prompt
    public static let idleMax: Double = 120           // seconds without keyboard or mouse before you count as away
    public static let settledMin: Double = 5          // minutes back at the computer before a prompt may appear

    public final class Entry {
        public var at: Date
        public var score: Int          // 1-10; 0 = dismissed
        public var predicted: Double   // what the untuned model said for that moment (0-1)
        public var sinceWake: Double   // hours since the final wake-up

        public init(at: Date, score: Int, predicted: Double = 0, sinceWake: Double = 0) {
            self.at = at
            self.score = score
            self.predicted = predicted
            self.sinceWake = sinceWake
        }
    }

    static var file: String { return Store.path("checkins.csv") }
    static let head = "time,score,predicted,hours_since_wake,active_min_today,active_min_session"

    public static func read() -> [Entry] {
        var list: [Entry] = []
        guard let lines = Store.readLines(file) else { return list }
        for line in lines {
            let p = line.components(separatedBy: ",")
            guard p.count >= 2, let t = CornerTime.parse(p[0]) else { continue }
            var score = Int(p[1].trimmed) ?? 0
            if score < 1 || score > 10 { score = 0 }
            var pred = 0.0
            var since = 0.0
            if p.count > 2 { pred = Double(p[2].trimmed) ?? 0 }
            if p.count > 3 { since = Double(p[3].trimmed) ?? 0 }
            list.append(Entry(at: t, score: score, predicted: pred, sinceWake: since))
        }
        return list
    }

    /// True when it is time to ask again: often in the first two weeks, then a few times a day.
    public static func due(_ now: Date, _ e: EnergyDay?) -> Bool {
        guard let e = e else { return false }
        if now < e.wake.addingHours(firstAfterWake) || now > e.bed { return false }
        let all = read()
        let todayKey = SleepLog.today(now)
        let today = all.filter { SleepLog.today($0.at) == todayKey }
        var intensive = all.isEmpty
        if !all.isEmpty {
            var earliest = all[0].at
            for x in all where x.at < earliest { earliest = x.at }
            intensive = now.timeIntervalSince(earliest) / 86400 < Double(intensiveDays)
        }
        var latest = Date.distantPast
        for x in today where x.at > latest { latest = x.at }
        let maxCount = intensive ? intensiveMax : normalMax
        let every = intensive ? intensiveEvery : normalEvery
        return today.count < maxCount && now.timeIntervalSince(latest) / 3600 >= every
    }

    /// Appends one answer ("1".."10", or "0" when dismissed) to checkins.csv.
    public static func log(_ now: Date, _ answer: String, _ e: EnergyDay?, activeToday: Double = 0, activeSession: Double = 0) {
        var line = CornerTime.format(now) + "," + answer + ","
        if let e = e { line += NetFormat.fixed(e.rawAt(now), 3) }
        line += ","
        if let e = e { line += NetFormat.fixed(now.timeIntervalSince(e.wake) / 3600, 2) }
        line += "," + NetFormat.plain(NetFormat.round(activeToday)) + "," + NetFormat.plain(NetFormat.round(activeSession))
        let fresh = !FileManager.default.fileExists(atPath: file)
        Store.appendText(file, (fresh ? head + "\r\n" : "") + line + "\r\n")
    }
}

// ---------------------------------------------------------------- tuning the curve from the check-ins

/// Learns where the model is regularly too high or too low for you, by time since waking,
/// and returns a small correction that is added to the curve. Nothing is changed until there are enough check-ins.
public enum Tuning {
    public static let minRatings = 12             // check-ins needed before any correction is applied
    public static let windowDays = 60             // only check-ins from the last this-many days are used
    public static let sigma: Double = 0.75        // hours; how far one check-in's influence spreads along the day
    public static let shrink: Double = 1.5        // caution: a spot needs about this many nearby check-ins before it moves half way
    public static let minCorrelation: Double = 0.3 // below this your scores do not track the model enough to fit a slope
    public static let maxShift: Double = 0.15     // the most the curve may be moved at any point
    public static let minSpread: Double = 0.05    // if all your scores are nearly the same there is nothing to learn

    public static var count = 0                   // check-ins available for tuning

    public static func build(_ all: [Feedback.Entry], _ now: Date) -> ((Double) -> Double)? {
        let window = Double(windowDays)
        let r = all.filter { $0.score > 0 && $0.predicted > 0 && now.timeIntervalSince($0.at) / 86400 <= window }
        count = r.count
        if r.count < minRatings { return nil }

        // Scores (1-10) and the model (0-1) are on different scales: line them up by their averages and spreads.
        let s = r.map { Double($0.score - 1) / 9.0 }
        let p = r.map { $0.predicted }
        var ms = 0.0
        for v in s { ms += v }
        ms /= Double(s.count)
        var mp = 0.0
        for v in p { mp += v }
        mp /= Double(p.count)
        var ss = 0.0
        for v in s { ss += (v - ms) * (v - ms) }
        var sp = 0.0
        for v in p { sp += (v - mp) * (v - mp) }
        let sds = sqrt(ss / Double(s.count))
        let sdp = sqrt(sp / Double(p.count))
        if sds < minSpread || sdp < 1e-6 { return nil }
        // score units per model unit: the fitted slope when your scores track the model at all, else the ratio of spreads
        var cov = 0.0
        for i in 0..<s.count { cov += (s[i] - ms) * (p[i] - mp) }
        cov /= Double(s.count)
        let corr = cov / (sds * sdp)
        let scale = max(0.5, min(4, corr > minCorrelation ? cov / (sdp * sdp) : sds / sdp))

        let hours = r.map { $0.sinceWake }
        var miss = [Double](repeating: 0, count: r.count) // how far the model was off, in the model's own units
        for i in 0..<miss.count { miss[i] = (s[i] - ms) / scale - (p[i] - mp) }
        let sg = sigma
        let sh = shrink
        let cap = maxShift

        return { t in
            var w = 0.0
            var sum = 0.0
            for i in 0..<miss.count {
                let k = exp(-(t - hours[i]) * (t - hours[i]) / (2 * sg * sg))
                w += k
                sum += k * miss[i]
            }
            return max(-cap, min(cap, sum / (w + sh)))
        }
    }

    /// The correction in use right now, or nil ("Tune" = 0 in settings.json switches it off).
    public static func active(_ now: Date) -> ((Double) -> Double)? {
        let fit = build(Feedback.read(), now)
        return Settings.get("Tune", 1) > 0.5 ? fit : nil
    }
}
