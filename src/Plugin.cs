using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

[assembly: AssemblyVersion(BUG_TerraGrid.Plugin.Version + ".0")]
[assembly: AssemblyFileVersion(BUG_TerraGrid.Plugin.Version + ".0")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Check")]

namespace BUG_TerraGrid;

/// <summary>Paint foundation over a whole rectangle with the game's foundation tool</summary>
[BepInPlugin(Guid, Name, Version)]
[BepInIncompatibility("ExcelDsp.Painter")]   // changes the same foundation tool hook; both at once would fight over it
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "bug.dsp.terragrid", Name = "BUG_TerraGrid", Version = "1.0.0";
    internal static ManualLogSource? Log;
    private Harmony? _harmony;
    private ConfigEntry<KeyboardShortcut>? _areaKey, _wayKey, _cancelKey, _pauseKey;

    public void Awake()
    {
        Log = Logger;
        _areaKey = Config.Bind("Keys", "AreaMode", KeyboardShortcut.Deserialize("D + LeftControl"), "Turn area mode of the foundation tool on or off (off also closes the tool)");
        _wayKey = Config.Bind("Keys", "LongWayAround", KeyboardShortcut.Deserialize("P + LeftControl"), "Between the two corners, go the short way or the long way around the planet");
        _cancelKey = Config.Bind("Keys", "Cancel", KeyboardShortcut.Deserialize("D + LeftControl + LeftShift"), "Cancel the painting in progress");
        _pauseKey = Config.Bind("Keys", "PauseResume", KeyboardShortcut.Deserialize("P + LeftControl + LeftShift"), "Pause or resume the painting in progress");
        var blockSize = Config.Bind("Painting", "BlockSize", 12, new ConfigDescription("Areas are painted in BlockSize x BlockSize blocks, one block per frame. Larger is faster but each frame does more work (restore: at most 10)", new AcceptableValueRange<int>(1, 20)));
        var pending = Config.Bind("Painting", "ShowPendingTiles", true, "Mark tiles still to be painted (for example out of mecha range) with blue squares");
        PaintJob.BlockSize = blockSize.Value;
        blockSize.SettingChanged += (_, _) => PaintJob.BlockSize = blockSize.Value;
        PendingTiles.Enabled = pending.Value;
        pending.SettingChanged += (_, _) => PendingTiles.Enabled = pending.Value;

        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    public void OnDestroy()
    {
        AreaTool.On = false;
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    public void Update()
    {
        // Shift combinations first, so Ctrl+Shift+D does not also count as Ctrl+D.
        if (_cancelKey!.Value.IsDown()) PaintJob.Cancel();
        else if (_areaKey!.Value.IsDown())
        {
            AreaTool.On = !AreaTool.On;
            if (!AreaTool.On) CloseTool();
            UIRealtimeTip.Popup(Text.Mode(AreaTool.On), false);
        }
        if (_pauseKey!.Value.IsDown())
        {
            if (PaintJob.Current != null) { PaintJob.Paused = !PaintJob.Paused; UIRealtimeTip.Popup(Text.Paused(PaintJob.Paused), false); }
        }
        else if (_wayKey!.Value.IsDown() && AreaTool.On)
        {
            AreaTool.ShortWay = !AreaTool.ShortWay;
            UIRealtimeTip.Popup(Text.Way(AreaTool.ShortWay), false);
        }
        PaintJob.Tick();
    }

    /// <summary>Leave the foundation tool the way Esc or a right click does</summary>
    private static void CloseTool()
    {
        Player player = GameMain.mainPlayer;
        BuildTool_Reform? tool = player?.controller?.actionBuild?.reformTool;
        if (tool is null || !tool.active) return;
        player!.SetHandItems(0, 0);
        tool._Close();
    }
}
