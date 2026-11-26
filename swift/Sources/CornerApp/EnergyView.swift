// Corner for macOS - the Energy side. In the corner panel it is the chart alone, in its tray (zones, sleep, naps,
// now, the cursor, scrolling). Full Mode has more sections (Sections.swift): five round icons (the sleep figures,
// "Log a nap" and "Rate your energy now") with what they open, the suggested sleep need, the sleep debt calculator
// and naps under them; a slim Routines bar; a slim Done bar; and the plan card. Port of DrawEnergy, DrawNeedHint,
// DrawNaps, DrawPlan and DrawRoutines (UiEnergy.cs), AddChart and DrawDebt (UiSleep.cs), DrawOrderPlan (Work.cs),
// DrawDoneCard and FillDone (FullLayout.cs).
import AppKit
import SwiftUI
import CornerCore

struct EnergyView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        VStack(alignment: .leading, spacing: m.gapY) {
            if let e = model.energy {
                CurveTray(e: e)
            } else {
                CardBox(surface: .tray) {
                    Text("Enter last night's sleep to see today's curve.")
                        .font(m.font(12.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                        .padding(.bottom, 10)
                    HStack { Btn(text: "Enter sleep", kind: 0) { model.openCheckIn(edit: false) }; Spacer() }
                }
            }
        }
    }
}

// ---------------------------------------------------------------- the curve

struct CurveTray: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    var body: some View {
        let now = AppModel.now()
        let line = e.zoneLine(now)
        let parts = line.components(separatedBy: " · ")
        let head = parts.first ?? line
        var rest = parts.dropFirst().joined(separator: " · ")
        if let f = rest.first { rest = String(f).uppercased() + rest.dropFirst() }
        if e.bedPush >= 5 { rest += (rest.isEmpty ? "" : " · ") + "late nap: wind-down about " + String(Int(e.bedPush.rounded())) + " min later" }
        return CardBox(surface: .tray) {
            // the words on the left; on the right, the curve's value right now as a percentage
            HStack(alignment: .top, spacing: 10) {
                VStack(alignment: .leading, spacing: 1) {
                    Text(head).font(m.head(m.sp(18, 16))).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
                    if !rest.isEmpty {
                        Text(rest).font(m.font(11.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                    }
                }
                Spacer(minLength: 0)
                EnergyCounter(now: now)
            }
            .padding(.bottom, m.sp(8, 5))
            if model.feelAsk != nil {
                FeelAskBox() // after a number was typed into the counter
            } else if model.feelAfter != nil {
                FeelAfterBox() // and what may follow from it
            }
            Chart(e: e, now: now)
        }
    }
}

/// Energy now, 1 to 100%: the curve's level at this moment, matched to your corrections, with the zone's colour
/// beside it. Click the number to type how you feel.
struct EnergyCounter: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let now: Date
    @State private var text = ""
    @FocusState private var editing: Bool

    var body: some View {
        let pct: Int = Int(model.energyPct(now))
        VStack(alignment: .trailing, spacing: 0) {
            if model.feelEditing {
                box(pct)
            } else {
                number(pct) // (a plain number, as on Windows: it does not count up)
            }
            HStack(spacing: 4) {
                Circle().fill(model.zoneColor(now)).frame(width: 6, height: 6).padding(.top, 1)
                Text("energy").font(m.font(10.5)).foregroundColor(T.muted)
            }
        }
        .fixedSize()
        .help("Your energy right now. Click the number if it feels different")
    }

    func number(_ pct: Int) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 0) {
            Text(String(pct)).font(m.head(m.sp(22, 19))).foregroundColor(T.text)
            Text("%").font(m.font(12, true)).foregroundColor(T.muted)
        }
        .contentShape(Rectangle())
        .onTapGesture { model.beginFeel() }
    }

    /// The counter's number as a box to type in: 1 to 100, then Enter. Escape leaves it as it was.
    func box(_ pct: Int) -> some View {
        TextField("", text: Binding(get: { text }, set: { v in text = String(v.filter { $0.isNumber || $0 == "%" }.prefix(4)) }),
                  onCommit: { model.commitFeel(text, shown: pct) })
            .textFieldStyle(.plain)
            .font(m.head(m.sp(20, 17)))
            .foregroundColor(T.text)
            .multilineTextAlignment(.trailing)
            .focused($editing)
            .onExitCommand { model.cancelFeel() }
            .onAppear {
                text = String(pct)
                DispatchQueue.main.async { editing = true }
            }
            .onChange(of: editing) { f in
                if !f && model.feelEditing { model.commitFeel(text, shown: pct) }
            }
            .frame(width: m.z(50))
            .padding(.horizontal, 6)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(T.field))
            .help("How much energy do you feel you have? 1 to 100, then Enter")
    }
}

/// A question with its two (or three) answers, in a white box on the chart's tray.
struct AskBox: View {
    @Environment(\.m) var m
    let title: String
    let text: String
    let buttons: [AnyView]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).font(m.font(12.5, true)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
            Text(text).font(m.font(11.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                .padding(.top, 3).padding(.bottom, m.sp(9, 7))
            FlowRow(items: buttons, hs: 6, vs: 6)
        }
    }
}

/// Under the chart's heading, after a number was typed into the counter.
struct FeelAskBox: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        if let a = model.feelAsk {
            AskBox(title: FeelAskBox.title(a), text: FeelAskBox.how(), buttons: [
                Btn(text: "Fine-tune the chart", kind: 0, on: 1) { model.feelFineTune() }.any(),
                Btn(text: "Only the number", kind: 1, on: 1) { model.feelOnlyNumber() }.any(),
                Btn(text: "Cancel", kind: 2, on: 1) { model.feelCancelAsk() }.any(),
            ])
            .modifier(FeelFrame())
        }
    }

    static func title(_ a: FeelAsk) -> String {
        let felt: Int = Int(NetFormat.round(a.felt * 100))
        return "You feel " + String(felt) + "%. The counter said " + String(a.shown) + "%."
    }

    static func how() -> String {
        let have: Int = Tuning.count + 1
        var how: String = "Fine-tuning also counts as a check-in, so the chart moves toward how you feel at this time of day."
        if have < Tuning.minRatings {
            how = "Fine-tuning also counts as a check-in. The chart starts to reshape after " + String(Tuning.minRatings)
                + " of them (this makes " + String(have) + ")."
        }
        return "The counter will match you from now on. " + how
    }
}

/// Where the correction box was: the questions that follow from it, each with its own answer.
struct FeelAfterBox: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let replan: Bool = model.feelAfter?.replan ?? false
        let high: TaskItem? = model.feelHighTask()
        if replan || high != nil {
            VStack(alignment: .leading, spacing: 0) {
                if replan { replanAsk }
                if replan && high != nil {
                    Rule().padding(.top, 2).padding(.bottom, m.sp(10, 8))
                }
                if let t = high { highAsk(t) }
            }
            .modifier(FeelFrame())
        }
    }

    var replanAsk: some View {
        AskBox(title: "Your peak and dip moved.",
               text: "Re-plan the rest of today to the new times above? You'll see the new plan before anything changes.",
               buttons: [
                   Btn(text: "Re-plan today", kind: 0, on: 1) { model.feelReplan(true) }.any(),
                   Btn(text: "Not now", kind: 2, on: 1) { model.feelReplan(false) }.any(),
               ])
    }

    func highAsk(_ t: TaskItem) -> some View {
        let felt: Int = model.feelAfter?.felt ?? 0
        let text: String = "You feel " + String(felt) + "%. Bring “" + t.title + "” (high effort) forward to now?"
        return AskBox(title: "You have energy now.", text: text, buttons: [
            Btn(text: "Move it to now", kind: 0, on: 1) { model.feelMoveHigh(true) }.any(),
            Btn(text: "Not now", kind: 2, on: 1) { model.feelMoveHigh(false) }.any(),
        ])
    }
}

