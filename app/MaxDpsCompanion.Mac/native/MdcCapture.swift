// MdcCapture — macOS capture backend for the MaxDPS Companion Mac host.
//
// WHY THIS EXISTS
//   CGWindowListCreateImage was OBSOLETED in macOS 15 ("Please use
//   ScreenCaptureKit instead") and is unavailable in current SDKs. The only
//   supported capture API on macOS 15+ is ScreenCaptureKit (SCK).
//
// WHY A NATIVE LIBRARY
//   SCK is an Objective-C framework driven by delegates + dispatch queues.
//   Driving it through C# P/Invoke (objc_msgSend + runtime class creation +
//   IMP trampolines) is fragile and unauditable. This file is the entire
//   native surface: a small C ABI over one persistent SCStream. The managed
//   host keeps its portable IScreenSampler contract; only the transport
//   changed.
//
// PERFORMANCE CONTRACT (hard goal: no frametime impact on the game)
//   * ONE persistent SCStream per run, filtered to the WoW window only
//     (desktopIndependentWindow) with a sourceRect bounded to the strip
//     band -> the compositor never runs a screen-wide capture, and the game
//     render loop is never synchronously captured. (The old design called
//     CGWindowListCreateImage on the engine tick: 20-60 ms of blocking
//     window-server work per sample.)
//   * Frames arrive asynchronously at up to `fps`; the delegate memcpys the
//     band into a double buffer and returns. queueDepth 2 => a busy consumer
//     drops frames instead of back-pressuring the capture pipeline.
//   * The hot path (engine tick) is a seqlock READ of the newest frame: no
//     syscall, no lock, no allocation, no IPC with the game.
//   * No audio, no cursor, no shadow, no scaling: 1:1 32BGRA at the
//     display's native scale.
//   * Region is reconfigurable at runtime (updateConfiguration): the host
//     starts on a bounded locate band and shrinks to the discovered strip
//     rect (43 cells x cellSize), which is <1% of a 1080p screen.
//
// ABI (C, called from C# DllImport — see MacCaptureStream.cs):
//   int32  mdc_cap_available(void);
//   int32  mdc_cap_authorized(void);
//   int32  mdc_cap_frontmost_pid(void);
//   int32  mdc_window_frame(uint32 pid, double *x, double *y, double *w, double *h);
//   double mdc_window_scale(uint32 pid);
//   int32  mdc_cap_start(uint32 pid, double x, double y, double w, double h, int32 fps);
//   int32  mdc_cap_stop(void);
//   int32  mdc_cap_set_region(double x, double y, double w, double h);
//   int32  mdc_cap_region(int32 *w, int32 *h);
//   int32  mdc_cap_snapshot(uint64 *seq, const uint8 **ptr, int32 *w, int32 *h, int32 *stride);
//   int32  mdc_cap_snapshot_if(uint64 minSeq, uint64 *seq, const uint8 **ptr, int32 *w, int32 *h, int32 *stride);
//   int64  mdc_cap_frame_age_ms(void);
//   int32  mdc_cap_stats(uint64 *delivered, uint64 *skipped, int32 *fps);
//   int32  mdc_cap_dead(void);
//   int32  mdc_cap_once(uint32 pid, double x, double y, double w, double h,
//                       uint8 *dst, int32 dstCap, int32 *outW, int32 *outH, int32 *outStride);
//   const char *mdc_cap_last_error(void);
//
// Error codes: 0 ok, -1 unavailable, -2 not authorized, -3 window not found,
//              -4 stream/config error, -5 timeout, -6 buffer too small,
//              -7 no frame yet, -8 bad args.

import Foundation
import AppKit
import CoreMedia
import CoreVideo
import ScreenCaptureKit

private let errOK: Int32 = 0
private let errUnavailable: Int32 = -1
private let errNotAuthorized: Int32 = -2
private let errNoWindow: Int32 = -3
private let errStream: Int32 = -4
private let errTimeout: Int32 = -5
private let errTooSmall: Int32 = -6
private let errNoFrame: Int32 = -7
private let errBadArgs: Int32 = -8

// MARK: - frame sink (double buffer + seqlock, copied on publish)

private final class Sink {
    private var bufA: UnsafeMutablePointer<UInt8>?
    private var bufB: UnsafeMutablePointer<UInt8>?
    private var cap = 0
    private var which = 0                 // buffer the NEXT frame is written to
    private var valid = false             // false until a frame of the current config lands

