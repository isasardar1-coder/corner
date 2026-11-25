// Corner for macOS - Full Mode's sections: two columns of them, each of which can be dragged to another place in
// either column by the dots at its right edge. To begin with (Settings has "Reset"):
//   tasks column:  the open tasks, add a task, then Plan
//   energy column: the chart, the icons (slept, nap, debt, rate, need) with what they open, routines, the done tasks
// "Energy left" in Settings swaps the two columns as they are. Port of FullLayout.cs.
import AppKit
import SwiftUI
import CornerCore

/// The sections and where each stands, in settings.json under the same names as on Windows: the column (0 tasks,
/// 1 energy) times 100, plus its place in it.
enum Sec {
    static let list = 0, add = 1, plan = 2, chart = 3, stats = 4, routines = 5, done = 6, count = 7
    static let keys: [String] = ["SecList", "SecAdd", "SecPlan", "SecChart", "SecStats", "SecRoutines", "SecDone"]
    static let home: [Int] = [0, 1, 2, 100, 101, 102, 103]

    static func pos(_ k: Int) -> Int { return Int(Settings.get(keys[k], Double(home[k]))) }
    static func column(_ k: Int) -> Int { return pos(k) >= 100 ? 1 : 0 }
    static func order(_ col: Int) -> [Int] {
        let mine: [Int] = (0..<count).filter { column($0) == col }
        return mine.sorted { (a: Int, b: Int) -> Bool in
            let pa: Int = pos(a)
            let pb: Int = pos(b)
            return pa == pb ? a < b : pa < pb
        }
    }

    static func reset() { for k in 0..<count { Settings.set(keys[k], Double(home[k])) } }

    /// Puts section k into a column, before the section `before` (-1: at the end).
    static func move(_ k: Int, _ col: Int, before: Int) {
        var places: [Int: Int] = [:] // read off the old places first, then written together
        for c in 0..<2 {
            var o: [Int] = order(c).filter { $0 != k }
            if c == col {
                let at: Int = o.firstIndex(of: before) ?? o.count
                o.insert(k, at: at)
            }
            for (i, s) in o.enumerated() { places[s] = c * 100 + i }
        }
        for (s, p) in places where pos(s) != p { Settings.set(keys[s], Double(p)) }
    }

    /// A section with nothing in it takes no room.
    static func shown(_ k: Int, _ model: AppModel) -> Bool {
        switch k {
        case plan, stats: return model.energy != nil
        case done: return model.energy != nil && model.data.tasks.contains { $0.done }
        default: return true
        }
    }
}

/// A section being dragged, and where everything is (all in the coordinates of FullBody).
final class SectionDrag: ObservableObject {
    @Published var k: Int = -1          // the section being dragged (-1: none)
    @Published var at: CGPoint = .zero  // the pointer
    @Published var line: CGRect = .zero // where it would go: the line between two sections
    var grab: CGSize = .zero
    var size: CGSize = .zero
    var col: Int = 0
    var before: Int = -1
    var cancelled = false               // Esc: the rest of this drag does nothing
    var serial: Int = 0
    var frames: [Int: CGRect] = [:]     // the sections, and the two columns' views (100 and 101)
    var keys: Any? = nil                // listens for Esc
    var scrollers: [Int: (Int, UnitPoint) -> Void] = [:] // how each column is scrolled to a section
    static let space = "full"

