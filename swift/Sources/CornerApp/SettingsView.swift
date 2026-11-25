// Corner for macOS - the morning sleep check-in tray, and the Settings page: the panel (look, text size, hot corner,
// open at login, position), energy (clock, tuning, Fajr), your record and the sleep history. Port of OpenCheckIn
// (UiEnergy.cs), DrawSettings and CornerPicker (Ui.cs), DrawPrayer (UiEnergy.cs), DrawRecord (Stats.cs) and
// DrawSleepHistory (UiSleep.cs).
import AppKit
import SwiftUI
import CornerCore

// ---------------------------------------------------------------- morning check-in

struct CheckInView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let name = Profile.name
        let title = model.checkInEdit ? "Last night's sleep" : "Good morning" + (name.isEmpty ? "" : ", " + name) + ". How did you sleep?"
        return CardBox(surface: .tray) {
            Text(title).font(m.head(14)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true).padding(.bottom, 10)
            HStack(spacing: 0) {
                Caps(text: "Fell asleep").frame(width: 92 + 6, alignment: .leading)
                Caps(text: "Woke")
                Spacer(minLength: 0)
            }
            .padding(.leading, 2).padding(.bottom, 4)
            ForEach(Array(model.checkRows.enumerated()), id: \.element[0].id) { pair in
                HStack(spacing: 6) {
                    TimeEntryView(e: pair.element[0], onSubmit: { model.saveCheckIn() })
                    TimeEntryView(e: pair.element[1], onSubmit: { model.saveCheckIn() })
                    if pair.offset > 0 {
                        IconHit(d: Icons.cross, hotColor: T.danger, box: 20) { model.removeCheckRow(pair.offset); model.shell?.contentChanged() }
                            .help("Remove this period")
                    }
                    Spacer(minLength: 0)
                }
                .padding(.bottom, 6)
            }
            if model.checkRows.count < 3 {
                HStack {
                    Btn(text: "+ Add another sleep period", kind: 2) { model.addCheckRow(nil, nil); model.shell?.contentChanged() }
                    Spacer(minLength: 0)
                }
                .padding(.leading, -3).padding(.bottom, 2)
            }
            Rectangle().fill(T.ctl).frame(height: 1).padding(.top, 4).padding(.bottom, 9)
            HStack {
                Btn(text: model.checkInEdit ? "Cancel" : "Same as last night", kind: 2) { model.skipCheckIn() }
                Spacer(minLength: 0)
                Btn(text: "Save", kind: 0) { model.saveCheckIn(); model.shell?.contentChanged() }
            }
            .padding(.leading, -3)
        }
    }
}

// ---------------------------------------------------------------- the Settings page

struct SettingsPage: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        VStack(alignment: .leading, spacing: m.gapY) {
            HStack {
                Text("Settings").font(m.head(16)).foregroundColor(T.text)
                Spacer()
                Btn(text: "Done", kind: 0) { model.closeSettings() }
            }
            .padding(.leading, m.padX)
            if model.full {
                HStack(alignment: .top, spacing: 2 * m.sideMid) {
                    VStack(alignment: .leading, spacing: m.gapY) { PlanningSettings(); PanelSettings() }.frame(maxWidth: .infinity)
                    VStack(alignment: .leading, spacing: m.gapY) { EnergySettings(); RecordCard(); SleepHistory() }.frame(maxWidth: .infinity)
                }
            } else {
                PlanningSettings()
                PanelSettings()
                EnergySettings()
                RecordCard()
                SleepHistory()
            }
        }
        .padding(.horizontal, m.side)
    }
}

/// The heading over a card on the Settings page.
struct SetCaps: View {
    @Environment(\.m) var m
    let text: String
    var body: some View { Caps(text: text).padding(.leading, m.padX).padding(.top, 2).padding(.bottom, -m.gapY + 6) }
}

/// A white card on the Settings page; its lines are ruled off from each other.
struct SetCard: View {
    @Environment(\.m) var m
    let lines: [AnyView]
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(0..<lines.count, id: \.self) { i in
                if i > 0 { Rule() }
                lines[i]
            }
        }
        .padding(.horizontal, m.padX).padding(.vertical, 1)
        .frame(maxWidth: .infinity, alignment: .leading)
        .plate(.field, radius: m.radius, rim: m.radius)
    }
}

