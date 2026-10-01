using Xunit;

namespace MaxDpsCompanion.Tests;

/// <summary>
/// Toggle authority. ADDON-WINS (2026-09-30 flip): the 14-bit mask pack, the
/// fail-closed mirror consumer (no echo = mask 0) and the fact that a settings
/// change never auto-pushes. The <c>/mdb mask</c> format, out-of-combat retry
/// ladder (max 2, then conflict) and the one-time settings migration that fixes
/// RC1/RC2 are retained for the explicit-push path and covered here. Serialized
/// with the other global-static suites because the migration republishes
/// <see cref="CrowdControlGate"/>.
/// </summary>
[Collection("GlobalStaticState")]
public class ToggleSyncTests
{
    private const long Now = 10_000;

    private static AppSettings Settings()
    {
        var s = new AppSettings
        {
            SoloEnabled = false,
            CombatOnly = true,
            AutoTargetEnabled = false,
            InteractEnabled = false,
            TimeToKillEnabled = true,
            CrowdControlEnabled = false,
        };
        for (var i = 0; i < s.SlotEnabled.Length; i++) s.SlotEnabled[i] = true;
        return s;
    }

    [Fact]
    public void BuildMask_Sets_Every_Toggle_At_Its_Canonical_Bit()
    {
        var s = Settings();
        s.SlotEnabled[3] = false;      // Consumable off
        s.SlotEnabled[5] = false;      // Interrupt off
        s.SoloEnabled = true;
        s.CombatOnly = false;          // OOC on
        s.AutoTargetEnabled = true;
        s.InteractEnabled = true;
        s.CrowdControlEnabled = true;

        var mask = ToggleSync.BuildMask(s);

        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitMain));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitOffensive));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitDefensive));
        Assert.False(ToggleSync.IsSet(mask, ToggleSync.BitConsumable));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitTrinket));
        Assert.False(ToggleSync.IsSet(mask, ToggleSync.BitInterrupt));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitMobility));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitSelfHeal));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitSolo));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitOutOfCombat));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitAutoTarget));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitAutoInteract));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitTimeToKill));
        Assert.True(ToggleSync.IsSet(mask, ToggleSync.BitCrowdControl));
        Assert.Equal(ToggleSync.MaskField & mask, mask);
    }

    [Fact]
    public void BuildMask_Ooc_Is_The_Inverse_Of_CombatOnly()
    {
        var combatOnly = Settings(); // CombatOnly = true
        var openWorld = Settings();
        openWorld.CombatOnly = false;

        Assert.False(ToggleSync.IsSet(ToggleSync.BuildMask(combatOnly), ToggleSync.BitOutOfCombat));
        Assert.True(ToggleSync.IsSet(ToggleSync.BuildMask(openWorld), ToggleSync.BitOutOfCombat));
    }

    [Fact]
    public void FormatCommand_Is_Four_Hex_Digits_And_Epoch()
    {
        Assert.Equal("mdb mask 0000 0", ToggleSync.FormatCommand(0, 0));
        Assert.Equal("mdb mask 3FFF 15", ToggleSync.FormatCommand(ToggleSync.MaskField, 15));
        // Mask is clamped to 14 bits, epoch to 4.
        Assert.Equal("mdb mask 3FFF 0", ToggleSync.FormatCommand(unchecked((int)0xFFFF), 0x20));
    }

    [Fact]
    public void EffectiveMask_Is_Zero_When_No_Valid_Echo_AddonWins()
    {
        // ADDON-WINS: without a bridge echo there is no addon truth, so the
        // companion fails closed instead of resurrecting its own mask.
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        Assert.True(sync.AppMask != 0);
        Assert.Equal(0, sync.EffectiveMask);
        Assert.False(sync.EffectiveFromMirror);

        sync.ObserveMirror(Now, null);
        Assert.Equal(0, sync.EffectiveMask);
        Assert.False(sync.EffectiveFromMirror);
    }

    [Fact]
    public void EffectiveMask_Uses_The_Mirrored_Mask_When_Ext3_Is_Valid()
    {
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        var mirrored = ToggleSync.BuildMask(Settings()) & ~(1 << ToggleSync.BitOffensive);

        sync.ObserveMirror(Now, new Ext3Block(mirrored, 3, 0));

        Assert.True(sync.MirrorValid);
        Assert.True(sync.EffectiveFromMirror);
        Assert.Equal(mirrored, sync.EffectiveMask);
    }

    [Fact]
    public void ObserveSettings_Never_Requests_A_Push_AddonWins()
    {
        // ADDON-WINS: a companion settings change only refreshes the diagnostic
        // AppMask; it must never queue a push that would overwrite the overlay.
        var sync = new ToggleSync();
        var s = Settings();
        sync.BeginSession(s);
        var first = sync.AppMask;
        Assert.False(sync.NeedsPush);

        s.SlotEnabled[6] = false;                // Mobility off
        sync.ObserveSettings(s);

        Assert.False(sync.NeedsPush);
        Assert.NotEqual(first, sync.AppMask);
    }

    [Fact]
    public void RequestExplicitPush_Marks_The_Only_Dirty_State()
    {
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        Assert.False(sync.NeedsPush);

        sync.RequestExplicitPush();
        Assert.True(sync.NeedsPush);
    }

    [Fact]
    public void BeginPush_Rotates_The_Epoch_And_Starts_Pending()
    {
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        sync.RequestExplicitPush();

        var first = sync.BeginPush(Now);
        Assert.Equal("mdb mask " + (ToggleSync.BuildMask(Settings())).ToString("X4") + " 1", first);
        Assert.Equal(ToggleSync.SyncState.Pending, sync.State);
        Assert.False(sync.NeedsPush);

        // Nothing owed: no second command until a mismatch or a change.
        Assert.Null(sync.BeginPush(Now + 10));

        sync.ObserveMirror(Now, new Ext3Block(sync.PendingMask!.Value, sync.PendingEpoch, 0));
        Assert.Equal(ToggleSync.SyncState.Synced, sync.State);

        // A later explicit change rotates the epoch again (2).
        var s = Settings();
        s.SlotEnabled[6] = false;
        sync.ObserveSettings(s);
        sync.RequestExplicitPush();
        var second = sync.BeginPush(Now + 100);
        Assert.EndsWith(" 2", second);
    }

    [Fact]
    public void Mismatch_Retries_Twice_Then_Surfaces_A_Conflict()
    {
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        var mask = ToggleSync.BuildMask(Settings());
        sync.RequestExplicitPush();

        sync.BeginPush(Now);
        // A wrong echo inside the window is ignored (the addon needs a frame).
        sync.ObserveMirror(Now + 100, new Ext3Block(mask, 0, 0));
        Assert.Equal(ToggleSync.SyncState.Pending, sync.State);
        Assert.Equal(0, sync.RetryCount);

        // Window elapsed: retry #1.
        sync.ObserveMirror(Now + ToggleSync.ResponseWindowMs + 1, new Ext3Block(mask, 0, 0));
        Assert.Equal(1, sync.RetryCount);
        Assert.True(sync.NeedsPush);
        Assert.Equal(ToggleSync.SyncState.Pending, sync.State);

        sync.BeginPush(Now + ToggleSync.ResponseWindowMs + 2);
        sync.ObserveMirror(Now + 2 * ToggleSync.ResponseWindowMs + 3, new Ext3Block(mask, 1, 0));
        Assert.Equal(2, sync.RetryCount);
        Assert.True(sync.NeedsPush);

        sync.BeginPush(Now + 2 * ToggleSync.ResponseWindowMs + 4);
        sync.ObserveMirror(Now + 3 * ToggleSync.ResponseWindowMs + 5, new Ext3Block(mask, 2, 0));

        Assert.Equal(ToggleSync.SyncState.Conflict, sync.State);
        Assert.True(sync.HasConflict);
        Assert.False(sync.NeedsPush);
        Assert.Null(sync.BeginPush(Now + 4 * ToggleSync.ResponseWindowMs));
    }

    [Fact]
    public void Matching_Echo_Syncs_And_Clears_The_Retry_Ladder()
    {
        var sync = new ToggleSync();
        sync.BeginSession(Settings());
        sync.RequestExplicitPush();
        sync.BeginPush(Now);

        sync.ObserveMirror(Now + 50, new Ext3Block(sync.PendingMask!.Value, sync.PendingEpoch, 0));

        Assert.Equal(ToggleSync.SyncState.Synced, sync.State);
        Assert.Equal(0, sync.RetryCount);
        Assert.False(sync.HasConflict);
        Assert.Null(sync.PendingMask);
    }

    // ----- one-time [Meta] migration (RC1 / RC2) ---------------------------

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"mdb-toggle-migration-{Guid.NewGuid():N}.ini");

    [Fact]
    public void Legacy_Config_With_Spells_Is_Migrated_Mobility_And_Cc_On()
    {
        var path = TempPath();
        // A pre-3.5 install: [Spells] present, no [Meta]; both RC1 and RC2 OFF.
        File.WriteAllText(path,
            "[Spells]\nspell1=1\nspell2=1\nspell3=1\nspell4=1\n" +
            "spell5=1\nspell6=1\nspell7=0\nspell8=1\n" +
            "[CrowdControl]\nEnabled=0\n");
        try
        {
            var settings = AppSettings.Load(path);

            Assert.True(settings.SlotEnabled[6]);        // RC1 fixed
            Assert.True(settings.CrowdControlEnabled);   // RC2 fixed
            Assert.True(CrowdControlGate.Enabled);
        }
        finally
        {
            CrowdControlGate.Reset();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Saved_Config_Is_Stamped_So_A_Later_User_Off_Choice_Sticks()
    {
        var path = TempPath();
        try
        {
            var settings = AppSettings.Load(path);   // absent file => defaults
            settings.SlotEnabled[6] = false;         // user turns Mobility OFF
            settings.CrowdControlEnabled = false;    // user turns CC OFF
            settings.Save();

            var reloaded = AppSettings.Load(path);

            Assert.False(reloaded.SlotEnabled[6]);
            Assert.False(reloaded.CrowdControlEnabled);
        }
        finally
        {
            CrowdControlGate.Reset();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Partial_Config_Without_Spells_Is_Not_Migrated()
    {
        var path = TempPath();
        File.WriteAllText(path, "[Intelligence]\nEnabled=1\n");
        try
        {
            var settings = AppSettings.Load(path);
            Assert.False(settings.CrowdControlEnabled);
        }
        finally
        {
            CrowdControlGate.Reset();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