    func begin(_ s: Int, _ p: CGPoint, _ model: AppModel) {
        guard k < 0, let f = frames[s], f.width > 1, f.height > 1 else { return }
        size = CGSize(width: f.width, height: min(f.height, 320))
        grab = CGSize(width: f.width - 8, height: min(size.height / 2, max(0, p.y - f.minY)))
        serial += 1
        model.dragging = true
        model.shell?.window.makeKey()
        if keys == nil {
            keys = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self, weak model] e in
                guard let me = self, let mo = model, me.k >= 0, e.keyCode == 53 else { return e }
                me.cancelled = true
                me.end(drop: false, mo)
                return nil
            }
        }
        k = s
        drag(p, model)
        watch(serial, model)
    }

    /// Should the button go up without the gesture saying so (the panel went away under it), the drag ends.
    func watch(_ n: Int, _ model: AppModel) {
        if model.demoRun { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self, weak model] in
            guard let me = self, let mo = model, me.k >= 0, me.serial == n else { return }
            if NSEvent.pressedMouseButtons & 1 != 0 { me.watch(n, mo); return }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self, weak model] in
                guard let me = self, let mo = model, me.k >= 0, me.serial == n else { return }
                me.end(drop: false, mo)
            }
        }
    }

    func drag(_ p: CGPoint, _ model: AppModel) {
        if k < 0 { return }
        let gap: CGFloat = M(slim: false).gapY
        let radius: CGFloat = M(slim: false).radius
        let left: Bool = p.x < fullW / 2
        col = left != model.swapped ? 0 : 1 // (swapped, the energy column is on the left)
        let view: CGRect = frames[100 + col] ?? .zero
        let here: [Int] = Sec.order(col).filter { $0 != k && Sec.shown($0, model) }
        let placed: [(Int, CGRect)] = here.compactMap { s in frames[s].map { (s, $0) } }
        if p.y < view.minY + 24 {
            if let s = placed.last(where: { $0.1.minY < view.minY - 1 }) { scrollers[col]?(s.0, .top) }
        } else if p.y > view.maxY - 24 {
            if let s = placed.first(where: { $0.1.maxY > view.maxY + 1 }) { scrollers[col]?(s.0, .bottom) }
        }
        before = -1
        var y: CGFloat = view.minY
        for (s, f) in placed {
            if p.y < f.midY { before = s; y = f.minY - gap / 2; break }
            y = f.maxY + gap / 2
        }
        let top: CGFloat = max(view.minY, min(view.maxY - 4, y - 2))
        let x: CGFloat = placed.first?.1.minX ?? (view.minX + M(slim: false).side)
        let w: CGFloat = placed.first?.1.width ?? size.width
        line = CGRect(x: x + radius, y: top, width: max(0, w - 2 * radius), height: 4)
        at = p
    }

    func end(drop: Bool, _ model: AppModel) {
        if k < 0 { return }
        let s: Int = k
        k = -1
        model.dragging = false
        if let t = keys { NSEvent.removeMonitor(t); keys = nil }
        if !drop { return }
        withAnimation(.easeOut(duration: 0.16)) {
            Sec.move(s, col, before: before)
            model.changed()
        }
        model.shell?.contentChanged()
    }
}

struct SecFrameKey: PreferenceKey {
    static var defaultValue: [Int: CGRect] = [:]
    static func reduce(value: inout [Int: CGRect], nextValue: () -> [Int: CGRect]) { value.merge(nextValue()) { _, b in b } }
}

struct SecHeightKey: PreferenceKey {
    static var defaultValue: [Int: CGFloat] = [:]
    static func reduce(value: inout [Int: CGFloat], nextValue: () -> [Int: CGFloat]) { value.merge(nextValue()) { _, b in b } }
}

// ---------------------------------------------------------------- the two columns

/// The tasks column and the energy column, side by side ("Full Mode sides" in Settings swaps them).
struct FullBody: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let maxHeight: CGFloat

    var body: some View {
        let cols: [Int] = model.swapped ? [1, 0] : [0, 1]
        HStack(alignment: .top, spacing: 0) {
            ForEach(cols, id: \.self) { c in
                FullColumn(col: c, onLeft: c == cols[0], maxHeight: maxHeight)
            }
        }
        .overlay(DragLayer(drag: model.secDrag), alignment: .topLeading)
        .coordinateSpace(name: SectionDrag.space)
        .onPreferenceChange(SecFrameKey.self) { v in model.secDrag.frames = v }
        .onDisappear { model.secDrag.end(drop: false, model) }
    }
}