/// First on the page: plan by schedule or by priority, the stopwatch, and the work log.
struct PlanningSettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let prio: Bool = AppModel.priorityMode
        var lines: [AnyView] = [planBy(prio)]
        if prio { lines.append(stopwatch()) }
        lines.append(workLog())
        return Group {
            SetCaps(text: "Planning")
            SetCard(lines: lines)
        }
    }

    func planBy(_ prio: Bool) -> AnyView {
        let sub: String = prio ? "What to do next, in order, for your energy now. No clock times." : "A clock time for each task, and a countdown."
        return SettingLine(title: "Plan by", sub: sub) {
            Segmented(names: ["Schedule", "Priority"], chosen: prio ? 1 : 0) { i in model.setPlanBy(i) }
        }.any()
    }

    func stopwatch() -> AnyView {
        let watch: Bool = model.showWatch
        return SettingLine(title: "Show stopwatch", sub: "The time on a task is logged either way") {
            Toggle2(on: watch) { model.setShowWatch(!watch) }
        }.any()
    }

    func workLog() -> AnyView {
        let n: Int = WorkLog.rows.count
        var sub: String = "Nothing yet. "
        if n > 0 { sub = String(n) + (n == 1 ? " stretch" : " stretches") + " of work. " }
        sub += "work.csv, in Corner's folder. It opens in Numbers."
        return SettingLine(title: "Work log", sub: sub) {
            Btn(text: "Show", kind: 1, on: 1) {
                let f: String = WorkLog.file
                Platform.reveal(FileManager.default.fileExists(atPath: f) ? f : TaskLog.folder)
            }
        }.any()
    }
}

struct PanelSettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let corners = ["Top right", "Top left", "Bottom right", "Bottom left"]
        let auto = Autostart.enabled
        let swapped: Bool = model.swapped
        let clicks: Bool = Click.on
        return Group {
            SetCaps(text: "Panel")
            SetCard(lines: [
                SkinPicker().any(),
                SettingLine(title: model.full ? "Text size in Full Mode" : "Text size", sub: "Cmd + scroll also works") {
                    HStack(spacing: 0) {
                        StepBtn(sign: "–") { model.setScale(model.currentScale - 0.05, save: true) }
                        Text(String(Int((model.currentScale * 100).rounded())) + "%").font(m.font(11.5, true)).foregroundColor(T.text).frame(width: 42)
                        StepBtn(sign: "+") { model.setScale(model.currentScale + 0.05, save: true) }
                    }
                }.any(),
                SettingLine(title: "Hot corner", sub: corners[model.corner] + " opens the list") { CornerPicker() }.any(),
                SettingLine(title: "Open at login", sub: Autostart.inApp ? nil : "Move Corner.app to Applications first") {
                    Toggle2(on: auto) { Autostart.set(!auto); model.changed() }
                }.any(),
                SettingLine(title: "Click sounds") {
                    Toggle2(on: clicks) { Settings.set("Click", clicks ? 0 : 1); model.changed() }
                }.any(),
                SettingLine(title: "Full Mode sides", sub: swapped ? "Energy on the left, tasks on the right" : "Tasks on the left, energy on the right") {
                    Segmented(names: ["Tasks left", "Energy left"], chosen: swapped ? 1 : 0) { i in
                        Settings.set("SwapSides", Double(i))
                        model.changed()
                        model.shell?.contentChanged()
                    }
                }.any(),
                SettingLine(title: "Full Mode sections", sub: "Drag one by the dots at its right edge") {
                    Btn(text: "Reset", kind: 1, on: 1) { Sec.reset(); model.changed(); model.shell?.contentChanged() }
                }.any(),
                SettingLine(title: "Panel position", sub: "Drag the header to move it") {
                    Btn(text: "Reset", kind: 1, on: 1) { model.shell?.resetPosition(); model.changed() }
                }.any(),
                updates(),
                version(),
            ])
        }
    }

    func updates() -> AnyView {
        let on: Bool = UpdateWatch.on
        return SettingLine(title: "Updates", sub: on ? "Automatic: Corner updates itself" : "Off: Corner doesn't look for updates") {
            Toggle2(on: on) { Settings.set("AutoUpdate", on ? 0 : 1); model.changed() }
        }.any()
    }

    /// The version, what the last check said, and "Check now" ("Download" when Corner could not replace itself).
    func version() -> AnyView {
        let w: UpdateWatch? = model.shell?.updates
        let every: String = "Checks every " + String(Updater.everyHours) + " hours"
        let stuck: Bool = w?.waiting != nil
        let label: String = w?.busy == true ? "Checking…" : (stuck ? "Download" : "Check now")
        return SettingLine(title: "Version " + Updater.version, sub: w?.note ?? every) {
            Btn(text: label, kind: 1, on: 1) { if stuck { w?.openPage() } else { w?.check(manual: true) } }
        }.any()
    }
}

