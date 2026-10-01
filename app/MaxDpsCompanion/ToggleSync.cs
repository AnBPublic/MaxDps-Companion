namespace MaxDpsCompanion;

/// <summary>
/// Toggle authority, ADDON-WINS (2026-09-30 authority flip). The in-game
/// overlay owns the effective toggle state: the bridge publishes its local
/// 14 toggles in the additive Ext3 cells (40-42) and the companion treats that
/// mirror as the single authority. The companion is deliberately permissive —
/// the user turns everything ON to mean "let the overlay drive" — and it never
/// overwrites the overlay with its own mask.
///
/// The old v3.5 S1 model was "app wins": the companion packed its 14 toggles
/// and auto-pushed <c>/mdb mask &lt;hhhh&gt; &lt;e&gt;</c> at Start and on every
/// settings change, which forced the bridge into app-controlled epoch ≠ 0 and
/// clobbered the in-game toggles. That auto-push is GONE: the companion does
/// not push CombatOnly/OOC, AutoTarget, AutoInteract or any slot toggle, so the
/// bridge stays at epoch 0 and its local toggles win. A push is only possible
/// through an explicit user-initiated <see cref="RequestExplicitPush"/> and has
/// no call site in the addon-wins workflow.
///
/// The mirror is fail-closed: <see cref="EffectiveMask"/> is the bridge echo
/// while valid and <c>0</c> otherwise (no Ext3 block = no addon truth = no
/// permission), never the app's own mask. <see cref="CombatGate"/> reads the
/// mirror bits directly for the out-of-combat / movement gates.
///
/// This type is a pure state machine: it never touches the game, the network or
/// the UI, and it takes the wall clock as a parameter so every transition is
/// deterministic and unit-testable. The caller drives it once per decoded frame
/// with the latest <see cref="Ext3Block"/>.
///
/// Bit layout (LSB first; the canonical order of <c>MDB.Toggles.Keys()</c> with
/// the 14th CC toggle appended):
/// <code>
///  0 Main        4 Trinket   8  Solo           12 TTK
///  1 Offensive   5 Interrupt 9  OOC            13 CrowdControl
///  2 Defensive   6 Mobility  10 AutoTarget
///  3 Consumable  7 SelfHeal  11 AutoInteract
/// </code>
/// </summary>
internal sealed class ToggleSync
{
    /// <summary>Mask width carried by Ext3 (cells 40-41 R).</summary>
    public const int MaskBitCount = 14;

    /// <summary>All 14 mask bits set; anything outside is masked away.</summary>
    public const int MaskField = (1 << MaskBitCount) - 1;

    /// <summary>Push retries allowed after the first, before the red badge.</summary>
    public const int MaxRetries = 2;

    /// <summary>Ext3 epoch is a 4-bit rotating tag.</summary>
    public const int EpochModulus = 16;

    /// <summary>How long to wait for a matching Ext3 echo before retrying.</summary>
    public const long ResponseWindowMs = 1500;

    // Canonical bit indices.
    public const int BitMain = 0;
    public const int BitOffensive = 1;
    public const int BitDefensive = 2;
    public const int BitConsumable = 3;
    public const int BitTrinket = 4;
    public const int BitInterrupt = 5;
    public const int BitMobility = 6;
    public const int BitSelfHeal = 7;
    public const int BitSolo = 8;
    public const int BitOutOfCombat = 9;
    public const int BitAutoTarget = 10;
    public const int BitAutoInteract = 11;
    public const int BitTimeToKill = 12;
    public const int BitCrowdControl = 13;

    /// <summary>Frozen v3.5 sync lifecycle.</summary>
    public enum SyncState
    {
        /// <summary>No push has happened yet (nothing to compare).</summary>
        Idle,
        /// <summary>A push is in flight, awaiting the matching Ext3 echo.</summary>
        Pending,
        /// <summary>The bridge echoed exactly the pushed mask + epoch.</summary>
        Synced,
        /// <summary>Retries exhausted: the bridge never echoed. Red badge.</summary>
        Conflict,
    }

    /// <summary>The mask the app currently wants in force.</summary>
    public int AppMask { get; private set; }

