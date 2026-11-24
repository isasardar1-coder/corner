// Corner for macOS - a notice in Corner's own look: a small card that slides in at the panel's corner (just past the
// panel when it is open), with ping.wav. Click it, or wait, to dismiss. Port of ShowToast (Toast.cs).
import AppKit
import SwiftUI
import CornerCore

struct ToastCard: View {
    let line: String
    let onClose: () -> Void
    @ObservedObject private var watch = SkinWatch.shared // drawn again when the skin changes
    var body: some View {
        let m = M(slim: true)
        HStack(alignment: .top, spacing: 0) {
            Text(line).font(Fonts.ui(m.z(15.5), true)).foregroundColor(T.text)
                .lineSpacing(m.z(15.5) * 0.3)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            Glyph(d: Icons.cross, color: T.muted, th: 1.4, w: 10, h: 10).padding(.leading, 12).padding(.top, 5).padding(.trailing, 2)
        }
        .padding(.leading, 18).padding(.trailing, 14).padding(.top, 16).padding(.bottom, 17)
        .frame(width: cardW)
        .background(PanelBackground(radius: 18))
        .contentShape(Rectangle())
        .onTapGesture { onClose() }
        .environment(\.m, m)
    }
}

final class ToastController {
    static let margin: CGFloat = 28
    static let stayMs = 13.0
    let model: AppModel
    let line: String
    var window: NSPanel!
    var closed = false

    init(model: AppModel, line: String) {
        self.model = model
        self.line = line
    }

    func show(near shell: PanelController) {
        let z = CGFloat(model.scale)
        let card = ToastCard(line: line) { [weak self] in self?.close(fade: true) }
            .scaleEffect(z, anchor: .topLeading)
        let host = NSHostingView(rootView: AnyView(ZStack(alignment: .topLeading) {
            if model.demoRun { T.backdrop }
            card.padding(ToastController.margin)
        }))
        // measure at scale 1, then size the window to the scaled card
        let probe = NSHostingView(rootView: ToastCard(line: line, onClose: {}))
        let size1 = probe.fittingSize
        let cw = size1.width * z, ch = size1.height * z
        let ww = cw + 2 * ToastController.margin, wh = ch + 2 * ToastController.margin

        let screen = shell.window.screen ?? NSScreen.main ?? NSScreen.screens.first
        let wa = screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let gap: CGFloat = 8
        var cx = model.onRight ? wa.maxX - gap - cw : wa.minX + gap
        var cyTop = model.onTop ? wa.maxY - gap : wa.minY + gap + ch // AppKit: the card's top edge
        if model.shown && !model.full && shell.window.isVisible {
            // the panel is open: sit just past it, not on it
            let f = shell.window.frame.insetBy(dx: shadowM, dy: shadowM)
            cx = model.onRight ? f.maxX - cw : f.minX
            cyTop = model.onTop ? f.minY - 10 : f.maxY + 10 + ch
        }
        cx = max(wa.minX + 4, min(wa.maxX - cw - 4, cx))
        cyTop = max(wa.minY + ch + 4, min(wa.maxY - 4, cyTop))

        let w = CornerPanel(contentRect: NSRect(x: cx - ToastController.margin, y: cyTop - ch - ToastController.margin, width: ww, height: wh),
                            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.hasShadow = false
        w.level = .statusBar
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        w.hidesOnDeactivate = false
        w.isReleasedWhenClosed = false
        w.animationBehavior = .none
        host.frame = NSRect(x: 0, y: 0, width: ww, height: wh)
        w.contentView = host
        window = w

        let end = w.frame
        if model.demoRun {
            w.alphaValue = 1
            w.orderFrontRegardless()
            return
        }
        w.alphaValue = 0
        w.setFrame(end.offsetBy(dx: model.onRight ? 14 : -14, dy: 0), display: false)
        w.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.32
            ctx.timingFunction = CAMediaTimingFunction(name: .easeOut)
            w.animator().alphaValue = 1
            w.animator().setFrame(end, display: true)
        }
        Platform.ping()
        wait(after: ToastController.stayMs)
    }

    /// Stays while the pointer is on it.
    func wait(after s: Double) {
        DispatchQueue.main.asyncAfter(deadline: .now() + s) { [weak self] in
            guard let t = self, !t.closed, let w = t.window else { return }
            if w.frame.contains(NSEvent.mouseLocation) { t.wait(after: 3) } else { t.close(fade: true) }
        }
    }

    func close(fade: Bool) {
        if closed { return }
        closed = true
        guard let w = window else { return }
        if !fade { w.orderOut(nil); return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            w.animator().alphaValue = 0
        }, completionHandler: { w.orderOut(nil) })
    }
}
