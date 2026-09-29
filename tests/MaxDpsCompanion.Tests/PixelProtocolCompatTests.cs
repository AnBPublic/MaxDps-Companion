using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Regression guard for the N/N-1 decode contract (see docs/PROTOCOL.md):
/// the companion must keep decoding v4 (current) and v1 (stale in-game addon),
/// must reject the rejected v2/v3 same-length revisions by the R check, and
/// must keep the v1 slot remap so priority/settings indexing never shifts.
/// The decision layer must not have changed any of this.
/// </summary>
public class PixelProtocolCompatTests
{
    private static Color[] BuildFrame(
        int cellCount,
        int statusIndex,
        int versionIndex,
        int version,
        int state,
        int heartbeat,
        int statusFlags,
        params (int Index, int R, int G, int B)[] slotNibbles)
    {
        var nibbles = new (int R, int G, int B)[cellCount];
        nibbles[0] = (15, 0, 15); // magic: R/B white, G black
        foreach (var (index, r, g, b) in slotNibbles) nibbles[index] = (r, g, b);
        nibbles[statusIndex] = (state, heartbeat, statusFlags);

        var sum = 0;
        for (var i = 1; i <= statusIndex; i++) sum += nibbles[i].R + nibbles[i].G + nibbles[i].B;
        nibbles[versionIndex] = (version, sum & 0xF, heartbeat);

        var cells = new Color[cellCount];
        for (var i = 0; i < cellCount; i++)
            cells[i] = Color.FromArgb(nibbles[i].R * 17, nibbles[i].G * 17, nibbles[i].B * 17);
        return cells;
    }

    /// <summary>Minimal valid v4 frame used by the v5 tests as the N-1 case.</summary>
    internal static Color[] BuildV4Frame() => BuildFrame(
        PixelProtocol.CellCountV4,
        PixelProtocol.StatusCellIndexV4,
        PixelProtocol.VersionCellIndexV4,
        PixelProtocol.SupportedVersionV4,
        state: 1,
        heartbeat: 5,
        statusFlags: PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
        (1, 4, 5, 8)); // Main: VK 0x45 ('E')

    [Fact]
    public void Decode_V4_Reads_Slots_State_And_Status_Flags()
    {        var cells = BuildFrame(
            PixelProtocol.CellCountV4,
            PixelProtocol.StatusCellIndexV4,
            PixelProtocol.VersionCellIndexV4,
            PixelProtocol.SupportedVersionV4,
            state: 1,
            heartbeat: 5,
            statusFlags: PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget,
            (1, 5, 2, 8 | 1)); // Main: VK 0x52 ('R'), shift held

        var frame = PixelProtocol.Decode(cells);

        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersionV4, frame!.Version);
        Assert.Equal(BridgeState.Active, frame.State);
        Assert.True(frame.InCombat);
        Assert.True(frame.HasTarget);
        Assert.False(frame.OnGcd);
        Assert.Equal(new KeyStroke(0x52, Shift: true, Ctrl: false, Alt: false), frame[Slot.Main]);
        Assert.Null(frame[Slot.Interrupt]);
        Assert.False(frame.ContextValid);
    }

    [Fact]
    public void Decode_V1_Remaps_Slots_And_Leaves_Trinket_Null()
    {
        // v1 order: 1 Main, 2 Offensive, 3 Interrupt, 4 Defensive, 5 Consumable.
        var cells = BuildFrame(
            PixelProtocol.CellCountV1,
            PixelProtocol.StatusCellIndexV1,
            PixelProtocol.VersionCellIndexV1,
            PixelProtocol.SupportedVersionV1,
            state: 1,
            heartbeat: 7,
            statusFlags: 0,
            (3, 0, 8, 8)); // Interrupt: VK 0x08

        var frame = PixelProtocol.Decode(cells);

        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersionV1, frame!.Version);
        Assert.Equal(new KeyStroke(0x08, false, false, false), frame[Slot.Interrupt]);
        Assert.Null(frame[Slot.Main]);
        Assert.Null(frame[Slot.Trinket]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Decode_Rejects_Rejected_Same_Length_Versions(int version)
    {
        var cells = BuildFrame(
            PixelProtocol.CellCountV4,
            PixelProtocol.StatusCellIndexV4,
            PixelProtocol.VersionCellIndexV4,
            version,
            state: 1,
            heartbeat: 5,
            statusFlags: 0);

        Assert.Null(PixelProtocol.Decode(cells));
    }

    [Fact]
    public void Decode_Rejects_Corrupted_Checksum()
    {
        var cells = BuildFrame(
            PixelProtocol.CellCountV4,
            PixelProtocol.StatusCellIndexV4,
            PixelProtocol.VersionCellIndexV4,
            PixelProtocol.SupportedVersionV4,
            state: 1,
            heartbeat: 5,
            statusFlags: 0);
        cells[2] = Color.FromArgb(0, 255, 0); // corrupt one slot channel

        Assert.Null(PixelProtocol.Decode(cells));
    }

    [Fact]
    public void Decode_Rejects_Unknown_Length()
    {
        Assert.Null(PixelProtocol.Decode(new Color[7]));
    }

    [Fact]
    public void TrimToV1_Keeps_The_First_Eight_Cells()
    {
        var cells = new Color[PixelProtocol.CellCount];
        for (var i = 0; i < cells.Length; i++) cells[i] = Color.FromArgb((i * 17) % 256, 0, 255);

        var v1 = PixelProtocol.TrimToV1(cells);

        Assert.Equal(PixelProtocol.CellCountV1, v1.Length);
        for (var i = 0; i < v1.Length; i++) Assert.Equal(cells[i], v1[i]);
    }
}
