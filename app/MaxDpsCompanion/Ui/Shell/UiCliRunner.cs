using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MaxDpsCompanion.Ui.Shell;

/// <summary>Hosted-window CLI modes (plan §5).</summary>
internal enum UiCliMode
{
    None,
    SnapshotPage,
    Snapshot,
    SnapshotAdvanced,
    SmokeTest,
    BenchUi,
}

/// <summary>One parsed UI CLI request.</summary>
internal sealed record UiCliRequest(UiCliMode Mode, string Page, string Path, int Width, int Height);

/// <summary>Parse outcome: unavailable (exit 3, "unavailable in this build") or a request.</summary>
internal readonly record struct UiCliParse(bool Unavailable, UiCliRequest? Request);

/// <summary>
/// P2-E port of the WinForms snapshot/smoke/bench CLI (plan §5). All modes run
/// against the real, SHOWN shell window: <see cref="RenderTargetBitmap"/>
/// renders the XAML tree off the compositor (no screen capture), the buffer is
/// encoded to PNG with <see cref="BitmapEncoder"/>, and the non-blank assertion
/// samples a sparse colour grid (the old <c>distinctColors</c> contract).
/// Failures write <c>ui-error.txt</c> and exit 3; <c>--bench-ui</c> keeps its
/// old exit-1-on-target-miss semantics. <c>--ui-snapshot-class-skills</c> and
/// <c>--ui-snapshot-scroll</c> stay dropped ("unavailable", exit 3).
/// </summary>
internal static class UiCliRunner
{
    internal static UiCliRequest? Request { get; set; }

    internal static int ExitCode { get; set; }

    private static readonly string AppDir = Program.AppDir;

    private static Stopwatch? _shellBuild;

    // ----- parsing -----

    public static UiCliParse Parse(string[] args)
    {
        static bool Is(string arg, string prefix) => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        var uiArgs = args.Where(a => Is(a, "--ui-")).ToArray();
        var bench = args.Any(a => a.Equals("--bench-ui", StringComparison.OrdinalIgnoreCase));
        if (uiArgs.Length == 0 && !bench) return new UiCliParse(false, null);

        // Permanent drops (plan §5): the legacy Class skills screen and the
        // advanced scroll-offset snapshot have no WinUI surface.
        if (uiArgs.Any(a => Is(a, "--ui-snapshot-class-skills") || Is(a, "--ui-snapshot-scroll")))
            return new UiCliParse(true, null);

        string? First(string prefix) => uiArgs.FirstOrDefault(a => Is(a, prefix));
        static string Value(string arg) => arg[(arg.IndexOf('=') + 1)..].Trim();
        int IntValue(string prefix, int fallback)
        {
            var arg = First(prefix);
            return arg is not null && int.TryParse(Value(arg), out var parsed) ? Math.Clamp(parsed, 320, 8000) : fallback;
        }

        var width = IntValue("--ui-snapshot-width=", 1280);
        var height = IntValue("--ui-snapshot-height=", 900);

        // Old Program.cs order: page > snapshot > advanced > smoke > bench.
        if (First("--ui-snapshot-page=") is { } pageArg)
        {
            var page = Value(pageArg);
            var path = First("--ui-snapshot=") is { } snapshotPath
                ? Value(snapshotPath)
                : System.IO.Path.Combine(AppDir, $"ui-snapshot-{page}.png");
            return new UiCliParse(false, new UiCliRequest(UiCliMode.SnapshotPage, page, path, width, height));
        }
        if (First("--ui-snapshot=") is { } snapshotArg)
            return new UiCliParse(false, new UiCliRequest(UiCliMode.Snapshot, "", Value(snapshotArg), width, height));
        if (First("--ui-snapshot-advanced=") is { } advancedArg)
            return new UiCliParse(false, new UiCliRequest(UiCliMode.SnapshotAdvanced, "", Value(advancedArg), width, height));
        if (uiArgs.Any(a => a.Equals("--ui-smoke-test", StringComparison.OrdinalIgnoreCase)))
            return new UiCliParse(false, new UiCliRequest(UiCliMode.SmokeTest, "", "", width, height));
        if (bench)
            return new UiCliParse(false, new UiCliRequest(UiCliMode.BenchUi, "", "", width, height));

        // Any other --ui-* flag: not implemented in this build (old P0 contract).
        return new UiCliParse(true, null);
    }

