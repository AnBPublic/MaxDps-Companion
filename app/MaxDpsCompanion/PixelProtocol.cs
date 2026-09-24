namespace MaxDpsCompanion;

internal enum BridgeState { Idle = 0, Active = 1, Paused = 2, NeedTarget = 3, NeedInteract = 4 }

// Slot order = the user's 5-icon model: Main (THE rotation — always first)
// + Offensive + Defensive + Consumable + Trinket + Interrupt (situational;
// the engine re-sorts by Priority at send time so it still preempts).
// Old "Cooldown" name = Offensive (classCooldowns offensive bucket).
internal enum Slot { Main = 0, Offensive = 1, Defensive = 2, Consumable = 3, Trinket = 4, Interrupt = 5 }

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

    public KeyStroke? this[Slot slot] => Slots[(int)slot];

    /// <summary>v4: player is in combat (UnitAffectingCombat, NeverSecret).</summary>
    public bool InCombat => (StatusFlags & PixelProtocol.StatusFlagInCombat) != 0;

    /// <summary>v4: a spell's GCD is currently active (isOnGCD, NeverSecret).</summary>
    public bool OnGcd => (StatusFlags & PixelProtocol.StatusFlagOnGcd) != 0;

    /// <summary>v4: a valid attackable target exists (wait instead of spamming).</summary>
    public bool HasTarget => (StatusFlags & PixelProtocol.StatusFlagHasTarget) != 0;
}

internal static class PixelProtocol
{
    /// <summary>
    /// Protocol versions. v1 = 8 cells (magic + 5 slots + status + version),
    /// v3 = 9 cells (magic + Main/Off/Def/Cons/Trin/Int + status + version),
    /// v4 = 9 cells, same layout, status cell B now carries flags
    /// (bit0 in-combat, bit1 on-GCD). The companion decodes v4 + v1: a stale
    /// in-game addon (user forgot /reload after install-addon) must never
    /// read as "no pattern" — that was the Sep-2026 calibrate outage. v2/v3
    /// (same length, R=2/3) are rejected by the R check so they can never
    /// cross-talk with v4.
    /// </summary>
    public const int CellCountV1 = 8;
    public const int CellCount = 9;
    public const int SupportedVersionV1 = 1;
    public const int SupportedVersion = 4;
    public const int SlotCountV1 = 5;
    public const int SlotCount = (int)Slot.Interrupt + 1;

    /// <summary>Status-cell B flag bits (v4).</summary>
    public const int StatusFlagInCombat = 1;
    public const int StatusFlagOnGcd = 2;
    public const int StatusFlagHasTarget = 4;

    /// <summary>Status cell index: encodes (state, heartbeat, 0).</summary>
    public const int StatusCellIndexV1 = 6;
    public const int StatusCellIndex = 7;

    /// <summary>Version cell index: encodes (ver, checksum, commit).</summary>
    public const int VersionCellIndexV1 = 7;
    public const int VersionCellIndex = 8;

    private const int FlagShift = 1;
    private const int FlagCtrl = 2;
    private const int FlagAlt = 4;
    private const int FlagValid = 8;

    /// <summary>Minimum separation between the magic cell's black and white channels.</summary>
    public const int MinContrast = ColorProfile.LegacyMinContrast;

    /// <summary>
    /// Recognises the magic cell loosely enough to survive the client's gamma,
    /// brightness and contrast sliders, which shift every rendered colour.
    /// A learned profile matches by distance from the measured reference;
    /// otherwise the legacy fixed contrast check applies.
    /// </summary>
    public static bool IsMagic(Color color) => IsMagic(color, profile: null);

    public static bool IsMagic(Color color, ColorProfile? profile) =>
        profile is { IsLearned: true }
            ? profile.IsMagic(color)
            : color.R - color.G >= ColorProfile.LegacyMinContrast
                && color.B - color.G >= ColorProfile.LegacyMinContrast;