/// The white box the questions about a typed energy % sit in.
struct FeelFrame: ViewModifier {
    @Environment(\.m) var m
    func body(content: Content) -> some View {
        content
            .padding(.horizontal, m.padX)
            .padding(.top, m.sp(10, 8))
            .padding(.bottom, m.sp(4, 2))
            .frame(maxWidth: .infinity, alignment: .leading)
            .plate(.field, radius: m.radius * 0.75, rim: m.radius * 0.75)
            .padding(.bottom, m.sp(10, 8))
    }
}

struct Chart: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay
    let now: Date
    @State private var cursorX: CGFloat? = nil
    @State private var dragStart: CGFloat? = nil
    @State private var thumbStart: CGFloat? = nil

    var view: CGFloat { return (model.full ? fullW / 2 : cardW) - m.sideMid - m.side - 2 * m.padX }
    var H: CGFloat { return m.sp(104, 86) }
    var W: CGFloat { return model.full ? view : view * 4 }

    var body: some View {
        let W = self.W, view = self.view
        let maxOff = max(0, W - view)
        var off = model.chartOffset
        if off < 0 {
            let total = e.nextWake.timeIntervalSince(e.wake)
            off = CGFloat(now.timeIntervalSince(e.wake) / total) * W - view / 2
        }
        off = max(0, min(maxOff, off))
        let thumbW = max(44, view * view / W)
        let run = view - thumbW
        return VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .topLeading) {
                Canvas { ctx, size in draw(ctx, W: W, off: off, view: view) }
                    .frame(width: view, height: H + 28)
            }
            .frame(width: view, height: H + 28, alignment: .topLeading)
            .clipped()
            .contentShape(Rectangle())
            .modifier(HoverX(x: $cursorX))
            .gesture(DragGesture(minimumDistance: 2).onChanged { v in
                guard maxOff > 0 else { return }
                if dragStart == nil { dragStart = off }
                model.chartOffset = max(0, min(maxOff, (dragStart ?? off) - v.translation.width))
                cursorX = v.location.x
                model.changed()
            }.onEnded { _ in dragStart = nil })
            if maxOff > 0 {
                // the scroll bar: a rail with a handle to drag
                ZStack(alignment: .leading) {
                    Capsule().fill(T.ctl).frame(height: 4)
                    Capsule().fill(T.muted).frame(width: thumbW, height: 4)
                        .padding(.leading, run > 0 ? off / maxOff * run : 0)
                }
                .frame(width: view, height: 12)
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 0).onChanged { v in
                    if thumbStart == nil {
                        let tx = run > 0 ? off / maxOff * run : 0
                        // a press beside the handle jumps there; on the handle it drags from where it is
                        if v.startLocation.x < tx || v.startLocation.x > tx + thumbW {
                            thumbStart = (v.startLocation.x - thumbW / 2) / max(1, run) * maxOff
                        } else { thumbStart = off }
                    }
                    model.chartOffset = max(0, min(maxOff, (thumbStart ?? off) + v.translation.width / max(1, run) * maxOff))
                    model.changed()
                }.onEnded { _ in thumbStart = nil })
                .help("Drag to move through the day")
                .padding(.bottom, -2)
            }
        }
    }

    func draw(_ ctx: GraphicsContext, W: CGFloat, off: CGFloat, view: CGFloat) {
        var ctx = ctx
        ctx.translateBy(x: -off, y: 0)
        let s = e.samples
        let n = s.count
        guard n > 1 else { return }
        let all = e.all
        let H = self.H
        let yTop: CGFloat = 26
        let lo = (s.min() ?? 0) - 0.03
        let hi = (s.max() ?? 1) + 0.03
        let total = e.nextWake.timeIntervalSince(e.wake)
        func X(_ t: Date) -> CGFloat { return W * CGFloat(max(0, min(1, t.timeIntervalSince(e.wake) / total))) }
        func Y(_ v: Double) -> CGFloat { return yTop + CGFloat(1 - (v - lo) / (hi - lo)) * (H - 6 - yTop) }
        func label(_ text: String, _ size: CGFloat, _ color: Color, semi: Bool = false) -> Text {
            return Text(text).font(Fonts.ui(m.z(size), semi)).foregroundColor(color)
        }
        // a name stays in sight while the chart is scrolled: it follows the left edge inside its zone
        func pinX(_ x0: CGFloat, _ x1: CGFloat, _ lw: CGFloat) -> CGFloat { return max(x0 + 6, min(x1 - lw - 5, off + 6)) }

        var pts: [CGPoint] = []
        for i in 0..<n { pts.append(CGPoint(x: X(e.timeAt(i)), y: Y(s[i]))) }
        let xBed = X(e.timeAt(n - 1))
        var area = Path()
        area.move(to: CGPoint(x: 0, y: H))
        for p in pts { area.addLine(to: p) }
        area.addLine(to: CGPoint(x: xBed, y: H))
        area.closeSubpath()

        if all.count > n {
            // expected sleep: a sand block, with the curve carried on faintly to the next wake-up
            ctx.fill(Path(roundedRect: CGRect(x: xBed, y: 0, width: max(1, W - xBed), height: H), cornerRadius: 8), with: .color(T.sand))
            let lw = Fonts.width("sleep", m.z(9))
            ctx.draw(label("sleep", 9, T.text), at: CGPoint(x: pinX(xBed, W, lw), y: 4), anchor: .topLeading)
            var tail = Path()
            for i in (n - 1)..<all.count {
                let p = CGPoint(x: X(e.timeAt(i)), y: Y(max(lo + 0.01, all[i])))
                if i == n - 1 { tail.move(to: p) } else { tail.addLine(to: p) }
            }
            ctx.stroke(tail, with: .color(T.text.opacity(0.4)), style: StrokeStyle(lineWidth: 1.25, dash: [3.75, 3.75]))
        }
        ctx.fill(area, with: .color(T.ctl))
        var names: [(Text, CGPoint)] = []
        for z in [e.peak1, e.dip, e.peak2] {
            guard let z = z else { continue }
            let zx = X(z.from)
            let zw = max(1, X(z.to) - zx)
            var c2 = ctx
            c2.clip(to: Path(CGRect(x: zx, y: 0, width: zw, height: H)))
            c2.fill(area, with: .color(z.peak ? T.accent : T.dip))
            if zw >= 28 {
                let name = z.peak ? "peak" : "dip"
                let lw = Fonts.width(name, m.z(9))
                names.append((label(name, 9, z.peak ? T.onAccent : T.onDip), CGPoint(x: pinX(zx, zx + zw, lw), y: H - 15)))
            }
        }
        for nap in e.naps {
            let r = CGRect(x: X(nap.from), y: 0, width: max(3, X(nap.to) - X(nap.from)), height: H)
            ctx.fill(Path(roundedRect: r, cornerRadius: 2), with: .color(T.strong.opacity(0.35)))
        }
        var curve = Path()
        curve.addLines(pts)
        ctx.stroke(curve, with: .color(T.text), style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
        for (t, p) in names { ctx.draw(t, at: p, anchor: .topLeading) }

        if now >= e.wake && now <= e.bed {
            let x = X(now)
            let y = Y(e.at(now))
            var drop = Path()
            drop.move(to: CGPoint(x: x, y: y))
            drop.addLine(to: CGPoint(x: x, y: H))
            ctx.stroke(drop, with: .color(T.text.opacity(0.5)), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))
            let dot = Path(ellipseIn: CGRect(x: x - 5, y: y - 5, width: 10, height: 10))
            ctx.fill(dot, with: .color(T.field))
            ctx.stroke(dot, with: .color(T.text), lineWidth: 2.25)
            // a small dark pill with the time, above the dot
            let txt = EnergyDay.clock(now)
            let tw = Fonts.width(txt, m.z(9.5), true) + 14
            let th = m.z(9.5) + 5
            let px = max(0, min(W - tw, x - tw / 2))
            let py = max(0, y - 9 - th)
            ctx.fill(Path(roundedRect: CGRect(x: px, y: py, width: tw, height: th), cornerRadius: 8), with: .color(T.strong))
            ctx.draw(label(txt, 9.5, T.onStrong, semi: true), at: CGPoint(x: px + tw / 2, y: py + th / 2), anchor: .center)
        }
        // time axis: a label at every hour in the corner panel, every second or third in Full Mode
        let step: Double = !model.full ? 1 : (EnergyDay.use12 ? 3 : 2)
        let wakeH = e.wake.timeIntervalSince(e.wake.dateOnly) / 3600
        var tick = e.wake.dateOnly.addingHours((wakeH / step).rounded(.up) * step)
        while tick <= e.nextWake {
            let x = X(tick)
            let txt = EnergyDay.hourLabel(tick)
            let lw = Fonts.width(txt, m.z(10))
            ctx.draw(label(txt, 10, T.faint), at: CGPoint(x: max(0, min(W - lw, x - lw / 2)), y: H + 13), anchor: .topLeading)
            tick = tick.addingHours(step)
        }
        // the cursor: the time and the zone under the mouse
        if let cx = cursorX {
            let x = max(0, min(W, cx + off))
            let t = e.wake.addingTimeInterval(Double(x / W) * total)
            let v = e.allAt(t)
            var zone = t >= e.bed ? "sleep" : ""
            for z in [e.peak1, e.dip, e.peak2] { if let z = z, t >= z.from && t < z.to { zone = z.peak ? "peak" : "dip" } }
            let y = max(4, min(H - 4, Y(v)))
            var l = Path()
            l.move(to: CGPoint(x: x, y: 12))
            l.addLine(to: CGPoint(x: x, y: H))
            ctx.stroke(l, with: .color(T.text.opacity(0.6)), lineWidth: 1)
            if t < e.bed { ctx.fill(Path(ellipseIn: CGRect(x: x - 3.5, y: y - 3.5, width: 7, height: 7)), with: .color(T.text)) }
            let txt = EnergyDay.clock(t) + (zone.isEmpty ? "" : "  ·  " + zone)
            let tw = Fonts.width(txt, m.z(9.5), true) + 12
            let th = m.z(9.5) + 4
            let px = max(off, min(off + view - tw, x - tw / 2))
            ctx.fill(Path(roundedRect: CGRect(x: px, y: -3 + 3, width: tw, height: th), cornerRadius: 7), with: .color(T.field))
            ctx.draw(label(txt, 9.5, T.text, semi: true), at: CGPoint(x: px + tw / 2, y: th / 2), anchor: .center)
        }
    }
}

