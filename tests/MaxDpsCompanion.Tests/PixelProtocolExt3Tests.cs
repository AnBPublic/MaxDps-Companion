using System.Drawing;
using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Ext3 (v3.5) T0 contract: a 43-cell capture keeps cells 0-39 byte-identical
/// to v5/Ext2 and adds cells 40-42 (14-bit mask + epoch + blocked nibble +
/// cell-42 checksum/commit). Presence is cell 28 B bit2; the cell-28 B mask
/// used for the SelfHeal2 range must keep bit2 out of the range value. A
/// cell-42 failure drops ONLY the Ext3 values; a clear bit2 (or a 40-cell
/// capture with junk in 40-42) decodes exactly as before. The decoder asserts
/// nothing about the reserved-zero R channels of cells 34/39/42.
/// </summary>
public class PixelProtocolExt3Tests
{
    private static TestV5Frame Core() => new TestV5Frame()
        .Slot(Slot.Main, 0x45)
        .SpellId(Slot.Main, 100)
        .Slot(Slot.SelfHeal, 0x48)
        .SpellId(Slot.SelfHeal, 202168)
        .ClassSpec("WARRIOR", 1)
        .Ranges((Slot.SelfHeal, TriState.Yes));

    /// <summary>
    /// Sets cell 28 B bit2 (Ext3 presence) and repairs the cell-34 extension
    /// checksum, whose 11-33 scope includes cell 28.
    /// </summary>
    private static void SetPresenceAndRepairCore(Color[] cells)
    {
        var c28 = cells[PixelProtocol.CastCellIndex];
        cells[PixelProtocol.CastCellIndex] = Color.FromArgb(
            c28.R, c28.G, c28.B | (PixelProtocol.CastFlagExt3Present * 17));

        var ext = 0;
        for (var i = PixelProtocol.SpellIdCellBase; i <= PixelProtocol.ClassSpecCellIndex; i++)
        {
            var c = cells[i];
            ext += c.R / 17 + c.G / 17 + c.B / 17;
        }
        var c34 = cells[PixelProtocol.ExtensionCellIndex];
        cells[PixelProtocol.ExtensionCellIndex] = Color.FromArgb(c34.R, (ext & 0xF) * 17, c34.B);
    }

    /// <summary>
    /// Post-processes a 35- or 40-cell core frame into a valid 43-cell Ext3
    /// capture: sets cell 28 B bit2, recomputes the cell-34 extension checksum
    /// (cell 28 lives inside its 11-33 scope), and writes cells 40-42.
    /// </summary>
    private static Color[] WithExt3(
        Color[] core,
        int heartbeat,
        int mask,
        int epoch = 0,
        int blocked = 0,
        bool corruptChecksum = false,
        bool tornCommit = false,
        bool setPresent = true)
    {
        var cells = new Color[PixelProtocol.CellCountExt3];
        for (var i = 0; i < core.Length; i++) cells[i] = core[i];

        if (setPresent) SetPresenceAndRepairCore(cells);

        var m0 = mask & 0xF;
        var m1 = (mask >> 4) & 0xF;
        var m2 = (mask >> 8) & 0xF;
        var mHi = (mask >> 12) & 3;
        cells[PixelProtocol.Ext3MaskCellIndex] = Color.FromArgb(m0 * 17, m1 * 17, m2 * 17);
        cells[PixelProtocol.Ext3FlagsCellIndex] = Color.FromArgb(mHi * 17, epoch * 17, blocked * 17);

        var sum = m0 + m1 + m2 + mHi + epoch + blocked;
        var checksum = (sum + (corruptChecksum ? 1 : 0)) & 0xF;
        cells[PixelProtocol.Ext3CommitCellIndex] = Color.FromArgb(
            0, checksum * 17, (tornCommit ? heartbeat + 1 : heartbeat) * 17);
        return cells;
    }

    [Fact]
    public void IsV5Length_Accepts_35_40_43_Rejects_Everything_Else()
    {
        Assert.True(PixelProtocol.IsV5Length(PixelProtocol.CellCount));
        Assert.True(PixelProtocol.IsV5Length(PixelProtocol.CellCountExt2));
        Assert.True(PixelProtocol.IsV5Length(PixelProtocol.CellCountExt3));
        Assert.Equal(14, PixelProtocol.Ext3MaskBitCount);
        foreach (var length in new[] { 7, 8, 9, 34, 36, 39, 41, 42, 44 })
            Assert.False(PixelProtocol.IsV5Length(length));
    }

