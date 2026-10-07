using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// 2026-10-04 rare/elite target fix: a trash-learned adaptive-history window must
/// not hold a major offensive against a live target that is demonstrably not
/// dying trash. Exercises <see cref="TtkPolicy.LiveReleasesHistory"/> and its
/// wiring into <see cref="TtkPolicy.HistoryWasteGuardHolds"/> with the real
/// curated Recklessness 1719 / Avatar 107574 / Ancestral Call 274738 rows from
/// <c>abilities.json</c>, so the buff-aware need is the shipping value rather
/// than a test-invented one.
/// </summary>
public class TtkRareTargetTests
{
    private static AbilityCatalog Catalog => AbilityCatalog.Default;

    private static AbilityDefinition Ability(int id)
    {
        var ability = Catalog.TryGet(id);
        Assert.NotNull(ability);
        return ability!;
    }

    private static double BuffDurSec(AbilityDefinition a) =>
        a.DurationMs > 0 ? a.DurationMs / 1000.0 : 0.0;

    /// <summary>
    /// A context with a short trash-learned history window (6 s) and a chosen
    /// live target state.
    /// </summary>
    private static CombatContext Ctx(
        bool ttkValid,
        double ttkSec,
        double ageSec,
        bool targetHpValid,
        double targetHpFrac,
        double histTtkSec = 6.0,
        bool histBinding = true) =>
        new()
        {
            TtkValid = ttkValid,
            TtkSec = ttkSec,
            TargetAgeSec = ageSec,
            TargetHpValid = targetHpValid,
            TargetHpFrac = targetHpFrac,
            TargetHpPct = (int)(targetHpFrac * 100),
            TtkHistBinding = histBinding,
            TtkHistRate = 0.1,
            TtkHistSec = histTtkSec,
            TtkHistProvisional = histBinding && !ttkValid,
            TtkHistKills = 6,
            TtkHistDurFactor = 0.5,
        };

    // (1) Trash history, but the live estimate is valid and long -> no hold.
    [Fact]
    public void Trash_History_With_Long_Live_Ttk_Does_Not_Hold()
    {
        var reck = Ability(1719);
        var ctx = Ctx(ttkValid: true, ttkSec: 60, ageSec: 12, targetHpValid: true, targetHpFrac: 0.95);

        Assert.True(TtkPolicy.LiveReleasesHistory(reck, ctx, TtkPolicy.BuffNeed(reck, 0.5)));
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(reck, ctx, BuffDurSec(reck), 0.5));
        Assert.False(TtkPolicy.WasteGuardHolds(reck, ctx, BuffDurSec(reck), 0.5));
    }

    // (2) Rare/elite: live invalid, but age 10 s and 95% HP -> history released.
    [Fact]
    public void Rare_Invalid_Live_Long_Lived_High_Hp_Does_Not_Hold()
    {
        var avatar = Ability(107574);
        var ctx = Ctx(ttkValid: false, ttkSec: TtkEstimator.MaxTtkSec, ageSec: 10,
            targetHpValid: true, targetHpFrac: 0.95);

        Assert.True(TtkPolicy.LiveReleasesHistory(avatar, ctx, TtkPolicy.BuffNeed(avatar, 0.5)));
        Assert.False(TtkPolicy.HistoryWasteGuardHolds(avatar, ctx, BuffDurSec(avatar), 0.5));
    }

    // (3) A valid live estimate genuinely below the need still holds.
    [Fact]
    public void Short_Live_Ttk_Still_Holds()
    {
        var reck = Ability(1719);
        var ctx = Ctx(ttkValid: true, ttkSec: 5, ageSec: 4, targetHpValid: true, targetHpFrac: 0.5);

        Assert.False(TtkPolicy.LiveReleasesHistory(reck, ctx, TtkPolicy.BuffNeed(reck, 0.5)));
        Assert.True(TtkPolicy.HistoryWasteGuardHolds(reck, ctx, BuffDurSec(reck), 0.5));
    }

    // (4) An invalid live estimate on a young target is not "long-lived" -> holds.
    [Fact]
    public void Early_Invalid_Live_Target_Still_Holds()
    {
        var ancestral = Ability(274738);
        var ctx = Ctx(ttkValid: false, ttkSec: TtkEstimator.MaxTtkSec, ageSec: 2,
            targetHpValid: true, targetHpFrac: 0.95);

        Assert.False(TtkPolicy.LiveReleasesHistory(ancestral, ctx, TtkPolicy.BuffNeed(ancestral, 0.5)));
        Assert.True(TtkPolicy.HistoryWasteGuardHolds(ancestral, ctx, BuffDurSec(ancestral), 0.5));
    }

    // (5) Invalid live, old but a low HP band (dying trash) -> holds.
    [Fact]
    public void Trash_Invalid_Live_Low_Hp_Still_Holds()
    {
        var avatar = Ability(107574);
        var ctx = Ctx(ttkValid: false, ttkSec: TtkEstimator.MaxTtkSec, ageSec: 10,
            targetHpValid: true, targetHpFrac: 0.4);

        Assert.False(TtkPolicy.LiveReleasesHistory(avatar, ctx, TtkPolicy.BuffNeed(avatar, 0.5)));
        Assert.True(TtkPolicy.HistoryWasteGuardHolds(avatar, ctx, BuffDurSec(avatar), 0.5));
    }
}
