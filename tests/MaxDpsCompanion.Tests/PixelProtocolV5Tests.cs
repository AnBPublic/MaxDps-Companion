using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Protocol v5 wire format: an independent encoder (mirroring Bridge.lua's
/// Update) plus the decoder round-trip. Pins the cell layout, both checksums,
/// the tear detection, and every new sensor field. The Lua side of the same
/// contract is verified by tests/secret_harness.lua section 9.
/// </summary>
public class PixelProtocolV5Tests
{
    private sealed class V5Builder
    {
        private readonly (int R, int G, int B)[] _nibbles = new (int, int, int)[PixelProtocol.CellCount];

        public V5Builder()
        {
            _nibbles[0] = (15, 0, 15);
            // Defaults are UNKNOWN, not zero: a bare frame must never claim
            // "HP 0%" or "target at 0%" just because a cell was unwritten.
            _nibbles[PixelProtocol.CastCellIndex] = (15, 15, 0);
            _nibbles[PixelProtocol.TargetCellIndex] = (0, 15, 2);
            _nibbles[PixelProtocol.VitalsCellIndex] = (0, 0, 0);
        }

        public V5Builder Slot(int slot, byte vk, int flags)
        {
            _nibbles[slot] = (vk >> 4, vk & 0x0F, flags);
            return this;
        }

        public V5Builder SpellId(int slot, int id)
        {
            var hi = (id >> 12) & 0x0FFF;
            var lo = id & 0x0FFF;
            var baseCell = PixelProtocol.SpellIdCellBase + (slot - 1) * 2;
            _nibbles[baseCell] = ((hi >> 8) & 0xF, (hi >> 4) & 0xF, hi & 0xF);
            _nibbles[baseCell + 1] = ((lo >> 8) & 0xF, (lo >> 4) & 0xF, lo & 0xF);
            return this;
        }

        public V5Builder Vitals(int hpPct)
        {
            _nibbles[PixelProtocol.VitalsCellIndex] = (hpPct / 16, hpPct % 16, 1);
            return this;
        }

        public V5Builder Cast(int state, int band = 15)
        {
            _nibbles[PixelProtocol.CastCellIndex] = (state, band, 0);
            return this;
        }

        public V5Builder Target(int meleeFlag, int hpBand, int castFlags)
        {
            _nibbles[PixelProtocol.TargetCellIndex] = (meleeFlag, hpBand, castFlags);
            return this;
        }

        public V5Builder Range(TriState[] tri)
        {
            int Bits(int slot) => tri[slot] switch
            {
                TriState.Yes => PixelProtocol.RangeIn,
                TriState.No => PixelProtocol.RangeOut,
                _ => PixelProtocol.RangeUnknown,
            };
            var r30 = Bits(0) | (Bits(1) << 2);
            var g30 = Bits(2) | (Bits(3) << 2);
            var b30 = Bits(4) | (Bits(5) << 2);
            var r31 = Bits(6) | (Bits(7) << 2);
            _nibbles[PixelProtocol.RangeCellIndex] = (r30, g30, b30);
            _nibbles[PixelProtocol.RangeCellIndex2] = (r31, 0, 0);
            return this;
        }

        public V5Builder Buffs(bool[] active)
        {
            var r = 0;
            for (var i = 0; i < 4; i++) if (active[i]) r |= 1 << i;
            var g = 0;
            for (var i = 4; i < 8; i++) if (active[i]) g |= 1 << (i - 4);
            _nibbles[PixelProtocol.BuffCellIndex] = (r, g, 0);
            // v2.7: declaring buffs declares the probe block trustworthy.
            var (classId, specId, _) = _nibbles[PixelProtocol.ClassSpecCellIndex];
            _nibbles[PixelProtocol.ClassSpecCellIndex] = (classId, specId, (classId > 0 ? 1 : 0) | 2);
            return this;
        }

        public V5Builder ClassSpec(int classId, int specId)
        {
            var (_, _, flags) = _nibbles[PixelProtocol.ClassSpecCellIndex];
            _nibbles[PixelProtocol.ClassSpecCellIndex] = (classId, specId, (classId > 0 ? 1 : 0) | (flags & 2));
            return this;
        }

        /// <summary>v2.7: force the self-buff probe block validity bit (legacy encoders leave it clear).</summary>
        public V5Builder BuffProbeValid(bool valid)
        {
            var (classId, specId, flags) = _nibbles[PixelProtocol.ClassSpecCellIndex];
            _nibbles[PixelProtocol.ClassSpecCellIndex] = (classId, specId, valid ? flags | 2 : flags & ~2);
            return this;
        }

        public Color[] Build(int state, int heartbeat, int statusFlags)
        {
            _nibbles[PixelProtocol.StatusCellIndex] = (state, heartbeat, statusFlags);

            var core = 0;
            for (var i = 1; i <= PixelProtocol.StatusCellIndex; i++)
                core += _nibbles[i].R + _nibbles[i].G + _nibbles[i].B;
            _nibbles[PixelProtocol.VersionCellIndex] = (PixelProtocol.SupportedVersion, core & 0xF, heartbeat);

            var ext = 0;
            for (var i = PixelProtocol.SpellIdCellBase; i <= PixelProtocol.ClassSpecCellIndex; i++)
                ext += _nibbles[i].R + _nibbles[i].G + _nibbles[i].B;
            _nibbles[PixelProtocol.ExtensionCellIndex] = (0, ext & 0xF, heartbeat);

            var cells = new Color[PixelProtocol.CellCount];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = Color.FromArgb(_nibbles[i].R * 17, _nibbles[i].G * 17, _nibbles[i].B * 17);
            return cells;
        }
    }

    private static V5Builder Base() => new V5Builder()
        .Slot(1, 0x45, 8)                                   // Main: E
        .Slot(2, 0x32, 8)                                   // Offensive: 2
        .Slot(6, 0x46, 8)                                   // Interrupt: F
        .Slot(7, 0x31, 8)                                   // Mobility: 1
        .SpellId(1, 100)
        .SpellId(6, 6552)
        .SpellId(7, 100)
        .Vitals(73)
        .Cast(PixelProtocol.CastStateCasting)
        .Target(meleeFlag: 0, hpBand: 10, castFlags: 0b0101)
        .Range([TriState.Yes, TriState.Unknown, TriState.No, TriState.Unknown,
                TriState.Unknown, TriState.Yes, TriState.Unknown, TriState.Unknown])
        .Buffs([true, false, false, false, false, false, false, false])
        .ClassSpec(13, 1);

    private static Color[] HappyFrame(int heartbeat = 7) =>
        Base().Build((int)BridgeState.Active,
            heartbeat,
            PixelProtocol.StatusFlagInCombat | PixelProtocol.StatusFlagHasTarget | PixelProtocol.StatusFlagContextValid);

    [Fact]
    public void V5_Round_Trips_Every_Field()
    {
        var frame = PixelProtocol.Decode(HappyFrame());

        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersion, frame!.Version);
        Assert.Equal(BridgeState.Active, frame.State);
        Assert.True(frame.InCombat);
        Assert.True(frame.HasTarget);
        Assert.True(frame.ContextValid);

        Assert.Equal(new KeyStroke(0x45, false, false, false), frame[Slot.Main]);
        Assert.Equal(new KeyStroke(0x32, false, false, false), frame[Slot.Offensive]);
        Assert.Equal(new KeyStroke(0x46, false, false, false), frame[Slot.Interrupt]);
        Assert.Equal(new KeyStroke(0x31, false, false, false), frame[Slot.Mobility]);
        Assert.Null(frame[Slot.Defensive]);
        Assert.Null(frame[Slot.SelfHeal]);

        Assert.Equal(100, frame.SpellId(Slot.Main));
        Assert.Equal(6552, frame.SpellId(Slot.Interrupt));
        Assert.Equal(100, frame.SpellId(Slot.Mobility));
        Assert.Equal(0, frame.SpellId(Slot.Defensive));

        Assert.Equal(73, frame.HpPct);
        Assert.Equal(PlayerCastState.Casting, frame.Cast);
        Assert.Equal(TriState.No, frame.TargetInMelee);
        Assert.Equal(67, frame.TargetHpPct);   // band 10 -> ~66.7%
        Assert.Equal(TriState.Yes, frame.TargetCasting);
        Assert.Equal(TriState.Yes, frame.TargetCastInterruptible);
        Assert.Equal(TriState.Yes, frame.SlotRange[0]);
        Assert.Equal(TriState.No, frame.SlotRange[2]);
        Assert.Equal(TriState.Yes, frame.SlotRange[5]);
        Assert.Equal(TriState.Yes, frame.SlotBuffActive[0]);
        Assert.Equal(TriState.No, frame.SlotBuffActive[1]);
        Assert.True(frame.BuffProbeValid);
        Assert.Equal("WARRIOR", frame.ClassName);
        Assert.Equal("Arms", frame.SpecName);
    }

    [Fact]
    public void V5_Buff_Bits_Without_The_Valid_Bit_Decode_As_Unknown()
    {
        // v2.7 additive semantics: a legacy encoder (or a failed/secret probe
        // block) clears cell33 B bit1, so every buff bit is UNKNOWN — never a
        // silent "buff absent". The policy keeps its documented fail-open.
        var cells = Base()
            .Buffs([true, false, false, false, false, false, false, false])
            .BuffProbeValid(false)
            .Build((int)BridgeState.Active, 7,
                PixelProtocol.StatusFlagHasTarget | PixelProtocol.StatusFlagContextValid);
        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.False(frame!.BuffProbeValid);
        Assert.Equal(TriState.Unknown, frame.SlotBuffActive[0]);
        Assert.Equal(TriState.Unknown, frame.SlotBuffActive[1]);

        var context = CombatContext.FromFrame(frame);
        Assert.False(context.BuffProbeValid);
        Assert.Equal(TriState.Unknown, context.SlotBuffActive[0]);
    }

    [Fact]
    public void V5_Spell_Id_Boundaries_Round_Trip()
    {
        foreach (var id in new[] { 1, 100, 65535, 1_048_575, 1_276_467, 0xFFFFFF })
        {
            var cells = Base().SpellId(1, id).Build((int)BridgeState.Active, 3, PixelProtocol.StatusFlagHasTarget | PixelProtocol.StatusFlagContextValid);
            var frame = PixelProtocol.Decode(cells);
            Assert.NotNull(frame);
            Assert.Equal(id, frame!.SpellId(Slot.Main));
        }
    }

    [Fact]
    public void V5_Corrupt_Extension_Checksum_Is_Rejected()
    {
        var cells = HappyFrame();
        cells[PixelProtocol.RangeCellIndex2] = Color.FromArgb(255, 255, 255);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void V5_Corrupt_Core_Checksum_Is_Rejected()
    {
        var cells = HappyFrame();
        cells[2] = Color.FromArgb(0, 255, 0);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Checksum, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void V5_Torn_Extension_Commit_Is_Rejected()
    {
        var cells = HappyFrame(heartbeat: 5);
        var ext = cells[PixelProtocol.ExtensionCellIndex];
        cells[PixelProtocol.ExtensionCellIndex] = Color.FromArgb(ext.R, ext.G, 17); // commit != heartbeat

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Commit, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void V5_Wrong_Version_Is_Rejected()
    {
        var cells = Base().Build((int)BridgeState.Active, 5, 0);
        cells[PixelProtocol.VersionCellIndex] = Color.FromArgb(4 * 17,
            cells[PixelProtocol.VersionCellIndex].G, 5 * 17);

        Assert.Null(PixelProtocol.Decode(cells));
        Assert.Equal(DecodeFault.Version, PixelProtocol.Diagnose(cells, null));
    }

    [Fact]
    public void V5_Unknown_Hp_Reads_Minus_One()
    {
        var cells = new V5Builder().Build((int)BridgeState.Active, 1, PixelProtocol.StatusFlagContextValid);
        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.Equal(-1, frame!.HpPct);
        Assert.Equal(-1, frame.TargetHpPct);
        Assert.Equal(PlayerCastState.Unknown, frame.Cast);
        Assert.True(frame.ContextValid);
    }

    [Fact]
    public void V5_Reserved_Cast_Nibble_Reads_Unknown()
    {
        var cells = Base().Cast(15).Build((int)BridgeState.Active, 1, PixelProtocol.StatusFlagContextValid);
        var frame = PixelProtocol.Decode(cells);
        Assert.NotNull(frame);
        Assert.Equal(PlayerCastState.Unknown, frame!.Cast);
    }

    [Fact]
    public void V5_Channel_State_Round_Trips()
    {
        var cells = Base().Cast(PixelProtocol.CastStateChanneling)
            .Build((int)BridgeState.Active, 1, PixelProtocol.StatusFlagContextValid);
        Assert.Equal(PlayerCastState.Channeling, PixelProtocol.Decode(cells)!.Cast);
    }

    [Fact]
    public void V4_Frame_Still_Decodes_Through_TrimToV4()
    {
        // A v5-window capture of a v4 strip: 9 real cells then background.
        var v4 = PixelProtocolCompatTests.BuildV4Frame();
        var sampled = new Color[PixelProtocol.CellCount];
        for (var i = 0; i < v4.Length; i++) sampled[i] = v4[i];
        for (var i = v4.Length; i < sampled.Length; i++) sampled[i] = Color.Black;

        Assert.Null(PixelProtocol.Decode(sampled));

        var trimmed = PixelProtocol.TrimToV4(sampled);
        var frame = PixelProtocol.Decode(trimmed);
        Assert.NotNull(frame);
        Assert.Equal(PixelProtocol.SupportedVersionV4, frame!.Version);
        Assert.Equal(PlayerCastState.Unknown, frame.Cast);
        Assert.False(frame.ContextValid);
    }

    [Fact]
    public void Combat_Context_Projects_From_Frame()
    {
        var frame = PixelProtocol.Decode(HappyFrame())!;
        var context = CombatContext.FromFrame(frame);

        Assert.True(context.ContextValid);
        Assert.True(context.HpValid);
        Assert.Equal(73, context.HpPct);
        Assert.Equal(PlayerCastState.Casting, context.Cast);
        Assert.Equal(TriState.No, context.TargetInMelee);
        Assert.Equal(TriState.Yes, context.TargetCasting);
        Assert.Equal(67, context.TargetHpPct);
        Assert.Equal(TriState.Yes, context.SlotBuffActive[0]);
        Assert.True(context.BuffProbeValid);
        Assert.Equal("WARRIOR", context.Class);
    }

    [Fact]
    public void V4_Frame_Yields_All_Unknown_Context()
    {
        var frame = PixelProtocol.Decode(PixelProtocolCompatTests.BuildV4Frame())!;
        var context = CombatContext.FromFrame(frame);

        Assert.False(context.ContextValid);
        Assert.False(context.HpValid);
        Assert.Equal(PlayerCastState.Unknown, context.Cast);
        Assert.Equal(TriState.Unknown, context.TargetInMelee);
        Assert.All(context.SlotRange, r => Assert.Equal(TriState.Unknown, r));
    }
}
