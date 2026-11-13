using System;
using HarmonyLib;
using UnityEngine;

namespace BUG_TerraGrid;

/// <summary>Area mode of the game's foundation tool: first click sets a corner, the second paints the rectangle up to the cursor</summary>
/// <remarks>The game's grid overlay marks the first brushSize² entries of <see cref="BuildTool_Reform.cursorIndices"/>, so the area
/// is shown through a square brush big enough to hold it, with spare slots set to -1. The tool's own buffers and brush size
/// are put back when area mode ends or the tool closes.</remarks>
[HarmonyPatch(typeof(BuildTool_Reform))]
internal static class AreaTool
{
    private static readonly AreaGrid Area = new();
    private static BuildTool_Reform? _tool;
    private static Vector3[]? _savedPoints;
    private static int[]? _savedIndices;
    private static int _savedSize, _shownSize;
    private static Vector3[] _points = new Vector3[400];
    private static int[] _indices = new int[400];
    private static Vector3? _corner;
    private static bool _on;
    private static (PlanetData?, long, long, bool) _lastKey;

    public static bool On { get => _on; set { if (!value) Release(); _on = value; } }
    public static bool ShortWay { get; set; } = true;

    [HarmonyPrefix, HarmonyPatch("UpdateBrushMode")]
    private static bool UpdateBrushMode(BuildTool_Reform __instance, Vector3 currentCastPos)
    {
        if (!_on) return true;
        Hover(__instance, currentCastPos);
        return false;
    }

    [HarmonyPrefix, HarmonyPatch("_OnClose")]
    private static void OnClose(BuildTool_Reform __instance) { if (ReferenceEquals(__instance, _tool)) Release(); }

    [HarmonyPrefix, HarmonyPatch("_OnFree")]
    private static void OnFree(BuildTool_Reform __instance) { if (ReferenceEquals(__instance, _tool)) Release(); }

    private static void Hover(BuildTool_Reform tool, Vector3 cursor)
    {
        if (!ReferenceEquals(_tool, tool))
        {
            Release();
            _tool = tool; _savedPoints = tool.cursorPoints; _savedIndices = tool.cursorIndices; _savedSize = _shownSize = tool.brushSize;
        }
        if (tool.brushSize != _shownSize) _savedSize = tool.brushSize;   // set by another mod (Galactic Scale's size buttons): keep it for later
        // The area only changes when a corner moves to another tile: a large area is not rebuilt every frame.
        int segment = tool.planet.aux.mainGrid.segment;
        var key = (tool.planet, AreaGrid.TileKey(segment, _corner ?? cursor), AreaGrid.TileKey(segment, cursor), ShortWay);
        if (!key.Equals(_lastKey)) { Area.Select(tool.planet, _corner ?? cursor, cursor, ShortWay); _lastKey = key; }
        int count = Area.Points.Count;
        Show(tool, count, count > 0 ? Area.Points[count / 2] : cursor);

        string text = _corner.HasValue ? Text.Selected(Area.ColumnCount, Area.RowCount, count) : Text.SelectStart;
        if (!ShortWay) text += " — " + Text.LongPath;
        if (PaintJob.Current is { } job) text = Text.Painting(job.Done, job.Total, PaintJob.Paused) + "\n" + text;
        tool.actionBuild.model.cursorText = text;
        UICursor.SetCursor(ECursor.Reform);

        if (!VFInput._buildConfirm.onDown || VFInput.onGUI) return;
        if (!_corner.HasValue) { _corner = cursor; return; }
        // A refused area keeps its corner, so it can be retried after fetching items or soil.
        if (PaintJob.Start(tool, Area, Brush.Capture(tool))) _corner = null;
    }

    /// <summary>Put the area in the tool's cursor buffers, as a square brush of at least <paramref name="count"/> slots</summary>
    private static void Show(BuildTool_Reform tool, int count, Vector3 center)
    {
        int side = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count))), slots = side * side;
        if (_indices.Length < slots) { _indices = new int[slots]; _points = new Vector3[slots]; }
        Area.Points.CopyTo(_points);
        Area.Indices.CopyTo(_indices);
        for (int i = count; i < slots; i++) _indices[i] = -1;
        tool.cursorPoints = _points; tool.cursorIndices = _indices;
        tool.cursorPointCount = count; tool.brushSize = _shownSize = side; tool.reformCenterPoint = center;
    }

    /// <summary>Give the tool its own buffers and brush size back; a running paint job continues</summary>
    private static void Release()
    {
        _corner = null;
        if (_tool != null)
        {
            _tool.cursorPoints = _savedPoints!; _tool.cursorIndices = _savedIndices!;
            _tool.brushSize = _savedSize; _tool.cursorPointCount = 0;
            for (int i = 0; i < _tool.cursorIndices.Length; i++) _tool.cursorIndices[i] = -1;
        }
        _tool = null; _savedPoints = null; _savedIndices = null; ShortWay = true; _lastKey = default;
    }
}
