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
    /// <summary>
    /// A stroke names a real key. The decoder never constructs a
    /// <see cref="KeyStroke"/> from a wire entry whose VALID flag is clear, so
    /// for blocks that store only the stroke (Ext5's CC entry) this is the
    /// same predicate as that flag: virtual key 0 is not a keystroke.
    /// </summary>
    public bool Valid => VirtualKey != 0;

    public string Describe()
    {
        var prefix = (Ctrl ? "Ctrl+" : "") + (Alt ? "Alt+" : "") + (Shift ? "Shift+" : "");
        return prefix + KeyNames.Describe(VirtualKey);
    }
}

/// <summary>
/// Ext2 (v3.0.0) second self-sustain candidate: the next distinct ready+bound
/// entry the bridge encoded into cells 36-38, with its own range tri-state from
/// cell 28 B bits0-1. Null when the block is absent (bit2=0), inactive, or the
/// cell-39 checksum failed.
/// </summary>
internal readonly record struct SelfHeal2Slot(KeyStroke Stroke, int SpellId, TriState Range);

/// <summary>
/// Ext3 (v3.5) block decoded from cells 40-42: a 14-bit mask (bits 0-11 in
/// cell 40 R/G/B, bits 12-13 in cell 41 R), a 4-bit epoch (cell 41 G) and a
/// 4-bit blocked nibble (cell 41 B). Null when the block is absent (cell 28 B
/// bit2 clear), the capture is not 43 cells wide, or the cell-42 checksum /
/// commit failed — mirroring Ext2's "drop only this block" contract.
/// </summary>
internal readonly record struct Ext3Block(int Mask, int Epoch, int Blocked);