    private let lock = NSLock()           // guards seq/which/valid/counters (uncontended)
    private var seq: UInt64 = 0
    private var delivered: UInt64 = 0
    private var skipped: UInt64 = 0
    private var lastFrameNs: UInt64 = 0
    private var fpsWindowNs: UInt64 = 0
    private var fpsWindowCount = 0
    private var fpsValue: Int32 = 0

    private var width: Int32 = 0
    private var height: Int32 = 0
    private var stride: Int32 = 0
    private var lastSkip = "none"

    var skipReason: String {
        lock.lock(); defer { lock.unlock() }
        return lastSkip
    }

    deinit {
        bufA?.deallocate()
        bufB?.deallocate()
    }

    /// (Re)allocates for a new region and invalidates the published frame.
    /// Frames are NORMALISED to a packed stride (w*4): ScreenCaptureKit pads
    /// rows (measured: 400 px -> stride 1664, i.e. +64 B/row), so the publish
    /// path row-copies when the delivered stride is wider. The managed reader
    /// therefore always sees stride == width*4.
    func prepare(w: Int, h: Int, strideBytes: Int) {
        lock.lock(); defer { lock.unlock() }
        let need = strideBytes * h
        if need > cap {
            bufA?.deallocate()
            bufB?.deallocate()
            bufA = UnsafeMutablePointer<UInt8>.allocate(capacity: need)
            bufB = UnsafeMutablePointer<UInt8>.allocate(capacity: need)
            cap = need
        }
        width = Int32(w); height = Int32(h); stride = Int32(strideBytes)
        valid = false
        seq &+= 1                          // odd: any in-flight read is torn
        seq &+= 1
    }

    func publish(_ sb: CMSampleBuffer) {
        // Status first: idle/blank frames legitimately carry no image.
        var statusText = "no-attachments"
        if let arr = CMSampleBufferGetSampleAttachmentsArray(sb, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
           let raw = arr.first?[SCStreamFrameInfo.status] as? Int {
            statusText = "status=\(raw)"
            if let status = SCFrameStatus(rawValue: raw), status != .complete && status != .started {
                lock.lock(); skipped &+= 1; lastSkip = statusText; lock.unlock(); return
            }
        }
        guard let px = CMSampleBufferGetImageBuffer(sb) else {
            lock.lock()
            skipped &+= 1
            let flags = CMSampleBufferGetNumSamples(sb)
            lastSkip = "no image buffer (\(statusText), samples=\(flags))"
            lock.unlock()
            return
        }
        CVPixelBufferLockBaseAddress(px, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(px, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(px) else {
            lock.lock(); skipped &+= 1; lastSkip = "no base address"; lock.unlock(); return
        }
        let w = CVPixelBufferGetWidth(px)
        let h = CVPixelBufferGetHeight(px)
        let s = CVPixelBufferGetBytesPerRow(px)

        lock.lock()
        guard w == Int(width), h == Int(height), let a = bufA, let b = bufB else {
            skipped &+= 1
            lastSkip = "size mismatch: got \(w)x\(h) stride=\(s), want \(width)x\(height)"
            lock.unlock()
            return
        }
        let packed = Int(stride)           // normalised row length (w * 4)
        let dst = which == 0 ? a : b
        seq &+= 1                          // odd: write in progress
        lock.unlock()

        if s == packed {
            memcpy(dst, base, packed * h)  // contiguous band: tens of µs
        } else {
            // Padded rows: copy only the usable pixels of each row, packed.
            let bytes = min(s, packed)
            for row in 0..<h {
                memcpy(dst + row * packed, base + row * s, bytes)
            }
        }

        lock.lock()
        which ^= 1
        valid = true
        seq &+= 1                          // even: published
        delivered &+= 1
        let now = DispatchTime.now().uptimeNanoseconds
        lastFrameNs = now
        if fpsWindowNs == 0 { fpsWindowNs = now; fpsWindowCount = 0 }
        fpsWindowCount += 1
        let elapsed = now &- fpsWindowNs
        if elapsed >= 1_000_000_000 {
            fpsValue = Int32(Double(fpsWindowCount) * 1e9 / Double(elapsed))
            fpsWindowNs = now
            fpsWindowCount = 0
        }
        lock.unlock()
    }

    struct Snapshot {
        let code: Int32
        let seq: UInt64
        let ptr: UnsafePointer<UInt8>?
        let w: Int32
        let h: Int32
        let stride: Int32
    }

    func snapshot(minSeq: UInt64) -> Snapshot {
        lock.lock(); defer { lock.unlock() }
        guard valid, (seq & 1) == 0, minSeq < seq else {
            return Snapshot(code: errNoFrame, seq: seq, ptr: nil, w: width, h: height, stride: stride)
        }
        let buf = which == 0 ? bufB : bufA  // last published
        guard let p = buf else {
            return Snapshot(code: errNoFrame, seq: seq, ptr: nil, w: width, h: height, stride: stride)
        }
        return Snapshot(code: errOK, seq: seq, ptr: UnsafePointer(p), w: width, h: height, stride: stride)
    }

    var ageMs: Int64 {
        lock.lock(); defer { lock.unlock() }
        guard lastFrameNs != 0 else { return -1 }
        return Int64((DispatchTime.now().uptimeNanoseconds &- lastFrameNs) / 1_000_000)
    }

    var stats: (UInt64, UInt64, Int32) {
        lock.lock(); defer { lock.unlock() }
        return (delivered, skipped, fpsValue)
    }
}

private final class SinkOutput: NSObject, SCStreamOutput {
    private let sink: Sink
    init(_ s: Sink) { sink = s }
    func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen else { return }
        sink.publish(sb)
    }
}

/// Reference box for results produced inside an escaping Task.
private final class ResultBox {
    var code: Int32 = errTimeout
}

private final class StreamWatchdog: NSObject, SCStreamDelegate {
    private let lock = NSLock()
    private var dead = false
    private var reason = ""

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        lock.lock(); dead = true; reason = "\(error)"; lock.unlock()
    }

    var isDead: Bool { lock.lock(); defer { lock.unlock() }; return dead }
    var errorText: String { lock.lock(); defer { lock.unlock() }; return reason }
}

// MARK: - engine

private final class CaptureEngine {
    static let shared = CaptureEngine()