    /// <summary>Bench-only: starts the construct+show stopwatch around App.OnLaunched.</summary>
    internal static void BeginShellBuild()
    {
        if (Request?.Mode == UiCliMode.BenchUi) _shellBuild = Stopwatch.StartNew();
    }

    internal static void EndShellBuild() => _shellBuild?.Stop();

    // ----- execution -----

    /// <summary>
    /// Runs the requested mode on the UI thread and closes the window; the exit
    /// code is read by Program.Main after Application.Start returns.
    /// </summary>
    internal static async Task RunAsync(ShellWindow shell, UiCliRequest request)
    {
        try
        {
            shell.SuppressSizePersistence = true;
            switch (request.Mode)
            {
                case UiCliMode.SnapshotPage:
                    await RunSnapshotPageAsync(shell, request);
                    break;
                case UiCliMode.Snapshot:
                    await RunSnapshotAsync(shell, request);
                    break;
                case UiCliMode.SnapshotAdvanced:
                    await RunSnapshotAdvancedAsync(shell, request);
                    break;
                case UiCliMode.SmokeTest:
                    await RunSmokeAsync(shell);
                    break;
                case UiCliMode.BenchUi:
                    await RunBenchAsync(shell);
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteError($"ui mode {request.Mode} failed: {ex}");
            ExitCode = 3;
        }
        finally
        {
            try { shell.Close(); }
            catch { /* the window may already be closing */ }
        }
    }

    private static async Task RunSnapshotPageAsync(ShellWindow shell, UiCliRequest request)
    {
        shell.ResizeForCli(request.Width, request.Height);
        await shell.WaitForShellReadyAsync();
        await Task.Delay(250); // resize settle

        var page = request.Page.Trim().ToLowerInvariant();
        switch (page)
        {
            case "" or "main" or "home" or "console":
                break;
            case "config" or "configuration" or "advanced-config":
                shell.ShowAdvancedTab(0);
                break;
            case "diag" or "diagnostics" or "advanced-diag":
                shell.ShowAdvancedTab(1);
                break;
            case "intel" or "intelligence" or "advanced-intel":
                shell.ShowAdvancedTab(2);
                break;
            case "abilities" or "abilities-class" or "abilities-explorer":
                shell.ShowAbilities();
                break;
            default:
                throw new InvalidOperationException(
                    $"unknown snapshot page '{request.Page}' (known: main, configuration, diagnostics, intelligence, abilities)");
        }

        await SettleAsync(shell);
        var render = await RenderPngAsync(shell);
        File.WriteAllText(Path.Combine(AppDir, "ui-snapshot.txt"),
            $"{request.Page}\t{render.Width}x{render.Height}\t{render.DistinctColors}\t{request.Path}");
        if (render.DistinctColors <= 1)
        {
            WriteError($"blank render: {request.Path} (distinct grid colours {render.DistinctColors})");
            ExitCode = 3;
            return;
        }
        File.WriteAllBytes(request.Path, render.Png);
        Console.WriteLine($"ui-snapshot-page: {request.Page} {render.Width}x{render.Height} distinct-colours={render.DistinctColors} -> {request.Path}");
    }

    private static async Task RunSnapshotAsync(ShellWindow shell, UiCliRequest request)
    {
        shell.ResizeForCli(request.Width, request.Height);
        await shell.WaitForShellReadyAsync();
        await Task.Delay(250);
        await WriteSnapshotAsync(shell, request.Path, "ui-snapshot");
    }

    private static async Task RunSnapshotAdvancedAsync(ShellWindow shell, UiCliRequest request)
    {
        shell.ResizeForCli(request.Width, request.Height);
        await shell.WaitForShellReadyAsync();
        await Task.Delay(250);
        shell.ShowAdvancedTab(0);
        await SettleAsync(shell);
        await WriteSnapshotAsync(shell, request.Path, "ui-snapshot-advanced");
    }

    private static async Task WriteSnapshotAsync(ShellWindow shell, string path, string label)
    {
        var render = await RenderPngAsync(shell);
        if (render.DistinctColors <= 1)
        {
            WriteError($"blank render: {path} (distinct grid colours {render.DistinctColors})");
            ExitCode = 3;
            return;
        }
        File.WriteAllBytes(path, render.Png);
        Console.WriteLine($"{label}: {render.Width}x{render.Height} distinct-colours={render.DistinctColors} -> {path}");
    }

    private static async Task RunSmokeAsync(ShellWindow shell)
    {
        var findings = new List<string>();
        await shell.WaitForShellReadyAsync();
        findings.AddRange(ShellValidation.Validate(shell.CliRoot, "shell", shell.Title, shell.SettingsInGameConfigMode));

        if (!shell.SettingsInGameConfigMode)
        {
            // Exercise both overlays and every Advanced tab: each open must be
            // visible, carry no structural findings, and close again.
            shell.ShowAdvanced();
            await SettleAsync(shell);
            if (!shell.AdvancedOverlayOpen) findings.Add("shell: the Advanced overlay did not open");
            findings.AddRange(ShellValidation.Validate(shell.CliRoot, "advanced", shell.Title, false));

            shell.ShowAdvancedTab(1);
            await SettleAsync(shell);
            findings.AddRange(ShellValidation.Validate(shell.CliRoot, "advanced-diagnostics", shell.Title, false));

            shell.ShowAdvancedTab(2);
            await SettleAsync(shell);
            findings.AddRange(ShellValidation.Validate(shell.CliRoot, "advanced-intelligence", shell.Title, false));

            shell.HideAdvanced();
            if (shell.AdvancedOverlayOpen) findings.Add("shell: HideAdvanced did not collapse the overlay");

            shell.ShowAbilities();
            await SettleAsync(shell);
            if (!shell.AbilitiesOverlayOpen) findings.Add("shell: the Abilities overlay did not open");
            findings.AddRange(ShellValidation.Validate(shell.CliRoot, "abilities", shell.Title, false));

            shell.HideAbilities();
            if (shell.AbilitiesOverlayOpen) findings.Add("shell: HideAbilities did not collapse the overlay");
        }

        await SettleAsync(shell);
        var render = await RenderPngAsync(shell);
        if (render.DistinctColors <= 1)
            findings.Add($"snapshot is blank (distinct grid colours {render.DistinctColors})");
        File.WriteAllBytes(Path.Combine(AppDir, "ui-smoke.png"), render.Png);

        var lines = new List<string>
        {
            $"window title: {shell.Title}",
            $"in-game-config shell: {shell.SettingsInGameConfigMode}",
            $"snapshot: ui-smoke.png {render.Width}x{render.Height} px, distinct grid colours {render.DistinctColors}",
        };
        var report = findings.Count == 0
            ? "ui-smoke-test: PASS"
            : "ui-smoke-test: FAIL\n" + string.Join("\n", findings.Select(f => "FINDING " + f));
        File.WriteAllText(Path.Combine(AppDir, "ui-smoke.txt"),
            report + Environment.NewLine + string.Join(Environment.NewLine, lines));
        var dump = ShellValidation.Dump(shell.CliRoot);
        File.WriteAllText(Path.Combine(AppDir, "ui-smoke-layout.txt"), dump);
        File.WriteAllText(Path.Combine(AppDir, "ui-smoke-shell.txt"), dump);

        Console.WriteLine(report);
        foreach (var line in lines) Console.WriteLine($"ui-smoke-test: {line}");
        if (findings.Count > 0)
        {
            WriteError(report);
            ExitCode = 3;
        }
        else
        {
            ExitCode = 0;
        }
    }

    private static async Task RunBenchAsync(ShellWindow shell)
    {
        await shell.WaitForShellReadyAsync();
        var startupMs = _shellBuild is { IsRunning: false } shellSw ? shellSw.Elapsed.TotalMilliseconds : -1;

        // Refresh body: the 250 ms timer payload (MainForm benchmark parity).
        const int iterations = 2000;
        for (var i = 0; i < 50; i++) shell.RefreshStatus(); // warm
        var samples = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var refreshSw = Stopwatch.StartNew();
            shell.RefreshStatus();
            refreshSw.Stop();
            samples[i] = refreshSw.Elapsed.TotalMicroseconds;
        }
        Array.Sort(samples);
        var meanUs = samples.Average();
        var p95Us = samples[(int)(iterations * 0.95)];

        // Snapshot cost (new P2-E metric): RenderTargetBitmap + PNG encode.
        await RenderPngAsync(shell); // warm
        var bestSnapshotMs = double.MaxValue;
        for (var snapshotPass = 0; snapshotPass < 3; snapshotPass++)
        {
            var snapshotSw = Stopwatch.StartNew();
            await RenderPngAsync(shell);
            snapshotSw.Stop();
            bestSnapshotMs = Math.Min(bestSnapshotMs, snapshotSw.Elapsed.TotalMilliseconds);
        }

        var benchPass = meanUs < 300 && p95Us < 1000 && startupMs >= 0 && startupMs < 500;
        var lines = new List<string>
        {
            $"startup (shell construct + Activate): {startupMs:F1} ms",
            $"refresh body: mean {meanUs:F2} us, p95 {p95Us:F2} us (n={iterations})",
            $"snapshot (RenderTargetBitmap + PNG encode): best {bestSnapshotMs:F1} ms of 3",
            $"targets: mean < 300 us, p95 < 1000 us, construct+show < 500 ms -> {(benchPass ? "PASS" : "FAIL")}",
        };
        File.WriteAllLines(Path.Combine(AppDir, "bench-ui.txt"), lines);
        foreach (var line in lines) Console.WriteLine(line);
        ExitCode = benchPass ? 0 : 1;
    }