    /// <summary>
    /// Decodes a sampled strip. Accepts both v1 (8 cells) and v2 (9 cells);
    /// returns null when the magic cell does not match, which means the
    /// block is not where we are looking or the addon is off.
    ///
    /// Without a learned profile the magic cell (pure magenta) also carries this
    /// screen's actual black point (its green channel) and white point (its red
    /// and blue). With a profile each channel is normalised against its own
    /// learned black/white range, which survives per-channel display-chain
    /// shifts from RenoDX, RTX HDR and calibration tools.
    ///
    /// Layout v3: cell 0 = magic, cells 1-6 = slots (Main, Offensive,
    /// Defensive, Consumable, Trinket, Interrupt), cell 7 = status
    /// (state/heartbeat/0), cell 8 = version (ver/checksum/commit).
    /// Layout v1: cells 1-5 = slots (Main/Off/Int/Def/Cons), cell 6 =
    /// status, cell 7 = version. Tear check: commit must equal heartbeat,
    /// else null. Checksum: sum of R+G+B nibbles of cells 1..status mod 16
    /// must equal the checksum nibble, else null.
    /// </summary>
    public static BridgeFrame? Decode(Color[] cells) => Decode(cells, profile: null);

    public static BridgeFrame? Decode(Color[] cells, ColorProfile? profile)
    {
        // v3 first (current), v1 fallback (stale in-game addon). A v1 frame
        // (Main/Off/Int/Def/Cons) maps onto v3 indices explicitly below —
        // Interrupt moves 3→5, Defensive 4→2, Consumable 5→3 — so engine
        // priority and settings indexing never shift. v2 (R=2, old order)
        // fails the R check and is rejected, never misdecoded.
        if (cells.Length == CellCount) return DecodeCells(cells, StatusCellIndex, VersionCellIndex, null, SupportedVersion, profile);
        if (cells.Length == CellCountV1) return DecodeCells(cells, StatusCellIndexV1, VersionCellIndexV1, V1ToV3, SupportedVersionV1, profile);
        return null;
    }

    /// <summary>
    /// v1 slot → v3 slot remap (Interrupt 3→5, Defensive 4→2, Consumable
    /// 5→3; Main/Offensive stay). v3 decode passes null (= identity).
    /// </summary>
    private static readonly int[] V1ToV3 = [0, 1, 5, 2, 3];

    private static BridgeFrame? DecodeCells(Color[] cells, int statusIndex, int versionIndex, int[]? remap, int version, ColorProfile? profile)
    {
        if (!IsMagic(cells[0], profile)) return null;

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
            if (white - black < ColorProfile.LegacyMinContrast) return null;
        }

        // v4: status cell B is FLAGS, not a zero guard (bit0 in-combat,
        // bit1 on-GCD). v1 frames carry 0 there, which is a valid empty
        // flag set — so no rejection either way.
        var (state, heartbeat, statusFlags) = Nibbles(cells[statusIndex], black, white, profile);
        var (ver, checksum, commit) = Nibbles(cells[versionIndex], black, white, profile);
        if (ver != version) return null;
        if (state is < 0 or > 4) return null;
        if (commit != heartbeat) return null;

        // Checksum over cells 1..status (slots + status), R+G+B nibbles, mod 16.
        var sum = 0;
        for (var i = 1; i <= statusIndex; i++)
        {
            var (r, g, b) = Nibbles(cells[i], black, white, profile);
            sum += r + g + b;
        }
        if ((sum & 0xF) != checksum) return null;

        // Always expose the full v3 slot array: a v1 frame remaps
        // (Int 3→5, Def 4→2, Cons 5→3) and leaves Trinket null, so engine
        // priority and settings indexing never shift.
        var slots = new KeyStroke?[SlotCount];
        var count = remap?.Length ?? SlotCount;
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
        };
    }

    /// <summary>
    /// Lightweight status-state probe for the calibrate pattern: decodes only
    /// the state nibble (R channel) of the status cell. Used by ColorLearner,
    /// where the slot cells hold the ramp and a full Decode cannot pass.
    /// Probe version: v1 status lives at index 6, v2 at index 7. Callers
    /// pass the cell they sampled as "status" — see ColorLearner.Classify,
    /// which tries v2 first and falls back to v1 (stale in-game addon).
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