/// The pointer's x over a view, where the system can say (macOS 13); nothing on 12.
struct HoverX: ViewModifier {
    @Binding var x: CGFloat?
    func body(content: Content) -> some View {
        if #available(macOS 13.0, *) {
            content.onContinuousHover { phase in
                switch phase {
                case .active(let p): x = p.x
                case .ended: x = nil
                }
            }
        } else {
            content
        }
    }
}

// ---------------------------------------------------------------- slept / debt / need

/// Full Mode: the three figures are round icons until clicked; each opens its own box underneath, and closes it
/// again. Midway between them stand the nap icon and the rating.
struct StatIcons: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let gap: CGFloat = m.sp(8, 6)
        GeometryReader { g in
            let third: CGFloat = (g.size.width - 2 * gap) / 3
            let step: CGFloat = (third + gap) / 2
            ForEach(0..<5, id: \.self) { i in
                icon(i).position(x: third / 2 + CGFloat(i) * step, y: 15) // a figure is in the middle of its third
            }
        }
        .frame(height: 30)
    }

    @ViewBuilder func icon(_ i: Int) -> some View {
        switch i {
        case 0: RoundChip(d: Icons.moon, on: model.statOpen[0], tip: "Slept") { toggle(0) }
        case 1: RoundChip(d: Icons.nap, on: model.napOpen, tip: model.napOpen ? "Close" : "Log a nap") { model.napOpen.toggle(); model.changed(); model.shell?.contentChanged() }
        case 2: RoundChip(d: Icons.debt, on: model.statOpen[1], tip: "Debt") { toggle(1) }
        case 3: RoundChip(d: Icons.rate, on: false, tip: "Rate your energy now") { model.rateNowPressed() }
        default: RoundChip(d: Icons.bed, on: model.statOpen[2], tip: "Need") { toggle(2) }
        }
    }

    func toggle(_ i: Int) {
        model.statOpen[i].toggle()
        if i == 1 && !model.statOpen[1] { model.debtOpen = false }
        model.changed()
        model.shell?.contentChanged()
    }
}

/// The boxes of the figures that are open, each under its icon (an empty place keeps the others where they are).
struct StatsRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    var body: some View {
        let now = AppModel.now()
        let night = e.last
        let sleep = night?.sleep ?? []
        var sleptSub = sleep.count > 1 ? String(sleep.count) + " periods" : (sleep.first.map { EnergyDay.clock($0.from) + "–" + EnergyDay.clock($0.to) } ?? "")
        let napH = e.naps.reduce(0.0) { $0 + $1.hours }
        if !e.naps.isEmpty { sleptSub += " + " + AppModel.durShort(napH) + " nap" }
        if let nt = night, nt.assumed || nt.wokeAt.dateOnly != SleepLog.today(now) { sleptSub += " (assumed)" }
        let sleptShort = sleep.count > 1 ? String(sleep.count) + " periods" : (sleep.first.map { "woke " + EnergyDay.clock($0.to) } ?? "")
        let debt = e.debtHours >= 0.25
        let open: [Bool] = model.full ? model.statOpen : [true, true, true]
        return HStack(alignment: .top, spacing: m.sp(8, 6)) {
            if open[0] {
                Stat(icon: Icons.moon, caption: "Slept", value: AppModel.durShort(night?.hours ?? 0),
                     sub: m.slim ? sleptShort : sleptSub, outlined: false, tip: sleptSub + "  ·  click to correct") { model.openCheckIn(edit: true) }
            } else {
                StatGap()
            }
            if open[1] {
                Stat(icon: Icons.debt, caption: "Debt", value: debt ? AppModel.durShort(e.debtHours) : "none",
                     sub: (m.slim ? "" : "over ") + String(EnergyModel.debtNights) + " nights", outlined: model.debtOpen, tip: "Sleep debt calculator") {
                    model.debtOpen.toggle()
                    model.changed()
                    model.shell?.contentChanged()
                }
            } else {
                StatGap()
            }
            if open[2] {
                Stat(icon: Icons.bed, caption: "Need", value: AppModel.durShort(e.need),
                     sub: nil, outlined: false, tip: model.needHint?.note, steps: true, action: nil)
            } else {
                StatGap()
            }
        }
    }
}

