using System.Diagnostics;

namespace MaxDpsCompanion;

internal static class Program
{
    /// <summary>Shortest useful --bench-scheduler run (one full scenario).</summary>
    private const int SchedulerBenchMinimumTicks = 900;

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
            // UI 2.0 page snapshot: --ui-snapshot-page=<home|abilities|...>
            // --ui-snapshot=<path>, --ui-snapshot-width=<px>. Renders
            // offscreen and asserts the PNG is non-blank (v2.7 §48).
            var pageArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot-page=", StringComparison.OrdinalIgnoreCase));
            if (pageArg is not null)
            {
                var page = pageArg[(pageArg.IndexOf('=') + 1)..].Trim();
                var outPath = ArgValue(args, "--ui-snapshot=", Path.Combine(AppDir, $"ui-snapshot-{page}.png"));
                var width = int.TryParse(ArgValue(args, "--ui-snapshot-width=", "1280"), out var snappedWidth) ? snappedWidth : 1280;
                var height = int.TryParse(ArgValue(args, "--ui-snapshot-height=", "900"), out var snappedHeight) ? snappedHeight : 900;
                using var window = new MainForm(settings);
                window.PrepareOffscreenSnapshot(width, height);
                window.Show();
                Application.DoEvents();
                window.OpenPageForSnapshot(page);
                window.PerformLayout();
                window.Refresh();
                Application.DoEvents();
                using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
                window.DrawToBitmap(bitmap, window.ClientRectangle);
                File.WriteAllText(Path.Combine(AppDir, "ui-snapshot.txt"),
                    $"{page}\t{window.ClientSize.Width}x{window.ClientSize.Height}\t{distinctColors(bitmap)}\t{outPath}");
                if (distinctColors(bitmap) <= 1)
                {
                    File.WriteAllText(Path.Combine(AppDir, "ui-snapshot-ERROR.txt"), $"blank render: {outPath}");
                    Environment.ExitCode = 2;
                    return;
                }
                bitmap.Save(outPath);
                return;
            }

            var snapshotArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
            if (snapshotArg is not null)
            {
                using var window = new MainForm(settings);
                window.PrepareOffscreenSnapshot(1280, 900);
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
                window.OpenAdvancedForSnapshot(ParseSnapshotScroll(args));
                window.PerformLayout();
                window.Refresh();
                Application.DoEvents();
                using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
                window.DrawToBitmap(bitmap, window.ClientRectangle);
                bitmap.Save(advArg[(advArg.IndexOf('=') + 1)..]);
                return;
            }
            // Renders the Class skills screen for design review (no game needed).
            var classSkillsArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot-class-skills=", StringComparison.OrdinalIgnoreCase));
            if (classSkillsArg is not null)
            {
                using var classWindow = new MainForm(settings);
                classWindow.StartPosition = FormStartPosition.Manual;
                classWindow.Location = new Point(-32000, -32000);
                classWindow.ShowInTaskbar = false;
                classWindow.Show();
                Application.DoEvents();
                classWindow.OpenClassSkillsForSnapshot(
                    ArgValue(args, "--ui-snapshot-class=", "ROGUE"),
                    ArgValue(args, "--ui-snapshot-spec=", "Outlaw"));
                classWindow.PerformLayout();
                classWindow.Refresh();
                classWindow.Update();
                Application.DoEvents();
                var classPath = classSkillsArg[(classSkillsArg.IndexOf('=') + 1)..];
                File.WriteAllText(classPath + ".state.txt",
                    classWindow.ClassSkillsDebugState + Environment.NewLine + classWindow.ClientSize);
                using var classBitmap = new Bitmap(classWindow.ClientSize.Width, classWindow.ClientSize.Height);
                if (args.Contains("--ui-snapshot-from-screen", StringComparer.OrdinalIgnoreCase))
                {
                    // Real render: move the window on-screen for one frame and
                    // copy the actual pixels (WM_PRINT can skip composed layers).
                    classWindow.Location = new Point(40, 40);
                    classWindow.Refresh();
                    classWindow.Update();
                    Application.DoEvents();
                    Thread.Sleep(250);
                    Application.DoEvents();
                    using var graphics = Graphics.FromImage(classBitmap);
                    graphics.CopyFromScreen(classWindow.Location, Point.Empty, classWindow.ClientSize);
                }
                else
                {
                    classWindow.DrawToBitmap(classBitmap, classWindow.ClientRectangle);
                }
                classBitmap.Save(classPath);
                return;
            }

