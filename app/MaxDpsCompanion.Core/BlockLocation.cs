namespace MaxDpsCompanion;

// Wave B (4.1 "Paragon"): SINGLE SOURCE. This record used to be declared in
// the Windows BlockLocator.cs as well; that copy was removed so the locator and
// the Core engine share one identity.
internal readonly record struct BlockLocation(int OffsetX, int OffsetY, int CellSize);