/// The place of a figure whose box is closed.
struct StatGap: View {
    var body: some View { Color.clear.frame(maxWidth: .infinity).frame(height: 1) }
}

struct Stat: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let icon: String
    let caption: String
    let value: String
    let sub: String?
    let outlined: Bool
    let tip: String?
    var steps = false
    let action: (() -> Void)?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Glyph(d: icon, color: T.muted, th: 1.4, w: 14, h: 14).padding(.bottom, m.sp(4, 3))
            Text(value).font(m.head(m.sp(17, 15))).foregroundColor(T.text).lineLimit(1)
            Text(caption).font(m.font(10.5)).foregroundColor(T.muted).padding(.top, 1)
            Group {
                if steps {
                    HStack(spacing: 4) {
                        StepBtn(sign: "–", height: m.sp(18, 16)) { model.stepNeed(-0.25) }
                        StepBtn(sign: "+", height: m.sp(18, 16)) { model.stepNeed(0.25) }
                    }
                } else if let s = sub {
                    Text(s).font(m.font(10)).foregroundColor(T.muted).lineLimit(1).truncationMode(.tail)
                }
            }
            .frame(minHeight: m.sp(20, 18), alignment: .leading)
            .padding(.top, 1).padding(.bottom, 2)
        }
        .padding(.leading, m.padX - 3.5).padding(.trailing, m.padX - 5.5)
        .padding(.top, m.padY - 3.5).padding(.bottom, m.padY - 5.5)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(boxShape(m.sp(18, 16)).stroke(outlined ? T.strong : Color.clear, lineWidth: 1.5))
        .plate(.field, radius: m.sp(18, 16), rim: m.sp(18, 16))
        .contentShape(Rectangle())
        .onTapGesture { action?() }
        .help(tip ?? "")
    }
}

/// The sleep need the check-ins point to, offered once it differs from the setting by 15 minutes or more.
struct NeedHintCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    var body: some View {
        let cur = e.need
        if let h = model.needHint, h.ready, abs(h.need - cur) >= 0.249, abs(Settings.get("NeedDeclined", 0) - h.need) >= 0.249 {
            let d = AppModel.durShort
            let text = message(h, cur)
            CardBox(surface: .tray) {
                Caps(text: "Sleep need")
                Text(text).font(m.font(12)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 6).padding(.bottom, 9)
                HStack(spacing: 6) {
                    Btn(text: "Use " + d(h.need), kind: 0) { Settings.set("SleepNeed", h.need); model.refreshEnergy(opening: false) }
                    Btn(text: "Keep " + d(cur), kind: 2) { Settings.set("NeedDeclined", h.need); model.changed() }
                    Spacer(minLength: 0)
                }
            }
        }
    }

    func message(_ h: NeedEstimate.Result, _ cur: Double) -> String {
        let d = AppModel.durShort
        let with = EnergyModel.build(SleepLog.data.nights, need: h.need, day: SleepLog.today(AppModel.now()))
        var text = "Your check-ins point to a sleep need of about " + d(h.need)
        if h.high - h.low > 0.01 { text += " (likely " + d(h.low) + " to " + d(h.high) + ")" }
        text += ", from " + String(h.days) + " days. It is set to " + d(cur) + "."
        if let w = with {
            text += " With " + d(h.need) + " your sleep debt would read " + (w.debtHours >= 0.25 ? d(w.debtHours) : "none")
                + " instead of " + (e.debtHours >= 0.25 ? d(e.debtHours) : "none") + "."
        }
        return text
    }
}

// ---------------------------------------------------------------- the sleep debt calculator