/// <summary>
/// Ext5 (4.6.0 "Cadence") block decoded from cells 51-57: the interrupt
/// insurance / GCD landing / opener-release inputs. Every field is a plain
/// 0-15 wire value with its own UNKNOWN code, so a secret-degraded or
/// pre-Ext5 probe reads as "no reading" instead of a fabricated one.
/// <see cref="Unknown"/> is the all-zero block; <see cref="CcKey"/> is the
/// full CC entry (keystroke + spell id, Ext4's proven packing) because a
/// synthesized press needs a keystroke.
/// </summary>
internal sealed record Ext5Block(
    int Rank, int Delve, int Interruptible, int CastKind,
    int CastAgeBucket, KeyStroke CcKey, int CcSpellId, int GcdBucket)
{
    /// <summary>Every field UNKNOWN (the all-zero wire block).</summary>
    public static readonly Ext5Block Unknown = new(0, 0, 0, 0, 0, default, 0, 0);

    /// <summary>Rank 6 (worldboss) or 7 (elite + UnitLevel -1 = elite-skull) only.</summary>
    public bool BossGrade => Rank is 6 or 7;

    /// <summary>
    /// Lower bound of the target's cast age in ms, or -1 when UNKNOWN/none.
    /// 0 = unknown/none, 1..14 = [(b-1)*250, b*250), 14 saturates, 15 reserved.
    /// </summary>
    public int CastAgeMinMs => CastAgeBucket is <= 0 or >= 15
        ? -1
        : (CastAgeBucket - 1) * PixelProtocol.Ext5CastAgeStepMs;

    /// <summary>
    /// Lower bound of the remaining GCD in ms (-1 UNKNOWN). 0 = unknown,
    /// 1 = not on GCD (0 ms left), 2..15 = [(b-2)*100, (b-1)*100), 15 saturates.
    /// </summary>
    public int GcdRemainMinMs => GcdBucket switch
    {
        <= 0 => -1,
        1 => 0,
        _ => (GcdBucket - 2) * PixelProtocol.Ext5GcdStepMs,
    };

    /// <summary>A usable CC press: a real key AND a spell id (0 id is forced with an invalid key).</summary>
    public bool CcValid => CcKey.Valid && CcSpellId > 0;
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

    /// <summary>
    /// Ext2 (v3.0.0): the HP-curve reading decoded from cell 35 is usable
    /// (bit2 EXT2 present AND bit3 HP CURVE ACTIVE, and the R+G nibble sum is
    /// in the accepted 14..16 band). False on older 35-cell frames.
    /// </summary>
    public bool HpCurveValid { get; init; }

    /// <summary>Ext2: HP-curve band (cell 35 R nibble, 0..15); -1 when not valid.</summary>
    public int HpCurveBand { get; init; } = -1;

    /// <summary>Ext2: the second self-sustain candidate (cells 36-38); null when absent/invalid.</summary>
    public SelfHeal2Slot? SelfHeal2 { get; init; }

    /// <summary>Ext3 (v3.5): cell 28 B bit2 says a 43-cell Ext3 block is present.</summary>
    public bool Ext3Present { get; init; }

    /// <summary>Ext3 (v3.5): decoded cells 40-42; null when absent or the cell-42 checksum/commit failed.</summary>
    public Ext3Block? Ext3 { get; init; }

    /// <summary>
    /// The user is typing in chat/an edit box (v4.0.3 cell 41 R bit2). True only
    /// when the Ext3 block is valid AND bit2 is set; an invalid/absent Ext3 block
    /// reads as false (typing not detected), never as a veto.
    /// </summary>
    public bool TypingActive { get; init; }

    /// <summary>Ext4 (v4.0.3): cell 28 B bit3 says a 51-cell Ext4 block is present.</summary>
    public bool Ext4Present { get; init; }

    /// <summary>Ext4 (v4.0.3): decoded cells 43-50; null when absent or the cell-50 checksum/commit failed.</summary>
    public Ext4Frame? Ext4 { get; init; }

    /// <summary>
    /// Ext5 (4.6.0): cell 50 R bit0 says a 58-cell Ext5 block is present
    /// (AND the Ext4 presence bit is set AND the capture is >= 58 cells).
    /// </summary>
    public bool Ext5Present { get; init; }

    /// <summary>
    /// Ext5 (4.6.0): decoded cells 51-57; null when absent, short, or the
    /// cell-57 checksum/commit failed. A failure drops ONLY this block.
    /// </summary>
    public Ext5Block? Ext5 { get; init; }

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

/// <summary>
/// Ext4 (v4.0.3) hotbar entry decoded from one 3-cell slot pair: the keystroke,
/// the 22-bit spell/item id, and the is-item / ready bits. Pad entries (flags
/// bit3 "valid" clear) are never surfaced as an <see cref="Ext4Entry"/>.
///
/// Wave B: these two wire-model types moved here from the Windows
/// HotbarMapAssembler.cs so the decoder and its model live in ONE assembly
/// (the Windows assembler consumes them through the project reference).
/// </summary>
internal readonly record struct Ext4Entry(KeyStroke Stroke, int Id, bool IsItem, bool Ready);

/// <summary>
/// One Ext4 frame (one strip capture): cells 43 (header: gen/seq/flags) plus
/// the up to two entry pairs (cells 44-49). <see cref="Entries"/> holds only the
/// non-pad entries in cell order (0-2 items); a frame whose cell-50 checksum or
/// heartbeat failed never produces an Ext4Frame at all (the decoder returns
/// null), so every Ext4Frame here is already integrity-checked.
/// </summary>
internal sealed class Ext4Frame
{
    /// <summary>4-bit rebuild generation (cell 43 R).</summary>
    public required int Gen { get; init; }

    /// <summary>4-bit sequence within the cycle (cell 43 G), 0..15.</summary>
    public required int Seq { get; init; }

    /// <summary>Cell 43 B bit0: the final frame of a cycle.</summary>
    public required bool Last { get; init; }

    /// <summary>Cell 43 B bit1: a seq-0 LAST frame that carries no entries.</summary>
    public required bool Empty { get; init; }

    /// <summary>Non-pad entries only, in cell order (0, 1 or 2).</summary>
    public required IReadOnlyList<Ext4Entry> Entries { get; init; }
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
    /// The companion decodes the v5/v6 layout at 35 (core), 40 (Ext2), 43
    /// (Ext3), 51 (Ext4) or 58 (Ext5) cells, v4 (9 cells) and v1 (8 cells); the
    /// addon encodes the widest layout it ships. Same-length misreads are
    /// rejected by the version nibble. A rescue fallback to the legacy windows
    /// is applied by the engine, so an old in-game addon still drives the
    /// current companion.
    /// </summary>
    public const int CellCountV1 = 8;
    public const int CellCountV4 = 9;

    /// <summary>v5 core width (35 cells). The addon still renders this layout.</summary>
    public const int CellCount = 35;

    /// <summary>
    /// Ext2 (v3.0.0): the frame becomes 40 cells wide. Cells 0-34 are the
    /// unchanged v5 layout; 35-39 are the HP-curve + SelfHeal2 block. The
    /// companion captures 40 cells so it can read the extension, while a
    /// 35-cell stale addon still decodes (cell 33 B bit2 = 0).
    /// </summary>
    public const int CellCountExt2 = 40;

    /// <summary>
    /// Ext3 (v3.5): the frame becomes 43 cells wide. Cells 0-39 are the
    /// unchanged v5 core + Ext2 layout; 40-42 are the additive mask/epoch/
    /// blocked block. Gated on cell 28 B bit2, so a 40-cell Ext2 or 35-cell
    /// core addon still decodes with cells 40-42 ignored.
    /// </summary>
    public const int CellCountExt3 = 43;

    /// <summary>
    /// Ext4 (v4.0.3): the frame becomes 51 cells wide. Cells 0-42 are the
    /// unchanged v5 core + Ext2 + Ext3 layout; 43-50 are the additive hotbar
    /// map stream (header + two entry pairs + commit). Gated on cell 28 B
    /// bit3, so a 43-cell Ext3 (or narrower) addon still decodes with cells
    /// 43-50 ignored.
    /// </summary>
    public const int CellCountExt4 = 51;

    /// <summary>
    /// Ext5 (4.6.0 "Cadence"): the frame becomes 58 cells wide. Cells 0-50 are
    /// the unchanged v5 core + Ext2 + Ext3 + Ext4 layout; 51-57 are the
    /// insurance / landing block (rank, delve, interruptible, cast age + kind,
    /// GCD bucket, CC key + spell id, commit). Presence is cell 50 R bit0 —
    /// a reserved-zero channel that sits outside every checksum — AND the Ext4
    /// presence bit AND a >= 58-cell capture, so a pre-Ext5 addon (51 cells)
    /// decodes byte-identically with cells 51-57 never read.
    /// </summary>
    public const int CellCountExt5 = 58;

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

    /// <summary>Ext2 cell indices (only present in a 40-cell capture).</summary>
    public const int HpCurveCellIndex = 35;
    public const int SelfHeal2CellIndex = 36;
    public const int SelfHeal2SpellIdCellIndex = 37;
    public const int SelfHeal2CommitCellIndex = 39;

    /// <summary>Ext3 cell indices (only present in a 43-cell capture).</summary>
    public const int Ext3MaskCellIndex = 40;
    public const int Ext3FlagsCellIndex = 41;
    public const int Ext3CommitCellIndex = 42;

    /// <summary>Ext3 mask width: bits 0-11 in cell 40, bits 12-13 in cell 41 R.</summary>
    public const int Ext3MaskBitCount = 14;

    /// <summary>
    /// Cell 41 R bit2 (v4.0.3): the addon reports chat/edit-box focus. Read only
    /// when the Ext3 block is valid; the Ext3 mask keeps it out of the mask with
    /// <c>&amp; 3</c> and the cell-42 checksum still sums the whole nibble.
    /// </summary>
    public const int Ext3FlagTyping = 4;

    /// <summary>Ext4 cell indices (only present in a 51-cell capture).</summary>
    public const int Ext4HeaderCellIndex = 43;
    public const int Ext4EntryACellIndex = 44;
    public const int Ext4EntryBCellIndex = 47;
    public const int Ext4CommitCellIndex = 50;

    /// <summary>Entry pairs per Ext4 frame (cell 44-46 = A, 47-49 = B).</summary>
    public const int Ext4EntryCount = 2;

    /// <summary>Cells per Ext4 entry.</summary>
    public const int Ext4EntryCells = 3;

    /// <summary>Ext4 id width (bits 21-0); the encoder skips ids &gt;= 2^22.</summary>
    public const int Ext4IdBitCount = 22;

    /// <summary>
    /// Ext5 cell indices (only present in a 58-cell capture). Cell 50 keeps
    /// Ext4's checksum (G) and heartbeat commit (B); its reserved R channel
    /// carries the Ext5 presence bit.
    /// </summary>
    public const int Ext5PresenceCellIndex = Ext4CommitCellIndex;
    public const int Ext5PresenceBit = 1;
    public const int Ext5RankCellIndex = 51;
    public const int Ext5CastCellIndex = 52;
    public const int Ext5GcdCellIndex = 53;
    public const int Ext5CcKeyCellIndex = 54;
    public const int Ext5CcSpellIdCellIndex = 55;
    public const int Ext5CommitCellIndex = 57;

    /// <summary>Cast-age bucket width in ms (bucket 1 starts at 0, bucket 14 saturates at 3250).</summary>
    public const int Ext5CastAgeStepMs = 250;

    /// <summary>GCD-remaining bucket width in ms (bucket 2 starts at 0, bucket 15 saturates at 1300).</summary>
    public const int Ext5GcdStepMs = 100;

    /// <summary>
    /// Cell 28 B bit2: the Ext3 block is present. Bits 0-1 stay the SelfHeal2
    /// range tri-state; every reader masks cell 28 B with <c>&amp; 3</c> so this
    /// bit can never bleed into the range value.
    /// </summary>
    public const int CastFlagExt3Present = 4;

    /// <summary>
    /// Cell 28 B bit3 (v4.0.3): the Ext4 hotbar-map block is present. Bits 0-1
    /// stay the SelfHeal2 range tri-state (masked <c>&amp; 3</c>) and bit2 stays
    /// Ext3; the Ext3/torn-less cell-28 write is covered by the cell-34
    /// extension checksum, so an old companion ignores it.
    /// </summary>
    public const int CastFlagExt4Present = 8;

    /// <summary>Cell 33 B bits (v5 class/spec + v2.7 buff probe + Ext2).</summary>
    public const int ClassFlagClassSpecValid = 1;
    public const int ClassFlagBuffProbeValid = 2;
    public const int ClassFlagExt2Present = 4;
    public const int ClassFlagHpCurveActive = 8;

    /// <summary>
    /// True for any capture width the v5 decoder understands: 58 (Ext5), 51
    /// (Ext4), 43 (Ext3), 40 (Ext2) or 35 (v5 core). Decode reads the widest
    /// block the capture can hold and falls back to core semantics when the
    /// presence bits are clear, so a stale addon still decodes.
    /// </summary>
    public static bool IsV5Length(int length) =>
        length is CellCount or CellCountExt2 or CellCountExt3 or CellCountExt4 or CellCountExt5;

    /// <summary>
    /// The v5-family widths every host tries, widest first. ONE ladder so the
    /// Windows sampler/locator, the macOS sampler/scanner and the colour
    /// learner cannot drift apart again (the Mac once pinned itself to the
    /// locator's first entry and made Ext4 unreachable). Reading a narrower
    /// addon at a wider width is safe: the extensions are gated on their own
    /// presence bits and the tail reads as background.
    /// </summary>
    public static readonly int[] V5Widths =
    {
        CellCountExt5,
        CellCountExt4,
        CellCountExt3,
        CellCountExt2,
        CellCount,
    };

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
        if (IsV5Length(cells.Length)) return DecodeV5(cells, profile);
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
        if (IsV5Length(cells.Length))
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

        // Ext2 block (v3.0.0): cells 35-39. A 35-cell capture has no such
        // cells, so it is always absent. bit2 says the block exists; bit3 says
        // cell 35 holds a live HP curve. The cell-39 checksum covers ONLY cells
        // 36-38: a failure drops only SelfHeal2, never the whole frame or the
        // curve. The curve itself has no checksum (Lua cannot read it), so its
        // validity is the R+G nibble sum being inside the accepted band.
        var ext2Present = (classFlags & ClassFlagExt2Present) != 0 && cells.Length >= CellCountExt2;
        var hpCurveValid = false;
        var hpCurveBand = -1;
        SelfHeal2Slot? selfHeal2 = null;
        if (ext2Present)
        {
            var (curveR, curveG, _) = N(HpCurveCellIndex);
            if ((classFlags & ClassFlagHpCurveActive) != 0
                && curveR + curveG is >= 14 and <= 16)
            {
                hpCurveValid = true;
                hpCurveBand = curveR;
            }

            var heal2Sum = 0;
            for (var i = SelfHeal2CellIndex; i <= SelfHeal2SpellIdCellIndex + 1; i++)
            {
                var (r, g, b) = N(i);
                heal2Sum += r + g + b;
            }
            var (_, heal2Checksum, heal2Commit) = N(SelfHeal2CommitCellIndex);
            if (heal2Commit == heartbeat && (heal2Sum & 0xF) == heal2Checksum)
            {
                var (h2hi, h2lo, h2flags) = N(SelfHeal2CellIndex);
                if ((h2flags & FlagValid) != 0)
                {
                    var (s0, s1, s2) = N(SelfHeal2SpellIdCellIndex);
                    var (s3, s4, s5) = N(SelfHeal2SpellIdCellIndex + 1);
                    var heal2SpellId = (s0 << 20) | (s1 << 16) | (s2 << 12) | (s3 << 8) | (s4 << 4) | s5;
                    // Cell 28 B bits0-1 carry the SelfHeal2 range tri-state; the
                    // mask keeps bit2 (Ext3 presence) from bleeding into it.
                    selfHeal2 = new SelfHeal2Slot(
                        new KeyStroke(
                            (byte)((h2hi << 4) | h2lo),
                            (h2flags & FlagShift) != 0,
                            (h2flags & FlagCtrl) != 0,
                            (h2flags & FlagAlt) != 0),
                        heal2SpellId,
                        DecodeRange(castFlagsRaw & 3));
                }
            }
        }

        // Ext3 block (v3.5): cells 40-42. Gated on cell 28 B bit2 AND a 43-cell
        // capture, so a 40-cell Ext2 (or 35-cell v5) addon decodes exactly as
        // before with cells 40-42 ignored. Cell 42 G covers cells 40-41 only; a
        // failure (or a torn commit) drops ONLY the Ext3 values, never the
        // frame, the v5 core or Ext2. Cell 42 R is reserved/ignored — like
        // cells 34 R and 39 R, the decoder never asserts it is zero.
        var ext3Present = (castFlagsRaw & CastFlagExt3Present) != 0 && cells.Length >= CellCountExt3;
        Ext3Block? ext3 = null;
        var typingActive = false;
        if (ext3Present)
        {
            var (m0, m1, m2) = N(Ext3MaskCellIndex);
            var (mHi, epoch, blocked) = N(Ext3FlagsCellIndex);
            var ext3Sum = m0 + m1 + m2 + mHi + epoch + blocked;
            var (_, ext3Checksum, ext3Commit) = N(Ext3CommitCellIndex);
            if (ext3Commit == heartbeat && (ext3Sum & 0xF) == ext3Checksum)
            {
                ext3 = new Ext3Block(
                    m0 | (m1 << 4) | (m2 << 8) | ((mHi & 3) << 12),
                    epoch,
                    blocked);
                // v4.0.3: cell 41 R bit2 is the typing veto. Only a valid Ext3
                // block can carry it; the mask above keeps it out of the mask.
                typingActive = (mHi & Ext3FlagTyping) != 0;
            }
        }

        // Ext4 block (v4.0.3): cells 43-50. Gated on cell 28 B bit3 AND a 51-cell
        // capture, so a 43-cell Ext3 (or narrower) addon decodes exactly as
        // before with cells 43-50 ignored. Cell 50 covers cells 43-49; a
        // checksum failure or a torn commit (heartbeat mismatch) drops ONLY this
        // frame (Ext4 = null), never the core, Ext2 or Ext3. Pad entries (cell
        // flags bit3 clear) are skipped, so Entries holds 0-2 live entries.
        var ext4Present = (castFlagsRaw & CastFlagExt4Present) != 0 && cells.Length >= CellCountExt4;
        Ext4Frame? ext4 = null;
        if (ext4Present)
        {
            var (gen, seq, hdrFlags) = N(Ext4HeaderCellIndex);
            var ext4Sum = 0;
            for (var i = Ext4HeaderCellIndex; i < Ext4CommitCellIndex; i++)
            {
                var (r, g, b) = N(i);
                ext4Sum += r + g + b;
            }
            var (_, ext4Checksum, ext4Commit) = N(Ext4CommitCellIndex);
            if (ext4Commit == heartbeat && (ext4Sum & 0xF) == ext4Checksum)
            {
                var entries = new List<Ext4Entry>(Ext4EntryCount);
                for (var e = 0; e < Ext4EntryCount; e++)
                {
                    var cell = Ext4EntryACellIndex + e * Ext4EntryCells;
                    var (vkHi, vkLo, keyFlags) = N(cell);
                    if ((keyFlags & FlagValid) == 0) continue;   // pad
                    var (idTop, id19_16, id15_12) = N(cell + 1);
                    var (id11_8, id7_4, id3_0) = N(cell + 2);
                    var id = ((idTop & 3) << 20) | (id19_16 << 16) | (id15_12 << 12)
                        | (id11_8 << 8) | (id7_4 << 4) | id3_0;
                    entries.Add(new Ext4Entry(
                        new KeyStroke(
                            (byte)((vkHi << 4) | vkLo),
                            (keyFlags & FlagShift) != 0,
                            (keyFlags & FlagCtrl) != 0,
                            (keyFlags & FlagAlt) != 0),
                        id,
                        (idTop & 8) != 0,
                        (idTop & 4) != 0));
                }
                ext4 = new Ext4Frame
                {
                    Gen = gen,
                    Seq = seq,
                    Last = (hdrFlags & 1) != 0,
                    Empty = (hdrFlags & 2) != 0,
                    Entries = entries,
                };
            }
        }

        // Ext5 block (4.6.0): cells 51-57. Presence is cell 50 R bit0 — a
        // reserved-zero channel outside every checksum — AND the Ext4 presence
        // bit AND a 58-cell capture. Cell 57 G covers cells 51-56 and its B is
        // the heartbeat commit; a checksum failure or a torn commit drops ONLY
        // Ext5, never Ext4, Ext3, Ext2 or the v5 core. Reading cell 50 R here
        // (instead of reusing the Ext4 commit tuple) keeps the Ext4 decode
        // byte-identical, which is the frozen contract.
        var ext5Present = ext4Present
            && cells.Length >= CellCountExt5
            && (N(Ext5PresenceCellIndex).R & Ext5PresenceBit) != 0;
        Ext5Block? ext5 = null;
        if (ext5Present)
        {
            var (rankBits, delveBits, _) = N(Ext5RankCellIndex);
            var (interruptibleBits, castAgeBucket, castKindBits) = N(Ext5CastCellIndex);
            var (gcdBucket, _, _) = N(Ext5GcdCellIndex);
            var (ccVkHi, ccVkLo, ccFlags) = N(Ext5CcKeyCellIndex);
            var (ccIdTop, ccId19_16, ccId15_12) = N(Ext5CcSpellIdCellIndex);
            var (ccId11_8, ccId7_4, ccId3_0) = N(Ext5CcSpellIdCellIndex + 1);

            var ext5Sum = 0;
            for (var i = Ext5RankCellIndex; i < Ext5CommitCellIndex; i++)
            {
                var (r, g, b) = N(i);
                ext5Sum += r + g + b;
            }
            var (_, ext5Checksum, ext5Commit) = N(Ext5CommitCellIndex);
            if (ext5Commit == heartbeat && (ext5Sum & 0xF) == ext5Checksum)
            {
                // An invalid CC key forces the spell id to 0 (D4: a synthesized
                // press needs a keystroke, so half an entry is never surfaced),
                // exactly like the Ext4 pad rule.
                var ccValid = (ccFlags & FlagValid) != 0;
                var ccSpellId = ccValid
                    ? ((ccIdTop & 3) << 20) | (ccId19_16 << 16) | (ccId15_12 << 12)
                        | (ccId11_8 << 8) | (ccId7_4 << 4) | ccId3_0
                    : 0;
                ext5 = new Ext5Block(
                    rankBits & 7,
                    delveBits & 3,
                    interruptibleBits & 3,
                    castKindBits & 3,
                    castAgeBucket,
                    ccValid
                        ? new KeyStroke(
                            (byte)((ccVkHi << 4) | ccVkLo),
                            (ccFlags & FlagShift) != 0,
                            (ccFlags & FlagCtrl) != 0,
                            (ccFlags & FlagAlt) != 0)
                        : default,
                    ccSpellId,
                    gcdBucket);
            }
        }

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
            HpCurveValid = hpCurveValid,
            HpCurveBand = hpCurveBand,
            SelfHeal2 = selfHeal2,
            Ext3Present = ext3Present,
            Ext3 = ext3,
            TypingActive = typingActive,
            Ext4Present = ext4Present,
            Ext4 = ext4,
            Ext5Present = ext5Present,
            Ext5 = ext5,
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
