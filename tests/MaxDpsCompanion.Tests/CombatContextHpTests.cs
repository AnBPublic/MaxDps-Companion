using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// v3.0.0 C3: HP precedence and the curve fallback. Plain cell 27 wins; when it
/// is secret/unknown a valid Ext2 curve supplies band-derived HP (HpPct) plus an
/// upper bound (HpPctUpper) for the overheal guard; <c>[Intelligence] HpCurve=0</c>
/// disables the fallback. When MaxDps's own defensive-urgency nibble (cell 31 G)
/// is unknown but the curve is valid, urgency is staged from curve HP at the
/// vendor control points.
/// </summary>
public class CombatContextHpTests
{
    private static TestV5Frame Core() => new TestV5Frame()
        .Slot(Slot.Main, 0x45)
        .SpellId(Slot.Main, 100)
        .ClassSpec("WARRIOR", 1);

    private static BridgeFrame Frame(int plainHpPct, int curveBand, bool curveActive = true)
    {
        var builder = Core();
        if (curveBand >= 0) builder.Ext2(curveActive, curveBand);
        var cells = curveBand >= 0 ? builder.BuildExt2(heartbeat: 7) : builder.Build(heartbeat: 7);
        if (plainHpPct >= 0)
        {
            // Write the plain vitals cell (R = hp/16, G = hp%16, B bit0 valid).
            cells[PixelProtocol.VitalsCellIndex] = Color.FromArgb(
                plainHpPct / 16 * 17, plainHpPct % 16 * 17, 17);
            // Recompute checksums so the frame still decodes.
            cells = Revalidated(cells);
        }
        return PixelProtocol.Decode(cells)!;
    }

    /// <summary>Patching a core cell invalidates both checksums; rebuild them in-place.</summary>
    private static Color[] Revalidated(Color[] cells)
    {
        int Nib(int index, int axis) => axis switch
        {
            0 => cells[index].R / 17,
            1 => cells[index].G / 17,
            _ => cells[index].B / 17,
        };
        // Version cell: R=version, G=core sum, B=heartbeat.
        var coreSum = 0;
        for (var i = 1; i <= PixelProtocol.StatusCellIndex; i++)
            coreSum += Nib(i, 0) + Nib(i, 1) + Nib(i, 2);
        var heartbeat = Nib(PixelProtocol.StatusCellIndex, 1);
        cells[PixelProtocol.VersionCellIndex] = Color.FromArgb(
            PixelProtocol.SupportedVersion * 17, (coreSum & 0xF) * 17, heartbeat * 17);
        var extSum = 0;
        for (var i = PixelProtocol.SpellIdCellBase; i <= PixelProtocol.ClassSpecCellIndex; i++)
            extSum += Nib(i, 0) + Nib(i, 1) + Nib(i, 2);
        cells[PixelProtocol.ExtensionCellIndex] = Color.FromArgb(0, (extSum & 0xF) * 17, heartbeat * 17);
        return cells;
    }

    [Fact]
    public void Plain_Hp_Wins_Over_Curve()
    {
        var context = CombatContext.FromFrame(Frame(plainHpPct: 73, curveBand: 3));
        Assert.True(context.HpValid);
        Assert.Equal(HpSource.Plain, context.HpSource);
        Assert.Equal(73, context.HpPct);
        Assert.Equal(73, context.HpPctUpper);
    }

    [Fact]
    public void Curve_Supplies_Hp_When_Plain_Unknown()
    {
        var context = CombatContext.FromFrame(Frame(plainHpPct: -1, curveBand: 6));
        Assert.True(context.HpValid);
        Assert.Equal(HpSource.Curve, context.HpSource);
        Assert.Equal(40, context.HpPct);        // round(6 * 100 / 15)
        Assert.Equal(43, context.HpPctUpper);   // round(6.5 * 100 / 15)
    }

    [Fact]
    public void HpCurve_Disabled_Ignores_The_Curve()
    {
        var context = CombatContext.FromFrame(Frame(plainHpPct: -1, curveBand: 6), hpCurve: false);
        Assert.False(context.HpValid);
        Assert.Equal(HpSource.Unknown, context.HpSource);
        Assert.Equal(0, context.HpPct);
        Assert.Equal(0, context.HpPctUpper);
    }

    [Fact]
    public void Invalid_Curve_Is_Unknown()
    {
        var inactive = CombatContext.FromFrame(Frame(plainHpPct: -1, curveBand: 6, curveActive: false));
        Assert.False(inactive.HpValid);
        Assert.Equal(HpSource.Unknown, inactive.HpSource);

        var noCurve = CombatContext.FromFrame(Frame(plainHpPct: -1, curveBand: -1));
        Assert.False(noCurve.HpValid);
        Assert.Equal(HpSource.Unknown, noCurve.HpSource);
    }

    [Theory]
    [InlineData(2, (int)DefensiveUrgency.Red)]     // 13% <= 30
    [InlineData(4, (int)DefensiveUrgency.Red)]     // 27% <= 30
    [InlineData(6, (int)DefensiveUrgency.Orange)]  // 40% < 50
    [InlineData(9, (int)DefensiveUrgency.Yellow)]  // 60% < 100
    [InlineData(15, (int)DefensiveUrgency.White)]  // 100%
    public void Unknown_Urgency_Is_Derived_From_A_Valid_Curve(int band, int expected)
    {
        var context = CombatContext.FromFrame(Frame(plainHpPct: -1, curveBand: band));
        Assert.Equal((DefensiveUrgency)expected, context.DefensiveUrgency);
    }

    [Fact]
    public void Plain_Hp_Does_Not_Invent_Urgency()
    {
        // The nibble is the vendor's own staged value; a plain HP read alone
        // must never be turned into a urgency the addon did not send.
        var context = CombatContext.FromFrame(Frame(plainHpPct: 20, curveBand: 2));
        Assert.Equal(DefensiveUrgency.Unknown, context.DefensiveUrgency);
    }

    [Fact]
    public void Explicit_Urgency_Is_Not_Overwritten_By_The_Curve()
    {
        var cells = Core().Ext2(hpCurveActive: true, curveBand: 4)
            .Urgency(DefensiveUrgency.White)
            .BuildExt2(heartbeat: 7);
        var context = CombatContext.FromFrame(PixelProtocol.Decode(cells)!);
        Assert.Equal(DefensiveUrgency.White, context.DefensiveUrgency);
    }
}