/// The five skins, as a row of pills that wraps: too many for joined segments in the corner panel. The chosen one
/// is the colour of the main button.
struct SkinPicker: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Look").font(m.font(12.5)).foregroundColor(T.text)
            Text(T.says[T.skin]).font(m.font(10.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                .padding(.top, 1).padding(.bottom, m.sp(8, 7))
            FlowRow(items: pills(), hs: 5, vs: 5)
        }
        .padding(.top, m.sp(10, 9)).padding(.bottom, m.sp(6, 5))
    }

    func pills() -> [AnyView] {
        var list: [AnyView] = []
        for i in [1, 2, 3, 4, 0] { // Glass first (the default), as on Windows; the numbers stay as saved
            let on: Bool = i == T.skin
            let pill = Text(T.names[i]).font(m.font(11)).foregroundColor(on ? T.onMain : T.text)
                .lineLimit(1).fixedSize()
                .padding(.horizontal, 12).padding(.top, 4).padding(.bottom, 5)
                .plate(on ? .main : .raised)
                .contentShape(Capsule())
                .onTapGesture { if !on { model.setSkin(i) } }
            list.append(pill.any())
        }
        return list
    }
}

/// A small picture of the screen; click a corner to make it the one that opens the list.
struct CornerPicker: View {
    @EnvironmentObject var model: AppModel
    var body: some View {
        let names = ["Top right", "Top left", "Bottom right", "Bottom left"]
        return VStack(spacing: 0) {
            ForEach(0..<2, id: \.self) { row in
                HStack(spacing: 0) {
                    ForEach(0..<2, id: \.self) { col in
                        let v = row == 0 ? (col == 1 ? 0 : 1) : (col == 1 ? 2 : 3)
                        let on = model.corner == v
                        let right = v == 0 || v == 2
                        let top = v < 2
                        RoundedRectangle(cornerRadius: on ? 3 : 2)
                            .fill(on ? T.main : T.ctl)
                            .frame(width: on ? 10 : 6, height: on ? 10 : 6)
                            .padding(on ? 5 : 7)
                            .frame(width: 30, height: 22, alignment: Alignment(horizontal: right ? .trailing : .leading, vertical: top ? .top : .bottom))
                            .contentShape(Rectangle())
                            .onTapGesture { model.setCorner(v); model.changed() }
                            .help(names[v])
                    }
                }
            }
        }
        .frame(width: 60, height: 44)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(T.raised))
    }
}

