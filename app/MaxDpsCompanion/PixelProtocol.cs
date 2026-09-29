namespace MaxDpsCompanion;

internal enum BridgeState { Idle = 0, Active = 1, Paused = 2, NeedTarget = 3, NeedInteract = 4 }

/// <summary>
/// Why a strip sample did not decode (telemetry/diagnostics only; Decode still
/// returns null either way). Classification order mirrors Decode's checks.
/// </summary>
internal enum DecodeFault
{
    None = 0,
    Length,
    Magic,
    Contrast,
    Version,
    State,
    Commit,
    Checksum,
}

// Slot order = the user's icon model: Main (THE rotation — always first)
// + Offensive + Defensive + Consumable + Trinket + Interrupt (situational)
// + Mobility + SelfHeal (companion-only extras resolved from the curated
// knowledge base; never surfaced by MaxDps itself).
internal enum Slot
{
    Main = 0,
    Offensive = 1,
    Defensive = 2,
    Consumable = 3,
    Trinket = 4,
    Interrupt = 5,
    Mobility = 6,
    SelfHeal = 7,
}

internal readonly record struct KeyStroke(byte VirtualKey, bool Shift, bool Ctrl, bool Alt)
{
    public string Describe()
    {
        var prefix = (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "");
        return prefix + KeyNames.Describe(VirtualKey);
    }
}

/// <summary>One decoded reading of the addon's pixel block.</summary>
internal sealed class BridgeFrame
{
    public required BridgeState State { get; init; }
    public required int Heartbeat { get; init; }
    public required int Version { get; init; }
    public required int StatusFlags { get; init; }
    public required KeyStroke?[] Slots { get; init; }

    /// <summary>Protocol v5: spell id per slot (0 = unknown). Empty on v4/v1 frames.</summary>
    public int[] SpellIds { get; init; } = new int[PixelProtocol.SlotCount];

    /// <summary>Player health percent; -1 when UNKNOWN (secret value or no v5 sensors).</summary>
    public int HpPct { get; init; } = -1;

    /// <summary>Player cast/channel state (arg-blind bridge events).</summary>
    public PlayerCastState Cast { get; init; } = PlayerCastState.Unknown;

    /// <summary>Cast remaining band (0..14 = 0..7 s in 0.5 s steps, 15 = unknown).</summary>
    public int CastRemainingBand { get; init; } = 15;

    public TriState TargetCasting { get; init; } = TriState.Unknown;
    public TriState TargetCastInterruptible { get; init; } = TriState.Unknown;
    public TriState TargetInMelee { get; init; } = TriState.Unknown;

    /// <summary>Target health percent; -1 when UNKNOWN.</summary>
    public int TargetHpPct { get; init; } = -1;

    /// <summary>
    /// Additive defensive urgency (cell 31 G, HP curve) staged at the vendor
    /// curve's control points. Unknown on old addons (reserved nibbles = 0)
    /// and when the probe failed.
    /// </summary>
    public DefensiveUrgency DefensiveUrgency { get; init; } = DefensiveUrgency.Unknown;

    /// <summary>Additive stagger-curve urgency for Purifying Brew-class abilities (Unknown when unreadable).</summary>
    public DefensiveUrgency StaggerUrgency { get; init; } = DefensiveUrgency.Unknown;

    /// <summary>True when the Defensive slot was filled from the catalog (MaxDps named no bound defensive).</summary>
    public bool DefensiveCatalogSource { get; init; }

    public TriState[] SlotRange { get; init; } = new TriState[PixelProtocol.SlotCount];

    /// <summary>Per-slot "the suggested spell's own helpful aura is on the player" (Unknown when the bridge could not probe).</summary>
    public TriState[] SlotBuffActive { get; init; } = new TriState[PixelProtocol.SlotCount];

    /// <summary>True when the bridge's self-buff probe block ran without a failed/secret probe (v2.7 additive cell 33 B bit1).</summary>
    public bool BuffProbeValid { get; init; }

    public string? ClassName { get; init; }
    public string? SpecName { get; init; }

    public KeyStroke? this[Slot slot] => Slots[(int)slot];

    /// <summary>v4+: player is in combat (UnitAffectingCombat, NeverSecret).</summary>
    public bool InCombat => (StatusFlags & PixelProtocol.StatusFlagInCombat) != 0;

