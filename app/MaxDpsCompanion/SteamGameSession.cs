using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace MaxDpsCompanion;

internal static class SteamGameSession
{
    private static AppSettings _settings = new();
    private static long _launchUntil;
    private static readonly object LaunchGate = new();
    private static string Target => _settings.GameExecutablePath;
    private static string PipeName => "MaxDps.Midnight.Steam." + Environment.UserName + "." + SessionId;
    private static int SessionId { get { using var own = Process.GetCurrentProcess(); return own.SessionId; } }

    internal static string Description => _settings.SteamLaunchEnabled && SteamLaunchPlan.IsShortcutId(_settings.SteamGameId)
        ? "Open/Restart Game use Steam and Battle.net. Steam Input supplies the controller layout; live in-game acceptance is required."
        : "Steam game launch is not configured. Run tools/Configure-Steam.ps1 with Steam and this game closed.";

    internal static void OpenControllerLayout()
    {
        if (!SteamLaunchPlan.IsShortcutId(_settings.SteamGameId)) return;
        var appId = ulong.Parse(_settings.SteamGameId, System.Globalization.CultureInfo.InvariantCulture) >> 32;
        Process.Start(new ProcessStartInfo { FileName = $"steam://controllerconfig/{appId}", UseShellExecute = true });
    }

    internal static void Configure(AppSettings settings) => _settings = settings;

    internal static bool IsTarget(Process process)
    {
        try
        {
            if (process.SessionId != SessionId) return false;
            var module = process.MainModule;
            if (string.IsNullOrWhiteSpace(Target))
                return SteamLaunchPlan.IsSupportedGameVersion(module?.FileVersionInfo.FileVersion);
            return string.Equals(Path.GetFullPath(Target), module?.FileName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool GameRunning()
    {
        foreach (var process in Process.GetProcessesByName("Wow"))
            using (process) if (IsTarget(process)) return true;
        return false;
    }

    private static bool BattleNetRunning()
    {
        var processes = Process.GetProcessesByName("Battle.net");
        var found = false;
        foreach (var process in processes)
            using (process) if (process.SessionId == SessionId) found = true;
        return found;
    }

    private static string? TargetError()
    {
        if (string.IsNullOrWhiteSpace(Target) || !File.Exists(Target)
            || !string.Equals(Path.GetFileName(Target), "Wow.exe", StringComparison.OrdinalIgnoreCase))
            return "Configure the exact Midnight Wow.exe before launching through Steam.";
        var versionText = FileVersionInfo.GetVersionInfo(Target).FileVersion;
        if (!SteamLaunchPlan.IsSupportedGameVersion(versionText))
            return "The configured game is not the Midnight 12.1 client.";
        return null;
    }

    internal static (bool Ok, string Message) Launch(string? battleNetPath)
    {
        if (!_settings.SteamLaunchEnabled)
            return BattleNetLauncher.TryLaunch(battleNetPath, out var directMessage) ? (true, directMessage) : (false, directMessage);
        try
        {
            if (TargetError() is { } error) return (false, error);
            if (TryRelay(out var response)) return response;
            if (BattleNetRunning())
                return (false, "Exit Battle.net completely from its tray menu, then retry Open Game. It must start inside this game's Steam session for overlay and Steam Input. Your running games have not been closed.");
            Process.Start(SteamLaunchPlan.CreateStartInfo(_settings.SteamGameId));
            return (true, "Starting WoW Midnight through Steam and Battle.net. Controller/overlay live verification is still required.");
        }
        catch (Exception ex) { return (false, "Steam launch failed: " + ex.Message); }
    }

    private static bool TryRelay(out (bool Ok, string Message) response)
    {
        response = default;
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { client.Connect(150); }
        catch (TimeoutException) { return false; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        writer.WriteLine("launch");
        var line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
        response = line is { Length: > 2 } ? (line[0] == '1', line[2..]) : (false, "Steam session did not acknowledge the launch request.");
        return true;
    }

    private static (bool Ok, string Message) StartGame()
    {
        lock (LaunchGate)
        {
            if (GameRunning()) return (true, "WoW Midnight is already running in this Steam session.");
            if (Environment.TickCount64 < Interlocked.Read(ref _launchUntil)) return (true, "Waiting for Battle.net to launch the game.");
            if (TargetError() is { } error) return (false, error);
            var configPath = Path.Combine(Path.GetDirectoryName(Target)!, "WTF", "Config.wtf");
            var config = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
            var enabled = SteamLaunchPlan.EnableNativeGamepadText(config);
            if (enabled != config)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                if (File.Exists(configPath)) File.Copy(configPath, configPath + ".maxdps-gamepad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak");
                var temporary = configPath + ".maxdps.tmp";
                File.WriteAllText(temporary, enabled, new UTF8Encoding(false));
                File.Move(temporary, configPath, overwrite: true);
            }
            var ok = BattleNetLauncher.TryLaunch(_settings.BNetPath, out var message);
            if (ok) Interlocked.Exchange(ref _launchUntil, Environment.TickCount64 + 90_000);
            return (ok, message);
        }
    }

    private static async Task ServeAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await server.WaitForConnectionAsync(stop);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(timeout.Token);
                var result = command == "launch" ? StartGame() : (false, "Unknown Steam-session request.");
                await writer.WriteLineAsync((result.Item1 ? "1|" : "0|") + result.Item2.Replace("\r", " ").Replace("\n", " "));
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }

    internal static async Task<int> RunHostAsync()
    {
        if (!SteamLaunchPlan.IsExpectedSession(_settings.SteamGameId, Environment.GetEnvironmentVariable("SteamGameId")))
            return Report("Start this game-session entry from Steam, not by opening the executable directly.");
        if (TargetError() is { } error) return Report(error);
        using var singleton = new Mutex(true, PipeName + ".Host", out var created);
        if (!created) return Report("This game's Steam session is already active.");
        if (GameRunning() || BattleNetRunning())
            return Report("Close this game and exit Battle.net completely before starting its Steam session.");
        using var stop = new CancellationTokenSource();
        var server = ServeAsync(stop.Token);
        try
        {
            var started = StartGame();
            if (!started.Ok) return Report(started.Message);
            File.WriteAllText(Path.Combine(Program.AppDir, "steam-launch-status.txt"), started.Message);
            var coldStartRetry = new SteamColdStartRetry(Environment.TickCount64);
            var deadline = Environment.TickCount64 + 90_000;
            while (Environment.TickCount64 < deadline)
            {
                var running = GameRunning();
                var retry = coldStartRetry.TryTake(Environment.TickCount64, running, BattleNetRunning());
                if (running)
                {
                    deadline = Environment.TickCount64 + 30_000;
                    Interlocked.Exchange(ref _launchUntil, 0);
                }
                else if (server.IsFaulted) return Report("Steam session control channel failed.");
                else if (retry)
                {
                    if (!BattleNetLauncher.TryLaunch(_settings.BNetPath, out var retryMessage)) return Report(retryMessage);
                    File.WriteAllText(Path.Combine(Program.AppDir, "steam-launch-status.txt"), retryMessage + " Cold-start request reissued once after launcher initialization.");
                }
                deadline = Math.Max(deadline, Interlocked.Read(ref _launchUntil));
                await Task.Delay(1000);
            }
            return 0;
        }
        finally
        {
            stop.Cancel();
            try { await server; } catch (OperationCanceledException) { }
        }
    }

    private static int Report(string message)
    {
        File.WriteAllText(Path.Combine(Program.AppDir, "steam-launch-status.txt"), message);
        return 3;
    }
}