struct DebtCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    var body: some View {
        let now = AppModel.now()
        let day = SleepLog.today(now)
        let need = e.need
        let span = EnergyModel.debtNights
        let nights = SleepLog.data.nights.filter { $0.valid && $0.wokeAt.dateOnly <= day && $0.wokeAt.dateOnly > day.addingDays(-span) }
        let top = need + 2
        let BH: CGFloat = 46
        var shortNights = 0
        var bars: [(h: CGFloat, dark: Bool, faint: Bool, tip: String, empty: Bool)] = []
        for i in stride(from: span - 1, through: 0, by: -1) {
            let d = day.addingDays(-i)
            guard let n = nights.last(where: { $0.wokeAt.dateOnly == d }) else { bars.append((2, false, false, "", true)); continue }
            let isShort = n.totalHours < need - 0.01
            if isShort { shortNights += 1 }
            var tip: String = HeaderView.fmt("EEE d MMM", d) + ": slept " + AppModel.durShort(n.totalHours)
            if isShort { tip += ", " + AppModel.mins((need - n.totalHours) * 60) + " short" }
            if n.assumed { tip += " (assumed)" }
            bars.append((max(3, CGFloat(min(1, n.totalHours / top)) * BH), isShort, n.assumed, tip, false))
        }
        let debt = e.debtHours
        return CardBox(surface: .field) {
            Caps(text: "Sleep debt calculator").padding(.bottom, 8)
            ZStack(alignment: .bottom) {
                HStack(alignment: .bottom, spacing: 0) {
                    ForEach(0..<bars.count, id: \.self) { i in
                        let b = bars[i]
                        Group {
                            if b.empty {
                                RoundedRectangle(cornerRadius: 1).fill(T.ctl).frame(width: 2, height: 2)
                            } else {
                                RoundedRectangle(cornerRadius: 2).fill(b.dark ? T.strong : T.ctl)
                                    .opacity(b.faint ? 0.5 : 1)
                                    .frame(height: b.h)
                                    .padding(.horizontal, 2)
                                    .help(b.tip)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .bottom)
                    }
                }
                .frame(height: BH, alignment: .bottom)
                Rectangle().fill(T.text.opacity(0.35)).frame(height: 1).padding(.bottom, CGFloat(need / top) * BH)
            }
            .frame(height: BH)
            .padding(.bottom, 4)
            Text("Last " + String(span) + " days · the line is your " + AppModel.durShort(need) + " need · " + String(nights.count) + " logged, " + String(shortNights) + " short")
                .font(m.font(10.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true).padding(.bottom, 8)
            if debt < 0.25 {
                Text("No sleep debt. Nothing to pay back.").font(m.font(12)).foregroundColor(T.text)
            } else {
                payBack(debt, need, now)
            }
        }
    }

    static func clearLine(_ n: Int, _ amount: String, _ by: Date, _ wake: Date) -> String {
        let nights: String = n == 1 ? " night" : " nights"
        var s: String = "Cleared in " + String(n) + nights + " of " + amount
        s += " sleep: asleep by about " + EnergyDay.clock(by) + " to wake at " + EnergyDay.clock(wake) + "."
        return s
    }

    static func totalLine(_ debt: Double, _ est: Double, _ logged: Int, _ unlogged: Int) -> String {
        let mins = AppModel.mins
        if !(est >= 0.01 && debt - est >= 0) { return "You are " + mins(debt * 60) + " short in total." }
        var s: String = "You are about " + mins(debt * 60) + " short in total: "
        s += mins((debt - est) * 60) + " over the " + String(logged) + " nights logged, "
        let nights: String = unlogged == 1 ? " night" : " nights"
        s += "and about " + mins(est * 60) + " for the " + String(unlogged) + nights + " not logged, counted as an average night."
        return s
    }

    @ViewBuilder func payBack(_ debt: Double, _ need: Double, _ now: Date) -> some View {
        let unlogged = EnergyModel.debtNights - e.debtLogged
        let est = e.debtEstimate
        let total = DebtCard.totalLine(debt, est, e.debtLogged, unlogged)
        let extra = model.debtExtra
        let nightsToClear = Int(ceil(debt * 60 / Double(extra)))
        let last = e.last
        let awake = last.map { max(0, $0.wokeAt.timeIntervalSince($0.asleepAt) / 3600 - $0.hours) } ?? 0
        let asleepBy = e.wake.addingHours(-(need + awake)).addingMinutes(-extra)
        let tip: String = {
            guard let dip = e.dip else { return "Recharge: a nap of about 20 minutes in the early afternoon pays back 20 minutes and lifts the hours after it." }
            if now < dip.to {
                return "Recharge in the dip: a nap of about 20 minutes between " + EnergyDay.clock(dip.from) + " and " + EnergyDay.clock(dip.to)
                    + " pays back 20 minutes and lifts the hours after it. Keep it under 30 minutes and finished by "
                    + EnergyDay.clock(now.dateOnly.addingHours(EnergyModel.lateNapHour)) + ", or tonight's sleep moves later. Use \"Log a nap\" afterwards so it counts."
            }
            return "Today's dip has passed. A late nap would push tonight's sleep later, so an earlier bedtime is the better way to pay back now."
        }()
        Text(total + " Longer nights pay it back; shorter ones add to it.").font(m.font(12)).foregroundColor(T.text)
            .fixedSize(horizontal: false, vertical: true).padding(.bottom, 6)
        HStack(spacing: 0) {
            Text("Sleep").font(m.font(11.5)).foregroundColor(T.muted)
            StepBtn(sign: "–") { model.debtExtra = max(15, model.debtExtra - 15); model.changed() }.padding(.leading, 8)
            Text(String(extra) + " min").font(m.font(11.5, true)).foregroundColor(T.text).frame(width: 52)
            StepBtn(sign: "+") { model.debtExtra = min(120, model.debtExtra + 15); model.changed() }.padding(.trailing, 8)
            Text("more a night").font(m.font(11.5)).foregroundColor(T.muted)
            Spacer(minLength: 0)
        }
        .padding(.bottom, 6)
        Text(DebtCard.clearLine(nightsToClear, AppModel.durShort(need + Double(extra) / 60), asleepBy, e.wake))
            .font(m.font(11.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true).padding(.bottom, 8)
        Text(tip).font(m.font(11.5)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
            .padding(.horizontal, 11).padding(.top, 8).padding(.bottom, 9)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(T.raised))
    }
}

// ---------------------------------------------------------------- naps

/// What the nap icon opens, the naps logged today, and the offer to plan again after one.
struct NapsBlock: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    static func shown(_ model: AppModel, _ e: EnergyDay) -> Bool {
        return model.napOpen || !e.naps.isEmpty || (model.napOffer && !AppModel.priorityMode)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: m.gapY) {
            if model.napOpen {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 6) {
                        TimeEntryView(e: model.napFrom, onSubmit: { model.addNapFromBoxes() })
                        TimeEntryView(e: model.napTo, onSubmit: { model.addNapFromBoxes() })
                        Btn(text: "Add nap", kind: 0) { model.addNapFromBoxes() }
                        Spacer(minLength: 0)
                    }
                    HStack(spacing: 6) {
                        Text("or just woke, slept").font(m.font(11.5)).foregroundColor(T.muted)
                        MiniBox(text: Binding(get: { model.napJust }, set: { model.napJust = $0; model.napJustBad = false }), width: 24, bad: model.napJustBad,
                                onSubmit: { model.addNapJustWoke() })
                        Text("min").font(m.font(11.5)).foregroundColor(T.muted)
                        Btn(text: "Log", kind: 1) { model.addNapJustWoke() }
                        Spacer(minLength: 0)
                    }
                }
            }
            ForEach(Array(e.naps.enumerated()), id: \.offset) { pair in
                let nap = pair.element
                HStack(spacing: 0) {
                    Circle().fill(T.strong).frame(width: 6, height: 6).padding(.trailing, 8).padding(.top, 1)
                    Text("Nap " + EnergyDay.clock(nap.from) + "–" + EnergyDay.clock(nap.to)).font(m.font(12)).foregroundColor(T.text)
                    Spacer(minLength: 0)
                    Btn(text: "Remove", kind: 2) { model.removeNap(nap) }
                }
                .padding(.leading, m.padX).padding(.trailing, m.padX - m.sp(6, 5)).padding(.vertical, 3)
                .plate(.field)
            }
            if model.napOffer && model.full && !AppModel.priorityMode {
                HStack(spacing: 4) {
                    Text("Re-plan what hasn't started?").font(m.font(11.5)).foregroundColor(T.muted)
                    Spacer(minLength: 0)
                    Btn(text: "No", kind: 2) { model.napOffer = false; model.changed() }
                    Btn(text: "Re-plan", kind: 0) { model.napOffer = false; model.makePlan(keepStarted: true) }
                }
                .padding(.leading, m.padX)
            }
        }
    }
}

// ---------------------------------------------------------------- the plan: a white card under the add box

/// The plan is a plain white card: the time it fills and its buttons, then (once there is a proposal) the proposed
/// tasks and a pale-lime foot with the key to the ticks. An applied plan is named on the task list instead, by the
/// upright strip beside the tasks it placed (TasksView.swift). With no sleep entered there is no curve to plan on,
/// and no card.
struct PlanCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        if let e = model.energy { card(e) }
    }

    func card(_ e: EnergyDay) -> some View {
        // "Plan by: Priority" (Settings): what comes after what, with no clock times
        let prio: Bool = AppModel.priorityMode
        let r: CGFloat = m.radius
        let proposed: Bool = prio ? model.orderProposal != nil : model.proposal != nil
        let placed: Bool = model.proposal?.contains { $0.placed } ?? false
        let key: Bool = placed && !prio
        return VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 0) {
                if proposed { Caps(text: prio ? "Proposed order" : "Proposed plan").padding(.bottom, 6) }
                if prio { OrderPlan() } else { SchedulePlan(e: e) }
            }
            .padding(.horizontal, m.padX).padding(.vertical, m.padY)
            if key { PlanKey() }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        // Sage and Glass: the card's ground is cut to shape together with its foot. A made skin: the ground is a
        // box of its own behind them, with its shadow and bevel
        .background(T.made ? Color.clear : T.field)
        .clipShape(boxShape(r))
        .plate(T.made ? .field : .clear, radius: r, rim: r)
    }
}