struct EnergySettings: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let tune = Settings.get("Tune", 1) > 0.5
        let status = Tuning.count < Tuning.minRatings
            ? String(Tuning.count) + " of " + String(Tuning.minRatings) + " check-ins so far"
            : (tune ? "Curve adjusted from " : "Off · ") + String(Tuning.count) + " check-ins"
        let prayer = Settings.get("PrayerOn", 0) > 0.5
        var lines: [AnyView] = [
            SettingLine(title: "Clock") {
                Segmented(names: ["24h", "am/pm"], chosen: EnergyDay.use12 ? 1 : 0) { i in
                    Settings.set("Clock12", Double(i)); model.refreshEnergy(opening: false)
                }
            }.any(),
            SettingLine(title: "Tuning", sub: status) {
                Toggle2(on: tune) { Settings.set("Tune", tune ? 0 : 1); model.refreshEnergy(opening: false) }
            }.any(),
        ]
        // the energy % typed over the counter: how many corrections it is matched to, and the way back
        let feels: Int = Calibration.count(AppModel.now())
        if feels > 0 { lines.append(feelLine(feels)) }
        lines.append(VStack(alignment: .leading, spacing: 0) {
            SettingLine(title: "I usually wake for Fajr", sub: prayer ? nil : "Fills in the split when you enter last night's sleep") {
                Toggle2(on: prayer) { Settings.set("PrayerOn", prayer ? 0 : 1); model.changed() }
            }
            if prayer { PrayerRow() }
        }.any())
        return Group {
            SetCaps(text: "Energy")
            SetCard(lines: lines)
        }
    }

    func feelLine(_ feels: Int) -> AnyView {
        let sub: String = "Matched to " + String(feels) + (feels == 1 ? " correction" : " corrections") + " you typed in"
        return SettingLine(title: "Energy %", sub: sub) {
            Btn(text: "Reset", kind: 1, on: 1) { model.resetFeel() }
        }.any()
    }
}

/// When the night is split for prayer: the time and how long. Stored on Enter or when a box is left.
struct PrayerRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @StateObject private var at = TimeEntryModel("time", waking: true)
    @State private var mins = ""
    @State private var bad = false
    @State private var filled = false

    var body: some View {
        HStack(spacing: 6) {
            Text("At").font(m.font(11.5)).foregroundColor(T.muted)
            TimeEntryView(e: at, surface: .raised, onSubmit: save)
            Text("for").font(m.font(11.5)).foregroundColor(T.muted)
            MiniBox(text: Binding(get: { mins }, set: { mins = $0; bad = false }), width: 24, bad: bad, surface: .raised, onSubmit: save)
            Text("min").font(m.font(11.5)).foregroundColor(T.muted)
            Spacer(minLength: 0)
        }
        .padding(.bottom, 10)
        .onAppear {
            if filled { return }
            filled = true
            at.fill(Date().dateOnly.addingMinutes(Settings.get("PrayerAt", AppModel.prayerAtDefault)))
            mins = String(Int(Settings.get("PrayerMin", AppModel.prayerMinDefault)))
        }
        .onDisappear { save() }
    }

    func save() {
        let t = at.get()
        let mm = Int(mins) ?? 0
        let okM = mm >= 5 && mm <= 180
        if t == nil { at.bad = true }
        if !okM { bad = true }
        guard let tt = t, okM else { return }
        let tm = tt / 60
        if tm != Settings.get("PrayerAt", AppModel.prayerAtDefault) { Settings.set("PrayerAt", tm) }
        if Double(mm) != Settings.get("PrayerMin", AppModel.prayerMinDefault) { Settings.set("PrayerMin", Double(mm)) }
    }
}

