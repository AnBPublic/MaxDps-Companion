using System.Diagnostics;

namespace MaxDpsCompanion;

internal static class Program
{
    // Environment.ProcessPath always resolves to the real .exe location,
    // so settings.ini sits next to the exe (dist\) and not in a temp dir.
    internal static string AppDir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory;

    [STAThread]
    private static void Main(string[] args)
    {
        // Must happen before any window exists, otherwise Windows virtualises our
        // window coordinates on a scaled display and every sampled pixel is wrong.
        try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch (EntryPointNotFoundException) { /* pre-1703 Windows; the manifest default applies */ }

        ApplicationConfiguration.Initialize();

        var settingsPath = Path.Combine(AppDir, "settings.ini");
        var settings = AppSettings.Load(settingsPath);

        try
        {
            var snapshotArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
            if (snapshotArg is not null)
            {
                using var window = new MainForm(settings);
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-32000, -32000);
                window.ShowInTaskbar = false;
                window.Show();
                Application.DoEvents();
                window.PerformLayout();
                window.Refresh();
                using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
                window.DrawToBitmap(bitmap, window.ClientRectangle);
                bitmap.Save(snapshotArg[(snapshotArg.IndexOf('=') + 1)..]);
                return;
            }
            // Renders the Advanced popup (overlay) for design review.
            var advArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot-advanced=", StringComparison.OrdinalIgnoreCase));
            if (advArg is not null)
            {
                using var window = new MainForm(settings);
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-32000, -32000);
                window.ShowInTaskbar = false;
                window.Show();
                Application.DoEvents();
                window.OpenAdvancedForSnapshot();
                window.PerformLayout();
                window.Refresh();
                Application.DoEvents();
                using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
                window.DrawToBitmap(bitmap, window.ClientRectangle);
                bitmap.Save(advArg[(advArg.IndexOf('=') + 1)..]);
                return;
            }
            // PERF harness: times the capture hot path (what the engine runs
            // every tick) and the legacy per-pixel path it replaced, so the
            // optimisation is a measured number, not a claim.
            var benchArg = args.FirstOrDefault(arg => arg.StartsWith("--bench-sample=", StringComparison.OrdinalIgnoreCase));
            if (benchArg is not null)
            {
                var n = int.TryParse(benchArg[(benchArg.IndexOf('=') + 1)..], out var parsed) ? parsed : 300;
                var lines = new List<string>();
                using (var sampler = new ScreenSampler())
                {
                    var at = new Point(200, 200);
                    // Warm up (first call allocates the DIB surface).
                    for (var i = 0; i < 50; i++) sampler.Sample(at, 8);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < n; i++) sampler.Sample(at, 8);
                    sw.Stop();
                    lines.Add($"sample(9 cells, 5 taps): {sw.Elapsed.TotalMicroseconds / n:F2} us/op  (n={n})");
                }
                // BitBlt cost with/without CAPTUREBLT: the flag forces DWM
                // compositing of layered windows and can dominate the tick.
                var src = Native.GetDC(IntPtr.Zero);
                var dst = Native.CreateCompatibleDC(src);
                var bmp = Native.CreateCompatibleBitmap(src, 72, 8);
                var prev = Native.SelectObject(dst, bmp);
                var blits = Math.Min(n, 600);
                foreach (var (label, rop) in new[]
                {
                    ("BitBlt 72x8 SRCCOPY", (int)Native.SRCCOPY),
                    ("BitBlt 72x8 SRCCOPY|CAPTUREBLT", (int)(Native.SRCCOPY | Native.CAPTUREBLT)),
                })
                {
                    var swb = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < blits; i++) Native.BitBlt(dst, 0, 0, 72, 8, src, 200, 200, rop);
                    swb.Stop();
                    lines.Add($"{label,-32} {swb.Elapsed.TotalMicroseconds / blits:F2} us/op  (n={blits})");
                }
                Native.SelectObject(dst, prev);
                Native.DeleteObject(bmp);
                Native.DeleteDC(dst);
                Native.ReleaseDC(IntPtr.Zero, src);

                // Legacy path cost reference: the same tap count via GetPixel.
                var dc = Native.GetDC(IntPtr.Zero);
                var legacy = Math.Min(n, 300);
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < legacy; i++)
                    for (var t = 0; t < 45; t++) Native.GetPixel(dc, 200 + (t % 9) * 8, 200);
                sw2.Stop();
                Native.ReleaseDC(IntPtr.Zero, dc);
                lines.Add($"legacy GetPixel x45:     {sw2.Elapsed.TotalMicroseconds / legacy:F2} us/op  (n={legacy})");
                File.WriteAllLines(Path.Combine(AppDir, "bench.txt"), lines);
                return;
            }
            if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                using var window = new MainForm(settings);
                window.CreateControl();
                return;
            }
            Application.Run(new MainForm(settings));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "MaxDPS Companion crashed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Debug.WriteLine(ex);
        }
    }
}
