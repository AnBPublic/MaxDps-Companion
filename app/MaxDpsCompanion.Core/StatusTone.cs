namespace MaxDpsCompanion;

// Wave B (4.1 Paragon): SINGLE SOURCE. This enum used to be duplicated at the
// tail of the Windows app's Ui/DesignTokens.cs; that copy was removed and the
// Windows app consumes this one through the Core project reference.

/// <summary>Semantic status tones; never used as the only signal (glyph/text accompanies).</summary>
internal enum StatusTone { Neutral, Success, Warning, Danger, Info, Muted }
