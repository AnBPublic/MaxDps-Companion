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
//     render loop is never synchronously captured.
//   * Frames arrive asynchronously at up to `fps`; the delegate memcpys the
//     band into a double buffer and returns. queueDepth 2 => a busy consumer
//     drops frames instead of back-pressuring the capture pipeline.
//   * The hot path (engine tick) is a seqlock READ of the newest frame: no
//     syscall, no lock, no allocation, no IPC with the game.
//   * No audio, no cursor, no shadow, no scaling: 1:1 32BGRA.
//   * Region is reconfigurable at runtime (updateConfiguration): the host
//     starts on a bounded locate band and shrinks to the discovered strip
//     rect, which is <1% of a 1080p screen.
//
// LIFETIME / RACE RULES (each addressed deliberately):
//   * Each stream start creates a fresh SESSION (its own sink, output and
//     watchdog). A stopped-but-not-yet-quiesced stream therefore writes into
//     ITS OWN sink, never into the live one, and `mdc_cap_dead` reports the
//     CURRENT session only.
//   * Frame buffers are retired for TWO generations after a region change,
//     then freed: a reader's snapshot pointer may be held across a region
//     change (use-after-free if freed immediately), and two generations is
//     far longer than any tick read. A discarded SESSION's sink is not freed
//     explicitly — the buffer is small and the process-lifetime leak is the
//     price of never invalidating a snapshot (see LIFETIME RULES above).
//   * All control operations (start/stop/setRegion/once) are serialised by
//     one lock, so a supervisor-driven restart cannot interleave with a stop.
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
//   int32  mdc_cap_snapshot_if(uint64 minSeq, ...) — same shape;
//   int64  mdc_cap_frame_age_ms(void);
//   int32  mdc_cap_stats(uint64 *delivered, uint64 *skipped, int32 *fps);
//   int32  mdc_cap_dead(void);
//   const char *mdc_cap_last_error(void);
//   const char *mdc_cap_last_skip(void);
//   int32  mdc_cap_once(uint32 pid, double x, double y, double w, double h,
//                       uint8 *dst, int32 dstCap, int32 *outW, int32 *outH, int32 *outStride);
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
    /// Retired buffers tagged with the generation they were retired in. A
    /// snapshot pointer handed out before a region change stays valid, so
    /// buffers are kept for TWO generations and then freed (see LIFETIME
    /// RULES in the header). Never freed immediately: that is a use-after-free.
    private var retired: [(gen: UInt64, buf: UnsafeMutablePointer<UInt8>)] = []
    private var generation: UInt64 = 0

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
    private var lastSkip = "none"

    private var width: Int32 = 0
    private var height: Int32 = 0
    private var stride: Int32 = 0

    var skipReason: String {
        lock.lock(); defer { lock.unlock() }
        return lastSkip
    }

    /// (Re)allocates for a new region and invalidates the published frame.
    /// Old buffers are retired for two generations, then freed (a reader may
    /// still hold one across the reconfigure).
    /// Frames are normalised to a packed stride (w*4): ScreenCaptureKit pads
    /// rows (measured: 400 px -> stride 1664, i.e. +64 B/row), so the publish
    /// path row-copies when the delivered stride is wider.
    func prepare(w: Int, h: Int, strideBytes: Int) {
        lock.lock(); defer { lock.unlock() }
        let need = strideBytes * h
        if need > cap {
            generation &+= 1
            if let a = bufA { retired.append((gen: generation, buf: a)) }
            if let b = bufB { retired.append((gen: generation, buf: b)) }
            // Free what was retired two or more generations ago: no snapshot
            // handed out since then can still point at it.
            let oldestKept = generation >= 2 ? generation - 2 : 0
            var kept: [(gen: UInt64, buf: UnsafeMutablePointer<UInt8>)] = []
            kept.reserveCapacity(retired.count)
            for entry in retired {
                if entry.gen <= oldestKept {
                    entry.buf.deallocate()
                } else {
                    kept.append(entry)
                }
            }
            retired = kept
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
            lastSkip = "no image buffer (\(statusText), samples=\(CMSampleBufferGetNumSamples(sb)))"
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

    /// Marks the current configuration as unusable (a failed reconfigure).
    func invalidate() {
        lock.lock(); defer { lock.unlock() }
        valid = false
    }
}

private final class SinkOutput: NSObject, SCStreamOutput {
    private let sink: Sink
    private let generation: UInt64
    private let owner: CaptureEngine

    init(_ s: Sink, generation: UInt64, owner: CaptureEngine) {
        sink = s
        self.generation = generation
        self.owner = owner
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen else { return }
        // A stream that was replaced (stop timeout, restart) must not publish
        // into the live session's sink.
        guard owner.isCurrent(generation) else { return }
        sink.publish(sb)
    }
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

    /// One live capture run: stream + sink + watchdog + geometry.
    private final class Session {
        let generation: UInt64
        let sink: Sink
        let output: SinkOutput
        var watchdog: StreamWatchdog
        var stream: SCStream?
        var region: CGRect
        var scale: CGFloat
        var fps: Int32

        init(generation: UInt64, sink: Sink, output: SinkOutput, watchdog: StreamWatchdog,
             stream: SCStream?, region: CGRect, scale: CGFloat, fps: Int32) {
            self.generation = generation
            self.sink = sink
            self.output = output
            self.watchdog = watchdog
            self.stream = stream
            self.region = region
            self.scale = scale
            self.fps = fps
        }
    }

    private let ctl = NSRecursiveLock()            // serialises every control op
    private let frameQueue = DispatchQueue(label: "com.maxdps.companion.capture.frames", qos: .userInitiated)
    private var session: Session?
    private var nextGeneration: UInt64 = 0
    private var lastError = ""
    /// Stored string backing the const char* returned by the error getters
    /// (a temporary NSString's utf8String would dangle once it is released).
    /// LOCKED: cacheError runs on the capture queue while the getters run on
    /// the probe/UI thread; an unlocked NSString swap is a retain/release data
    /// race (crash class: EXC_BAD_ACCESS in _objc_release).
    private let errorLock = NSLock()
    private var storedError: NSString = ""

    var cachedError: NSString {
        get {
            errorLock.lock()
            defer { errorLock.unlock() }
            return storedError
        }
        set {
            errorLock.lock()
            defer { errorLock.unlock() }
            storedError = newValue
        }
    }

    /// Pointer valid until the next cacheError (the C# side copies it
    /// immediately), returned under the same lock as the swap.
    var cachedErrorPointer: UnsafePointer<CChar>? {
        errorLock.lock()
        defer { errorLock.unlock() }
        return storedError.utf8String
    }

    private func setError(_ s: String) { lastError = s }
    var errorText: String { lastError }

    func isCurrent(_ generation: UInt64) -> Bool {
        ctl.lock(); defer { ctl.unlock() }
        return session?.generation == generation
    }

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
                code = CGPreflightScreenCaptureAccess() ? errStream : errNotAuthorized
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

    private func pixelSize(_ rect: CGRect, scale: CGFloat) -> (Int, Int) {
        (max(1, Int((rect.width * scale).rounded())), max(1, Int((rect.height * scale).rounded())))
    }

    private func makeConfig(_ rect: CGRect, scale: CGFloat, fps: Int32) -> SCStreamConfiguration {
        let (pxW, pxH) = pixelSize(rect, scale: scale)
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
        ctl.lock(); defer { ctl.unlock() }
        if !CGPreflightScreenCaptureAccess() { return errNotAuthorized }
        _ = stopLocked()

        let (content, code) = shareable()
        guard let content else { return code }
        guard let win = gameWindow(content, pid: wantPid) else {
            setError("no capturable window for pid \(wantPid)")
            return errNoWindow
        }
        let scale = scale(for: win)
        // Clamp to the window so a tiny window cannot produce a region that
        // never matches the delivered frames.
        let w = max(8, min(rect.width, win.frame.width))
        let h = max(8, min(rect.height, win.frame.height))
        let region = CGRect(x: max(0, rect.origin.x), y: max(0, rect.origin.y), width: w, height: h)

        nextGeneration &+= 1
        let sink = Sink()
        let (pxW, pxH) = pixelSize(region, scale: scale)
        sink.prepare(w: pxW, h: pxH, strideBytes: pxW * 4)
        let watchdog = StreamWatchdog()
        let output = SinkOutput(sink, generation: nextGeneration, owner: self)
        let s = SCStream(filter: SCContentFilter(desktopIndependentWindow: win),
                         configuration: makeConfig(region, scale: scale, fps: wantFps),
                         delegate: watchdog)
        do {
            try s.addStreamOutput(output, type: .screen, sampleHandlerQueue: frameQueue)
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

        session = Session(generation: nextGeneration, sink: sink, output: output,
                          watchdog: watchdog, stream: s, region: region,
                          scale: scale, fps: max(1, min(120, wantFps)))
        setError("")
        return errOK
    }

    func stop() -> Int32 {
        ctl.lock(); defer { ctl.unlock() }
        return stopLocked()
    }

    private func stopLocked() -> Int32 {
        guard let current = session else { return errOK }
        // Drop the session FIRST: a stream that does not quiesce within the
        // timeout can no longer publish (its generation is no longer current).
        session = nil
        let sem = DispatchSemaphore(value: 0)
        current.stream?.stopCapture { _ in sem.signal() }
        if sem.wait(timeout: .now() + 3.0) == .timedOut {
            setError("stopCapture timed out; the retired stream keeps running into its own sink")
        }
        return errOK
    }

    func setRegion(_ rect: CGRect) -> Int32 {
        ctl.lock(); defer { ctl.unlock() }
        guard let current = session, let stream = current.stream else { return errStream }
        let w = max(8, min(rect.width, current.region.width + 8192))
        let h = max(8, min(rect.height, current.region.height + 8192))
        let clamped = CGRect(x: max(0, rect.origin.x), y: max(0, rect.origin.y), width: w, height: h)
        let (pxW, pxH) = pixelSize(clamped, scale: current.scale)
        current.sink.prepare(w: pxW, h: pxH, strideBytes: pxW * 4)

        let sem = DispatchSemaphore(value: 0)
        var code: Int32 = errOK
        stream.updateConfiguration(makeConfig(clamped, scale: current.scale, fps: current.fps)) { err in
            if let err { code = errStream; self.setError("updateConfiguration: \(err)") }
            sem.signal()
        }
        if sem.wait(timeout: .now() + 3.0) == .timedOut { return errTimeout }
        if code == errOK {
            current.region = clamped          // commit only on success
        } else {
            setError("updateConfiguration failed; region left at \(current.region)")
        }
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
        let box = ResultBox()
        Task.detached {
            do {
                let img = try await SCScreenshotManager.captureImage(
                    contentFilter: SCContentFilter(desktopIndependentWindow: win), configuration: cfg)
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
                    // Packed rows on the way out (same normalisation as the stream path).
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

    var currentSink: Sink? {
        ctl.lock(); defer { ctl.unlock() }
        return session?.sink
    }

    var regionPixels: (Int32, Int32) {
        ctl.lock(); defer { ctl.unlock() }
        guard let s = session else { return (0, 0) }
        let (w, h) = pixelSize(s.region, scale: s.scale)
        return (Int32(w), Int32(h))
    }

    var regionRect: CGRect {
        ctl.lock(); defer { ctl.unlock() }
        return session?.region ?? .zero
    }

    var isDead: Bool {
        ctl.lock(); defer { ctl.unlock() }
        return session?.watchdog.isDead ?? false
    }

    var watchdogText: String {
        ctl.lock(); defer { ctl.unlock() }
        return session?.watchdog.errorText ?? ""
    }

    var errorOrWatchdog: String {
        ctl.lock(); defer { ctl.unlock() }
        if let s = session, s.watchdog.isDead { return s.watchdog.errorText }
        return lastError
    }

    var hasSession: Bool {
        ctl.lock(); defer { ctl.unlock() }
        return session != nil
    }
}

/// Reference box for results produced inside an escaping Task.
private final class ResultBox {
    var code: Int32 = errTimeout
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
    guard let sink = CaptureEngine.shared.currentSink else {
        seq?.pointee = 0; ptr?.pointee = nil; w?.pointee = 0; h?.pointee = 0; stride?.pointee = 0
        return errNoFrame
    }
    let snap = sink.snapshot(minSeq: 0)
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
    guard let sink = CaptureEngine.shared.currentSink else {
        seq?.pointee = 0; ptr?.pointee = nil; w?.pointee = 0; h?.pointee = 0; stride?.pointee = 0
        return errNoFrame
    }
    let snap = sink.snapshot(minSeq: minSeq)
    seq?.pointee = snap.seq
    ptr?.pointee = snap.ptr
    w?.pointee = snap.w
    h?.pointee = snap.h
    stride?.pointee = snap.stride
    return snap.code
}

@_cdecl("mdc_cap_frame_age_ms")
public func mdc_cap_frame_age_ms() -> Int64 { CaptureEngine.shared.currentSink?.ageMs ?? -1 }

@_cdecl("mdc_cap_stats")
public func mdc_cap_stats(_ delivered: UnsafeMutablePointer<UInt64>?, _ skipped: UnsafeMutablePointer<UInt64>?,
                          _ fps: UnsafeMutablePointer<Int32>?) -> Int32 {
    guard let sink = CaptureEngine.shared.currentSink else {
        delivered?.pointee = 0; skipped?.pointee = 0; fps?.pointee = 0
        return errNoFrame
    }
    let s = sink.stats
    delivered?.pointee = s.0
    skipped?.pointee = s.1
    fps?.pointee = s.2
    return errOK
}

@_cdecl("mdc_cap_last_skip")
public func mdc_cap_last_skip() -> UnsafePointer<CChar>? {
    CaptureEngine.shared.cacheError(CaptureEngine.shared.currentSink?.skipReason ?? "")
    return CaptureEngine.shared.cachedErrorPointer
}

@_cdecl("mdc_cap_dead")
public func mdc_cap_dead() -> Int32 { CaptureEngine.shared.isDead ? 1 : 0 }

@_cdecl("mdc_cap_has_session")
public func mdc_cap_has_session() -> Int32 { CaptureEngine.shared.hasSession ? 1 : 0 }

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
    CaptureEngine.shared.cacheError(CaptureEngine.shared.errorOrWatchdog)
    return CaptureEngine.shared.cachedErrorPointer
}

private extension CaptureEngine {
    /// Copies a Swift string into a stored NSString so the returned C pointer
    /// stays valid until the next call (a temporary would dangle). The store
    /// is locked (see cachedError).
    func cacheError(_ text: String) {
        cachedError = text as NSString
    }
}