    private let sink = Sink()
    private lazy var sinkOutput = SinkOutput(sink)
    private let watchdog = StreamWatchdog()
    private let frameQueue = DispatchQueue(label: "com.maxdps.companion.capture.frames", qos: .userInitiated)

    private var stream: SCStream?
    private let ctl = NSLock()            // start/stop/setRegion are control-plane only
    private var region = CGRect(x: 0, y: 0, width: 1400, height: 48)
    private var scale: CGFloat = 1
    private var fps: Int32 = 60
    private var lastError = ""

    private func setError(_ s: String) { lastError = s }
    var errorText: String { lastError }

    /// Blocking fetch of shareable content. Control paths only (start /
    /// setRegion / once) — never the engine tick.
    fileprivate func shareable(timeout: Double = 6.0) -> (SCShareableContent?, Int32) {
        let sem = DispatchSemaphore(value: 0)
        var content: SCShareableContent?
        var code: Int32 = errTimeout
        Task.detached {
            do {
                content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
                code = errOK
            } catch {
                let ns = error as NSError
                if !CGPreflightScreenCaptureAccess() {
                    code = errNotAuthorized
                } else {
                    code = errStream
                }
                self.setError("SCShareableContent \(ns.domain)#\(ns.code): \(ns.localizedDescription)")
            }
            sem.signal()
        }
        if sem.wait(timeout: .now() + timeout) == .timedOut {
            setError("SCShareableContent timed out")
            return (nil, errTimeout)
        }
        return (content, code)
    }

    /// Largest layer-0 window owned by pid (the game window).
    func gameWindow(_ content: SCShareableContent, pid: UInt32) -> SCWindow? {
        var best: SCWindow?
        var bestArea: CGFloat = 0
        for w in content.windows where (w.owningApplication?.processID ?? -1) == pid {
            if w.frame.width < 200 || w.frame.height < 150 { continue }
            let area = w.frame.width * w.frame.height
            if area > bestArea { bestArea = area; best = w }
        }
        return best
    }

    private func scale(for win: SCWindow) -> CGFloat {
        var best: CGFloat = 1
        for screen in NSScreen.screens where screen.frame.intersects(win.frame) {
            best = max(best, screen.backingScaleFactor)
        }
        return best
    }

