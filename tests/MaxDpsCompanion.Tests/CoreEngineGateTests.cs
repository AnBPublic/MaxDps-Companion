using System.Drawing;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Wave B parity P0s in CORE's engine (the Windows app keeps its own engine
/// until Wave C; the Mac host and the future shared engine run this one):
///
///  1. the 4.0.4 MAIN MASTER GATE - a valid Ext3 mirror with Main OFF suspends
///     EVERY send path (rotation, auto-target, auto-interact), not just the
///     slot rotation, and reports "holding (addon Main off - rotation
///     suspended)";
///  2. the 4.0.3 TYPING VETO - cell 41 R bit2 (frame.TypingActive) blocks
///     sends while the player types in chat/an edit box.
///
/// These drive ONE real tick through fake IPlatform adapters and a REAL wire
/// frame (TestV5Frame + Ext3 cells), so capture → decode → mirror publish →
/// gates → send all run exactly as in production. Every case has a positive
/// control (same frame, gate released ⇒ a send happens), so a test that fails
/// because the frame could not send at all would fail the control too.
/// Unqualified engine/types here are CORE's - the Windows assembly is `win`.
/// </summary>
public class CoreEngineGateTests
{
    // ---------------------------------------------------------------- fakes

    private sealed class FakeClock : IClock
    {
        public long Now { get; set; } = 1000;
        public long ElapsedMs => Now;
        public void Sleep(int milliseconds) => Now += milliseconds;
        public void Wait(int milliseconds) => Now += milliseconds;
    }

    private sealed class FakeWindow : IGameWindow
    {
        public IntPtr Handle => (IntPtr)0x1234;
        public bool IsValid => true;
        public bool IsForeground => true;
        public uint ProcessId => 4242;
        public bool IsGameWindow(IntPtr candidate) => candidate == Handle;
        public bool Refresh(string processName) => true;
        public bool TryGetClientOrigin(out Point origin, out Size clientSize)
        {
            origin = new Point(0, 0);
            clientSize = new Size(200, 200);
            return true;
        }
        public void Reset() { }
    }

    private sealed class FakeSampler : IScreenSampler
    {
        public Color[] Cells { get; set; } = [];
        public Color[] Sample(Point origin, int cellSize) => Cells;
        public Color[] SampleV1(Point origin, int cellSize) => Cells;
        public (int Mask, int Epoch, int Blocked)? Ext3Mirror => null;
        public bool Ext3MirrorPresent => false;
        public void Reset() { }
    }

    private sealed class FakeKeys : IKeySender
    {
        public List<(IntPtr Target, Keys.KeyRef Stroke)> Posted { get; } = [];
        public List<Keys.KeyRef> Sent { get; } = [];
        public bool SendToWindow(IntPtr target, uint expectedProcessId, Keys.KeyRef stroke, int holdMilliseconds)
        {
            Posted.Add((target, stroke));
            return true;
        }
        public void Send(Keys.KeyRef stroke, int holdMilliseconds) => Sent.Add(stroke);
        public bool IsMouse(byte virtualKey) => Keys.IsMouse(virtualKey);
        public string DescribeStroke(Keys.KeyRef stroke) => Keys.Describe(stroke);
    }

    private sealed class FakePhysical : IPhysicalKeys
    {
        public bool IsPhysicallyDown(byte virtualKey) => false;
    }

    private sealed class FakeScanner : IBlockScanner
    {
        public (int OffsetX, int OffsetY, int CellSize)? Locate(Point origin, Size area) => null;
    }

    // ------------------------------------------------------------ fixtures

    /// <summary>One deterministic tick against the fakes; returns the send count.</summary>
    private static (int Sends, List<string> Status) TickOnce(
        Color[] cells, Action<AppSettings>? configure = null)
    {
        // Scheduler + intelligence OFF: the legacy send path is the shortest,
        // fully deterministic route to a real send, so a control that sends
        // proves the frame/gates below are the only thing standing between the
        // engine and the key. The gates under test run BEFORE both send paths.
        var settings = new AppSettings { SchedulerEnabled = false, IntelligenceEnabled = false };
        configure?.Invoke(settings);

        var sampler = new FakeSampler { Cells = cells };
        var keys = new FakeKeys();
        var clock = new FakeClock();
        var status = new List<string>();
        using var engine = new RotationEngine(
            settings, new FakeWindow(), sampler, keys, new FakePhysical(), new FakeScanner(), clock);
        engine.StatusChanged += s => status.Add(s.Message);

        engine.Tick();
        return (keys.Posted.Count + keys.Sent.Count, status);
    }