/// The key to the ticks of a proposed schedule, in the card's foot.
struct PlanKey: View {
    @Environment(\.m) var m

    var body: some View {
        HStack(spacing: 0) {
            ForEach(0..<3, id: \.self) { i in
                let names = ["on a peak", "in the dip", "between"]
                let fills = [T.accent, T.strong, T.ctl]
                RoundedRectangle(cornerRadius: 2.5).fill(fills[i])
                    .overlay(RoundedRectangle(cornerRadius: 2.5).stroke(T.muted, lineWidth: i == 1 ? 0 : 0.75))
                    .frame(width: 5, height: 11).padding(.trailing, 5)
                Text(names[i]).font(m.font(10)).foregroundColor(T.muted).padding(.trailing, 12)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, m.padX).padding(.top, 6).padding(.bottom, 7)
        .background(T.soft)
    }
}

/// Plan by schedule: the time Plan fills and its buttons, then the proposed tasks with their start times.
struct SchedulePlan: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let e: EnergyDay

    var body: some View {
        let now = AppModel.now()
        let planEnd: Date = Planner.until(e, now)
        let from: String = now < e.wake ? EnergyDay.clock(e.wake) : "now"
        let window: String = now >= planEnd ? "Too close to bedtime to plan" : "Plans from " + from + " to " + EnergyDay.clock(planEnd)
        let p: [PlanItem]? = model.proposal
        let placedAny: Bool = p?.contains { $0.placed } ?? false
        return VStack(alignment: .leading, spacing: 0) {
            // the time Plan fills on the left, what can be done on the right
            HStack(alignment: .center, spacing: 0) {
                Text(window).font(m.font(11.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                    .padding(.trailing, 10)
                Spacer(minLength: 0)
                HStack(spacing: 4) {
                    if p == nil {
                        if model.planUndo != nil { Btn(text: "Undo plan", kind: 2, on: 1) { model.undoPlan() } }
                        Btn(text: "Plan my day", kind: 0, on: 1) { model.makePlan() }
                    } else {
                        Btn(text: "Discard", kind: 2, on: 1) { model.discardPlan() }
                        if placedAny { Btn(text: "Apply", kind: 0, on: 1) { model.applyPlan() } }
                    }
                }
            }
            .padding(.bottom, p == nil ? 0 : 6)
            if let items = p { PlanRows(p: items, planEnd: planEnd) }
        }
    }
}

struct PlanRows: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let p: [PlanItem]
    let planEnd: Date

    var body: some View {
        let why: String? = note()
        let timeW: CGFloat = Fonts.width(EnergyDay.use12 ? "12:00pm" : "00:00", m.z(11.5), true) + 10
        let items: [PlanItem] = p.sorted { a, b in
            if a.placed != b.placed { return a.placed }
            return a.start < b.start
        }
        return VStack(alignment: .leading, spacing: 0) {
            if let w = why {
                Text(w).font(m.font(12)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true).padding(.bottom, 4)
            }
            ForEach(Array(items.enumerated()), id: \.offset) { pair in
                PlanRowView(it: pair.element, timeW: timeW)
            }
        }
    }

    func note() -> String? {
        if p.isEmpty {
            let anyOpen: Bool = model.data.tasks.contains { !$0.done }
            return anyOpen
                ? "Nothing to plan: your open tasks either have a time you set or belong to a later day. Clear a task's time to let Plan place it."
                : "Nothing to plan yet: add a few tasks first, with an effort level (!high, !med, !low) and a length (45m)."
        }
        let placedAny: Bool = p.contains { $0.placed }
        if !placedAny { return "None of these fit before " + EnergyDay.clock(planEnd) + ". Shorten them, or plan again tomorrow." }
        return nil
    }
}

/// One task of the proposed schedule: the zone's tick, its start, the task, and its effort and length. Under a
/// high task that could go on both peaks as two sessions: the offer to split it.
struct PlanRowView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let it: PlanItem
    let timeW: CGFloat

    var body: some View {
        let title: String = it.splitOf > 0 ? AppModel.splitTitle(it.title, it.splitOf) : it.title
        let ask: Bool = (it.canSplit && !model.planKeep.contains(it.id)) || it.splitOf == 1
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                RoundedRectangle(cornerRadius: 2).fill(it.placed ? model.zoneColor(it.start) : T.ctl).frame(width: 4, height: 14)
                    .frame(width: 12, alignment: .leading)
                Text(it.placed ? EnergyDay.clock(it.start) : "—").font(m.font(11.5, true)).foregroundColor(it.placed ? T.text : T.faint)
                    .frame(width: timeW, alignment: .leading)
                Text(title).font(m.font(12)).foregroundColor(it.placed ? T.text : T.muted).lineLimit(1).truncationMode(.tail)
                Spacer(minLength: 0)
                Text(it.placed ? tags() : "no room today").font(m.font(10.5)).foregroundColor(T.muted).padding(.leading, 8).lineLimit(1).fixedSize()
            }
            .padding(.top, 3).padding(.bottom, 4)
            if ask { SplitAsk(it: it) }
        }
    }

    func tags() -> String {
        var bits: [String] = []
        if let s = TaskTags.short(it.level) { bits.append(s) }
        if it.physical { bits.append("phys") }
        bits.append(TaskTags.len(it.minutes))
        return bits.joined(separator: " · ")
    }
}

/// Under a high task's row in the proposed plan: split it across both peaks, or keep it as one session?
struct SplitAsk: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let it: PlanItem

    var body: some View {
        Group {
            if it.splitOf == 1 { done } else { offer }
        }
        .padding(.leading, 10).padding(.trailing, 8).padding(.top, 7).padding(.bottom, 8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(T.soft))
        .padding(.leading, 12).padding(.top, 1).padding(.bottom, 6)
    }

    /// Already split: one line, with the way back.
    var done: some View {
        let id: String = it.id
        return HStack(spacing: 8) {
            Text("Split across both peaks.").font(m.font(11.5)).foregroundColor(T.muted)
            Spacer(minLength: 0)
            Btn(text: "Keep as one", kind: 2, on: 1) { model.splitInPlan(id, false) }
        }
    }

    var offer: some View {
        let id: String = it.id
        let msg: String = it.placed
            ? "Doesn't fit in what's left of your first peak as one session, so it went to " + EnergyDay.clock(it.start) + ". Split it in two instead?"
            : "No room for it as one session. Split it in two?"
        let first: String = TaskTags.len(it.splitA) + " at " + EnergyDay.clock(it.splitAt1)
        let second: String = TaskTags.len(it.splitB) + " at " + EnergyDay.clock(it.splitAt2)
        return VStack(alignment: .leading, spacing: 0) {
            Text(msg).font(m.font(11.5)).foregroundColor(T.text).fixedSize(horizontal: false, vertical: true)
            Text(first + "  ·  " + second).font(m.font(11, true)).foregroundColor(T.muted).padding(.top, 2)
            HStack(spacing: 4) {
                Spacer(minLength: 0)
                Btn(text: "Keep as one", kind: 2, on: 1) { model.splitInPlan(id, false) }
                Btn(text: "Split in two", kind: 0, on: 1) { model.splitInPlan(id, true) }
            }
            .padding(.top, 6)
        }
    }
}