    /// <summary>v4+: a spell's GCD is currently active (isOnGCD, NeverSecret).</summary>
    public bool OnGcd => (StatusFlags & PixelProtocol.StatusFlagOnGcd) != 0;

    /// <summary>v4+: a valid attackable target exists (wait instead of spamming).</summary>
    public bool HasTarget => (StatusFlags & PixelProtocol.StatusFlagHasTarget) != 0;

    /// <summary>v5: the bridge computed the sensor block this frame (false = all UNKNOWN).</summary>
    public bool ContextValid => (StatusFlags & PixelProtocol.StatusFlagContextValid) != 0;

    /// <summary>The per-slot spell id (0 = unknown).</summary>
    public int SpellId(Slot slot) => SpellIds.Length > (int)slot ? SpellIds[(int)slot] : 0;
}

internal static class PixelProtocol
{
    /// <summary>
    /// Protocol versions.
    ///  v1 = 8 cells (magic + 5 slots + status + version).
    ///  v4 = 9 cells (magic + Main/Off/Def/Cons/Trin/Int + status + version),
    ///       status cell B carries combat/GCD/target flags.
    ///  v5 = 35 cells — the sensor protocol (see the cell map below). The
    ///       defensive-urgency block (cell 31 G/B, cell 32 B) is ADDITIVE on
    ///       top of v5: those nibbles were reserved and always 0 in the
    ///       original v5 encoder, so a v5 companion ignores them (and its
    ///       extension checksum over cells 11-33 already covers them) while a
    ///       v2.3+ companion reads them whenever they are non-zero. The wire
    ///       version nibble therefore STAYS 5, which keeps an updated in-game
    ///       addon working with an older companion exe — the Sep-2026 "updated
    ///       addon + stale exe = nothing fires" outage class.
    ///  v6 = 6 — accepted for forward compatibility if an encoder ever bumps
    ///       the version nibble explicitly; same 35-cell layout.
    ///
    /// The companion decodes v5/v6 (35 cells), v4 (9 cells) and v1 (8 cells);
    /// the addon encodes v5. Same-length misreads are rejected by the version
    /// nibble. A rescue fallback to the legacy windows is applied by the
    /// engine, so an old in-game addon still drives the current companion.
    /// </summary>
    public const int CellCountV1 = 8;
    public const int CellCountV4 = 9;
    public const int CellCount = 35;
    public const int SupportedVersionV1 = 1;
    public const int SupportedVersionV4 = 4;
    public const int SupportedVersion = 5;
    public const int SupportedVersionV6 = 6;
    public const int SlotCountV1 = 5;
    public const int SlotCountV4 = 6;
    public const int SlotCount = (int)Slot.SelfHeal + 1;

    /// <summary>Status-cell B flag bits (v4+; v5 adds bit3).</summary>
    public const int StatusFlagInCombat = 1;
    public const int StatusFlagOnGcd = 2;
    public const int StatusFlagHasTarget = 4;
    public const int StatusFlagContextValid = 8;

    /// <summary>Status cell index per protocol version.</summary>
    public const int StatusCellIndexV1 = 6;
    public const int StatusCellIndexV4 = 7;
    public const int StatusCellIndex = 9;

    /// <summary>Version cell index per protocol version.</summary>
    public const int VersionCellIndexV1 = 7;
    public const int VersionCellIndexV4 = 8;
    public const int VersionCellIndex = 10;

    /// <summary>v5 cell indices.</summary>
    public const int SpellIdCellBase = 11;
    public const int VitalsCellIndex = 27;
    public const int CastCellIndex = 28;
    public const int TargetCellIndex = 29;
    public const int RangeCellIndex = 30;
    public const int RangeCellIndex2 = 31;
    public const int BuffCellIndex = 32;
    public const int ClassSpecCellIndex = 33;
    public const int ExtensionCellIndex = 34;

    /// <summary>v5 sentinel nibble for "value unknown / not applicable".</summary>
    public const int UnknownNibble = 15;

    /// <summary>v5 cast-state nibbles.</summary>
    public const int CastStateNone = 0;
    public const int CastStateCasting = 1;
    public const int CastStateChanneling = 2;
    public const int CastStateUnknown = 15;

