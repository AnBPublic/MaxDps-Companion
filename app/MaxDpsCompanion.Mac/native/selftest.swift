// Self-test for the native capture layer: draws a known 43-cell strip pattern
// in a window's top-left corner, captures it through libmdc_capture, and
// verifies pixel correctness + read cost. Run: swiftc MdcCapture.swift selftest.swift
import Foundation
import AppKit
import CoreGraphics

// MARK: pattern

let cell = 16
let count = 43
let stripW = cell * count
let stripH = cell

func nibbleColor(_ i: Int) -> CGColor {
    if i == 0 { return CGColor(red: 1, green: 0, blue: 1, alpha: 1) }   // magic magenta
    let n = CGFloat(i % 16) / 15.0
    return CGColor(red: n, green: n, blue: n, alpha: 1)
}

final class StripView: NSView {
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        for i in 0..<count {
            ctx.setFillColor(nibbleColor(i))
            ctx.fill(CGRect(x: i * cell, y: 0, width: cell, height: cell))
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var view: StripView!
    var timer: Timer?
    var tick = 0

    func applicationDidFinishLaunching(_ notification: Notification) {
        window = NSWindow(contentRect: NSRect(x: 60, y: 60, width: stripW + 40, height: stripH + 40),
                          styleMask: [.borderless], backing: .buffered, defer: false)
        view = StripView(frame: NSRect(x: 0, y: 0, width: stripW + 40, height: stripH + 40))
        view.wantsLayer = true
        window.contentView = view
        window.orderFrontRegardless()
        // Keep repainting so the capture pipeline has fresh frames.
        timer = Timer.scheduledTimer(withTimeInterval: 0.033, repeats: true) { _ in
            self.tick += 1
            self.view.needsDisplay = true
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { self.runTest() }
    }

    func runTest() {
        let pid = UInt32(ProcessInfo.processInfo.processIdentifier)
        print("selftest: pid=\(pid) window=\(Int(window.frame.origin.x)),\(Int(window.frame.origin.y)) size=\(stripW)x\(stripH)")
        print("selftest: authorized=\(mdc_cap_authorized()) frontmostPid=\(mdc_cap_frontmost_pid())")

        // window frame lookup through the same code path the host uses
        var wx = 0.0, wy = 0.0, ww = 0.0, wh = 0.0
        let wf = mdc_window_frame(pid, &wx, &wy, &ww, &wh)
        print(String(format: "selftest: window_frame code=%d %.0f,%.0f %.0fx%.0f scale=%.2f", wf, wx, wy, ww, wh, mdc_window_scale(pid)))

        let started = mdc_cap_start(pid, 0, 0, Double(stripW), Double(stripH), 60)
        print("selftest: start code=\(started) region=\(regionPixels())")

        // wait for the first frame
        var waited = 0
        while mdc_cap_frame_age_ms() < 0 && waited < 3000 {
            usleep(10_000)
            waited += 10
        }
        let age = mdc_cap_frame_age_ms()
        print("selftest: first frame after \(waited) ms (age=\(age) ms)")

        guard let snap = snapshot() else {
            print("selftest: FAIL - no frame")
            NSApp.terminate(nil)
            return
        }
        print("selftest: frame \(snap.w)x\(snap.h) stride=\(snap.stride) seq=\(snap.seq)")

        // verify: cell i centre should equal nibbleColor(i)
        var bad = 0
        var firstBad = ""
        for i in 0..<count {
            let cx = i * cell + cell / 2
            let cy = cell / 2
            let p = snap.ptr + cy * snap.stride + cx * 4
            let b = Int(p[0]), g = Int(p[1]), r = Int(p[2])
            let want = i == 0 ? (255, 0, 255) : { () -> (Int, Int, Int) in
                let n = Int(round(Double(i % 16) / 15.0 * 255.0)); return (n, n, n)
            }()
            if abs(r - want.0) > 2 || abs(g - want.1) > 2 || abs(b - want.2) > 2 {
                bad += 1
                if firstBad.isEmpty { firstBad = "cell \(i): got \(r),\(g),\(b) want \(want.0),\(want.1),\(want.2)" }
            }
        }
        print("selftest: pixel check bad=\(bad)/43 \(firstBad.isEmpty ? "OK" : firstBad)")

        // read cost: 3000 snapshots + 43-cell median reads (the engine tick shape)
        var samples: [Double] = []
        samples.reserveCapacity(3000)
        for _ in 0..<3000 {
            let t0 = DispatchTime.now().uptimeNanoseconds
            if let s = snapshot() {
                var acc = 0
                for i in 0..<count {
                    let x = i * cell + cell / 2
                    let p = s.ptr + (cell / 2) * s.stride + x * 4
                    acc &+= Int(p[0]) + Int(p[1]) + Int(p[2])
                }
                if acc == -1 { print("") }
            }
            samples.append(Double(DispatchTime.now().uptimeNanoseconds - t0) / 1000.0)
        }
        samples.sort()
        let mean = samples.reduce(0, +) / Double(samples.count)
        print(String(format: "selftest: read cost us  mean=%.2f p50=%.2f p99=%.2f max=%.2f",
                     mean, samples[samples.count / 2], samples[samples.count * 99 / 100], samples[samples.count - 1]))

        var delivered: UInt64 = 0, skipped: UInt64 = 0
        var fps: Int32 = 0
        _ = mdc_cap_stats(&delivered, &skipped, &fps)
        print("selftest: delivered=\(delivered) skipped=\(skipped) fps=\(fps) ageMs=\(mdc_cap_frame_age_ms())")

        // reconfig (the locate -> strip shrink path)
        let t0 = DispatchTime.now().uptimeNanoseconds
        let rc = mdc_cap_set_region(0, 0, Double(stripW), Double(stripH))
        print("selftest: set_region code=\(rc) took \(Double(DispatchTime.now().uptimeNanoseconds - t0) / 1e6) ms")
        _ = mdc_cap_stop()
        print("selftest: DONE")
        NSApp.terminate(nil)
    }

    func regionPixels() -> String {
        var w: Int32 = 0, h: Int32 = 0
        _ = mdc_cap_region(&w, &h)
        return "\(w)x\(h)"
    }

    struct Snap { let ptr: UnsafePointer<UInt8>; let w: Int32; let h: Int32; let stride: Int32; let seq: UInt64 }
    func snapshot() -> Snap? {
        var seq: UInt64 = 0
        var ptr: UnsafePointer<UInt8>? = nil
        var w: Int32 = 0, h: Int32 = 0, s: Int32 = 0
        let code = mdc_cap_snapshot(&seq, &ptr, &w, &h, &s)
        guard code == 0, let p = ptr else { return nil }
        return Snap(ptr: p, w: w, h: h, stride: s, seq: seq)
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