    /// <summary>Last epoch pushed to the bridge (0-15).</summary>
    public int CurrentEpoch { get; private set; }

    /// <summary>Mask of the push currently in flight (null when none).</summary>
    public int? PendingMask { get; private set; }

    /// <summary>Epoch of the push currently in flight.</summary>
    public int PendingEpoch { get; private set; }

    /// <summary>Retries spent since the current desired mask became dirty.</summary>
    public int RetryCount { get; private set; }

    /// <summary>Become true when the desired mask differs from what was pushed.</summary>
    public bool NeedsPush { get; private set; }

    /// <summary>Latest Ext3 echo was present and checksum-valid.</summary>
    public bool MirrorValid { get; private set; }

    /// <summary>Mask echoed by the bridge in the last valid Ext3 block.</summary>
    public int? MirrorMask { get; private set; }

    /// <summary>Epoch echoed by the bridge in the last valid Ext3 block.</summary>
    public int? MirrorEpoch { get; private set; }

    public SyncState State { get; private set; } = SyncState.Idle;

    /// <summary>Red badge: retries exhausted without a matching echo.</summary>
    public bool HasConflict => State == SyncState.Conflict;

    private long _awaitSinceMs = long.MinValue;

    private static int Bit(int index, bool on) => on ? 1 << index : 0;

    /// <summary>
    /// Packs the 14 companion toggles into the canonical Ext3 bit layout. OOC is
    /// <c>!CombatOnly</c>, matching the addon key. ADDON-WINS: this mask is now
    /// purely the diagnostic "what the companion would choose" view consumed by
    /// <see cref="Diagnostics.InstallDoctor"/>'s drift report — it is never
    /// pushed to the bridge.
    /// </summary>
    public static int BuildMask(AppSettings settings)
    {
        var slots = settings.SlotEnabled;
        var mask = 0;
        mask |= Bit(BitMain, slots.Length > 0 && slots[0]);
        mask |= Bit(BitOffensive, slots.Length > 1 && slots[1]);
        mask |= Bit(BitDefensive, slots.Length > 2 && slots[2]);
        mask |= Bit(BitConsumable, slots.Length > 3 && slots[3]);
        mask |= Bit(BitTrinket, slots.Length > 4 && slots[4]);
        mask |= Bit(BitInterrupt, slots.Length > 5 && slots[5]);
        mask |= Bit(BitMobility, slots.Length > 6 && slots[6]);
        mask |= Bit(BitSelfHeal, slots.Length > 7 && slots[7]);
        mask |= Bit(BitSolo, settings.SoloEnabled);
        mask |= Bit(BitOutOfCombat, !settings.CombatOnly);
        mask |= Bit(BitAutoTarget, settings.AutoTargetEnabled);
        mask |= Bit(BitAutoInteract, settings.InteractEnabled);
        mask |= Bit(BitTimeToKill, settings.TimeToKillEnabled);
        mask |= Bit(BitCrowdControl, settings.CrowdControlEnabled);
        return mask & MaskField;
    }

    /// <summary>True when <paramref name="bit"/> is set in <paramref name="mask"/>.</summary>
    public static bool IsSet(int mask, int bit) => (mask & (1 << bit)) != 0;

    /// <summary>
    /// Re-reads the companion's configured toggles. ADDON-WINS: this only
    /// refreshes <see cref="AppMask"/> for diagnostics (the InstallDoctor
    /// app-vs-mirror drift report) and clears a stale conflict; it never marks
    /// a push owed, because the overlay owns the effective mask. A settings
    /// change can therefore never overwrite the in-game toggles.
    /// </summary>
    public void ObserveSettings(AppSettings settings)
    {
        var mask = BuildMask(settings);
        if (mask == AppMask) return;
        AppMask = mask;
        if (State == SyncState.Conflict)
        {
            State = SyncState.Idle;
            RetryCount = 0;
        }
    }

    /// <summary>
    /// Engine Start: begin a fresh session and observe the settings. ADDON-WINS:
    /// no push is queued — the bridge keeps (and re-publishes) its own toggles.
    /// The retry/echo bookkeeping is only reset.
    /// </summary>
    public void BeginSession(AppSettings settings)
    {
        ObserveSettings(settings);
        NeedsPush = false;
        RetryCount = 0;
        PendingMask = null;
        State = SyncState.Idle;
        _awaitSinceMs = long.MinValue;
    }