    /// <summary>
    /// A protocol-v5 core capture (35 cells) that carries a live Main slot, so
    /// the rotation path has something to send on every enabled path.
    /// </summary>
    private static Color[] CoreCapture(
        BridgeState state = BridgeState.Active, bool hasTarget = true, bool withSlot = true)
    {
        var frame = new TestV5Frame();
        if (withSlot)
        {
            frame.Slot(Slot.Main, 0x45).SpellId(Slot.Main, 100);
        }
        var cells = frame.Build(heartbeat: 7, inCombat: true, onGcd: false, hasTarget: hasTarget);
        if (state != BridgeState.Active)
        {
            var status = cells[PixelProtocol.StatusCellIndex];
            cells[PixelProtocol.StatusCellIndex] = Color.FromArgb((int)state * 17, status.G, status.B);
            // The version cell G nibble is the core checksum over cells 1..9.
            var core = 0;
            for (var i = 1; i <= PixelProtocol.StatusCellIndex; i++)
                core += cells[i].R / 17 + cells[i].G / 17 + cells[i].B / 17;
            var version = cells[PixelProtocol.VersionCellIndex];
            cells[PixelProtocol.VersionCellIndex] = Color.FromArgb(version.R, (core & 0xF) * 17, version.B);
        }
        return cells;
    }

    /// <summary>
    /// Appends a valid Ext3 block (cells 40-42) to a core capture: presence bit
    /// on cell 28 B, repaired cell-34 extension checksum, the 14-bit toggle
    /// mask, the typing flag (cell 41 R bit2) and the cell-42 commit. Mirrors
    /// PixelProtocolExt3Tests' encoder.
    /// </summary>
    private static Color[] WithExt3(
        Color[] core, int mask, bool typing = false, bool present = true,
        int heartbeat = 7, int epoch = 0, int blocked = 0)
    {
        var cells = new Color[PixelProtocol.CellCountExt3];
        Array.Copy(core, cells, core.Length);

        if (present)
        {
            var cast = cells[PixelProtocol.CastCellIndex];
            cells[PixelProtocol.CastCellIndex] = Color.FromArgb(
                cast.R, cast.G, cast.B | (PixelProtocol.CastFlagExt3Present * 17));

            var ext = 0;
            for (var i = PixelProtocol.SpellIdCellBase; i <= PixelProtocol.ClassSpecCellIndex; i++)
                ext += cells[i].R / 17 + cells[i].G / 17 + cells[i].B / 17;
            var extension = cells[PixelProtocol.ExtensionCellIndex];
            cells[PixelProtocol.ExtensionCellIndex] =
                Color.FromArgb(extension.R, (ext & 0xF) * 17, extension.B);
        }

        var m0 = mask & 0xF;
        var m1 = (mask >> 4) & 0xF;
        var m2 = (mask >> 8) & 0xF;
        var mHi = ((mask >> 12) & 3) | (typing ? PixelProtocol.Ext3FlagTyping : 0);
        cells[PixelProtocol.Ext3MaskCellIndex] = Color.FromArgb(m0 * 17, m1 * 17, m2 * 17);
        cells[PixelProtocol.Ext3FlagsCellIndex] = Color.FromArgb(mHi * 17, epoch * 17, blocked * 17);

        var sum = m0 + m1 + m2 + mHi + epoch + blocked;
        cells[PixelProtocol.Ext3CommitCellIndex] =
            Color.FromArgb(0, (sum & 0xF) * 17, heartbeat * 17);
        return cells;
    }

    private static int Mask(params int[] bits) => bits.Aggregate(0, (mask, bit) => mask | (1 << bit));

    // -------------------------------------------------- main master gate