    [Fact]
    public void Ext3_Valid_Decodes_Mask_Epoch_Blocked()
    {
        const int mask = 0x2A5; // 14-bit span: low 12 in cell 40, high 2 in cell 41
        var cells = WithExt3(Core().Build(heartbeat: 7), heartbeat: 7, mask: mask, epoch: 3, blocked: 9);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext3Present);
        Assert.NotNull(frame.Ext3);
        Assert.Equal(mask, frame.Ext3!.Value.Mask);
        Assert.Equal(3, frame.Ext3.Value.Epoch);
        Assert.Equal(9, frame.Ext3.Value.Blocked);
        // Core untouched.
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
        Assert.Equal(7, frame.Heartbeat);
    }

    [Fact]
    public void Ext3_Presence_Clear_On_43_Cell_Capture_Ignores_Junk()
    {
        var core = Core().Build(heartbeat: 7);
        var cells = new Color[PixelProtocol.CellCountExt3];
        for (var i = 0; i < core.Length; i++) cells[i] = core[i];
        for (var i = core.Length; i < cells.Length; i++) cells[i] = Color.FromArgb(255, 0, 255);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext3Present);
        Assert.Null(frame.Ext3);
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
    }

    [Fact]
    public void Ext3_Present_Bit_On_40_Cell_Capture_Is_Length_Gated()
    {
        // A 40-cell Ext2 capture whose reserved cell-28 B bit2 is somehow set
        // must NOT decode an Ext3 block: there are no cells 40-42 to read.
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);
        SetPresenceAndRepairCore(cells);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.Ext3Present);
        Assert.Null(frame.Ext3);
        // Ext2 still decoded (the presence bit is not asserted to be 0).
        Assert.NotNull(frame.SelfHeal2);
    }

    [Fact]
    public void Ext3_Cell42_Checksum_Failure_Drops_Only_Ext3()
    {
        var cells = WithExt3(Core().Build(heartbeat: 7), 7, mask: 0x155, epoch: 1, corruptChecksum: true);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.True(frame!.Ext3Present);
        Assert.Null(frame.Ext3);
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
    }

    [Fact]
    public void Ext3_Cell42_Torn_Commit_Drops_Only_Ext3()
    {
        var cells = WithExt3(Core().Build(heartbeat: 7), 7, mask: 0x155, epoch: 1, tornCommit: true);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.Null(frame!.Ext3);
        Assert.Equal(202168, frame.SpellId(Slot.SelfHeal));
    }

    [Fact]
    public void Ext3_Cell42_Reserved_R_Is_Ignored_Not_Asserted_Zero()
    {
        var cells = WithExt3(Core().Build(heartbeat: 7), 7, mask: 0x001);
        cells[PixelProtocol.Ext3CommitCellIndex] = Color.FromArgb(15 * 17, 1 * 17, 7 * 17);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.NotNull(frame!.Ext3);
        Assert.Equal(0x001, frame.Ext3!.Value.Mask);
    }

    [Fact]
    public void Ext3_Cell34_And_Cell39_Reserved_R_Are_Not_Asserted_Zero()
    {
        var cells = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);
        cells[PixelProtocol.ExtensionCellIndex] =
            Color.FromArgb(15 * 17, cells[PixelProtocol.ExtensionCellIndex].G, cells[PixelProtocol.ExtensionCellIndex].B);
        cells[PixelProtocol.SelfHeal2CommitCellIndex] =
            Color.FromArgb(15 * 17, cells[PixelProtocol.SelfHeal2CommitCellIndex].G, cells[PixelProtocol.SelfHeal2CommitCellIndex].B);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.NotNull(frame!.SelfHeal2);
        Assert.Equal(34428, frame.SelfHeal2!.Value.SpellId);
    }

    [Fact]
    public void Ext3_Presence_Bit_Does_Not_Bleed_Into_SelfHeal2_Range()
    {
        // The cell-28 B mask (& 3) must keep bit2 out of the range tri-state.
        var core = Core()
            .Ext2(hpCurveActive: true, curveBand: 6)
            .SelfHeal2(new KeyStroke(0x48, false, false, false), 34428, TriState.No)
            .BuildExt2(heartbeat: 7);
        var cells = WithExt3(core, 7, mask: 0x3FF, epoch: 2, blocked: 4);

        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.NotNull(frame!.SelfHeal2);
        Assert.Equal(TriState.No, frame.SelfHeal2!.Value.Range);
        Assert.NotNull(frame.Ext3);
        Assert.Equal(0x3FF, frame.Ext3!.Value.Mask);
    }

    [Fact]
    public void Ext3_Mask_Full_14_Bits_Round_Trips()
    {
        var cells = WithExt3(Core().Build(heartbeat: 7), 7, mask: 0x3FFF, epoch: 15, blocked: 15);
        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.Equal(0x3FFF, frame!.Ext3!.Value.Mask);
        Assert.Equal(15, frame.Ext3.Value.Epoch);
        Assert.Equal(15, frame.Ext3.Value.Blocked);
    }

    [Fact]
    public void Ext3_Must_Not_Mask_A_Core_Fault()
    {
        var cells = WithExt3(Core().Build(heartbeat: 7), 7, mask: 0x123);
        cells[2] = Color.FromArgb(0, 255, 0); // break the core checksum

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }
}
