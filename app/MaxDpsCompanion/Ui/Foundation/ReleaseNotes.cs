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
