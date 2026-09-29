using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Ext2 (v3.0.0) decode: the 40-cell frame keeps cells 0-34 byte-identical to
/// v5 and adds the HP curve (cell 35) + SelfHeal2 (cells 36-38) + cell-39
/// checksum. A cell-39 failure drops ONLY SelfHeal2; a stale 35-cell addon
/// (bit2=0) decodes exactly as before with any junk in 35-39 ignored.
/// </summary>
public class PixelProtocolExt2Tests
{
    private static TestV5Frame Core() => new TestV5Frame()
        .Slot(Slot.Main, 0x45)
        .SpellId(Slot.Main, 100)
        .Slot(Slot.SelfHeal, 0x48)
        .SpellId(Slot.SelfHeal, 202168)
        .ClassSpec("WARRIOR", 1)
        .Ranges((Slot.SelfHeal, TriState.Yes));

    [Fact]
    public void Ext2_Valid_Decodes_Curve_And_SelfHeal2()
    {
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.HpCurveValid);
        Assert.Equal(6, frame.HpCurveBand);

        Assert.NotNull(frame.SelfHeal2);
        Assert.Equal(new KeyStroke(0x48, false, false, false), frame.SelfHeal2!.Value.Stroke);
        Assert.Equal(34428, frame.SelfHeal2.Value.SpellId);
        Assert.Equal(TriState.No, frame.SelfHeal2.Value.Range);
    }

    [Fact]
    public void Ext2_Curve_Out_Of_Band_Is_Invalid_But_Frame_And_SelfHeal2_Kept()
    {
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SetCurve(r: 5, g: 5) // sum 10, outside 14..16
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.Unknown)
            .BuildExt2(heartbeat: 7);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.HpCurveValid);
        Assert.Equal(-1, frame.HpCurveBand);
        Assert.NotNull(frame.SelfHeal2);
        Assert.Equal(34428, frame.SelfHeal2!.Value.SpellId);
    }

    [Fact]
    public void Ext2_Absent_Ignores_Junk_In_Cells_35_To_39()
    {
        // A 35-cell stale addon: the companion's 40-cell capture has background
        // junk in the extension cells, but bit2=0 means ignore them entirely.
        var core = Core().Build(heartbeat: 7);
        var captured = new Color[PixelProtocol.CellCountExt2];
        for (var i = 0; i < core.Length; i++) captured[i] = core[i];
        for (var i = core.Length; i < captured.Length; i++) captured[i] = Color.FromArgb(255, 0, 255);

        var frame = PixelProtocol.Decode(captured);
        Assert.NotNull(frame);
        Assert.False(frame!.HpCurveValid);
        Assert.Equal(-1, frame.HpCurveBand);
        Assert.Null(frame.SelfHeal2);
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
    }

    [Fact]
    public void Ext2_Curve_Inactive_Bit3_Zero_Invalid_But_SelfHeal2_Kept()
    {
        var cells = Core()
            .Ext2(hpCurveActive: false, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.Yes)
            .BuildExt2(heartbeat: 7);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.HpCurveValid);
        Assert.Equal(-1, frame.HpCurveBand);
        Assert.NotNull(frame.SelfHeal2);
    }

    [Fact]
    public void Ext2_Cell39_Checksum_Failure_Drops_Only_SelfHeal2()
    {
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);
        var commit = cells[PixelProtocol.SelfHeal2CommitCellIndex];
        cells[PixelProtocol.SelfHeal2CommitCellIndex] = Color.FromArgb(commit.R, (commit.G + 1) % 16 * 17, commit.B);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.HpCurveValid);
        Assert.Null(frame.SelfHeal2);
        // The core frame is fully intact.
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
    }

    [Fact]
    public void Ext2_Cell39_Torn_Commit_Drops_Only_SelfHeal2()
    {
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);
        cells[PixelProtocol.SelfHeal2CommitCellIndex] = Color.FromArgb(0, 0, 17); // commit != heartbeat

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.HpCurveValid);
        Assert.Null(frame.SelfHeal2);
    }

    [Fact]
    public void Core_Decode_Is_Identical_Between_35_And_40_Cell_Captures()
    {
        var core = Core().Urgency(DefensiveUrgency.Orange, DefensiveUrgency.Unknown, catalogSource: true).Build(heartbeat: 7);
        var padded = new Color[PixelProtocol.CellCountExt2];
        for (var i = 0; i < core.Length; i++) padded[i] = core[i];
        for (var i = core.Length; i < padded.Length; i++) padded[i] = Color.Black;

        var a = PixelProtocol.Decode(core);
        var b = PixelProtocol.Decode(padded);
        Assert.NotNull(a);
        Assert.NotNull(b);

        Assert.Equal(a!.Version, b!.Version);
        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Heartbeat, b.Heartbeat);
        Assert.Equal(a.HpPct, b.HpPct);
        Assert.Equal(a.TargetHpPct, b.TargetHpPct);
        Assert.Equal(a.Cast, b.Cast);
        Assert.Equal(a.DefensiveUrgency, b.DefensiveUrgency);
        Assert.Equal(a.StaggerUrgency, b.StaggerUrgency);
        Assert.Equal(a.DefensiveCatalogSource, b.DefensiveCatalogSource);
        Assert.Equal(a.ClassName, b.ClassName);
        Assert.Equal(a.SpecName, b.SpecName);
        Assert.Equal(a.BuffProbeValid, b.BuffProbeValid);
        for (var i = 0; i < PixelProtocol.SlotCount; i++)
        {
            Assert.Equal(a[(Slot)i], b[(Slot)i]);
            Assert.Equal(a.SpellId((Slot)i), b.SpellId((Slot)i));
            Assert.Equal(a.SlotRange[i], b.SlotRange[i]);
            Assert.Equal(a.SlotBuffActive[i], b.SlotBuffActive[i]);
        }
    }

    [Fact]
    public void Diagnose_Still_Reports_Core_Faults_On_Ext2_Captures()
    {
        var cells = Core().Ext2(hpCurveActive: true, curveBand: 6).BuildExt2(heartbeat: 7);
        Assert.NotNull(PixelProtocol.Decode(cells));

        // Corrupt the core checksum; Ext2 must not mask a core fault.
        cells[2] = Color.FromArgb(0, 255, 0);
        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }
}
