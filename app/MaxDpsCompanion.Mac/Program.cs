// Mac companion host.
// No args (Finder double-click): opens the Avalonia UI (Start/Pause/Stop,
//   Launch Game, Open Folder, live status).
// --ui:          force the Avalonia UI.
// --probe:       read-only attach + sample + decode report (probe.txt + stdout).
// --locate:      one-shot strip sweep (BlockLocator equivalent).
// --run:         headless rotation loop until Ctrl+C (no window).
// Probe must NEVER prompt for Accessibility (capture = Screen Recording;
// probe does no sending).
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace MaxDpsCompanion.Mac;

internal static class Program
{
    private static string AppSupportDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/MaxDpsCompanion");

    // Avalonia entry: platform detect + Inter font, fluent theme in MacApp.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MacApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int Main(string[] args)
    {
        // Settings live in ~/Library/Application Support/MaxDpsCompanion/
        // (tracked settings.ini is the default; per-machine edits are never
        // committed — same rule as dist\ on Windows).
        var settings = LoadSettings();

        // Headless CLI first: --probe/--locate/--run never open a window.
        if (HasFlag(args, "--probe"))
        {
            var lines = Probe(settings);
            Directory.CreateDirectory(AppSupportDir);
            File.WriteAllLines(Path.Combine(AppSupportDir, "probe.txt"), lines);
            foreach (var line in lines)
                Console.WriteLine(line);
            return 0;
        }

        if (HasFlag(args, "--locate"))
            return Locate(settings);

        if (HasFlag(args, "--bench-capture"))
            return BenchCapture(settings, args);

        if (HasFlag(args, "--run"))
            return Run(settings);

        // No args (Finder) or --ui: open the desktop UI.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    internal static AppSettings LoadSettings()
    {
        var path = Path.Combine(AppSupportDir, "settings.ini");
        if (!File.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(AppSupportDir);
                var seed = Path.Combine(AppContext.BaseDirectory, "settings.ini");
                if (File.Exists(seed)) File.Copy(seed, path);
            }
            catch { /* best effort; defaults apply */ }
        }
        var settings = AppSettings.Load(path);
        // Mac default process name: the short "Wow" owner string also
        // matches, but the full name is the documented canonical value.
        return settings;
    }

