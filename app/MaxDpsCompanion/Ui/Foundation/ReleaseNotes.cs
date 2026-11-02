namespace MaxDpsCompanion;

/// <summary>
/// In-app changelog rendered by the Exp console's Details disclosure.
///
/// 4.5.0 "Voidlight" made this the release norm (AGENTS.md, "Release notes"):
/// every version bump must add a <see cref="Release"/> here AND a matching
/// section at the top of <c>docs/CHANGELOG.md</c> in the same commit.
/// <c>ReleaseNotesTests</c> fails the build when the current entry disagrees
/// with <see cref="Native.AppVersion"/>/<see cref="Native.Codename"/> or when
/// the changelog document has no heading for the current version, so a release
/// cannot ship without notes.
/// </summary>
internal static class ReleaseNotes
{
    /// <summary>One release: headline for the at-a-glance list, highlights for the current one.</summary>
    internal sealed record Release(
        string Version,
        string Codename,
        string Date,
        string Headline,
        string[] Highlights)
    {
        /// <summary>"4.5.0 Voidlight" - the label used in the app and the docs.</summary>
        public string Label => $"{Version} {Codename}";
    }

    /// <summary>Newest first. <see cref="Current"/> is always index 0.</summary>
    public static readonly IReadOnlyList<Release> All = new[]
    {
        new Release(
            "5.0.0", "Ascendant", "2026-11-02",
            "12.1.5 target, all-spec readiness, measured survival and long-cooldown intelligence.",
            new[]
            {
                "All 40 specializations inventoried by role and ability ownership, with honest older-patch and unverified spell coverage; inventory is not exhaustive live validation.",
                "Shared sustained plain-health-pressure detector may promote MaxDps-offered, confirmed-unbuffed minor mitigation; major, unknown and immunity gates remain conservative.",
                "Measured short target-lifetime conserves long 90s+ companion-generated offensive gap-fills without altering MaxDps Main/direct cooldown ordering.",
                "Health-pressure hot path no longer creates capturing RemoveAll/Any delegates on every poll.",
                "Targets WoW Retail Midnight 12.1.5 with addon TOC 120105, retaining honest 12.1 source provenance until refreshed and live-verified.",
                "Solo/Group behavior, all user per-ability OFF switches, 51/58-cell protocol and 4.9 thin-strip screen capture are preserved.",
                "Offline test coverage is not proof of perfect gameplay across leveling, talents, raids, dungeons, Delves or FPS; controlled live testing is mandatory.",
            }),
        new Release(
            "4.9.0", "Slipstream", "2026-11-02",
            "Copy only three pixel rows per screen poll, preserving every rotation sample.",
            new[]
            {
                "Screen capture now transfers only the center three horizontal pixel rows required for the HDR-safe five-point median; it no longer copies the entire 8-64px cell height.",
                "At the 8px default and 58-cell wire, BitBlt pixel area drops from 3712 to 1392 pixels (62.5% less data), without a second capture, altered polling or reduced color fidelity.",
                "Source-row cropping retains all original 5 tap coordinates and byte-for-byte wire width; the decoder still receives every slot, heartbeat, checksum and optional Ext5 cell.",
                "One low-overhead GDI BitBlt remains; the game/desktop GPU synchronization cost may dominate, so reduced pixels cannot be equated to FPS gains.",
                "Windows capture recovery, minimized/black detection, HDR calibration, all Sentinel/Lightfoot intelligence and version 5 protocol remain unchanged.",
                "Native benchmarking on the player's Windows hardware is still mandatory before quantifying 1% lows, p99 frame time, CPU/GPU or average FPS.",
            }),
        new Release(
            "4.8.0", "Lightfoot", "2026-11-02",
            "Lean Windows pixel capture and lower-GC Lua candidate scans; core rotations unchanged.",
            new[]
            {
                "Windows samples the same five tap locations directly from the mapped DIB, avoiding delegate invocations and per-cell closures while retaining one BitBlt per poll.",
                "The in-game bridge now appends curated ready/bound abilities directly into a capped pool; no temporary sub-list or per-pool append closure.",
                "The bridge caches safe player HP and stagger urgency once per rendered tick; FrameKey shares this reading, while out-of-tick diagnostics remain live.",
                "The Solo HP ladder reuses one threshold table; no per-tick table allocation.",
                "All Sentinel DPS/tank cooldown policies, main rotation ordering, per-ability controls and 51/58-cell wire are unchanged.",
                "Offline CI verifies build/regression stability. FPS, frametimes and GPU utilization require separate controlled Retail A/B testing.",
            }),
        new Release(
            "4.7.0", "Sentinel", "2026-11-01",
            "Shared DPS/tank cooldown awareness, reliable mitigation routing and rejection recovery.",
            new[]
            {
                "The same bounded rapid-health-loss observation applies to every class and spec. A major defensive may receive one urgency-stage promotion only with trustworthy plain HP loss; unknown/secret observations never trigger it.",
                "Tank maintenance can honor a MaxDps-offered, buff-trackable minor defensive at healthy HP when its self-buff is confirmed inactive. No new automatic bridge candidates or hidden game APIs.",
                "Absorbs and mitigation carried in the SelfHeal transport slot use the Defensive provider, not self-heal/overheal logic.",
                "With Burst mode and Ext5 enabled, a confirmed worldboss/elite-skull may use a major offensive opener before a TTK sample exists; ordinary/unknown targets retain the existing Burst hold.",
                "A GCD-riding cooldown whose press is confirmed rejected now restores the prior defensive/offensive coverage window instead of keeping phantom mitigation active.",
                "Same 51/58-cell wire and MaxDps-owned main rotations. Windows companion and addon ZIPs are generated by GitHub Actions CI. Real retail validation remains OWED.",
            }),
        new Release(
            "4.6.0", "Cadence", "2026-10-28",
            "Context on the wire, interrupt insurance, and GCD-perfect delivery - all default off.",
            new[]
            {
                "Ext5 adds target rank, an in-delve flag, interruptible cast age, GCD remaining and the interrupt-fallback CC key/id in cells 51-57 (58-cell strip, presence cell 50 R bit0). With the addon Ext5 key off the strip is still exactly 51 cells.",
                "Interrupt insurance fires the curated single-target stun/silence when the kick cannot cover a live interruptible cast, through the existing DR, immunity and ineffective-stun vetoes - a GCD-riding CC respects the GCD.",
                "GCD landing pre-fires one GCD-riding stroke per GCD at gcd_end - lead, aborting if the candidate changes; adaptive polling bursts to ~16 ms near the predicted end.",
                "Opener release pre-ranks one ready major on a worldboss or elite-skull pull only; elite, rare, normal and unknown ranks keep today's behaviour.",
                "Enable manually: [Interrupt] InsuranceEnabled, [Scheduler] Landing=Auto, AdaptivePolling, OpenerRelease, and /mcb ext5 on (requires /reload). Live retail checks remain OWED.",
            }),
        new Release(
            "4.5.8", "Voidlight", "2026-10-26",
            "Completes the cooldown shim on the main action bar; no third-party addon patched.",
            new[]
            {
                "GW2_UI styles the main bar in place, so 4.5.7 missed ActionButton1. The bridge now scans addon-created gw_ keys (keys only) and shims the whole button family.",
                "Stops the remaining 83 ActionButton.lua:847 SetCooldown 'secret values' errors on ActionButton1Cooldown.",
                "Clean verdicts expire after 10 s so a late gw_ marker is picked up; clean families stay untouched.",
                "No wire, protocol, rotation or addon-message change. The guard stays silent; /mcb guard reports shims=N.",
                "OverrideActionBarButton/ExtraActionButton remain covered; the 4.5.5 GW2_UI patch tool stays removed.",
            }),
        new Release(
            "4.5.7", "Voidlight", "2026-10-26",
            "Stops the secret-cooldown flood on tainted action bars; no third-party addon patched.",
            new[]
            {
                "The bridge now shims Blizzard's SetCooldown on action bars the client reports as tainted by another addon, and on override/extra buttons while secrets are active. Clean bars are untouched.",
                "Stops the thousands of ActionButton.lua:847 SetCooldown 'secret values' errors logged while the companion pressed in combat.",
                "OverrideActionBarButton/ExtraActionButton are covered too, including the path the old GW2_UI hotfix addressed.",
                "No wire, protocol, rotation or addon-message change. The guard stays silent; /mcb guard reports shims=N on demand.",
                "Removed the GW2_UI patch tool from this release. The companion no longer modifies third-party addons. The remaining GW2_UI SetPassThroughButtons, TalentTreeTweaks SetShown and APR errors are third-party and out of scope.",
            }),
        new Release(
            "4.5.6", "Voidlight", "2026-10-26",
            "Quiet logs: no in-game chat spam; one companion-log line per toggle switched off.",
            new[]
            {
                "The in-game addon no longer prints the [taint] secret-cooldown guard lines to chat. Bridge 4.5.2 keeps the guard running silently.",
                "The companion log now writes exactly one line when a toggle is switched OFF in the in-game addon: 'Main switched OFF...' or 'Trinket switched OFF...'.",
                "No more ON, first-sight or mirror-loss log lines; Details still shows the live toggle summary and the status card still names a hold.",
                "No wire change: /mcb guard is the only in-game output, and only when you run it.",
            }),
        new Release(
            "4.5.5", "Voidlight", "2026-10-26",
            "Corrects the taint diagnosis and ships the GW2_UI hotfix tool.",
            new[]
            {
                "The 3,619 SetCooldown errors came from the shared ActionBarButtonEventsFrame after GW2_UI's OverrideActionBar:HookScript called Hide() on the protected bar in combat; the bridge 4.5.1 guard stops the assisted-frame cascade but cannot untaint a button.",
                "tools/patch-gw2ui-taint.ps1 commented out the GW2_UI override-bar hooks and the extra-action ActionButton_UpdateCooldown call; it was removed in 4.5.7, which covers the same paths in the bridge.",
                "No wire, rotation or addon change: the in-game bridge stays 4.5.1 and the strip is unchanged.",
                "If the error persists, run /mcb guard and send the tainted button/source line.",
                "macOS 4.5.5 Apple Silicon build ships from the same tag; it shares the engine, wire and bridge 4.5.1 and keeps the native Avalonia shell (the Windows Midnight theme is not ported).",
            }),
        new Release(
            "4.5.4", "Voidlight", "2026-10-26",
            "Stops the in-game secret-cooldown error flood (bridge 4.5.1).",
            new[]
            {
                "Bridge 4.5.1 pauses Blizzard's assisted-combat icon refresh only while cooldowns are secret, on override/vehicle buttons and on any button the client names as tainted.",
                "This stops the thousands of ActionButton.lua:847 SetCooldown 'secret values' errors logged while the companion pressed in combat.",
                "Clean main/multi-bar buttons, C_AssistedCombat, key delivery, the strip and the wire format are unchanged.",
                "In game: /mcb guard reports the tainted button/addon; /mcb guard off restores the stock assisted-combat refresh.",
            }),
        new Release(
            "4.5.3", "Voidlight", "2026-10-26",
            "The status card and log now name the toggle state and any hold.",
            new[]
            {
                "The addon's 14 toggles are logged: on first sight, on every change, and summarised in Details.",
                "A hold reads 'holding - <reason>' (Main off in game, typing, out of combat, link lost) instead of the green 'waiting for suggestion'.",
                "'Main off in game' also names the fix: turn Main ON in the overlay or /mcb t Main on.",
                "Same wording in the classic shell; the strip, protocol and key delivery are unchanged.",
            }),
        new Release(
            "4.5.2", "Voidlight", "2026-10-25",
            "Edge and corner resizing works again.",
            new[]
            {
                "The window declares its four border regions again, so the resize arrows and edge/corner drag are back.",
                "The title bar keeps drag-to-move below the top grip, and the caption buttons still take clicks.",
                "Same Midnight chrome: the frame stays invisible while the system sizes the window.",
            }),
        new Release(
            "4.5.1", "Voidlight", "2026-10-25",
            "Live resizing, wrapped log lines and a changelog that is never cut.",
            new[]
            {
                "Log lines wrap instead of being clipped, with continuation lines aligned under the message.",
                "The Details disclosure re-clamps live while you resize, so the changelog is never cut off.",
                "Classic toggle rows and console cards stack into one column on narrow windows.",
                "Transport buttons, type and card padding follow the window width tier.",
            }),
        new Release(
            "4.5.0", "Voidlight", "2026-10-25",
            "Midnight palette, gradient surfaces and no title-bar band.",
            new[]
            {
                "Midnight palette: void-violet ground, amethyst accents, Sunwell gold, silver text.",
                "Gradients replace flat fills on the ground, title bar, buttons, cards and pill.",
                "The white band above the title bar is gone; edge resizing still works.",
                "Changelog lives in Details; the full history is in docs/CHANGELOG.md.",
            }),
        new Release(
            "4.4.0", "Wayfinder", "2026-10-25",
            "Transport rework, foreground-gated detection, quiet bridge log.",
            Array.Empty<string>()),
        new Release(
            "4.3.0", "Namesake", "2026-10-18",
            "Addon renamed to MaxDps Companion Bridge; the in-game-config shell became the default.",
            Array.Empty<string>()),
        new Release(
            "4.2.0", "Nightblade", "2026-10-18",
            "CC intelligence: auto-eligible CC narrowed, 12.1.5 DR ladder, manual party-CC hold.",
            Array.Empty<string>()),
        new Release(
            "4.1.0", "Paragon", "2026-10-18",
            "Audit remediation across the addon hot path, the Windows engine and the Mac host.",
            Array.Empty<string>()),
        new Release(
            "4.0.4", "Aegis", "2026-10-17",
            "In-game Main toggle became the master switch.",
            Array.Empty<string>()),
    };

    /// <summary>The release this build ships (index 0, enforced by tests).</summary>
    public static Release Current => All[0];

    /// <summary>
    /// "4.5.0 Voidlight  ·  4.4.0 Wayfinder  ·  ..." - the one-line history tail.
    /// Capped to the four most recent releases (plus an ellipsis) so the line
    /// stays on one row at the default width and the Details body keeps fitting.
    /// </summary>
    public static string EarlierSummary
    {
        get
        {
            var text = string.Join("  \u00B7  ", All.Skip(1).Take(4).Select(r => r.Label));
            return All.Count > 5 ? text + "  \u00B7  \u2026" : text;
        }
    }
}