    /// <summary>v5 range tri-state values packed 2 bits per slot.</summary>
    public const int RangeUnknown = 0;
    public const int RangeIn = 1;
    public const int RangeOut = 2;

    private const int FlagShift = 1;
    private const int FlagCtrl = 2;
    private const int FlagAlt = 4;
    private const int FlagValid = 8;

    /// <summary>Minimum separation between the magic cell's black and white channels.</summary>
    public const int MinContrast = ColorProfile.LegacyMinContrast;

    /// <summary>
    /// Recognises the magic cell loosely enough to survive the client's gamma,
    /// brightness and contrast sliders, which shift every rendered colour.
    /// </summary>
    public static bool IsMagic(Color color) => IsMagic(color, profile: null);

    public static bool IsMagic(Color color, ColorProfile? profile) =>
        profile is { IsLearned: true }
            ? profile.IsMagic(color)
            : color.R - color.G >= ColorProfile.LegacyMinContrast
                && color.B - color.G >= ColorProfile.LegacyMinContrast;

    /// <summary>PERF: derive the v4 (9-cell) window from an existing v5 capture.</summary>
    public static Color[] TrimToV4(Color[] cells)
    {
        var old = new Color[CellCountV4];
        for (var i = 0; i < CellCountV4 && i < cells.Length; i++) old[i] = cells[i];
        return old;
    }

    /// <summary>PERF: derive the v1 (8-cell) window from an existing capture.</summary>
    public static Color[] TrimToV1(Color[] cells)
    {
        var old = new Color[CellCountV1];
        for (var i = 0; i < CellCountV1 && i < cells.Length; i++) old[i] = cells[i];
        return old;
    }

    public static BridgeFrame? Decode(Color[] cells) => Decode(cells, profile: null);

    public static BridgeFrame? Decode(Color[] cells, ColorProfile? profile)
    {
        // v5 first (current), then v4 (stale addon), then v1 (older stale
        // addon). A v4 addon renders only 9 cells; the extra cells the sampler
        // captured are background, so v5 decode fails on the version nibble
        // and the v4 path takes over.
        if (cells.Length == CellCount) return DecodeV5(cells, profile);
        if (cells.Length == CellCountV4)
            return DecodeCells(cells, StatusCellIndexV4, VersionCellIndexV4, null, SupportedVersionV4, profile);
        if (cells.Length == CellCountV1)
            return DecodeCells(cells, StatusCellIndexV1, VersionCellIndexV1, V1ToV4, SupportedVersionV1, profile);
        return null;
    }

    /// <summary>
    /// Why <see cref="Decode"/> rejected the sample (telemetry/diagnostics).
    /// Runs the exact same checks through the same core as Decode.
    /// </summary>
    public static DecodeFault Diagnose(Color[] cells, ColorProfile? profile)
    {
        if (cells.Length == CellCount)
        {
            DecodeV5(cells, profile, out var fault);
            return fault;
        }
        if (cells.Length == CellCountV4)
        {
            DecodeCells(cells, StatusCellIndexV4, VersionCellIndexV4, null, SupportedVersionV4, profile, out var fault);
            return fault;
        }
        if (cells.Length == CellCountV1)
        {
            DecodeCells(cells, StatusCellIndexV1, VersionCellIndexV1, V1ToV4, SupportedVersionV1, profile, out var fault);
            return fault;
        }
        return DecodeFault.Length;
    }

    /// <summary>
    /// v1 slot → v4 slot remap (Interrupt 3→5, Defensive 4→2, Consumable
    /// 5→3; Main/Offensive stay). v4/v5 decode passes null (= identity).
    /// </summary>
    private static readonly int[] V1ToV4 = [0, 1, 5, 2, 3];

    private static BridgeFrame? DecodeCells(Color[] cells, int statusIndex, int versionIndex, int[]? remap, int version, ColorProfile? profile) =>
        DecodeCells(cells, statusIndex, versionIndex, remap, version, profile, out _);