    private func pixelSize(_ rect: CGRect) -> (Int, Int) {
        (max(1, Int((rect.width * scale).rounded())), max(1, Int((rect.height * scale).rounded())))
    }

    private func makeConfig(_ rect: CGRect) -> SCStreamConfiguration {
        let (pxW, pxH) = pixelSize(rect)
        let cfg = SCStreamConfiguration()
        cfg.sourceRect = rect
        cfg.width = pxW
        cfg.height = pxH
        cfg.pixelFormat = kCVPixelFormatType_32BGRA
        cfg.colorSpaceName = CGColorSpace.sRGB
        cfg.showsCursor = false
        cfg.capturesAudio = false
        cfg.queueDepth = 2
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(max(1, fps)))
        cfg.scalesToFit = false
        cfg.preservesAspectRatio = false
        cfg.ignoreShadowsSingleWindow = true
        return cfg
    }

    func start(pid wantPid: UInt32, rect: CGRect, wantFps: Int32) -> Int32 {
        if !CGPreflightScreenCaptureAccess() { return errNotAuthorized }
        _ = stop()
        region = rect
        fps = max(1, min(120, wantFps))

        let (content, code) = shareable()
        guard let content else { return code }
        guard let win = gameWindow(content, pid: wantPid) else {
            setError("no capturable window for pid \(wantPid)")
            return errNoWindow
        }
        scale = scale(for: win)
        let (pxW, pxH) = pixelSize(region)
        sink.prepare(w: pxW, h: pxH, strideBytes: pxW * 4)

        let filter = SCContentFilter(desktopIndependentWindow: win)
        let cfg = makeConfig(region)
        let s = SCStream(filter: filter, configuration: cfg, delegate: watchdog)
        do {
            try s.addStreamOutput(sinkOutput, type: .screen, sampleHandlerQueue: frameQueue)
        } catch {
            setError("addStreamOutput: \(error)")
            return errStream
        }
        let sem = DispatchSemaphore(value: 0)
        var startCode: Int32 = errOK
        s.startCapture { err in
            if let err {
                startCode = CGPreflightScreenCaptureAccess() ? errStream : errNotAuthorized
                self.setError("startCapture: \(err)")
            }
            sem.signal()
        }
        if sem.wait(timeout: .now() + 6.0) == .timedOut {
            setError("startCapture timed out")
            return errTimeout
        }
        if startCode != errOK { return startCode }
        stream = s
        setError("")
        return errOK
    }

    func stop() -> Int32 {
        guard let s = stream else { return errOK }
        stream = nil
        let sem = DispatchSemaphore(value: 0)
        s.stopCapture { _ in sem.signal() }
        _ = sem.wait(timeout: .now() + 3.0)
        return errOK
    }

    func setRegion(_ rect: CGRect) -> Int32 {
        region = rect
        guard let s = stream else { return errStream }
        let (pxW, pxH) = pixelSize(rect)
        sink.prepare(w: pxW, h: pxH, strideBytes: pxW * 4)
        let sem = DispatchSemaphore(value: 0)
        var code: Int32 = errOK
        s.updateConfiguration(makeConfig(rect)) { err in
            if let err { code = errStream; self.setError("updateConfiguration: \(err)") }
            sem.signal()
        }
        if sem.wait(timeout: .now() + 3.0) == .timedOut { return errTimeout }
        return code
    }