/// Plan by priority: what comes after what, each with its reason. No clock times.
struct OrderPlan: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let p: [Ranked]? = model.orderProposal
        let hasRows: Bool = !(p?.isEmpty ?? true)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 0) {
                Text("Puts today's tasks in order for your energy now").font(m.font(11.5)).foregroundColor(T.muted)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.trailing, 10)
                Spacer(minLength: 0)
                HStack(spacing: 4) {
                    if p == nil {
                        if model.orderToday { Btn(text: "Undo plan", kind: 2, on: 1) { model.undoOrder() } }
                        Btn(text: "Plan my day", kind: 0, on: 1) { model.makePlan() }
                    } else {
                        Btn(text: "Discard", kind: 2, on: 1) { model.discardOrder() }
                        if hasRows { Btn(text: "Apply", kind: 0, on: 1) { model.applyOrder() } }
                    }
                }
            }
            .padding(.bottom, p == nil ? 0 : 6)
            if let items = p { OrderRows(p: items) }
        }
    }
}

struct OrderRows: View {
    @Environment(\.m) var m
    let p: [Ranked]

    var body: some View {
        let numW: CGFloat = Fonts.width("00", m.z(11.5), true) + 8
        return VStack(alignment: .leading, spacing: 0) {
            if p.isEmpty {
                Text("Nothing to order yet: add a few tasks first, with an effort level (!high, !med, !low) and a length (45m).")
                    .font(m.font(12)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true).padding(.bottom, 4)
            }
            ForEach(Array(p.enumerated()), id: \.offset) { pair in
                HStack(spacing: 0) {
                    Text(String(pair.offset + 1)).font(m.font(11.5, true)).foregroundColor(T.text)
                        .frame(width: numW, alignment: .leading)
                    Text(pair.element.task.title).font(m.font(12)).foregroundColor(T.text).lineLimit(1).truncationMode(.tail)
                    Spacer(minLength: 0)
                    Text(OrderRows.tag(pair.element)).font(m.font(10.5)).foregroundColor(T.muted)
                        .padding(.leading, 8).lineLimit(1).fixedSize()
                }
                .padding(.top, 3).padding(.bottom, 4)
            }
        }
    }

    /// Why it stands where it does; or, with no reason to give, its effort and length.
    static func tag(_ it: Ranked) -> String {
        if let w = it.why { return w }
        var bits: [String] = []
        if let lv = TaskTags.level(it.task.energy), let s = TaskTags.short(lv) { bits.append(s) }
        bits.append(TaskTags.len(AppModel.durationMin(it.task)))
        return bits.joined(separator: " · ")
    }
}

// ---------------------------------------------------------------- routines

/// The line that names a box that opens: an arrow, the name in small capitals, a count.
struct SectionHead: View {
    @Environment(\.m) var m
    let text: String
    let count: String
    let open: Bool

    var body: some View {
        HStack(spacing: 0) {
            Glyph(d: Icons.chevron, color: T.muted, th: 1.4, w: 8, h: 8)
                .rotationEffect(.degrees(open ? 90 : 0))
            Text(text.uppercased()).font(m.font(10, true)).foregroundColor(T.muted).lineLimit(1)
                .padding(.leading, 9).padding(.trailing, 6)
            Text(count).font(m.font(m.sp(10.5, 10))).foregroundColor(T.faint)
        }
        .frame(minHeight: m.sp(24, 22))
        .contentShape(Rectangle())
    }
}

/// Closed, it is one slim line, no taller than the buttons above it; open, the routines and the box to add one.
struct RoutinesCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @FocusState private var editFocus: Bool

    var body: some View {
        let all = model.routines
        let waiting = model.routinesWaiting()
        let open: Bool = model.routinesOpen
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                SectionHead(text: "Routines", count: String(all.count), open: open)
                    .onTapGesture { model.routinesOpen.toggle(); model.editRoutine = nil; model.changed(); model.shell?.contentChanged() }
                Spacer(minLength: 0)
                if !waiting.isEmpty {
                    Btn(text: waiting.count == 1 ? "Add 1 to today" : "Add " + String(waiting.count) + " to today", kind: 1) { _ = model.addRoutinesToToday(waiting) }
                        .help("Send today's routines to your tasks, so Plan my day can place them")
                }
            }
            if open {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(all.enumerated()), id: \.element.id) { pair in
                        RoutineRow(i: pair.offset, r: pair.element, all: all, editFocus: $editFocus)
                    }
                    NewRoutineBox()
                }
                .padding(.top, 6)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, m.padX)
        .padding(.vertical, open ? m.padY : m.sp(5, 4))
        .plate(.field, radius: m.radius, rim: m.radius)
    }
}

/// Full Mode: the done tasks, under the routines. It is the Routines box again in the pale shade: one slim line
/// while closed, the done tasks under it when open ("Clear" removes them). Today's by default; the arrows in its
/// head step back through the days before (from done.csv and work.csv, so a day stays after "Clear") and forward
/// again to today. The head also says the day's time worked.
struct DoneCard: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @State private var clearHot = false

    var body: some View {
        let done: [TaskItem] = model.doneToday()
        let past: [Date] = model.pastDays()
        if !done.isEmpty || !past.isEmpty { card(done, past) }
    }

    static func dayName(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = CornerTime.calendar
        f.timeZone = CornerTime.calendar.timeZone
        f.dateFormat = "EEE d MMM"
        return f.string(from: d)
    }

    func card(_ done: [TaskItem], _ past: [Date]) -> some View {
        let open: Bool = model.data.doneOpen
        var shown: Date? = model.doneDay
        if let d = shown, !past.contains(d) { shown = nil }
        let isToday: Bool = shown == nil
        let day: Date = shown ?? SleepLog.today(Date())
        let items: [History.Item] = isToday ? [] : History.of(WorkLog.rows, DoneHistory.doneRows(), day)
        let n: Int = isToday ? done.count : items.count
        let worked: Double = History.worked(WorkLog.rows, day) // all the day's timed work, finished or not
        let count: String = String(n) + (worked >= 0.5 ? "  ·  " + WorkLog.dur(worked) + " worked" : "")
        let canBack: Bool = past.contains { $0 < day }
        let label: String = isToday ? "Today" : DoneCard.dayName(day)
        let anyDone: Bool = model.data.tasks.contains { $0.done }
        let empty: String = isToday ? "Nothing finished yet today." : "Nothing finished that day."
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 0) {
                SectionHead(text: "Done", count: count, open: open)
                Spacer(minLength: 0)
                if open {
                    DayArrow(back: true, on: canBack) { model.doneStep(-1) }
                    Text(label).font(m.font(11.5)).foregroundColor(T.text).lineLimit(1).fixedSize()
                    DayArrow(back: false, on: !isToday) { model.doneStep(1) }
                    if isToday && anyDone {
                        Text("Clear").font(m.font(11.5)).foregroundColor(clearHot ? T.danger : T.muted)
                            .padding(.leading, 8).padding(.vertical, 2)
                            .contentShape(Rectangle())
                            .onHover { clearHot = $0 }
                            .onTapGesture { model.clearDone() }
                    }
                }
            }
            .contentShape(Rectangle())
            .onTapGesture { model.toggleDoneOpen() }
            if open {
                VStack(alignment: .leading, spacing: 0) {
                    if isToday {
                        ForEach(done, id: \.id) { t in TaskRow(t: t, dragging: false) }
                    } else {
                        ForEach(Array(items.enumerated()), id: \.offset) { pair in HistoryRow(it: pair.element) }
                    }
                    if n == 0 {
                        Text(empty).font(m.font(12)).foregroundColor(T.muted)
                            .padding(.horizontal, rowOver).padding(.vertical, 4)
                    }
                }
                .padding(.horizontal, -rowOver) // (a row reaches rowOver past its content)
                .padding(.vertical, m.sp(4, 3))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, m.padX)
        .padding(.vertical, m.sp(5, 4))
        .plate(.soft, radius: m.radius, rim: m.radius)
    }
}