    private static BridgeFrame? DecodeCells(Color[] cells, int statusIndex, int versionIndex, int[]? remap, int version, ColorProfile? profile, out DecodeFault fault)
    {
        fault = DecodeFault.None;
        if (!IsMagic(cells[0], profile)) { fault = DecodeFault.Magic; return null; }

        int black, white;
        if (profile is { IsLearned: true })
        {
            black = -1;
            white = -1;
        }
        else
        {
            var levels = (profile ?? new ColorProfile()).LegacyLevels(cells[0]);
            black = levels.Black;
            white = levels.White;
            if (white - black < ColorProfile.LegacyMinContrast) { fault = DecodeFault.Contrast; return null; }
        }

        var (state, heartbeat, statusFlags) = Nibbles(cells[statusIndex], black, white, profile);
        var (ver, checksum, commit) = Nibbles(cells[versionIndex], black, white, profile);
        if (ver != version) { fault = DecodeFault.Version; return null; }
        if (state is < 0 or > 4) { fault = DecodeFault.State; return null; }
        if (commit != heartbeat) { fault = DecodeFault.Commit; return null; }

        var sum = 0;
        for (var i = 1; i <= statusIndex; i++)
        {
            var (r, g, b) = Nibbles(cells[i], black, white, profile);
            sum += r + g + b;
        }
        if ((sum & 0xF) != checksum) { fault = DecodeFault.Checksum; return null; }

        // Always expose the full v5 slot array; v4/v1 frames leave the new
        // slots null and their spell ids 0 so priority/settings indexing never
        // shift between protocol versions.
        var slots = new KeyStroke?[SlotCount];
        var count = remap?.Length ?? SlotCountV4;
        for (var i = 0; i < count; i++)
        {
            var (hi, lo, flags) = Nibbles(cells[i + 1], black, white, profile);
            if ((flags & FlagValid) == 0) continue;

            slots[remap?[i] ?? i] = new KeyStroke(
                (byte)((hi << 4) | lo),
                (flags & FlagShift) != 0,
                (flags & FlagCtrl) != 0,
                (flags & FlagAlt) != 0);
        }

        return new BridgeFrame
        {
            State = (BridgeState)state,
            Heartbeat = heartbeat,
            Version = ver,
            StatusFlags = statusFlags,
            Slots = slots,
            SpellIds = new int[SlotCount],
            HpPct = -1,
            Cast = PlayerCastState.Unknown,
            TargetCasting = TriState.Unknown,
            TargetCastInterruptible = TriState.Unknown,
            TargetInMelee = TriState.Unknown,
            TargetHpPct = -1,
            SlotRange = new TriState[SlotCount],
            SlotBuffActive = new TriState[SlotCount],
        };
    }

    /// <summary>v5 decode: core cells identical to v4 plus the sensor block.</summary>
    private static BridgeFrame? DecodeV5(Color[] cells, ColorProfile? profile) =>
        DecodeV5(cells, profile, out _);

