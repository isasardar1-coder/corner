// Port of the model part of windows/src/EnergyFeel.cs: "I feel 63%, not 30%". You type how much energy you feel you
// have, and the energy counter is matched to you from then on (Calibration; feel.csv beside tasks.json). FeelPlan says
// what a correction may lead to in today's tasks. The boxes that ask are the app's side (`partial class Shell`).
import Foundation

/// Your corrections of the energy counter, and the counter's number worked out from them.
public enum Calibration {
    public static let keep = 20                  // the latest this-many corrections are used
    public static let windowDays = 60            // and none older than this
    public static let stretchMin = 3             // corrections needed before the scale may stretch, not only shift
    public static let spreadMin: Double = 0.04   // ...and they must be this far apart on the curve

    public final class Entry {
        public var at: Date
        public var model: Double   // the curve's level then, 0..1
        public var felt: Double    // what you said you felt, 0..1

        public init(at: Date, model: Double, felt: Double) {
            self.at = at
            self.model = model
            self.felt = felt
        }
    }

    static var file: String { return Store.path("feel.csv") }
    static let head = "time,curve,felt"

    public static func read() -> [Entry] {
        var list: [Entry] = []
        guard let lines = Store.readLines(file) else { return list }
        for line in lines {
            let p: [String] = line.components(separatedBy: ",")
            if p.count < 3 { continue }
            guard let t = CornerTime.parse(p[0]) else { continue }
            guard let m = Double(p[1].trimmed), let f = Double(p[2].trimmed) else { continue }
            list.append(Entry(at: t, model: m, felt: f))
        }
        return list
    }

    public static func add(_ at: Date, model: Double, felt: Double) {
        let line: String = CornerTime.format(at) + "," + NetFormat.fixed(model, 3) + "," + NetFormat.fixed(felt, 2)
        let fresh: Bool = !FileManager.default.fileExists(atPath: file)
        Store.appendText(file, (fresh ? head + "\r\n" : "") + line + "\r\n")
    }

    public static func clear() {
        if FileManager.default.fileExists(atPath: file) {
            do { try FileManager.default.removeItem(atPath: file) } catch { Store.log(error) }
        }
    }

    public static func count(_ now: Date) -> Int { return use(read(), now).count }

    static func use(_ all: [Entry], _ now: Date) -> [Entry] {
        let window = Double(windowDays)
        let recent: [Entry] = all.filter { x in now.timeIntervalSince(x.at) / 86400 <= window }
        let r: [Entry] = stableSorted(recent) { a, b in a.at < b.at }
        if r.count > keep { return Array(r[(r.count - keep)...]) }
        return r
    }

    /// The counter's level (0..1) for the curve's level v. One correction shifts the scale; several far enough
    /// apart on the curve also stretch it, so the counter moves as much as you say your energy does.
    public static func map(_ v: Double, _ all: [Entry], _ now: Date) -> Double {
        let r: [Entry] = use(all, now)
        if r.count == 0 { return v }
        let n = Double(r.count)
        var mm = 0.0
        var mf = 0.0
        for x in r {
            mm += x.model
            mf += x.felt
        }
        mm /= n
        mf /= n
        var b = 1.0
        if r.count >= stretchMin {
            var vm = 0.0
            for x in r { vm += (x.model - mm) * (x.model - mm) }
            vm /= n
            if sqrt(vm) >= spreadMin {
                var cov = 0.0
                for x in r { cov += (x.model - mm) * (x.felt - mf) }
                cov /= n
                b = max(0.5, min(4, cov / vm))
            }
        }
        return max(0, min(1, mf + b * (v - mm)))
    }

    public static func map(_ v: Double, _ now: Date) -> Double { return map(v, read(), now) }
}

/// What a correction may lead to in today's tasks: a re-plan when the peaks and dip moved, and a high-effort task
/// brought forward when you say you have more energy than the counter showed.
public enum FeelPlan {
    public static let moveMin = 10               // a zone edge that moves less than this many minutes is not a move
    public static let highFelt: Double = 0.55    // you need to feel at least this...
    public static let highGap = 15               // ...and this many points above the counter, for the high-effort offer
    public static let soonMin = 60               // no offer when a high-effort task is already timed within this many minutes

    public static func zonesMoved(_ a: EnergyDay?, _ b: EnergyDay?) -> Bool {
        guard let a = a, let b = b else { return false }
        return moved(a.peak1, b.peak1) || moved(a.dip, b.dip) || moved(a.peak2, b.peak2)
    }

    static func moved(_ x: Zone?, _ y: Zone?) -> Bool {
        guard let zx = x, let zy = y else { return (x == nil) != (y == nil) }
        let limit = Double(moveMin)
        let fromMin: Double = abs(zx.from.timeIntervalSince(zy.from)) / 60
        let toMin: Double = abs(zx.to.timeIntervalSince(zy.to)) / 60
        return fromMin >= limit || toMin >= limit
    }

    /// The high-effort task to bring forward now, or nil. Overdue ones first, then later today, then those without a time.
    /// `felt` is 0..1, `shown` is the counter's number (1 to 100) before the correction, `inTask`: a task timer is running.
    public static func highNow(_ tasks: [TaskItem], felt: Double, shown: Int, now: Date, inTask: Bool) -> TaskItem? {
        if inTask || felt < highFelt || felt * 100 - Double(shown) < Double(highGap) { return nil }
        let today: Date = now.dateOnly
        let soon: Date = now.addingMinutes(soonMin)
        var high: [TaskItem] = []
        for t in tasks {
            if t.done || TaskTags.level(t.energy) != "high" { continue }
            if let d = t.dueAt, d.dateOnly > today { continue }
            high.append(t)
        }
        for t in high {
            if !t.hasTime { continue }
            if let d = t.dueAt, d >= now, d <= soon { return nil } // one is coming up anyway
        }
        func group(_ t: TaskItem) -> Int {
            guard t.hasTime, let d = t.dueAt else { return 2 }
            return d < now ? 0 : 1
        }
        let sorted: [TaskItem] = stableSorted(high) { a, b in
            let ga: Int = group(a)
            let gb: Int = group(b)
            if ga != gb { return ga < gb }
            let da: Date = a.dueAt ?? CornerTime.maxValue
            let db: Date = b.dueAt ?? CornerTime.maxValue
            return da < db
        }
        return sorted.first
    }
}