    /// One-shot capture (locate escalation / diagnostics). Blocking; callers
    /// run it off the engine thread.
    func once(pid: UInt32, rect: CGRect, dst: UnsafeMutablePointer<UInt8>, dstCap: Int,
              outW: inout Int32, outH: inout Int32, outStride: inout Int32) -> Int32 {
        if !CGPreflightScreenCaptureAccess() { return errNotAuthorized }
        let (content, code) = shareable()
        guard let content else { return code }
        guard let win = gameWindow(content, pid: pid) else { return errNoWindow }
        let winScale = scale(for: win)
        let pxW = max(1, Int((rect.width * winScale).rounded()))
        let pxH = max(1, Int((rect.height * winScale).rounded()))
        let filter = SCContentFilter(desktopIndependentWindow: win)
        let cfg = SCStreamConfiguration()
        cfg.sourceRect = rect
        cfg.width = pxW
        cfg.height = pxH
        cfg.pixelFormat = kCVPixelFormatType_32BGRA
        cfg.colorSpaceName = CGColorSpace.sRGB
        cfg.showsCursor = false
        cfg.scalesToFit = false
        cfg.preservesAspectRatio = false
        cfg.ignoreShadowsSingleWindow = true

        let sem = DispatchSemaphore(value: 0)
        var result: Int32 = errTimeout
        var gotW: Int32 = 0
        var gotH: Int32 = 0
        var gotStride: Int32 = 0
        // Boxed results: the Task is an escaping closure and cannot capture
        // inout parameters.
        let box = ResultBox()
        Task.detached {
            do {
                let img = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg)
                guard let data = img.dataProvider?.data, let ptr = CFDataGetBytePtr(data) else {
                    box.code = errStream
                    sem.signal()
                    return
                }
                let imgStride = img.bytesPerRow
                let packed = img.width * 4
                let need = packed * img.height
                guard need <= dstCap else {
                    box.code = errTooSmall
                    sem.signal()
                    return
                }
                if imgStride == packed {
                    memcpy(dst, ptr, need)
                } else {
                    // Padded rows: pack them on the way out (the same
                    // normalisation the stream publish path does).
                    for row in 0..<img.height {
                        memcpy(dst + row * packed, ptr + row * imgStride, packed)
                    }
                }
                gotW = Int32(img.width); gotH = Int32(img.height); gotStride = Int32(packed)
                box.code = errOK
            } catch {
                box.code = CGPreflightScreenCaptureAccess() ? errStream : errNotAuthorized
                self.setError("captureImage: \(error)")
            }
            sem.signal()
        }
        if sem.wait(timeout: .now() + 8.0) == .timedOut { return errTimeout }
        result = box.code
        if result == errOK {
            outW = gotW; outH = gotH; outStride = gotStride
        }
        return result
    }

    var shell: Sink { sink }
    var regionPixels: (Int32, Int32) {
        let (w, h) = pixelSize(region)
        return (Int32(w), Int32(h))
    }
    var isDead: Bool { watchdog.isDead }
    var watchdogText: String { watchdog.errorText }
    var errorOrWatchdog: String { watchdog.isDead ? watchdog.errorText : lastError }
}

// MARK: - C ABI

@_cdecl("mdc_cap_available")
public func mdc_cap_available() -> Int32 { 1 }

@_cdecl("mdc_cap_authorized")
public func mdc_cap_authorized() -> Int32 { CGPreflightScreenCaptureAccess() ? 1 : 0 }

@_cdecl("mdc_cap_frontmost_pid")
public func mdc_cap_frontmost_pid() -> Int32 {
    if let app = NSWorkspace.shared.frontmostApplication { return Int32(app.processIdentifier) }
    return -1
}

@_cdecl("mdc_window_frame")
public func mdc_window_frame(_ pid: UInt32, _ x: UnsafeMutablePointer<Double>?, _ y: UnsafeMutablePointer<Double>?,
                             _ w: UnsafeMutablePointer<Double>?, _ h: UnsafeMutablePointer<Double>?) -> Int32 {
    let (content, code) = CaptureEngine.shared.shareable()
    guard let content else { return code }
    guard let win = CaptureEngine.shared.gameWindow(content, pid: pid) else { return errNoWindow }
    x?.pointee = Double(win.frame.origin.x)
    y?.pointee = Double(win.frame.origin.y)
    w?.pointee = Double(win.frame.width)
    h?.pointee = Double(win.frame.height)
    return errOK
}

@_cdecl("mdc_window_scale")
public func mdc_window_scale(_ pid: UInt32) -> Double {
    let (content, _) = CaptureEngine.shared.shareable()
    guard let content, let win = CaptureEngine.shared.gameWindow(content, pid: pid) else { return 1 }
    var best: CGFloat = 1
    for screen in NSScreen.screens where screen.frame.intersects(win.frame) {
        best = max(best, screen.backingScaleFactor)
    }
    return Double(best)
}

@_cdecl("mdc_cap_start")
public func mdc_cap_start(_ pid: UInt32, _ x: Double, _ y: Double, _ w: Double, _ h: Double, _ fps: Int32) -> Int32 {
    guard w > 0, h > 0 else { return errBadArgs }
    return CaptureEngine.shared.start(pid: pid, rect: CGRect(x: x, y: y, width: w, height: h), wantFps: fps)
}

