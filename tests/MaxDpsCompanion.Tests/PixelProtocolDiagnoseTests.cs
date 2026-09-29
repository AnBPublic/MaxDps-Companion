using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Decode-fault classification (telemetry diagnostics). Diagnose runs the same
/// checks as Decode and must never disagree: fault != None iff Decode is null.
/// </summary>
public class PixelProtocolDiagnoseTests
{
    private static Color[] BuildFrame(
        int state = 1,
        int heartbeat = 5,
        int version = PixelProtocol.SupportedVersionV4,
        int statusFlags = 0,
        params (int Index, int R, int G, int B)[] slotNibbles)
    {
        // v4 window: the protocol's N-1 compatibility path. v5 fault
        // classification is covered by PixelProtocolV5Tests.
        const int cellCount = PixelProtocol.CellCountV4;
        const int statusIndex = PixelProtocol.StatusCellIndexV4;
        const int versionIndex = PixelProtocol.VersionCellIndexV4;
        var nibbles = new (int R, int G, int B)[cellCount];
        nibbles[0] = (15, 0, 15);
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

    [Fact]
    public void Valid_Frame_Classifies_As_None_And_Decodes()
    {
        var cells = BuildFrame(slotNibbles: [(1, 5, 2, 8)]);

        Assert.NotNull(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.None, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Wrong_Magic_Classifies_As_Magic()
    {
        var cells = BuildFrame();
        cells[0] = Color.FromArgb(255, 255, 255);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Magic, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Corrupted_Slot_Classifies_As_Checksum()
    {
        var cells = BuildFrame();
        cells[2] = Color.FromArgb(0, 255, 0);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Stale_Protocol_Version_Classifies_As_Version()
    {
        var cells = BuildFrame(version: 2);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Version, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Out_Of_Range_State_Classifies_As_State()
    {
        var cells = BuildFrame(state: 5);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.State, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Torn_Commit_Classifies_As_Commit()
    {
        var cells = BuildFrame(heartbeat: 5);
        var version = cells[PixelProtocol.VersionCellIndexV4];
        // Commit (B) no longer equals heartbeat -> tear.
        cells[PixelProtocol.VersionCellIndexV4] = Color.FromArgb(version.R, version.G, 1 * 17);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Commit, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void Unknown_Length_Classifies_As_Length()
    {
        Assert.Null(PixelProtocol.Decode(new Color[7]));
        Assert.Equal(DecodeFault.Length, PixelProtocol.Diagnose(new Color[7], null));
    }
}
