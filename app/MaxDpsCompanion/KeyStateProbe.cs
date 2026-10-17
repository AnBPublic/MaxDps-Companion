namespace MaxDpsCompanion;

/// <summary>
/// v4.0.3 hotbar press queue: reads the PHYSICAL key state for the watched
/// strokes. A thin, injectable wrapper over <c>GetAsyncKeyState</c> — the same
/// user-mode API the <see cref="MovementGuard"/> hold-collision skip already
/// uses. No hooks, no injected input, no memory reads, and the engine only
/// ever queries the VKs present in the decoded Ext4 hotbar map, so this can
/// never observe keys outside the user's own keybound action bars.
/// </summary>
internal sealed class KeyStateProbe
{
    private readonly Func<byte, short> _query;

    /// <summary>Test seam: inject a scripted key-state source.</summary>
    public KeyStateProbe(Func<byte, short>? query = null) => _query = query ?? DefaultQuery;

    private static short DefaultQuery(byte virtualKey)
    {
        try
        {
            return Native.GetAsyncKeyState(virtualKey);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Raw state word: bit 0x8000 = currently down, bit 0x0001 = pressed since
    /// the previous call (the tap-latch used to catch sub-scan taps).
    /// </summary>
    public short Query(byte virtualKey) => _query(virtualKey);

    /// <summary>True while the physical key is held (mirrors MovementGuard.IsPhysicallyDown).</summary>
    public bool IsDown(byte virtualKey) => (_query(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// Fresh physical press for the scan: currently down and not down at the
    /// previous scan. Deliberately does NOT OR-in GetAsyncKeyState's "pressed
    /// since the last call" low bit — that latch is shared with every other
    /// caller (the game, the engine's own hold-collision checks), so it cannot
    /// be trusted as a private edge signal. A human tap is 30-150 ms; the
    /// ≤10 ms scan catches it on <c>down &amp;&amp; !was</c> alone.
    /// </summary>
    public static bool IsFreshPress(short state, bool wasDown) => (state & 0x8000) != 0 && !wasDown;
}
