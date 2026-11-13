namespace BUG_TerraGrid;

/// <summary>Messages; the game's own strings are used where it has them</summary>
internal static class Text
{
    public static string LackOfItem => "物品不足".Translate();
    public static string NotEnoughSoilPile => "沙土不足".Translate();
    public static string OutOfRange => "目标超出范围".Translate() + " — painting continues when you get close to the blue squares.";

    public const string SelectStart = "Click the first corner";
    public const string PitInSelection = "A Dark Fog base pit is in the selection; remove it with the normal foundation brush first.";
    public const string SelectFoundation = "Pick a foundation first.";
    public const string Cancelled = "Area painting cancelled.";
    public const string LeftPlanet = "Area painting stopped: you left the planet.";
    public const string Failed = "Area painting stopped; see the BepInEx log.";
    public const string LongPath = "long way around";

    public static string Mode(bool on) => on ? "Area painting on" : "Area painting off";
    public static string Way(bool shortest) => shortest ? "Short way between corners" : "Long way around the planet";
    public static string Paused(bool paused) => paused ? "Area painting paused" : "Area painting resumed";
    public static string Selected(int columns, int rows, int count) => $"Selected: {columns} × {rows} ({count} tiles)";
    public static string Painting(int done, int total, bool paused) => $"Painting: {done}/{total} ({(total > 0 ? done * 100 / total : 0)}%)" + (paused ? " (paused)" : "");
    public static string Complete(int tiles) => $"Area painting done: {tiles} tiles.";
    public static string LackOfItems(int need, int have) => $"{LackOfItem} — need {need}, have {have}.";
    public static string NotEnoughSoil(long need, long have) => $"{NotEnoughSoilPile} — need ≈{need}, have {have}.";
}