@_cdecl("mdc_cap_stop")
public func mdc_cap_stop() -> Int32 { CaptureEngine.shared.stop() }

@_cdecl("mdc_cap_set_region")
public func mdc_cap_set_region(_ x: Double, _ y: Double, _ w: Double, _ h: Double) -> Int32 {
    guard w > 0, h > 0 else { return errBadArgs }
    return CaptureEngine.shared.setRegion(CGRect(x: x, y: y, width: w, height: h))
}

@_cdecl("mdc_cap_region")
public func mdc_cap_region(_ w: UnsafeMutablePointer<Int32>?, _ h: UnsafeMutablePointer<Int32>?) -> Int32 {
    let size = CaptureEngine.shared.regionPixels
    w?.pointee = size.0
    h?.pointee = size.1
    return errOK
}

@_cdecl("mdc_cap_snapshot")
public func mdc_cap_snapshot(_ seq: UnsafeMutablePointer<UInt64>?, _ ptr: UnsafeMutablePointer<UnsafePointer<UInt8>?>?,
                             _ w: UnsafeMutablePointer<Int32>?, _ h: UnsafeMutablePointer<Int32>?,
                             _ stride: UnsafeMutablePointer<Int32>?) -> Int32 {
    let snap = CaptureEngine.shared.shell.snapshot(minSeq: 0)
    seq?.pointee = snap.seq
    ptr?.pointee = snap.ptr
    w?.pointee = snap.w
    h?.pointee = snap.h
    stride?.pointee = snap.stride
    return snap.code
}

@_cdecl("mdc_cap_snapshot_if")
public func mdc_cap_snapshot_if(_ minSeq: UInt64, _ seq: UnsafeMutablePointer<UInt64>?,
                                _ ptr: UnsafeMutablePointer<UnsafePointer<UInt8>?>?,
                                _ w: UnsafeMutablePointer<Int32>?, _ h: UnsafeMutablePointer<Int32>?,
                                _ stride: UnsafeMutablePointer<Int32>?) -> Int32 {
    let snap = CaptureEngine.shared.shell.snapshot(minSeq: minSeq)
    seq?.pointee = snap.seq
    ptr?.pointee = snap.ptr
    w?.pointee = snap.w
    h?.pointee = snap.h
    stride?.pointee = snap.stride
    return snap.code
}

@_cdecl("mdc_cap_frame_age_ms")
public func mdc_cap_frame_age_ms() -> Int64 { CaptureEngine.shared.shell.ageMs }

@_cdecl("mdc_cap_stats")
public func mdc_cap_stats(_ delivered: UnsafeMutablePointer<UInt64>?, _ skipped: UnsafeMutablePointer<UInt64>?,
                          _ fps: UnsafeMutablePointer<Int32>?) -> Int32 {
    let s = CaptureEngine.shared.shell.stats
    delivered?.pointee = s.0
    skipped?.pointee = s.1
    fps?.pointee = s.2
    return errOK
}

@_cdecl("mdc_cap_last_skip")
public func mdc_cap_last_skip() -> UnsafePointer<CChar>? {
    return (CaptureEngine.shared.shell.skipReason as NSString).utf8String
}

@_cdecl("mdc_cap_dead")
public func mdc_cap_dead() -> Int32 { CaptureEngine.shared.isDead ? 1 : 0 }

@_cdecl("mdc_cap_once")
public func mdc_cap_once(_ pid: UInt32, _ x: Double, _ y: Double, _ w: Double, _ h: Double,
                         _ dst: UnsafeMutablePointer<UInt8>?, _ dstCap: Int32,
                         _ outW: UnsafeMutablePointer<Int32>?, _ outH: UnsafeMutablePointer<Int32>?,
                         _ outStride: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let dst, w > 0, h > 0 else { return errBadArgs }
    var ow: Int32 = 0, oh: Int32 = 0, os: Int32 = 0
    let code = CaptureEngine.shared.once(pid: pid, rect: CGRect(x: x, y: y, width: w, height: h),
                                         dst: dst, dstCap: Int(dstCap), outW: &ow, outH: &oh, outStride: &os)
    outW?.pointee = ow; outH?.pointee = oh; outStride?.pointee = os
    return code
}

@_cdecl("mdc_cap_last_error")
public func mdc_cap_last_error() -> UnsafePointer<CChar>? {
    return (CaptureEngine.shared.errorOrWatchdog as NSString).utf8String
}