/// The last seven days: done, high effort, routines, time on timers.
struct RecordCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let r = model.record()
        func pct(_ a: Int, _ b: Int) -> String { return b > 0 ? String(Int((100.0 * Double(a) / Double(b)).rounded())) + "%" : "–" }
        func big(_ s: String) -> Text { return Text(s).font(m.head(15)).foregroundColor(T.text) }
        let perDay = r.days > 1 ? String(format: "%.1f", Double(r.done) / Double(r.days)) + " a day" : "today so far"
        var lines: [AnyView] = [
            SettingLine(title: "Done in 7 days", sub: r.total > 0 ? String(r.done) + " of " + String(r.total) + " tasks · " + perDay : "Nothing yet") { big(pct(r.done, r.total)) }.any(),
            SettingLine(title: "High effort", sub: r.hiTotal > 0 ? String(r.hiDone) + " of " + String(r.hiTotal) + " done" + (r.hiDone > 0 ? " · " + pct(r.hiPeak, r.hiDone) + " of them on a peak" : "") : "No high-effort tasks yet") { big(pct(r.hiDone, r.hiTotal)) }.any(),
        ]
        if r.rSent > 0 { lines.append(SettingLine(title: "Routines", sub: String(r.rDone) + " of " + String(r.rSent) + " sent to the day were done") { big(pct(r.rDone, r.rSent)) }.any()) }
        if r.timed > 0 { lines.append(SettingLine(title: "Timed", sub: "Minutes on task timers this week") { big(String(Int(r.timed.rounded()))) }.any()) }
        lines.append(SettingLine(title: "The full log", sub: "done.csv and days.csv, in Corner's folder. They open in Numbers.") {
            Btn(text: "Show", kind: 1, on: 1) {
                let f = (TaskLog.folder as NSString).appendingPathComponent("done.csv")
                Platform.reveal(FileManager.default.fileExists(atPath: f) ? f : TaskLog.folder)
            }
        }.any())
        return Group {
            SetCaps(text: "Your record")
            SetCard(lines: lines)
        }
    }
}