/// The Done card's "the day before" and "the day after"; faint when there is none.
struct DayArrow: View {
    let back: Bool
    let on: Bool
    let action: () -> Void

    var body: some View {
        Glyph(d: back ? "M5.5,1 L2.5,4 L5.5,7" : "M2.5,1 L5.5,4 L2.5,7", color: T.text, th: 1.5, w: 8, h: 8)
            .opacity(on ? 1 : 0.3)
            .padding(.horizontal, 7).padding(.vertical, 5)
            .contentShape(Rectangle())
            .onTapGesture { if on { action() } }
            .help(on ? (back ? "The day before" : "The day after") : "")
    }
}

/// A finished task of an earlier day: the look of a done row, but only to read.
struct HistoryRow: View {
    @Environment(\.m) var m
    let it: History.Item

    var body: some View {
        let rowH: CGFloat = m.sp(34, 26)
        let ck: CGFloat = m.sp(18, 15)
        let note: String = WorkLog.note(it.span, it.at, it.timerMinutes)
        HStack(alignment: .top, spacing: 0) {
            ZStack {
                Circle().fill(T.ctl)
                Glyph(d: "M5,9.4 L7.9,12.2 L13.2,6.2", color: T.text, th: 1.8, w: 18, h: 18)
            }
            .frame(width: 18, height: 18)
            .scaleEffect(ck / 18)
            .frame(width: ck, height: ck)
            .padding(.leading, rowOver)
            .frame(width: rowOver + ck + m.sp(10, 8), height: rowH, alignment: .leading)
            Text(it.title)
                .font(m.font(13.5))
                .foregroundColor(T.muted)
                .strikethrough(true, color: T.muted)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, m.sp(8, 5)).padding(.bottom, m.sp(9, 6)).padding(.trailing, m.sp(6, 4))
                .frame(maxWidth: .infinity, minHeight: rowH, alignment: .leading)
            Text(note).font(m.font(11)).foregroundColor(T.muted).lineLimit(1).fixedSize()
                .padding(.leading, 8).padding(.bottom, 1)
                .frame(height: rowH)
            Color.clear.frame(width: rowOver, height: 1)
        }
        .frame(minHeight: rowH)
    }
}

struct RoutineRow: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let i: Int
    let r: Routine
    let all: [Routine]
    var editFocus: FocusState<Bool>.Binding
    @State private var delHot = false
    @State private var sendHot = false

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            Text(String(i + 1)).font(m.font(11)).foregroundColor(T.faint).frame(width: 18, alignment: .leading).padding(.top, 1)
            if model.editRoutine == r.id {
                TextField("", text: Binding(get: { model.editRoutineText }, set: { model.editRoutineText = $0 }),
                          onCommit: { model.commitEditRoutine(r, keep: true) })
                    .textFieldStyle(.plain)
                    .font(m.font(12))
                    .foregroundColor(T.text)
                    .focused(editFocus)
                    .onExitCommand { model.commitEditRoutine(r, keep: false) }
                    .onAppear { DispatchQueue.main.async { model.shell?.window.makeKey(); editFocus.wrappedValue = true } }
                    .padding(.horizontal, 9).padding(.top, 4).padding(.bottom, 5)
                    .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(T.raised))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).stroke(T.strong, lineWidth: 1.5))
            } else {
                VStack(alignment: .leading, spacing: 1) {
                    Text(r.title).font(m.font(12)).foregroundColor(r.off ? T.muted : T.text).fixedSize(horizontal: false, vertical: true)
                    Text(Routines.summary(r, all)).font(m.font(10.5)).foregroundColor(T.muted).fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
                .onTapGesture { model.beginEditRoutine(r) }
                let inToday = model.routineInToday(r)
                Glyph(d: inToday ? "M1.5,5.5 L4,8 L8.5,2" : "M5,1 V9 M1,5 H9", color: inToday ? T.muted : (sendHot ? T.text : T.faint), th: 1.4, w: 10, h: 10)
                    .frame(width: 22, height: 18)
                    .contentShape(Rectangle())
                    .onHover { sendHot = $0 }
                    .onTapGesture { if !inToday { _ = model.addRoutinesToToday([r]) } }
                    .help(inToday ? "In today's tasks" : "Add to today's tasks")
                Glyph(d: Icons.cross, color: delHot ? T.danger : T.faint, th: 1.3, w: 10, h: 10)
                    .frame(width: 18, height: 18, alignment: .trailing)
                    .contentShape(Rectangle())
                    .onHover { delHot = $0 }
                    .onTapGesture { model.deleteRoutine(r) }
            }
        }
        .padding(.top, 3).padding(.bottom, 5)
    }
}

/// "Add a routine": the title, then its effort (click the bars) and length in the box itself.
struct NewRoutineBox: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m

    var body: some View {
        let efforts: [String?] = [nil, "low", "medium", "high"]
        let names = ["not set", "low", "medium", "high"]
        let lvl = max(0, efforts.firstIndex(where: { $0 == model.newRoutineEnergy }) ?? 0)
        return HStack(spacing: 0) {
            HintField(hint: "Add a routine", text: Binding(get: { model.newRoutineText }, set: { model.newRoutineText = $0 }), size: 12,
                      onSubmit: { model.addRoutine() })
            Bars(level: lvl)
                .frame(width: 24, height: 20)
                .contentShape(Rectangle())
                .onTapGesture { model.cycleNewRoutineEnergy() }
                .help("Effort: " + names[lvl] + " (click to change)")
                .padding(.leading, 6).padding(.trailing, 4)
            MiniBox(text: Binding(get: { model.newRoutineMin }, set: { model.newRoutineMin = $0; model.newRoutineBad = false }), width: 26,
                    bad: model.newRoutineBad, onSubmit: { model.addRoutine() })
                .help("Length in minutes")
            Text("min").font(m.font(11)).foregroundColor(T.muted).padding(.leading, 5).padding(.trailing, 8)
        }
        .padding(.leading, 12).padding(.trailing, 5).padding(.vertical, 4)
        .plate(.raised)
        .padding(.top, 4).padding(.bottom, 2)
    }
}
