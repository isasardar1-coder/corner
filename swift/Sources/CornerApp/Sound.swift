// Corner for macOS - the click: a short, quiet tick when a task is ticked off (or put back) and when a switch is
// turned on or off. It is made here, not read from a file: 40 ms of a 1.5 kHz tone that dies away at once, at about a
// fifth of full volume. "Click sounds" in Settings turns it off ("Click" = 0 in settings.json). Test runs are silent.
// Port of Sound.cs.
import AppKit
import CornerCore

enum Click {
    private static var sound: NSSound? = nil // kept, ready to play at once

    static var on: Bool { return Settings.get("Click", 1) > 0.5 }

    static func play() {
        if ProcessInfo.processInfo.environment["CORNER_DATA"] != nil || !on { return }
        if sound == nil { sound = NSSound(data: wav()) }
        sound?.stop()
        sound?.play()
    }

    /// The tick as a .wav: 44.1 kHz, 16 bit, one channel.
    static func wav() -> Data {
        let rate: Int = 44100
        let volume: Double = 0.20
        let decay: Double = 0.006
        let attack: Double = 0.0008
        let n: Int = Int(Double(rate) * 0.040)
        var d = Data()
        func text(_ s: String) { d.append(contentsOf: Array(s.utf8)) }
        func u32(_ v: Int) { for k in 0..<4 { d.append(UInt8((v >> (8 * k)) & 0xFF)) } }
        func u16(_ v: Int) { for k in 0..<2 { d.append(UInt8((v >> (8 * k)) & 0xFF)) } }
        text("RIFF"); u32(36 + n * 2); text("WAVE")
        text("fmt "); u32(16); u16(1); u16(1); u32(rate); u32(rate * 2); u16(2); u16(16)
        text("data"); u32(n * 2)
        for i in 0..<n {
            let t: Double = Double(i) / Double(rate)
            let low: Double = sin(2 * Double.pi * 1500 * t)
            let high: Double = 0.35 * sin(2 * Double.pi * 3100 * t) // a little brightness on top
            let shape: Double = min(1, t / attack) * exp(-t / decay)
            let v: Double = volume / 1.35 * (low + high) * shape * 32767
            u16(Int(v))
        }
        return d
    }
}