/// Weekly and monthly averages, and the last 30 nights as bars from falling asleep to waking.
struct SleepHistory: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let _ = SleepLog.load()
        let day = SleepLog.today(AppModel.now())
        let need = Settings.get("SleepNeed", EnergyModel.defaultNeed)
        let nights = SleepLog.data.nights
        let logged = nights.filter { $0.valid && !$0.assumed && $0.wokeAt.dateOnly <= day }.sorted { $0.wokeAt < $1.wokeAt }
        return Group {
            SetCaps(text: "Sleep history")
            VStack(alignment: .leading, spacing: 0) {
                if logged.isEmpty {
                    Text("Enter your sleep in the morning check-in and the weekly and monthly averages appear here. Mornings you skip are left out.")
                        .font(m.font(11.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                } else {
                    averages(logged, day, need).padding(.bottom, 10)
                    pattern(nights, day, need)
                    Text("Each bar is one night, from falling asleep (top) to waking (bottom). Dark nights were shorter than your " + AppModel.durShort(need) + " need; faint ones were assumed, not entered. Averages use entered nights only.")
                        .font(m.font(10.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true).padding(.top, 4)
                }
            }
            .padding(.horizontal, m.padX).padding(.top, m.padY - 4).padding(.bottom, m.padY - 2)
            .frame(maxWidth: .infinity, alignment: .leading)
            .plate(.field, radius: m.radius, rim: m.radius)
        }
    }

    static func meanBedHour(_ ns: [Night]) -> Double {
        let hs = ns.map { n -> Double in
            let h = n.asleepAt.timeIntervalSince(n.asleepAt.dateOnly) / 3600
            return h < 12 ? h + 24 : h
        }
        let mm = hs.reduce(0, +) / Double(max(1, hs.count))
        return mm >= 24 ? mm - 24 : mm
    }

    func averages(_ logged: [Night], _ day: Date, _ need: Double) -> some View {
        let heads = ["", "Asleep", "Woke", "Slept", "Debt"]
        var rows: [[String]] = []
        for days in [7, 30] {
            let w = logged.filter { $0.wokeAt.dateOnly > day.addingDays(-days) }
            let period: String = days == 7 ? "Week" : "Month"
            let count: String = String(w.count) + (w.count == 1 ? " night" : " nights")
            var row: [String] = [period + "\n" + count]
            if w.isEmpty { row += ["–", "", "", ""] } else {
                let debt = max(0, w.reduce(0.0) { $0 + (need - $1.totalHours) })
                let woke = w.map { $0.wokeAt.timeIntervalSince($0.wokeAt.dateOnly) / 3600 }.reduce(0, +) / Double(w.count)
                let slept = w.map { $0.totalHours }.reduce(0, +) / Double(w.count)
                let bed: String = EnergyDay.clock(Date().dateOnly.addingHours(SleepHistory.meanBedHour(w)))
                let up: String = EnergyDay.clock(Date().dateOnly.addingHours(woke))
                let debtText: String = debt < 0.25 ? "none" : AppModel.durShort(debt)
                row += [bed, up, AppModel.durShort(slept), debtText]
            }
            rows.append(row)
        }
        // five columns, the first a little wider (as the Windows grid's star widths)
        let inner: CGFloat = model.full ? (fullW - 2 * m.side - 2 * m.sideMid) / 2 - 2 * m.padX : cardW - 2 * m.side - 2 * m.padX
        let k: CGFloat = m.sp(1.25, 0.95)
        let colW: [CGFloat] = [inner * k / (k + 4)] + Array(repeating: inner / (k + 4), count: 4)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                ForEach(0..<5, id: \.self) { c in
                    Text(heads[c].uppercased()).font(m.font(10, true)).foregroundColor(T.muted)
                        .lineLimit(1).frame(width: colW[c], alignment: .leading)
                }
            }
            .padding(.vertical, 3)
            ForEach(0..<rows.count, id: \.self) { r in
                HStack(alignment: .center, spacing: 0) {
                    ForEach(0..<5, id: \.self) { c in
                        Text(rows[r][c]).font(c == 0 ? m.font(11) : m.font(m.sp(12, 11), true)).foregroundColor(c == 0 ? T.muted : (rows[r][c] == "–" ? T.faint : T.text))
                            .lineLimit(c == 0 ? 2 : 1).fixedSize(horizontal: false, vertical: true)
                            .frame(width: colW[c], alignment: .leading)
                    }
                }
                .padding(.vertical, 3)
            }
        }
    }

    func pattern(_ nights: [Night], _ day: Date, _ need: Double) -> some View {
        let days = 30
        let PH: CGFloat = 78
        let from: Double = 18
        let hours: Double = 20
        let labelW: CGFloat = 26
        let recent = nights.filter { $0.valid && $0.wokeAt.dateOnly <= day && $0.wokeAt.dateOnly > day.addingDays(-days) }
        return Canvas { ctx, size in
            let PW = size.width - labelW
            let col = PW / CGFloat(days)
            for hour in [0, 6, 12] {
                let y = CGFloat(Double((hour + 24 - Int(from)) % 24) / hours) * PH
                var l = Path()
                l.move(to: CGPoint(x: labelW, y: y)); l.addLine(to: CGPoint(x: labelW + PW, y: y))
                ctx.stroke(l, with: .color(T.line), lineWidth: 1)
                ctx.draw(Text(EnergyDay.hourLabel(Date().dateOnly.addingHours(hour))).font(Fonts.ui(m.z(9))).foregroundColor(T.faint),
                         at: CGPoint(x: 0, y: y - 6), anchor: .topLeading)
            }
            for n in recent {
                let back = CornerTime.calendar.dateComponents([.day], from: n.wokeAt.dateOnly, to: day).day ?? 0
                let origin = n.wokeAt.dateOnly.addingDays(-1).addingHours(from)
                for part in n.sleep {
                    let y0 = max(0, min(PH, CGFloat(part.from.timeIntervalSince(origin) / 3600 / hours) * PH))
                    let y1 = max(0, min(PH, CGFloat(part.to.timeIntervalSince(origin) / 3600 / hours) * PH))
                    if y1 - y0 < 1 { continue }
                    let r = CGRect(x: labelW + CGFloat(days - 1 - back) * col + 1.5, y: y0, width: max(2, col - 3), height: y1 - y0)
                    let c = n.totalHours < need - 0.01 ? T.strong : T.ctl
                    ctx.fill(Path(roundedRect: r, cornerRadius: 2), with: .color(c.opacity(n.assumed ? 0.35 : 1)))
                }
            }
            ctx.draw(Text(String(days) + " days ago").font(Fonts.ui(m.z(9))).foregroundColor(T.faint), at: CGPoint(x: labelW, y: PH + 3), anchor: .topLeading)
            ctx.draw(Text("today").font(Fonts.ui(m.z(9))).foregroundColor(T.faint), at: CGPoint(x: size.width, y: PH + 3), anchor: .topTrailing)
        }
        .frame(height: PH + 16)
    }
}