/// One column. The prompts stand on top of the tasks column. The list takes the room the other sections leave (but
/// never less than 170) and its card scrolls when it needs more; the plan may take up to half; when that is still
/// not enough, the column scrolls.
struct FullColumn: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let col: Int
    let onLeft: Bool
    let maxHeight: CGFloat
    @State private var heights: [Int: CGFloat] = [:]
    @State private var promptsH: CGFloat = 0

    var body: some View {
        let secs: [Int] = Sec.order(col).filter { Sec.shown($0, model) }
        let avail: CGFloat = max(120, maxHeight - m.side - (col == 0 ? promptsH : 0))
        let others: CGFloat = secs.filter { $0 != Sec.list }.reduce(0) { $0 + (heights[$1] ?? 0) }
        let gaps: CGFloat = m.gapY * CGFloat(max(0, secs.count - 1))
        let listMax: CGFloat = max(170, avail - others - gaps)
        let planMax: CGFloat = max(120, (maxHeight - m.side) * 0.5)
        let lead: CGFloat = onLeft ? m.side : m.sideMid
        let trail: CGFloat = onLeft ? m.sideMid : m.side
        VStack(alignment: .leading, spacing: 0) {
            if col == 0 {
                Prompts()
                    .padding(.leading, lead).padding(.trailing, trail)
                    .background(GeometryReader { g in Color.clear.preference(key: UsedHeightKey.self, value: g.size.height) })
            }
            ScrollViewReader { proxy in
                FitScroll(maxHeight: avail) {
                    VStack(alignment: .leading, spacing: m.gapY) {
                        ForEach(secs, id: \.self) { k in
                            SectionBox(k: k, listMax: listMax, planMax: planMax, drag: model.secDrag).id("sec" + String(k))
                        }
                    }
                    .padding(.leading, lead).padding(.trailing, trail)
                    .frame(width: fullW / 2, alignment: .topLeading)
                }
                .background(GeometryReader { g in
                    Color.clear.preference(key: SecFrameKey.self, value: [100 + col: g.frame(in: .named(SectionDrag.space))])
                })
                .onAppear { hook(proxy) }
            }
        }
        .onPreferenceChange(UsedHeightKey.self) { v in if abs(v - promptsH) > 0.5 { promptsH = v } }
        .onPreferenceChange(SecHeightKey.self) { v in if differs(v) { heights = v } }
        .padding(.bottom, m.side)
        .frame(width: fullW / 2, alignment: .top)
    }

    func differs(_ v: [Int: CGFloat]) -> Bool {
        if v.count != heights.count { return true }
        return v.contains { abs(($0.value) - (heights[$0.key] ?? -1)) > 0.5 }
    }

    /// How a drag scrolls this column.
    func hook(_ proxy: ScrollViewProxy) {
        model.secDrag.scrollers[col] = { (k: Int, anchor: UnitPoint) in
            withAnimation(.easeOut(duration: 0.25)) { proxy.scrollTo("sec" + String(k), anchor: anchor) }
        }
    }
}

/// The check-ins and the time's-up prompt, each with the gap under it.
struct Prompts: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if model.checkInOpen { CheckInView().padding(.bottom, m.gapY) }
            if model.feedbackOpen { FeedbackView().padding(.bottom, m.gapY) }
            if model.alertTask != nil { AlertView().padding(.bottom, m.gapY) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// ---------------------------------------------------------------- one section

/// A section with the dots it is dragged by: seen while the pointer is on it.
struct SectionBox: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let k: Int
    let listMax: CGFloat
    let planMax: CGFloat
    @ObservedObject var drag: SectionDrag
    @State private var hot = false

    var body: some View {
        let picked: Bool = drag.k == k
        let dots: Bool = (hot && drag.k < 0) || model.showGrips
        SectionInner(k: k, listMax: listMax, planMax: planMax)
            .frame(maxWidth: .infinity, alignment: .leading)
            .opacity(picked ? 0.3 : 1)
            .background(GeometryReader { g in
                Color.clear
                    .preference(key: SecFrameKey.self, value: [k: g.frame(in: .named(SectionDrag.space))])
                    .preference(key: SecHeightKey.self, value: [k: g.size.height])
            })
            .overlay(grip(dots), alignment: .trailing)
            .contentShape(Rectangle())
            .onHover { h in withAnimation(.easeOut(duration: h ? 0.12 : 0.16)) { hot = h } }
    }

    func grip(_ on: Bool) -> some View {
        GripDots()
            .frame(width: 12, height: 26)
            .contentShape(Rectangle())
            .opacity(on ? 1 : 0)
            .padding(.trailing, 1)
            .onHover { h in if h { NSCursor.openHand.set() } else { NSCursor.arrow.set() } }
            .gesture(
                DragGesture(minimumDistance: 2, coordinateSpace: .named(SectionDrag.space))
                    .onChanged { v in
                        if drag.cancelled { return }
                        if drag.k < 0 { drag.begin(k, v.startLocation, model) }
                        drag.drag(v.location, model)
                    }
                    .onEnded { v in
                        if !drag.cancelled { drag.drag(v.location, model); drag.end(drop: true, model) }
                        drag.cancelled = false
                    }
            )
            .help("Drag to move this section")
    }
}

/// Two columns of three dots.
struct GripDots: View {
    var body: some View {
        VStack(spacing: 3) {
            ForEach(0..<3, id: \.self) { _ in
                HStack(spacing: 2) {
                    Circle().fill(T.muted).frame(width: 2, height: 2)
                    Circle().fill(T.muted).frame(width: 2, height: 2)
                }
            }
        }
    }
}

