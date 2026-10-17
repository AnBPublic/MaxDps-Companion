namespace MaxDpsCompanion;

// Verbatim from the tail of Ui/DesignTokens.cs (Windows app): the semantic
// status-tone enum shared with Diagnostics/InstallDoctor. Copied (not moved)
// so Windows stays byte-identical; Core owns this copy for the doctor.

/// <summary>Semantic status tones; never used as the only signal (glyph/text accompanies).</summary>
internal enum StatusTone { Neutral, Success, Warning, Danger, Info, Muted }
