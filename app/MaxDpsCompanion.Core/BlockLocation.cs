namespace MaxDpsCompanion;

// Verbatim from BlockLocator.cs (Windows file stays as-is): the sweep result
// type surfaced by RotationEngine.Locate / LocationChanged. Copied (not moved)
// so Windows stays byte-identical.
internal readonly record struct BlockLocation(int OffsetX, int OffsetY, int CellSize);