    [Fact]
    public void Main_Off_Holds_The_Rotation_Instead_Of_Sending()
    {
        var allOn = Mask(ToggleSync.BitMain, ToggleSync.BitOffensive, ToggleSync.BitDefensive);

        var control = TickOnce(WithExt3(CoreCapture(), allOn));
        Assert.True(control.Sends > 0, "control: a sendable Main frame with Main ON must send");

        var gate = TickOnce(WithExt3(CoreCapture(), Mask(ToggleSync.BitOffensive, ToggleSync.BitDefensive)));
        Assert.Equal(0, gate.Sends);
        Assert.Equal("holding (addon Main off - rotation suspended)", gate.Status[^1]);
    }

    [Fact]
    public void Main_Off_Also_Blocks_Auto_Target()
    {
        var frame = CoreCapture(BridgeState.NeedTarget, hasTarget: false, withSlot: false);
        var autoTargetMask = Mask(ToggleSync.BitMain, ToggleSync.BitOutOfCombat, ToggleSync.BitAutoTarget);
        void Configure(AppSettings s)
        {
            s.AutoTargetEnabled = true;
            s.CombatOnly = false;
        }

        var control = TickOnce(WithExt3(frame, autoTargetMask), Configure);
        Assert.True(control.Sends > 0, "control: auto-target must press TargetKey with Main ON");

        var gate = TickOnce(
            WithExt3(frame, Mask(ToggleSync.BitOutOfCombat, ToggleSync.BitAutoTarget)), Configure);
        Assert.Equal(0, gate.Sends);
        Assert.Equal("holding (addon Main off - rotation suspended)", gate.Status[^1]);
    }

    [Fact]
    public void Main_Off_Also_Blocks_Auto_Interact()
    {
        var frame = CoreCapture(BridgeState.NeedInteract, hasTarget: true, withSlot: false);
        var autoInteractMask = Mask(ToggleSync.BitMain, ToggleSync.BitOutOfCombat, ToggleSync.BitAutoInteract);
        void Configure(AppSettings s)
        {
            s.InteractEnabled = true;
            s.CombatOnly = false;
        }

        var control = TickOnce(WithExt3(frame, autoInteractMask), Configure);
        Assert.True(control.Sends > 0, "control: auto-interact must press InteractKey with Main ON");

        var gate = TickOnce(
            WithExt3(frame, Mask(ToggleSync.BitOutOfCombat, ToggleSync.BitAutoInteract)), Configure);
        Assert.Equal(0, gate.Sends);
        Assert.Equal("holding (addon Main off - rotation suspended)", gate.Status[^1]);
    }

    [Fact]
    public void Missing_Mirror_Fails_Open_So_An_Old_Addon_Keeps_Sending()
    {
        // 35-cell capture: no Ext3 block at all (pre-v3.5 addon). The master
        // gate must NOT brick the rotation.
        var sends = TickOnce(CoreCapture()).Sends;
        Assert.True(sends > 0);
    }

    // -------------------------------------------------------- typing veto

    [Fact]
    public void Typing_Veto_Blocks_The_Rotation_Even_With_Main_On()
    {
        var allOn = Mask(ToggleSync.BitMain);

        var control = TickOnce(WithExt3(CoreCapture(), allOn, typing: false));
        Assert.True(control.Sends > 0, "control: not typing must send");

        var veto = TickOnce(WithExt3(CoreCapture(), allOn, typing: true));
        Assert.Equal(0, veto.Sends);
        Assert.Equal("holding (typing in game chat)", veto.Status[^1]);
    }

    [Fact]
    public void Typing_Veto_Blocks_Auto_Interact_Too()
    {
        var frame = CoreCapture(BridgeState.NeedInteract, hasTarget: true, withSlot: false);
        var autoInteractMask = Mask(
            ToggleSync.BitMain, ToggleSync.BitOutOfCombat, ToggleSync.BitAutoInteract);
        void Configure(AppSettings s)
        {
            s.InteractEnabled = true;
            s.CombatOnly = false;
        }

        var control = TickOnce(WithExt3(frame, autoInteractMask, typing: false), Configure);
        Assert.True(control.Sends > 0, "control: not typing must interact");

        var veto = TickOnce(WithExt3(frame, autoInteractMask, typing: true), Configure);
        Assert.Equal(0, veto.Sends);
        Assert.Equal("holding (typing in game chat)", veto.Status[^1]);
    }
}
