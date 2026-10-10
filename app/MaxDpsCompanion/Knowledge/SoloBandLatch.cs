namespace MaxDpsCompanion;

/// <summary>
/// Pure enter/exit hysteresis for the Solo HP bands (Stream 1, spec §1.2).
///
/// The banding in <see cref="PolicyOptions"/> is a single threshold per tier
/// (Minor &lt;=75, Major &lt;=50, Immunity &lt;=30). A single threshold flaps
/// when the player's HP hovers exactly on the edge: the tier is eligible this
/// tick and held the next, so the ladder oscillates until something fires.
///
/// This type adds a five-point release gap without owning any state: the
/// caller supplies whether the ladder was already engaged (<c>wasEngaged</c>)
/// and gets back a stable boolean. When the caller has no memory of a prior
/// tick (fresh session, tests, unknown context) it passes <c>false</c> and the
/// latch degrades to the plain enter threshold — stateless-safe by design.
///
///   * not engaged: latched when <c>hp &lt;= enter</c>  (plain threshold)
///   * engaged:     stays latched until <c>hp &gt;= exit</c> (release gap)
///
/// A pair that is not strictly increasing is repaired locally so a bad caller
/// can never turn the latch into a flap. No game API, no mutation, no I/O.
/// </summary>
internal static class SoloBandLatch
{
    /// <summary>Default release gap: leave a band only once HP recovers to enter+5.</summary>
    public const int DefaultExitGap = 5;

    /// <summary>The release threshold for an enter threshold (never below enter).</summary>
    public static int ExitFor(int enterHp, int gap = DefaultExitGap) =>
        enterHp + Math.Max(0, gap);

    /// <summary>
    /// Pure latch. <paramref name="enterHp"/> is the tier's at/below band,
    /// <paramref name="exitHp"/> the at/above release point. Returns true while
    /// the tier should remain eligible.
    /// </summary>
    public static bool Latched(int enterHp, int exitHp, int hp, bool wasEngaged)
    {
        // Never let an inverted pair flap: an exit at/below enter is repaired
        // to enter+1 so the engaged branch still has one point of hysteresis.
        if (exitHp <= enterHp) exitHp = enterHp + 1;
        return wasEngaged ? hp < exitHp : hp <= enterHp;
    }

    /// <summary>Convenience overload using the default release gap.</summary>
    public static bool Latched(int enterHp, int hp, bool wasEngaged) =>
        Latched(enterHp, ExitFor(enterHp), hp, wasEngaged);
}