    private static BridgeFrame? DecodeV5(Color[] cells, ColorProfile? profile, out DecodeFault fault)
    {
        fault = DecodeFault.None;
        if (!IsMagic(cells[0], profile)) { fault = DecodeFault.Magic; return null; }

        int black, white;
        if (profile is { IsLearned: true })
        {
            black = -1;
            white = -1;
        }
        else
        {
            var levels = (profile ?? new ColorProfile()).LegacyLevels(cells[0]);
            black = levels.Black;
            white = levels.White;
            if (white - black < ColorProfile.LegacyMinContrast) { fault = DecodeFault.Contrast; return null; }
        }

        (int R, int G, int B) N(int index) => Nibbles(cells[index], black, white, profile);

        var (state, heartbeat, statusFlags) = N(StatusCellIndex);
        var (ver, checksum, commit) = N(VersionCellIndex);
        // v5 is what the addon encodes; v6 is accepted for forward
        // compatibility (same 35-cell layout, additive nibbles).
        if (ver != SupportedVersion && ver != SupportedVersionV6) { fault = DecodeFault.Version; return null; }
        if (state is < 0 or > 4) { fault = DecodeFault.State; return null; }
        if (commit != heartbeat) { fault = DecodeFault.Commit; return null; }

        // Core checksum over cells 1..status (identical formula to v4).
        var sum = 0;
        for (var i = 1; i <= StatusCellIndex; i++)
        {
            var (r, g, b) = N(i);
            sum += r + g + b;
        }
        if ((sum & 0xF) != checksum) { fault = DecodeFault.Checksum; return null; }

        // Extension checksum over the sensor block (11..33) + second commit.
        var extSum = 0;
        for (var i = SpellIdCellBase; i <= ClassSpecCellIndex; i++)
        {
            var (r, g, b) = N(i);
            extSum += r + g + b;
        }
        var (_, extChecksum, extCommit) = N(ExtensionCellIndex);
        if (extCommit != heartbeat) { fault = DecodeFault.Commit; return null; }
        if ((extSum & 0xF) != extChecksum) { fault = DecodeFault.Checksum; return null; }

        var slots = new KeyStroke?[SlotCount];
        var spellIds = new int[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var (hi, lo, flags) = N(i + 1);
            if ((flags & FlagValid) != 0)
            {
                slots[i] = new KeyStroke(
                    (byte)((hi << 4) | lo),
                    (flags & FlagShift) != 0,
                    (flags & FlagCtrl) != 0,
                    (flags & FlagAlt) != 0);
            }

            // Six nibbles per spell id: cell0 = bits 23..12, cell1 = bits 11..0.
            var (r0, g0, b0) = N(SpellIdCellBase + i * 2);
            var (r1, g1, b1) = N(SpellIdCellBase + i * 2 + 1);
            var spellId = (r0 << 20) | (g0 << 16) | (b0 << 12) | (r1 << 8) | (g1 << 4) | b1;
            spellIds[i] = spellId;
        }

        var (hpHi, hpLo, vitalsFlags) = N(VitalsCellIndex);
        var hpValid = (vitalsFlags & 1) != 0 && hpHi <= 6;
        var hpPct = hpValid ? hpHi * 16 + hpLo : -1;

        var (castState, castBand, castFlagsRaw) = N(CastCellIndex);
        var cast = castState switch
        {
            CastStateNone => PlayerCastState.None,
            CastStateCasting => PlayerCastState.Casting,
            CastStateChanneling => PlayerCastState.Channeling,
            _ => PlayerCastState.Unknown,
        };

        var (targetFlags, targetHpBand, targetCastFlags) = N(TargetCellIndex);
        // Target cell R: bit0 = in melee, bit1 = melee unknown.
        var melee = (targetFlags & 1) != 0
            ? TriState.Yes
            : (targetFlags & 2) != 0
                ? TriState.Unknown
                : TriState.No;
        // Target cell B: bit0 = casting, bit1 = cast state unknown,
        // bit2 = interruptible, bit3 = not interruptible.
        var targetCasting = (targetCastFlags & 1) != 0
            ? TriState.Yes
            : (targetCastFlags & 2) != 0
                ? TriState.Unknown
                : TriState.No;
        var targetInterruptible = (targetCastFlags & 4) != 0
            ? TriState.Yes
            : (targetCastFlags & 8) != 0
                ? TriState.No
                : TriState.Unknown;
        var targetHpPct = targetHpBand <= 14
            ? (int)Math.Round(targetHpBand * 100.0 / 15.0)
            : -1;

        // Range cells: cell30 = slots 1-4 (R=s1|s2, G=s3|s4, B=s5|s6),
        // cell31 = slots 7-8 in R (2 bits per slot); cell31 G carries the
        // ADDITIVE defensive urgency (HP curve), cell31 B bit0 the catalog
        // gap-fill source flag. Original v5 encoders left these reserved
        // nibbles at 0, which reads as UNKNOWN — the same semantic as "no
        // reading", so decoding them unconditionally is safe for old addons
        // AND old companions (they ignore reserved nibbles and their extension
        // checksum already covered them).
        var range = new TriState[SlotCount];
        var (r30, g30, b30) = N(RangeCellIndex);
        var (r31, urgencyNibble, defensiveSourceBits) = N(RangeCellIndex2);
        range[0] = DecodeRange((r30 >> 0) & 3);
        range[1] = DecodeRange((r30 >> 2) & 3);
        range[2] = DecodeRange((g30 >> 0) & 3);
        range[3] = DecodeRange((g30 >> 2) & 3);
        range[4] = DecodeRange((b30 >> 0) & 3);
        range[5] = DecodeRange((b30 >> 2) & 3);
        range[6] = DecodeRange((r31 >> 0) & 3);
        range[7] = DecodeRange((r31 >> 2) & 3);

        // v2.7 cell33 B bit1: the bridge ran the self-buff probe block and no
        // probe failed. Pre-2.7 encoders leave it 0, so every buff bit decodes
        // as UNKNOWN (never a silent "buff absent"); the policy keeps its
        // documented fail-open for Unknown, so legacy addons behave exactly as
        // before while current ones get a real tri-state.
        var (buffR, buffG, staggerNibble) = N(BuffCellIndex);
        var (classId, specId, classFlags) = N(ClassSpecCellIndex);
        var buffProbeValid = (classFlags & 2) != 0;
        var buffs = new TriState[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var active = i < 4 ? ((buffR >> i) & 1) != 0 : ((buffG >> (i - 4)) & 1) != 0;
            buffs[i] = !buffProbeValid ? TriState.Unknown : active ? TriState.Yes : TriState.No;
        }

        // Additive urgency block. Out-of-range nibbles (5-15) degrade to
        // UNKNOWN, so a corrupted or reserved value can never read as
        // White/Red; zero is UNKNOWN (old addons write zero).
        var urgency = DecodeUrgency(urgencyNibble);
        var staggerUrgency = DecodeUrgency(staggerNibble);
        var defensiveCatalogSource = (defensiveSourceBits & 1) != 0;

        var className = (classFlags & 1) != 0 ? AbilityCatalog.ClassName(classId) : null;
        var specName = className is not null ? AbilityCatalog.SpecName(className, specId) : null;

        return new BridgeFrame
        {
            State = (BridgeState)state,
            Heartbeat = heartbeat,
            Version = ver,
            StatusFlags = statusFlags,
            Slots = slots,
            SpellIds = spellIds,
            HpPct = hpPct,
            Cast = cast,
            CastRemainingBand = castBand,
            TargetCasting = targetCasting,
            TargetCastInterruptible = targetInterruptible,
            TargetInMelee = melee,
            TargetHpPct = targetHpPct,
            SlotRange = range,
            SlotBuffActive = buffs,
            BuffProbeValid = buffProbeValid,
            ClassName = className,
            SpecName = specName,
            DefensiveUrgency = urgency,
            StaggerUrgency = staggerUrgency,
            DefensiveCatalogSource = defensiveCatalogSource,
        };
    }

