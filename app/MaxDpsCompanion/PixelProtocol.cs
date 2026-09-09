namespace MaxDpsCompanion;

internal enum BridgeState { Idle = 0, Active = 1, Paused = 2, NeedTarget = 3, NeedInteract = 4 }

internal enum Slot { Main = 0, Cooldown = 1, Interrupt = 2, Defensive = 3, Consumable = 4 }

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
    public required KeyStroke?[] Slots { get; init; }

    public KeyStroke? this[Slot slot] => Slots[(int)slot];
}

internal static class PixelProtocol
{
    public const int CellCount = 8;
    public const int SupportedVersion = 1;
    public const int SlotCount = 5;

    /// <summary>Status cell index: encodes (state, heartbeat, 0).</summary>
    public const int StatusCellIndex = 6;

    /// <summary>Version cell index: encodes (ver, checksum, commit).</summary>
    public const int VersionCellIndex = 7;

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
    /// Decodes eight sampled cell colours. Returns null when the magic cell does not
    /// match, which means the block is not where we are looking or the addon is off.
    ///
    /// Without a learned profile the magic cell (pure magenta) also carries this
    /// screen's actual black point (its green channel) and white point (its red
    /// and blue). With a profile each channel is normalised against its own
    /// learned black/white range, which survives per-channel display-chain
    /// shifts from RenoDX, RTX HDR and calibration tools.
    ///
    /// Layout: cell 0 = magic, cells 1-5 = slots, cell 6 = status
    /// (state/heartbeat/0), cell 7 = version (ver/checksum/commit).
    /// Tear check: commit must equal heartbeat, else null. Checksum: sum of
    /// R+G+B nibbles of cells 1-6 mod 16 must equal the checksum nibble, else null.
    /// </summary>
    public static BridgeFrame? Decode(Color[] cells) => Decode(cells, profile: null);

    public static BridgeFrame? Decode(Color[] cells, ColorProfile? profile)
    {
        if (cells.Length != CellCount) return null;
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

        var (state, heartbeat, statusZero) = Nibbles(cells[StatusCellIndex], black, white, profile);
        if (statusZero != 0) return null;
        var (ver, checksum, commit) = Nibbles(cells[VersionCellIndex], black, white, profile);
        if (ver != SupportedVersion) return null;
        if (state is < 0 or > 4) return null;
        if (commit != heartbeat) return null;

        // Checksum over cells 1-6 (5 slots + status), R+G+B nibbles, mod 16.
        var sum = 0;
        for (var i = 1; i <= StatusCellIndex; i++)
        {
            var (r, g, b) = Nibbles(cells[i], black, white, profile);
            sum += r + g + b;
        }
        if ((sum & 0xF) != checksum) return null;

        var slots = new KeyStroke?[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var (hi, lo, flags) = Nibbles(cells[i + 1], black, white, profile);
            if ((flags & FlagValid) == 0) continue;

            slots[i] = new KeyStroke(
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
            Slots = slots,
        };
    }

    /// <summary>
    /// Lightweight status-state probe for the calibrate pattern: decodes only
    /// the state nibble (R channel) of the status cell. Used by ColorLearner,
    /// where the slot cells hold the ramp and a full Decode cannot pass.
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