            // Maintenance: dump the merged per-class/spec ability tree (used to
            // curate the passive/junk filter; see docs/KNOWLEDGE.md).
            var dumpArg = args.FirstOrDefault(arg => arg.StartsWith("--dump-class-skills=", StringComparison.OrdinalIgnoreCase));
            if (dumpArg is not null)
            {
                var dumpPath = dumpArg[(dumpArg.IndexOf('=') + 1)..];
                var catalog = AbilityCatalog.Default;
                var book = ClassSpellBook.Default;
                var lines = new List<string>();
                foreach (var className in AbilityCatalog.ClassOrder)
                {
                    if (className.Length == 0) continue;
                    if (!AbilityCatalog.SpecOrder.TryGetValue(className, out var specs)) continue;
                    for (var i = 1; i < specs.Length; i++)
                    {
                        var build = ClassSkillTree.Build(catalog, book, className, specs[i]);
                        foreach (var ability in build.Shared)
                            lines.Add($"{className}\t{specs[i]}\tshared\t{ability.Provenance}\t{ability.Name}\t{ability.SpellId}");
                        foreach (var group in ClassSkillTree.SectionOrder)
                        {
                            if (!build.Groups.TryGetValue(group, out var list)) continue;
                            foreach (var ability in list)
                                lines.Add($"{className}\t{specs[i]}\t{group}\t{ability.Provenance}\t{ability.Name}\t{ability.SpellId}");
                        }
                    }
                }
                File.WriteAllLines(dumpPath, lines);
                return;
            }