    /// <summary>Additive urgency nibble: 1-4 are the staged vendor values, everything else (incl. reserved 0) is UNKNOWN.</summary>
    private static DefensiveUrgency DecodeUrgency(int nibble) => nibble switch
    {
        1 => DefensiveUrgency.White,
        2 => DefensiveUrgency.Yellow,
        3 => DefensiveUrgency.Orange,
        4 => DefensiveUrgency.Red,
        _ => DefensiveUrgency.Unknown,
    };

    private static TriState DecodeRange(int value) => value switch
    {
        RangeIn => TriState.Yes,
        RangeOut => TriState.No,
        _ => TriState.Unknown,
    };

    /// <summary>
    /// Lightweight status-state probe for the calibrate pattern: decodes only
    /// the state nibble (R channel) of the status cell. Callers pass the cell
    /// they sampled as "status" — see ColorLearner.Classify, which tries v5,
    /// v4 and v1 windows (stale in-game addon).
    /// </summary>
    public static bool IsPausedStatus(Color status, Color magic, ColorProfile? profile)
    {
        int black, white;
        if (profile is { IsLearned: true })
        {
            black = -1;
            white = -1;
        }
        else
        {
            var levels = (profile ?? new ColorProfile()).LegacyLevels(magic);
            black = levels.Black;
            white = levels.White;
            if (white - black < ColorProfile.LegacyMinContrast) return false;
        }
        return Nibble(status.R, black, white, 0, profile) == (int)BridgeState.Paused;
    }

    private static (int R, int G, int B) Nibbles(Color color, int black, int white, ColorProfile? profile) =>
        (Nibble(color.R, black, white, 0, profile),
         Nibble(color.G, black, white, 1, profile),
         Nibble(color.B, black, white, 2, profile));

    private static int Nibble(byte channel, int black, int white, int axis, ColorProfile? profile)
    {
        if (profile is { IsLearned: true }) return profile.Nibble(channel, axis);
        var scaled = (channel - black) * 15.0 / (white - black);
        return Math.Clamp((int)Math.Round(scaled), 0, 15);
    }
}