    /// <summary>Shows probe output when launched from Finder (no terminal).
    /// osascript only — no new native dependency, never prompts for permissions.
    /// Newlines use AppleScript's `return` concatenation: a literal line break
    /// inside a display-dialog string is a syntax error, so the text is split
    /// per line (only `"` needs escaping — backslash is literal in AppleScript).</summary>
    private static void ShowDialog(string text)
    {
        // osascript via a script FILE: the -e "..." form breaks on the
        // shell-quoting layer ((...)) parens + & return & + embedded quotes,
        // which is why the dialog never appeared while probe.txt was written.
        var scriptPath = Path.Combine(Path.GetTempPath(), "maxdps-probe-dialog.applescript");
        try
        {
            var parts = text.Split('\n').Select(l => "\"" + l.Replace("\"", "\\\"") + "\"");
            var expr = string.Join(" & return & ", parts);
            var script = $"display dialog {expr} with title \"MaxDPS Companion (Mac probe)\" buttons {{\"Show probe.txt\", \"OK\"}} default button \"OK\" with icon note";
            File.WriteAllText(scriptPath, script);
            var psi = new System.Diagnostics.ProcessStartInfo("osascript", $"\"{scriptPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            if (p?.StandardOutput.ReadToEnd().Contains("Show probe.txt") == true)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", $"\"{Path.Combine(AppSupportDir, "probe.txt")}\"") { UseShellExecute = true });
        }
        catch { /* dialog is best-effort; probe.txt is the record */ }
    }

    private static List<string> Probe(AppSettings settings)
    {
        var lines = new List<string>();
        // Windows shape: process='...' refresh=... valid=... handle=0x...
        // Mac equivalent: window-list lookup + CGWindowList bounds.
        lines.Add($"process='World of Warcraft' pid={WowPid()} bounds={WowBounds()} frontmost={WowFrontmost()}");
        // Windows shape: client origin=x,y size=wxh
        // Mac adds the display scale (points -> device px) and the capture
        // backend; the capture layer is window-relative, so no Y-flip math is
        // involved (CGWindowList and SCK window frames share the top-left
        // origin coordinate space).
        lines.Add($"client origin={WowClientOrigin()} size={WowClientSize()} displayScale={DisplayScale()} (window-relative capture)");
        lines.Add($"capture-backend={CaptureBackend()}");
        lines.Add($"screen-recording={(new MacPermissions().CheckCapturePermission() ? "granted" : "NOT-GRANTED (capture returns blank; grant in System Settings > Privacy & Security)")}");
        // Live sweep + sample + decode through the real capture path.
        lines.AddRange(SweepAndDecode(settings));
        lines.Add("permissions: Screen Recording needed for capture (ScreenCaptureKit), Accessibility needed for send (CGEventPostToPid); probe is read-only and must NOT prompt for Accessibility");
        return lines;
    }

    /// <summary>One line describing which capture backend is live.</summary>
    private static string CaptureBackend() =>
        !MdcNative.AvailableSafe
            ? $"legacy CGWindowListCreateImage (native SCK library missing: {MdcNative.ProbeError})"
            : MdcNative.Authorized() == 1
                ? "ScreenCaptureKit (native libmdc_capture present, Screen Recording granted)"
                : "ScreenCaptureKit present but Screen Recording NOT granted";

    private static string DisplayScale() => TryNative("scale", NativeMac.BackingScale);

    /// <summary>One-shot locate + sample + decode over the live game window.</summary>
    private static List<string> SweepAndDecode(AppSettings settings)
    {
        var lines = new List<string>();
        MacCaptureStream? stream = null;
        try
        {
            var window = new MacGameWindow();
            if (!window.Refresh(settings.ProcessName))
            {
                lines.Add($"locate=null (no process named '{settings.ProcessName}')");
                return lines;
            }
            if (!window.TryGetClientOrigin(out var origin, out var size))
            {
                lines.Add("locate=null (game window has no client area)");
                return lines;
            }

            // Real capture path: window-filtered SCK stream on the locate band.
            stream = new MacCaptureStream((int)window.ProcessId, fps: 60);
            if (stream.Start())
            {
                WaitForFirstFrame(stream, 800);
                var stats = stream.Stats();
                lines.Add($"stream=started region={stream.Region.W:F0}x{stream.Region.H:F0}pt px={stream.RegionPixels.W}x{stream.RegionPixels.H} frames={stats.Delivered} skipped={stats.Skipped} fps={stats.Fps} ageMs={stream.FrameAgeMs}");
            }
            else
            {
                lines.Add($"stream=unavailable ({stream.LastErrorCode}: {stream.LastErrorText}) - falling back to legacy capture");
            }

            var scanner = new MacBlockScanner(stream.Running ? stream : null, settings.Color);
            var found = scanner.Locate(origin, size);
            if (found is null)
            {
                lines.Add($"locate=null (no strip in {size.Width}x{size.Height} at {origin.X},{origin.Y})");
                lines.Add($"debugshot={MacDebugShot.Capture(origin, size)}");
                lines.AddRange(Diagnose(origin, settings, stream.Running ? stream : null));
                return lines;
            }
            lines.Add($"locate={found.Value.OffsetX},{found.Value.OffsetY} cell={found.Value.CellSize}");
            var sampler = new MacScreenSampler(stream.Running ? stream : null, settings.Color);
            var at = new System.Drawing.Point(origin.X + found.Value.OffsetX, origin.Y + found.Value.OffsetY);
            var cells = sampler.Sample(at, found.Value.CellSize);
            var frame = PixelProtocol.Decode(cells, settings.Color)
                ?? PixelProtocol.Decode(PixelProtocol.TrimToV4(cells), settings.Color)
                ?? PixelProtocol.Decode(PixelProtocol.TrimToV1(cells), settings.Color);
            if (frame is null)
            {
                var fault = PixelProtocol.Diagnose(cells, settings.Color);
                lines.Add($"decode=null (fault={fault})");
                return lines;
            }
            var slots = string.Join(",", frame.Slots.Select(s => s?.Describe() ?? "-"));
            lines.Add($"decode=proto={frame.Version} state={frame.State} slots={slots} ext3={frame.Ext3Present}");
            if (stream.Running)
            {
                var after = stream.Stats();
                var (rw, rh) = stream.RegionPixels;
                lines.Add($"stream=after-locate regionPx={rw}x{rh} frames={after.Delivered} fps={after.Fps} ageMs={stream.FrameAgeMs}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"locate/decode error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            stream?.Dispose();
        }
        return lines;
    }

    /// <summary>Waits (bounded) for the first delivered frame.</summary>
    private static void WaitForFirstFrame(MacCaptureStream stream, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (stream.FrameAgeMs >= 0) return;
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Hard-number capture benchmark: what does the sampling path cost us,
    /// and what does the game pay for it? Run it while playing to measure the
    /// real thing; with no game running it reports the window-missing path.
    ///   --bench-capture[=seconds]   (default 5 s)
    /// </summary>
    private static int BenchCapture(AppSettings settings, string[] args)
    {
        var seconds = 5.0;
        foreach (var a in args)
        {
            var i = a.IndexOf('=');
            if (i > 0 && a[..i].Equals("--bench-capture", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(a[(i + 1)..], out var v) && v > 0)
                seconds = Math.Min(120, v);
        }

        var window = new MacGameWindow();
        if (!window.Refresh(settings.ProcessName))
        {
            Console.WriteLine($"bench: no process named '{settings.ProcessName}' - start WoW first");
            return 1;
        }
        if (!window.TryGetClientOrigin(out var origin, out var size))
        {
            Console.WriteLine("bench: game window has no client area");
            return 1;
        }

        using var stream = new MacCaptureStream((int)window.ProcessId, fps: 60);
        if (!stream.Start())
        {
            Console.WriteLine($"bench: stream start failed ({stream.LastErrorCode}: {stream.LastErrorText})");
            return 1;
        }
        WaitForFirstFrame(stream, 1000);
        var scanner = new MacBlockScanner(stream, settings.Color);
        var locateSw = System.Diagnostics.Stopwatch.StartNew();
        var found = scanner.Locate(origin, size);
        locateSw.Stop();
        if (found is not { } loc)
        {
            Console.WriteLine("bench: strip not found - start the game with the MaxDpsBridgeExp addon loaded, then re-run");
            return 1;
        }
        var at = new System.Drawing.Point(origin.X + loc.OffsetX, origin.Y + loc.OffsetY);
        var sampler = new MacScreenSampler(stream, settings.Color);
        var (rw, rh) = stream.RegionPixels;
        Console.WriteLine($"bench: strip at {loc.OffsetX},{loc.OffsetY} cell={loc.CellSize}pt  locate={locateSw.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"bench: steady-state region {rw}x{rh} px = {rw * rh * 4 / 1024.0:F1} KB/frame");

        // Warm-up (JIT + first frames).
        for (var i = 0; i < 50; i++) sampler.Sample(at, loc.CellSize);

        var times = new List<double>(4096);
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var cpuBefore = proc.TotalProcessorTime;
        var samplesBefore = 0L;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var cells = sampler.Sample(at, loc.CellSize);
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            times.Add((t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            samplesBefore++;
            // 33 ms cadence: exactly what the engine tick does.
            Thread.Sleep(33);
        }
        sw.Stop();
        var cpuAfter = proc.TotalProcessorTime;
        var allocAfter = GC.GetAllocatedBytesForCurrentThread();
        var stats = stream.Stats();

        times.Sort();
        double P(int pct) => times.Count == 0 ? 0 : times[Math.Min(times.Count - 1, times.Count * pct / 100)];
        var mean = times.Count == 0 ? 0 : times.Sum() / times.Count;
        var cpuMs = (cpuAfter - cpuBefore).TotalMilliseconds;
        Console.WriteLine($"bench: {samplesBefore} samples in {sw.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"bench: per-sample us  mean={mean * 1000:F1}  p50={P(50) * 1000:F1}  p95={P(95) * 1000:F1}  p99={P(99) * 1000:F1}  max={times[^1] * 1000:F1}");
        Console.WriteLine($"bench: managed alloc {allocAfter - allocBefore:N0} bytes total ({((allocAfter - allocBefore) / Math.Max(1, samplesBefore)):F0} per sample)");
        Console.WriteLine($"bench: our CPU {cpuMs:F1} ms over {sw.Elapsed.TotalSeconds:F1} s = {cpuMs / (sw.Elapsed.TotalSeconds * 10):F2}% of one core");
        Console.WriteLine($"bench: frames delivered={stats.Delivered} skipped={stats.Skipped} fps={stats.Fps} ageMs={stream.FrameAgeMs}");
        Console.WriteLine("bench: the game's render loop is never captured synchronously - the stream is asynchronous, bounded to the strip rect");
        return 0;
    }

    /// <summary>--locate: strip sweep only.</summary>
    private static int Locate(AppSettings settings)
    {
        foreach (var line in SweepAndDecode(settings))
            Console.WriteLine(line);
        return 0;
    }

    /// <summary>
    /// Diagnostic: try the strip at the top-left corner across the plausible
    /// UI-scale cell sizes; report the magic cell, version nibble and the
    /// 43 sampled colours so a byte-order or scale mismatch is obvious.
    /// </summary>
    private static List<string> Diagnose(System.Drawing.Point origin, AppSettings settings, MacCaptureStream? stream)
    {
        var lines = new List<string>();
        var sampler = new MacScreenSampler(stream is { Running: true } ? stream : null, settings.Color);
        foreach (var cs in new[] { 6, 7, 8, 9, 10, 11, 12, 13, 14, 16 })
        {
            try
            {
                var cells = sampler.Sample(origin, cs);
                var c0 = cells.Length > 0 ? cells[0] : System.Drawing.Color.Empty;
                var version = cells.Length > 10 ? (int)Math.Round(cells[10].R / 17.0) : -1;
                var fault = PixelProtocol.Diagnose(cells, settings.Color);
                var decoded = PixelProtocol.Decode(cells, settings.Color) is not null;
                lines.Add($"diag cs={cs}: magic={c0.R},{c0.G},{c0.B} versionNibble={version} fault={fault} decoded={decoded}");
            }
            catch (Exception ex)
            {
                lines.Add($"diag cs={cs}: error {ex.GetType().Name}");
            }
        }
        return lines;
    }

    /// <summary>--run: full rotation loop until Ctrl+C.</summary>
    private static int Run(AppSettings settings)
    {
        using var host = new MacEngineHost(settings);
        host.StatusChanged += s => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s.Message} | {s.SlotSummary} | {s.LastKeySent}");
        if (!host.EnsurePermissions(out var perm))
        {
            Console.WriteLine(perm);
            Console.WriteLine("Grant Screen Recording, then re-run. (Accessibility can follow; sends hold until granted.)");
        }
        else
        {
            Console.WriteLine(perm);
        }
        Console.WriteLine("Starting rotation (Ctrl+C to stop)...");
        host.Start();
        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        done.Wait();
        Console.WriteLine("Stopping...");
        host.Stop();
        return 0;
    }

    private static string WowPid() => TryNative("pid", NativeMac.WowPid);
    private static string WowBounds() => TryNative("bounds", NativeMac.WowBounds);
    private static string WowFrontmost() => TryNative("frontmost", NativeMac.WowFrontmost);
    private static string WowClientOrigin() => TryNative("origin", NativeMac.WowClientOrigin);
    private static string WowClientSize() => TryNative("size", NativeMac.WowClientSize);
    private static string BackingScale() => TryNative("scale", NativeMac.BackingScale);

    private static string TryNative(string what, Func<string> fn)
    {
        try
        {
            return fn();
        }
        catch (DllNotFoundException)
        {
            return $"UNIMPLEMENTED-mac-todo ({what} native lib missing)";
        }
        catch (EntryPointNotFoundException)
        {
            return $"UNIMPLEMENTED-mac-todo ({what} entry point missing)";
        }
        catch (Exception ex)
        {
            return $"UNIMPLEMENTED-mac-todo ({what}: {ex.GetType().Name})";
        }
    }
}

// Real P1 macOS host services: WoW window lookup via CGWindowList,
// strip capture via CGWindowListCreateImage (MVP one-shot; ScreenCaptureKit
// steady-state is P2), pixel math in device px with Y-flip. Read-only:
// Screen Recording covers capture; probe never touches input, never prompts
// for Accessibility. Every entry point fails closed to UNIMPLEMENTED-... so
// --probe always writes probe.txt.
internal static class NativeMac
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int kCGWindowListOptionOnScreenOnly = 1 << 0;
    private const int kCGWindowListExcludeDesktopElements = 1 << 4;
    private const int kCGWindowImageDefault = 0;

    [DllImport(CoreGraphics, EntryPoint = "CGWindowListCopyWindowInfo")]
    private static extern IntPtr CGWindowListCopyWindowInfo(int option, uint relativeToWindow);

    [DllImport(CoreGraphics, EntryPoint = "CGWindowListCreateImage")]
    private static extern IntPtr CGWindowListCreateImage(CGRect rect, int listOption, uint windowID, int imageOption);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetWidth")]
    private static extern nuint CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageGetHeight")]
    private static extern nuint CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphics, EntryPoint = "CGImageRelease")]
    private static extern void CGImageRelease(IntPtr image);

    [DllImport(CoreFoundation, EntryPoint = "CFArrayGetCount")]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation, EntryPoint = "CFArrayGetValueAtIndex")]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);

    [DllImport(CoreFoundation, EntryPoint = "CFDictionaryGetValue")]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out long value);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    // CFString helpers: build once from UTF-8 literals via CFStringCreateWithCString.
    private const int kCFStringEncodingUTF8 = 0x08000100;
    [DllImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString", CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

    private static IntPtr Key(string name)
    {
        var k = CFStringCreateWithCString(IntPtr.Zero, name, kCFStringEncodingUTF8);
        return k;
    }

    private static long Number(IntPtr dict, string key)
    {
        var k = Key(key);
        try
        {
            var v = CFDictionaryGetValue(dict, k);
            if (v == IntPtr.Zero) return -1;
            return CFNumberGetValue(v, 10 /* kCFNumberSInt64Type */, out var n) ? n : -1; // 10 = SInt64
        }
        finally { CFRelease(k); }
    }

    private static string CFString(IntPtr dict, string key)
    {
        var k = Key(key);
        try
        {
            var v = CFDictionaryGetValue(dict, k);
            if (v == IntPtr.Zero) return "";
            // kCGWindowName is CFNull (not a string) when the window is
            // unnamed — verified live: only 10 keys present, no Name/Number.
            if (CFGetTypeID(v) != CFStringGetTypeID()) return "";
            var len = CFStringGetLength(v);
            if (len <= 0 || len > 512) return "";
            var sb = new StringBuilder((int)len + 1);
            CFStringGetCharacters(v, new CFRange(0, len), sb);
            return sb.ToString();
        }
        finally { CFRelease(k); }
    }

    [DllImport(CoreFoundation, EntryPoint = "CFGetTypeID")]
    private static extern nint CFGetTypeID(IntPtr obj);

    [DllImport(CoreFoundation, EntryPoint = "CFStringGetTypeID")]
    private static extern nint CFStringGetTypeID();

    [DllImport(CoreFoundation, EntryPoint = "CFStringGetLength")]
    private static extern nint CFStringGetLength(IntPtr str);

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange { public nint Location, Length; public CFRange(nint l, nint n) { Location = l; Length = n; } }

    [DllImport(CoreFoundation, EntryPoint = "CFStringGetCharacters")]
    private static extern void CFStringGetCharacters(IntPtr str, CFRange range, StringBuilder buffer);

    private static bool DictRect(IntPtr dict, string key, out double x, out double y, out double w, out double h)
    {
        x = y = w = h = 0;
        var k = Key(key);
        try
        {
            var v = CFDictionaryGetValue(dict, k);
            if (v == IntPtr.Zero) return false;
            // CGRectMakeWithDictionaryRepresentation(dict, &rect) fills a
            // 4-double rect; Height/Width/X/Y read directly as CFNumbers.
            if (!CGRectMakeWithDictionaryRepresentation(v, out var r)) return false;
            x = r.X; y = r.Y; w = r.Width; h = r.Height;
            return w > 0 && h > 0;
        }
        finally { CFRelease(k); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGArrowRect { public double X, Y, Width, Height; }

    [DllImport(CoreGraphics, EntryPoint = "CGRectMakeWithDictionaryRepresentation")]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGArrowRect rect);

    internal sealed record WindowInfo(long Pid, string Name, string Owner, long Layer, double X, double Y, double W, double H, bool Onscreen);

    private static List<WindowInfo> Windows()
    {
        var list = CGWindowListCopyWindowInfo(
            kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, 0);
        var out_ = new List<WindowInfo>();
        if (list == IntPtr.Zero) return out_;
        try
        {
            var n = CFArrayGetCount(list);
            for (nint i = 0; i < n; i++)
            {
                var d = CFArrayGetValueAtIndex(list, i);
                var pid = Number(d, "kCGWindowOwnerPID");
                var name = CFString(d, "kCGWindowName");
                var owner = CFString(d, "kCGWindowOwnerName");
                var layer = Number(d, "kCGWindowLayer");
                DictRect(d, "kCGWindowBounds", out var x, out var y, out var w, out var h);
                var onscreen = Number(d, "kCGWindowIsOnscreen") == 1;
                out_.Add(new WindowInfo(pid, name, owner, layer, x, y, w, h, onscreen));
            }
        }
        finally { CFRelease(list); }
        return out_;
    }

    // NOTE: ProcessName on macOS truncates to 15 chars ("World of Warcra").
    // Match on BOTH the full and truncated spellings, plus the exe stem
    // some installs report ("Wow"). Exact-equality on ANY of them only.
    private static readonly string[] WowNames =
        ["World of Warcraft", "World of Warcra", "Wow", "World of Warcraft.app"];

    internal static int? WowPidRaw()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                string name;
                try { name = p.ProcessName; }
                catch { continue; } // exited / access-denied mid-enumeration
                foreach (var want in WowNames)
                    if (name.Equals(want, StringComparison.OrdinalIgnoreCase))
                        return p.Id;
            }
        }
        catch { /* enumeration itself failed — fall through to window list */ }
        // Fallback: window-list owner match. NOTE: the Mac owner string is the
        // SHORT process name ("Wow", not "World of Warcraft") — verified live
        // 2026-10-09: owner=Wow pid=26374 layer=0 bounds=1920x1080.
        foreach (var w in Windows())
            if ((w.Owner.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase)
                    || w.Owner.Equals("Wow", StringComparison.OrdinalIgnoreCase))
                && w.W > 400 && w.H > 300)
                return (int)w.Pid;
        return null;
    }

    internal static WindowInfo? WowWindow()
    {
        var pid = WowPidRaw();
        if (pid is null) return null;
        WindowInfo? best = null;
        foreach (var w in Windows())
        {
            if (w.Pid != pid || !w.Onscreen || w.Layer != 0) continue;
            if (w.W < 400 || w.H < 300) continue;
            if (best is null || w.W * w.H > best.W * best.H) best = w;
        }
        return best;
    }

    internal static (double X, double Y, double W, double H)? WowBoundsRaw() =>
        WowWindow() is { } w ? (w.X, w.Y, w.W, w.H) : null;

    internal static string WowPid() => Guard(() =>
        WowPidRaw() is { } pid ? pid.ToString() : "UNIMPLEMENTED-not-running (World of Warcraft process not found)");

    internal static string WowBounds() => Guard(() =>
        WowWindow() is { } w ? $"{w.X:F0},{w.Y:F0} {w.W:F0}x{w.H:F0} (CG global coords, Y down from menu-bar)" : "UNIMPLEMENTED-no-window");

    internal static string WowFrontmost() => Guard(() =>
    {
        var pid = WowPidRaw();
        if (pid is null) return "UNIMPLEMENTED-not-running";
        // Frontmost via NSWorkspace would need AppKit; approximate: largest
        // on-screen layer-0 WoW window present => visible, frontmost unknown.
        return WowWindow() is not null ? "visible (frontmost check needs AppKit — P2)" : "not-visible";
    });

    internal static string WowClientOrigin() => Guard(() =>
        WowWindow() is { } w ? $"{w.X:F0},{w.Y:F0} (window bounds ~= client area on Mac; WoW is borderless)" : "UNIMPLEMENTED-no-window");

    internal static string WowClientSize() => Guard(() =>
        WowWindow() is { } w ? $"{w.W:F0}x{w.H:F0}" : "UNIMPLEMENTED-no-window");

    internal static string BackingScale() => Guard(() =>
        WowWindow() is { } w
            ? $"{MacCaptureStream.DisplayScaleFor(w.X, w.Y, w.W, w.H):0.##} (device pixels per point for the game's display)"
            : "UNIMPLEMENTED-no-window");

    // MVP capture entry points for P2 (compiled now, wired to decode later):
    // grabs the strip rect in device px; caller applies the Y-flip + scale.
    internal static nint CaptureWidth(IntPtr image) => GuardN(() => (nint)CGImageGetWidth(image));
    internal static nint CaptureHeight(IntPtr image) => GuardN(() => (nint)CGImageGetHeight(image));
    internal static void ReleaseImage(IntPtr image) => CGImageRelease(image);

    private static string Guard(Func<string> fn)
    {
        try
        {
            return fn();
        }
        catch (DllNotFoundException)
        {
            return "UNIMPLEMENTED-mac-todo";
        }
        catch (EntryPointNotFoundException)
        {
            return "UNIMPLEMENTED-mac-todo";
        }
        // Never throw out of probe stubs.
        catch
        {
            return "UNIMPLEMENTED-mac-todo";
        }
    }

    private static nint GuardN(Func<nint> fn)
    {
        try { return fn(); }
        catch { return -1; }
    }
}
