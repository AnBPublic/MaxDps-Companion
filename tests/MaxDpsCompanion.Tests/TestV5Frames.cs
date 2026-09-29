using System.Drawing;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Independent protocol-v5 wire encoder (with the additive v2.3 urgency block) for end-to-end tests. Mirrors
/// Bridge.lua's Update (the same layout PixelProtocolV5Tests pins) so the
/// self-sustain tests exercise the REAL decode path — a test that fabricates
/// a <see cref="BridgeFrame"/> object directly would skip the wire.
/// </summary>
internal sealed class TestV5Frame
{
    private readonly (int R, int G, int B)[] _nibbles = new (int, int, int)[PixelProtocol.CellCount];

    public TestV5Frame()
    {
        _nibbles[0] = (15, 0, 15);
        // UNKNOWN defaults, never "0%" claims for unwritten cells.
        _nibbles[PixelProtocol.CastCellIndex] = (15, 15, 0);
        _nibbles[PixelProtocol.TargetCellIndex] = (0, 15, 2);
        _nibbles[PixelProtocol.VitalsCellIndex] = (0, 0, 0);
    }

    public TestV5Frame Slot(Slot slot, byte vk, int flags = 8)
    {
        _nibbles[(int)slot + 1] = (vk >> 4, vk & 0x0F, flags);
        return this;
    }

    public TestV5Frame SpellId(Slot slot, int id)
    {
        var hi = (id >> 12) & 0x0FFF;
        var lo = id & 0x0FFF;
        var baseCell = PixelProtocol.SpellIdCellBase + (int)slot * 2;
        _nibbles[baseCell] = ((hi >> 8) & 0xF, (hi >> 4) & 0xF, hi & 0xF);
        _nibbles[baseCell + 1] = ((lo >> 8) & 0xF, (lo >> 4) & 0xF, lo & 0xF);
        return this;
    }

    public TestV5Frame Vitals(int hpPct)
    {
        _nibbles[PixelProtocol.VitalsCellIndex] = (hpPct / 16, hpPct % 16, 1);
        return this;
    }

    public TestV5Frame Cast(PlayerCastState state)
    {
        var nibble = state switch
        {
            PlayerCastState.None => PixelProtocol.CastStateNone,
            PlayerCastState.Casting => PixelProtocol.CastStateCasting,
            PlayerCastState.Channeling => PixelProtocol.CastStateChanneling,
            _ => PixelProtocol.CastStateUnknown,
        };
        _nibbles[PixelProtocol.CastCellIndex] = (nibble, 15, 0);
        return this;
    }

    /// <summary>Target cell: R bit0 = in melee, B bit0 = casting, bit2 = interruptible.</summary>
    public TestV5Frame Target(bool inMelee, bool casting, bool interruptible)
    {
        var castFlags = (casting ? 1 : 0) | (interruptible ? 4 : 0) | (casting ? 0 : 2);
        _nibbles[PixelProtocol.TargetCellIndex] = (inMelee ? 1 : 0, 10, castFlags);
        return this;
    }

    public TestV5Frame Ranges(params (Slot Slot, TriState State)[] entries)
    {
        int Code(TriState state) => state switch
        {
            TriState.Yes => PixelProtocol.RangeIn,
            TriState.No => PixelProtocol.RangeOut,
            _ => PixelProtocol.RangeUnknown,
        };

        var values = new int[PixelProtocol.SlotCount];
        foreach (var (slot, state) in entries) values[(int)slot] = Code(state);

        var r30 = values[0] | (values[1] << 2);
        var g30 = values[2] | (values[3] << 2);
        var b30 = values[4] | (values[5] << 2);
        var r31 = values[6] | (values[7] << 2);
        // Preserve a previously set v6 urgency block on cell 31.
        var (_, urgency, source) = _nibbles[PixelProtocol.RangeCellIndex2];
        _nibbles[PixelProtocol.RangeCellIndex] = (r30, g30, b30);
        _nibbles[PixelProtocol.RangeCellIndex2] = (r31, urgency, source);
        return this;
    }

    /// <summary>v6 urgency block: cell31 G + B bit0, cell32 B (stagger).</summary>
    public TestV5Frame Urgency(
        DefensiveUrgency hp,
        DefensiveUrgency stagger = DefensiveUrgency.Unknown,
        bool catalogSource = false)
    {
        var (r31, _, _) = _nibbles[PixelProtocol.RangeCellIndex2];
        _nibbles[PixelProtocol.RangeCellIndex2] = (r31, (int)hp, catalogSource ? 1 : 0);
        var (br, bg, _) = _nibbles[PixelProtocol.BuffCellIndex];
        _nibbles[PixelProtocol.BuffCellIndex] = (br, bg, (int)stagger);
        return this;
    }

    public TestV5Frame ClassSpec(string className, int specId)
    {
        var classId = AbilityCatalog.ClassId(className);
        var (_, _, flags) = _nibbles[PixelProtocol.ClassSpecCellIndex];
        _nibbles[PixelProtocol.ClassSpecCellIndex] = (classId, specId, (classId > 0 ? 1 : 0) | (flags & 2));
        return this;
    }

    /// <summary>
    /// v2.7: set the self-buff bits AND mark the probe block valid (a fixture
    /// that declares buffs declares them known).
    /// </summary>
    public TestV5Frame Buffs(params (Slot Slot, bool Active)[] entries)
    {
        var (r, g, b) = _nibbles[PixelProtocol.BuffCellIndex];
        foreach (var (slot, active) in entries)
        {
            if (!active) continue;
            var index = (int)slot;
            if (index < 4) r |= 1 << index;
            else g |= 1 << (index - 4);
        }
        _nibbles[PixelProtocol.BuffCellIndex] = (r, g, b);
        var (classId, specId, flags) = _nibbles[PixelProtocol.ClassSpecCellIndex];
        _nibbles[PixelProtocol.ClassSpecCellIndex] = (classId, specId, flags | 2);
        return this;
    }

    public Color[] Build(int heartbeat, bool inCombat = true, bool onGcd = false, bool hasTarget = true, int version = 0)
    {
        var flags = (inCombat ? PixelProtocol.StatusFlagInCombat : 0)
            | (onGcd ? PixelProtocol.StatusFlagOnGcd : 0)
            | (hasTarget ? PixelProtocol.StatusFlagHasTarget : 0)
            | PixelProtocol.StatusFlagContextValid;
        _nibbles[PixelProtocol.StatusCellIndex] = ((int)BridgeState.Active, heartbeat, flags);

        var core = 0;
        for (var i = 1; i <= PixelProtocol.StatusCellIndex; i++)
            core += _nibbles[i].R + _nibbles[i].G + _nibbles[i].B;
        _nibbles[PixelProtocol.VersionCellIndex] =
            (version > 0 ? version : PixelProtocol.SupportedVersion, core & 0xF, heartbeat);

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