/// What a section shows.
struct SectionInner: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    let k: Int
    let listMax: CGFloat
    let planMax: CGFloat

    var body: some View {
        switch k {
        case Sec.list:
            let side: CGFloat = T.skin >= 2 ? 8 : 0 // room beside the card for its shadow (a made skin)
            FitScroll(maxHeight: listMax, round: m.radius, inset: side) { TaskListCard().padding(.horizontal, side) }
                .padding(.horizontal, -side)
        case Sec.add:
            AddBox()
        case Sec.plan:
            let over: CGFloat = 8 // room inside the plan's scrolling part for its card's shadow (a made skin)
            FitScroll(maxHeight: planMax) { PlanCard().padding(.horizontal, over) }
                .padding(.horizontal, -over)
        case Sec.chart:
            EnergyView()
        case Sec.stats:
            StatsSection()
        case Sec.routines:
            RoutinesCard()
        default:
            DoneCard()
        }
    }
}

/// The five icons, and under them what they open.
struct StatsSection: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    var body: some View {
        if let e = model.energy {
            VStack(alignment: .leading, spacing: m.gapY) {
                StatIcons()
                if model.statOpen.contains(true) { StatsRow(e: e) }
                NeedHintCard(e: e)
                if model.debtOpen && model.statOpen[1] { DebtCard(e: e) }
                if NapsBlock.shown(model, e) { NapsBlock(e: e) }
            }
        }
    }
}

// ---------------------------------------------------------------- dragging

/// Over the two columns while a section is dragged: the section itself, see-through, under the pointer, and a line
/// where it would go.
struct DragLayer: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.m) var m
    @ObservedObject var drag: SectionDrag

    var body: some View {
        ZStack(alignment: .topLeading) {
            if drag.k >= 0 {
                let w: CGFloat = drag.size.width
                let h: CGFloat = drag.size.height
                let r: CGFloat = min(m.radius, h / 2)
                SectionGhost(k: drag.k)
                    .frame(width: w, alignment: .topLeading)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(width: w, height: h, alignment: .top)
                    .background(T.glass ? T.pop : T.bg)
                    .clipShape(RoundedRectangle(cornerRadius: r, style: .continuous))
                    .compositingGroup()
                    .opacity(0.8)
                    .offset(x: drag.at.x - drag.grab.width, y: drag.at.y - drag.grab.height)
                Capsule().fill(T.strong)
                    .frame(width: drag.line.width, height: 4)
                    .offset(x: drag.line.minX, y: drag.line.minY)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .allowsHitTesting(false)
    }
}

/// The dragged section once more, not in a scrolling part of its own.
struct SectionGhost: View {
    let k: Int
    var body: some View {
        Group {
            switch k {
            case Sec.list: TaskListCard()
            case Sec.plan: PlanCard()
            default: SectionInner(k: k, listMax: 320, planMax: 320)
            }
        }
    }
}

// ---------------------------------------------------------------- the logo

/// The ball on the hill as two lines, drawn in a box of 46 x 28.
struct LogoShape: Shape {
    func path(in rect: CGRect) -> Path {
        var p = Path()
        p.addEllipse(in: CGRect(x: 155 - 42, y: 168 - 42, width: 84, height: 84))
        p.move(to: CGPoint(x: 41, y: 285))
        p.addCurve(to: CGPoint(x: 155, y: 212), control1: CGPoint(x: 75, y: 243), control2: CGPoint(x: 110, y: 212))
        p.addCurve(to: CGPoint(x: 328, y: 298), control1: CGPoint(x: 240, y: 212), control2: CGPoint(x: 255, y: 298))
        p.addCurve(to: CGPoint(x: 388, y: 266), control1: CGPoint(x: 352, y: 298), control2: CGPoint(x: 372, y: 286))
        return p.applying(CGAffineTransform(a: 0.127, b: 0, c: 0, d: 0.127, tx: -4.2, ty: -12.8))
    }
}

/// The logo at the header's left, in the skin's own ink. The header is dragged by it as well.
struct HeadLogo: View {
    let scale: CGFloat
    let tip: String
    var body: some View {
        LogoShape()
            .stroke(T.ink, style: StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
            .frame(width: 46, height: 28)
            .scaleEffect(scale)
            .frame(width: 46 * scale, height: 28 * scale)
            .overlay(DragArea())
            .help(tip)
    }
}