    /// <summary>
    /// Explicit user-initiated push request. This is the ONLY path that marks a
    /// push owed; the addon-wins workflow never calls it (the companion UI is
    /// permissive and the overlay is authoritative). Retained so a future
    /// "hand authority to the app" affordance can reuse the v3.5 push ladder
    /// without restoring the automatic push.
    /// </summary>
    public void RequestExplicitPush() => NeedsPush = true;

    /// <summary>
    /// Formats the push command body (ChatCommander prepends the leading '/').
    /// <c>mask &lt;hhhh&gt; &lt;e&gt;</c>: 4 uppercase hex digits (14-bit value)
    /// and the 0-15 epoch.
    /// </summary>
    public static string FormatCommand(int mask, int epoch) =>
        $"mdb mask {(mask & MaskField):X4} {epoch & 0xF}";

    /// <summary>
    /// Starts a push attempt, rotating the epoch and marking the state Pending.
    /// Returns the command body to send, or null when no push is owed. The
    /// CALLER gates this on out-of-combat and a live game window, then sends the
    /// result through <see cref="ChatCommander"/>.
    /// </summary>
    public string? BeginPush(long nowMs)
    {
        if (!NeedsPush || State == SyncState.Conflict) return null;
        NeedsPush = false;
        CurrentEpoch = (CurrentEpoch + 1) & (EpochModulus - 1);
        PendingMask = AppMask;
        PendingEpoch = CurrentEpoch;
        _awaitSinceMs = nowMs;
        State = SyncState.Pending;
        return FormatCommand(PendingMask.Value, PendingEpoch);
    }

    /// <summary>
    /// Feeds one decoded frame's Ext3 block. While a push is in flight and the
    /// response window has elapsed without a matching echo, the push is retried
    /// (up to <see cref="MaxRetries"/>) and then surfaces as a conflict.
    /// A mismatch before the window expires is ignored (the addon repaints at
    /// 30 Hz and needs a frame to render the new epoch).
    /// </summary>
    public void ObserveMirror(long nowMs, Ext3Block? ext3)
    {
        MirrorValid = ext3 is not null;
        MirrorMask = ext3?.Mask;
        MirrorEpoch = ext3?.Epoch;

        if (State != SyncState.Pending) return;

        if (ext3 is { } block && block.Mask == PendingMask && block.Epoch == PendingEpoch)
        {
            State = SyncState.Synced;
            RetryCount = 0;
            PendingMask = null;
            _awaitSinceMs = long.MinValue;
            return;
        }

        if (nowMs - _awaitSinceMs < ResponseWindowMs) return;

        if (RetryCount < MaxRetries)
        {
            RetryCount++;
            NeedsPush = true; // caller re-invokes BeginPush on the next OOC tick
            return;
        }

        State = SyncState.Conflict;
        PendingMask = null;
        _awaitSinceMs = long.MinValue;
    }

    /// <summary>
    /// The authoritative mask: the bridge's Ext3 echo while it is valid, else
    /// <c>0</c>. ADDON-WINS means the companion never substitutes its own mask —
    /// a missing/stale Ext3 block is no addon truth, so it fails closed (no
    /// toggle is assumed ON) rather than resurrecting app authority.
    ///
    /// Diagnostics-only: the sole production reader is
    /// <see cref="Diagnostics.InstallDoctor"/>'s app-vs-mirror drift report
    /// (plus <c>MainForm</c> forwarding <see cref="MirrorMask"/> to it). The
    /// send path keys off <see cref="CombatGate.MirrorOutOfCombatSet"/> and the
    /// per-toggle helpers, and an in-combat frame always passes the OOC gate, so
    /// this <c>0</c> (an old 40-cell addon / pre-first-mirror session) can never
    /// blank the rotation in combat.
    /// </summary>
    public int EffectiveMask =>
        MirrorValid && MirrorMask is { } mirrored ? mirrored & MaskField : 0;

    /// <summary>True when the effective value comes from the bridge echo.</summary>
    public bool EffectiveFromMirror => MirrorValid && MirrorMask.HasValue;
}