            // Diagnostics: one-shot attach + locate + sample + decode probe,
            // written to probe.txt next to the exe. Answers "why no pixel
            // block" without the UI (settings come from the local settings.ini).
            if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
            {
                var lines = new List<string>();
                var window = new WowWindow();
                lines.Add($"process='{settings.ProcessName}' refresh={window.Refresh(settings.ProcessName)} valid={window.IsValid} handle=0x{window.Handle:X}");
                if (window.TryGetClientOrigin(out var o, out var s))
                {
                    lines.Add($"client origin={o.X},{o.Y} size={s.Width}x{s.Height}");
                    var loc = BlockLocator.Locate(o, s, settings.Color);
                    lines.Add($"locate={(loc is { } l ? $"{l.OffsetX},{l.OffsetY} cell {l.CellSize}" : "null")}");
                    using var sampler = new ScreenSampler();
                    lines.Add($"settings offset={settings.OffsetX},{settings.OffsetY} cell={settings.CellSize} learned={settings.Color.IsLearned}");
                    var at = new Point(o.X + settings.OffsetX, o.Y + settings.OffsetY);
                    var cells = sampler.Sample(at, settings.CellSize);
                    lines.Add("cells(settings)=" + string.Join(" ", cells.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}")));
                    var frame = DecodeLikeEngine(cells, settings.Color);
                    lines.Add($"decode(settings)={(frame is null ? "null" : $"proto={frame.Version} state={frame.State} slots={string.Join(",", frame.Slots.Select(x => x?.Describe() ?? "-"))}")}");
                    if (loc is { } l2)
                    {
                        var at2 = new Point(o.X + l2.OffsetX, o.Y + l2.OffsetY);
                        var c2 = sampler.Sample(at2, l2.CellSize);
                        lines.Add("cells(locate)=" + string.Join(" ", c2.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}")));
                        var f2 = DecodeLikeEngine(c2, settings.Color);
                        lines.Add($"decode(locate)={(f2 is null ? "null" : $"proto={f2.Version} state={f2.State} slots={string.Join(",", f2.Slots.Select(x => x?.Describe() ?? "-"))}")}");
                    }
                }
                else
                {
                    lines.Add("no client origin");
                }
                File.WriteAllLines(Path.Combine(AppDir, "probe.txt"), lines);
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
                    lines.Add($"sample({PixelProtocol.CellCount} cells, 5 taps): {sw.Elapsed.TotalMicroseconds / n:F2} us/op  (n={n})");
                }
                // BitBlt cost with/without CAPTUREBLT: the flag forces DWM
                // compositing of layered windows and can dominate the tick.
                var src = Native.GetDC(IntPtr.Zero);
                var dst = Native.CreateCompatibleDC(src);
                var bmp = Native.CreateCompatibleBitmap(src, 8 * PixelProtocol.CellCount, 8);
                var prev = Native.SelectObject(dst, bmp);
                var blits = Math.Min(n, 600);
                foreach (var (label, rop) in new[]
                {
                    ($"BitBlt {8 * PixelProtocol.CellCount}x8 SRCCOPY", (int)Native.SRCCOPY),
                    ($"BitBlt {8 * PixelProtocol.CellCount}x8 SRCCOPY|CAPTUREBLT", (int)(Native.SRCCOPY | Native.CAPTUREBLT)),
                })
                {
                    var swb = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < blits; i++) Native.BitBlt(dst, 0, 0, 8 * PixelProtocol.CellCount, 8, src, 200, 200, rop);
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
            // Local telemetry perf probe: serialization + ring append cost and
            // bytes/event for a representative decision tick. Writes
            // bench-telemetry.txt; no game required.
            var benchTelemetryArg = args.FirstOrDefault(arg => arg.StartsWith("--bench-telemetry", StringComparison.OrdinalIgnoreCase));
            if (benchTelemetryArg is not null)
            {
                var split = benchTelemetryArg.IndexOf('=');
                var n = split > 0 && int.TryParse(benchTelemetryArg[(split + 1)..], out var parsedTelemetry)
                    ? Math.Clamp(parsedTelemetry, 100, 1_000_000)
                    : 20_000;

                var frame = new BridgeFrame
                {
                    State = BridgeState.Active,
                    Heartbeat = 7,
                    Version = PixelProtocol.SupportedVersion,
                    StatusFlags = PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
                    Slots =
                    [
                        new KeyStroke(0x45, false, false, false),
                        new KeyStroke(0x32, false, false, false),
                        null, null, null,
                        new KeyStroke(0x46, false, false, false),
                    ],
                };
                var candidates = new[]
                {
                    new ActionCandidate(Slot.Main, new KeyStroke(0x45, false, false, false), true, true, 0, 0, 0, true),
                    new ActionCandidate(Slot.Offensive, new KeyStroke(0x32, false, false, false), true, true, 0, 0, 0, false),
                    new ActionCandidate(Slot.Interrupt, new KeyStroke(0x46, false, false, false), true, true, 0, 0, 0, false),
                };
                var context = new DecisionContext
                {
                    InCombat = true, OnGcd = false, HasTarget = true,
                    State = BridgeState.Active, NowMs = 10_000, StaleAfterMs = 1500,
                    Candidates = candidates,
                };
                var decision = DecisionEngine.Evaluate(context);

                TelemetryEvent Capture() => TelemetryEvent.Tick(
                    12_345, frame, DecodeFault.None, context, decision, true, candidates, "sending", true);
                var prebuilt = Capture();

                // Warm every path (JIT + tiered compilation + serializer init)
                // before measuring any of them.
                var recorder = new TelemetryRecorder(10_000);
                for (var i = 0; i < 1000; i++) recorder.Append(Capture());

                // Best (minimum) of 3 passes per path: least interference wins,
                // which is the standard way to report microbenchmarks on a
                // machine that is also drawing a desktop.
                double Best(Action body)
                {
                    var best = double.MaxValue;
                    for (var pass = 0; pass < 3; pass++)
                    {
                        var swInner = System.Diagnostics.Stopwatch.StartNew();
                        body();
                        swInner.Stop();
                        best = Math.Min(best, swInner.Elapsed.TotalMicroseconds / n);
                    }
                    return best;
                }

                var captureUs = Best(() => { for (var i = 0; i < n; i++) GC.KeepAlive(Capture()); });
                var bytes = 0L;
                var serializeUs = Best(() => { for (var i = 0; i < n; i++) bytes += TelemetryJson.Serialize(prebuilt).Length; });
                var perEventBytes = (int)(bytes / (n * 3L));
                var appendUs = Best(() => { for (var i = 0; i < n; i++) recorder.Append(prebuilt); });
                var endToEnd = new TelemetryRecorder(10_000);
                var endToEndUs = Best(() => { for (var i = 0; i < n; i++) endToEnd.Append(Capture()); });

                var exportPath = Path.Combine(AppDir, "bench-telemetry.jsonl");
                var swExport = System.Diagnostics.Stopwatch.StartNew();
                var written = recorder.Export(exportPath);
                swExport.Stop();

                var lines = new List<string>
                {
                    $"tick event (3 candidates): {perEventBytes} bytes serialized",
                    $"capture (event model + candidate map): {captureUs:F2} us/op",
                    $"serialize (source-gen): {serializeUs:F2} us/op",
                    $"append (serialize + ring store, prebuilt): {appendUs:F2} us/op",
                    $"END-TO-END capture + append (engine pays this per tick): {endToEndUs:F2} us/op (n={n}, best of 3)",
                    $"ring memory at capacity {recorder.Capacity}: {recorder.Capacity * (double)perEventBytes / 1024 / 1024:F2} MB",
                    $"export {written} events: {swExport.ElapsedMilliseconds} ms -> {exportPath} ({new FileInfo(exportPath).Length / 1024.0:F0} KB)",
                    $"implied CPU at 30 Hz (end-to-end): {endToEndUs * 30 / 10_000:F3}% of one core",
                };
                File.WriteAllLines(Path.Combine(AppDir, "bench-telemetry.txt"), lines);
                return;
            }

            // Deterministic scheduler measurement: scripted synthetic rotation
            // on a fake clock -> press counts, interval stats, hold reasons and
            // a plan hash in bench-scheduler.txt. No game required.
            var benchSchedulerArg = args.FirstOrDefault(arg => arg.StartsWith("--bench-scheduler", StringComparison.OrdinalIgnoreCase));
            if (benchSchedulerArg is not null)
            {
                var split = benchSchedulerArg.IndexOf('=');
                var schedTicks = split > 0 && int.TryParse(benchSchedulerArg[(split + 1)..], out var parsedSched)
                    ? Math.Clamp(parsedSched, SchedulerBenchMinimumTicks, 1_000_000)
                    : 27_000;
                File.WriteAllLines(Path.Combine(AppDir, "bench-scheduler.txt"), SchedulerBench.Run(schedTicks));
                return;
            }

            // Emits the bridge's generated Catalog.lua (class/spec ids +
            // curated Mobility/SelfHeal extras) from the embedded knowledge
            // base. The committed file is verified by CatalogLuaSyncTests.
            var genCatalogArg = args.FirstOrDefault(arg => arg.StartsWith("--gen-catalog", StringComparison.OrdinalIgnoreCase));
            if (genCatalogArg is not null)
            {
                var split = genCatalogArg.IndexOf('=');
                var outPath = split > 0
                    ? genCatalogArg[(split + 1)..].Trim().Trim('"')
                    : Path.Combine(AppDir, "Catalog.lua");
                File.WriteAllText(outPath, CatalogLuaGenerator.Generate(AbilityCatalog.Default));
                return;
            }

            // Ability registry audit + v2.7 coverage manifest: machine-generated
            // report (registry §33/§54) plus the machine-readable coverage JSON
            // (§6; discovered/registered/automatable/missing/stale classes).
            // Either flag can be used alone.
            var auditArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-audit", StringComparison.OrdinalIgnoreCase));
            var coverageArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-coverage", StringComparison.OrdinalIgnoreCase));
            if (auditArg is not null || coverageArg is not null)
            {
                var catalogForAudit = AbilityCatalog.Default;
                if (coverageArg is not null)
                {
                    var coverageSplit = coverageArg.IndexOf('=');
                    var coveragePath = coverageSplit > 0
                        ? coverageArg[(coverageSplit + 1)..].Trim().Trim('"')
                        : Path.Combine(AppDir, "ABILITY_COVERAGE.json");
                    File.WriteAllText(coveragePath, AbilityCoverage.ToJson(AbilityCoverage.Build(catalogForAudit)));
                }
                if (auditArg is not null)
                {
                    var split = auditArg.IndexOf('=');
                    var outPath = split > 0
                        ? auditArg[(split + 1)..].Trim().Trim('"')
                        : Path.Combine(AppDir, "ABILITY_REGISTRY_AUDIT.md");
                    File.WriteAllText(outPath, AbilityIntelligence.ToMarkdown(
                        AbilityIntelligence.Audit(catalogForAudit), catalogForAudit));
                }
                return;
            }

            // Ability inspector: one registry entry's full intelligence view
            // (the app-side equivalent of "/mdb ability <spellId>", §53).
            var infoArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-info=", StringComparison.OrdinalIgnoreCase));
            if (infoArg is not null)
            {
                var raw = infoArg[(infoArg.IndexOf('=') + 1)..].Trim().Trim('"');
                var lines = new List<string>();
                if (int.TryParse(raw, out var spellId))
                {
                    lines.Add(AbilityIntelligence.Describe(spellId, AbilityCatalog.Default));
                }
                else
                {
                    var catalogForSearch = AbilityCatalog.Default;
                    lines.Add($"no spell id parsed from '{raw}'; name search:");
                    foreach (var ability in catalogForSearch.All)
                    {
                        if (ability.Name.Contains(raw, StringComparison.OrdinalIgnoreCase))
                            lines.Add($"  {ability.SpellId}  {ability.Name}  [{string.Join("/", ability.Classes)}]");
                    }
                }
                foreach (var line in lines) Console.WriteLine(line);
                File.WriteAllLines(Path.Combine(AppDir, "ability-info.txt"), lines);
                return;
            }

            // v2.7 registry CLI search (§56): filter the embedded registry by
            // free text / class / spec without the UI; one line per ability.
            var searchArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-search", StringComparison.OrdinalIgnoreCase));
            var classArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-class=", StringComparison.OrdinalIgnoreCase));
            var specArg = args.FirstOrDefault(arg => arg.StartsWith("--ability-spec=", StringComparison.OrdinalIgnoreCase));
            if (searchArg is not null || classArg is not null || specArg is not null)
            {
                var searchCatalog = AbilityCatalog.Default;
                var text = ArgValue(args, "--ability-search=", "");
                var className = ArgValue(args, "--ability-class=", "");
                var specName = ArgValue(args, "--ability-spec=", "");
                var lines = new List<string>();
                foreach (var ability in searchCatalog.All.OrderBy(a => a.SpellId))
                {
                    if (className.Length > 0 && !ability.Classes.Any(c => c.Contains(className, StringComparison.OrdinalIgnoreCase))) continue;
                    if (specName.Length > 0 && !ability.Specs.Any(s => s.Contains(specName, StringComparison.OrdinalIgnoreCase))) continue;
                    if (text.Length > 0)
                    {
                        var match = ability.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                            || ability.SpellId.ToString().Contains(text, StringComparison.Ordinal)
                            || ability.Category.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
                            || ability.Ownership.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
                            || ability.Status.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)
                            || ability.Classes.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase))
                            || ability.Specs.Any(s => s.Contains(text, StringComparison.OrdinalIgnoreCase));
                        if (!match) continue;
                    }
                    lines.Add($"{ability.SpellId,7}  {ability.Name,-34} {ability.Category,-10} {ability.Ownership}/{ability.Completeness}  {ability.Automation}  {ability.Status}");
                }
                lines.Insert(0, $"{lines.Count} match(es); id  name  category  owner/completeness  automation  status");
                foreach (var line in lines) Console.WriteLine(line);
                File.WriteAllLines(Path.Combine(AppDir, "ability-search.txt"), lines);
                return;
            }

            // Replay a telemetry JSONL file without the UI: each recorded
            // decision context is re-run through the deterministic evaluator
            // and the report is written to <file>.replay.txt next to the input.
            var replayArg = args.FirstOrDefault(arg => arg.StartsWith("--replay=", StringComparison.OrdinalIgnoreCase));
            if (replayArg is not null)
            {
                var path = replayArg[(replayArg.IndexOf('=') + 1)..].Trim().Trim('"');
                try
                {
                    ReplayRunner.RunFile(path);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDir, "replay-error.txt"), ex.ToString());
                }
                return;
            }

            if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                using var window = new MainForm(settings);
                window.CreateControl();
                var findings = window.RunSmokeTest();
                var report = findings.Count == 0
                    ? "ui-smoke-test: PASS"
                    : "ui-smoke-test: FAIL\n" + string.Join("\n", findings.Select(f => "FINDING " + f));
                Console.WriteLine(report);
                File.WriteAllText(Path.Combine(AppDir, "ui-smoke.txt"), report);
                File.WriteAllText(Path.Combine(AppDir, "ui-smoke-layout.txt"), UiShellValidation.Dump(window.HomeForTest));
                File.WriteAllText(Path.Combine(AppDir, "ui-smoke-shell.txt"), UiShellValidation.Dump(window));
                if (findings.Count > 0) Environment.ExitCode = 1;
                return;
            }
            Application.Run(new MainForm(settings));
        }
        catch (Exception ex)
        {
            // Headless UI flags must never block on a modal dialog.
            if (args.Any(a => a.StartsWith("--ui-", StringComparison.OrdinalIgnoreCase)))
            {
                try { File.WriteAllText(Path.Combine(AppDir, "ui-error.txt"), ex.ToString()); } catch { /* best effort */ }
                Environment.ExitCode = 3;
                return;
            }
            MessageBox.Show(ex.ToString(), "MaxDPS Companion crashed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// Distinct-colour count over a sparse grid: a blank (single-colour) render
    /// returns 1. Used to assert snapshots are non-blank (v2.7 §48).
    /// </summary>
    private static int distinctColors(Bitmap bitmap)
    {
        var seen = new HashSet<int>();
        var stepX = Math.Max(1, bitmap.Width / 48);
        var stepY = Math.Max(1, bitmap.Height / 32);
        for (var y = 0; y < bitmap.Height; y += stepY)
            for (var x = 0; x < bitmap.Width; x += stepX)
                seen.Add(bitmap.GetPixel(x, y).ToArgb());
        return seen.Count;
    }

    /// <summary>
    /// The engine's real decode chain over one capture: the 35-cell protocol
    /// first, then the legacy 9-cell (v4) and 8-cell (v1) windows trimmed out
    /// of the same sample. The probe must report what the engine would decode,
    /// so a stale in-game addon is not misdiagnosed as "null".
    /// </summary>
    private static BridgeFrame? DecodeLikeEngine(Color[] cells, ColorProfile profile)
    {
        var frame = PixelProtocol.Decode(cells, profile);
        if (frame is not null) return frame;
        frame = PixelProtocol.Decode(PixelProtocol.TrimToV4(cells), profile);
        if (frame is not null) return frame;
        return PixelProtocol.Decode(PixelProtocol.TrimToV1(cells), profile);
    }

    /// <summary>`--name=value` argument lookup with a default.</summary>
    private static string ArgValue(string[] args, string prefix, string fallback)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return arg is null ? fallback : arg[(arg.IndexOf('=') + 1)..].Trim();
    }

    /// <summary>
    /// Snapshot design-review scroll target: `--ui-snapshot-scroll=bottom`
    /// selects the tail of the Advanced stack, a number selects an absolute
    /// pixel offset, absent keeps the top (-1).
    /// </summary>
    private static int ParseSnapshotScroll(string[] args)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--ui-snapshot-scroll=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return -1;
        var value = arg[(arg.IndexOf('=') + 1)..].Trim();
        if (string.Equals(value, "bottom", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        return int.TryParse(value, out var y) && y >= 0 ? y : -1;
    }
}