    // ----- rendering -----

    private readonly record struct RenderResult(byte[] Png, int Width, int Height, int DistinctColors);

    private static async Task SettleAsync(ShellWindow shell)
    {
        shell.CliRoot.UpdateLayout();
        await Task.Delay(250);
    }

    /// <summary>
    /// Renders the shell root through the XAML compositor and encodes a PNG.
    /// No screen capture: RenderTargetBitmap works with the window shown even
    /// when OS-level screen grabs of this session come back black.
    /// </summary>
    private static async Task<RenderResult> RenderPngAsync(ShellWindow shell)
    {
        var target = new RenderTargetBitmap();
        await target.RenderAsync(shell.CliRoot);
        var width = target.PixelWidth;
        var height = target.PixelHeight;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"RenderTargetBitmap produced {width}x{height} px");

        var buffer = await target.GetPixelsAsync();
        var pixels = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(pixels);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();

        var size = checked((int)stream.Size);
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)size);
        var png = new byte[size];
        reader.ReadBytes(png);

        return new RenderResult(png, width, height, DistinctColors(pixels, width, height));
    }

    /// <summary>Distinct colours over a sparse grid: a blank render returns 1 (old snapshot contract).</summary>
    private static int DistinctColors(byte[] bgra, int width, int height)
    {
        const int step = 8;
        var seen = new HashSet<uint>();
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var i = (y * width + x) * 4;
                if (i + 3 >= bgra.Length) continue;
                var colour = ((uint)bgra[i]) | ((uint)bgra[i + 1] << 8)
                    | ((uint)bgra[i + 2] << 16) | ((uint)bgra[i + 3] << 24);
                seen.Add(colour);
            }
        }
        return seen.Count;
    }

    private static void WriteError(string message)
    {
        try { File.WriteAllText(Path.Combine(AppDir, "ui-error.txt"), message); } catch { /* best effort */ }
        Console.Error.WriteLine(message);
    }
}
