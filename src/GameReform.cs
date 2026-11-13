using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace BUG_TerraGrid;

/// <summary>Brush settings captured when a selection is confirmed, so later tool changes cannot alter a running job</summary>
internal readonly struct Brush(int mode, int color, int type, bool bury, ItemProto? item)
{
    public readonly int Mode = mode, Color = color, Type = type;
    public readonly bool Bury = bury;
    public readonly ItemProto? Item = item;
    public static Brush Capture(BuildTool_Reform tool) => new(tool.reformMode, tool.brushColor, tool.brushType, tool.buryVeins, tool.handItem);
}

internal static class GameReform
{
    private static readonly MethodInfo Prepare = AccessTools.Method(typeof(BuildTool_Reform), "PrepareBrushPoints");
    private static readonly MethodInfo Flatten = AccessTools.Method(typeof(BuildTool_Reform), "DoFlattenExecute");
    private static readonly MethodInfo Restore = AccessTools.Method(typeof(BuildTool_Reform), "DoRestoreExecute");
    private static readonly FieldInfo Pit = AccessTools.Field(typeof(BuildTool_Reform), "removeBasePitRuinId");
    private static readonly FieldInfo Line = AccessTools.Field(typeof(BuildTool_Reform), "lineDrawingMode");
    private static readonly FieldInfo Package = AccessTools.Field(typeof(BuildTool), "tmpPackage");
    private static readonly MethodInfo HandCount = AccessTools.PropertySetter(typeof(Player), nameof(Player.inhandItemCount));
    private static readonly MethodInfo HandInc = AccessTools.PropertySetter(typeof(Player), nameof(Player.inhandItemInc));
    private const float Radius = 0.990946f;

    /// <summary>Check every tile before painting: Dark Fog pits, foundation items and soil (range is checked per block while painting)</summary>
    /// <returns>Null when the whole selection can be painted, otherwise the reason it cannot</returns>
    internal static string? Measure(BuildTool_Reform tool, Brush brush, List<List<Vector3>> blocks, out int tiles, out long sand)
    {
        Player player = tool.player;
        long cost = 0, gain = 0;
        // count: tiles to paint, including ones only re-textured; items: tiles not yet foundation (native snap points), the only ones that use an item
        int count = 0, items = 0;
        var snapped = new List<Vector3>();
        string? error = WithBrush(tool, brush, () =>
        {
            foreach (List<Vector3> block in blocks)
            {
                snapped.Clear();
                foreach (Vector3 point in block)
                {
                    Snap(tool, point);
                    if ((int)Pit.GetValue(tool) != 0) return Text.PitInSelection;
                    if (tool.cursorIndices[0] < 0) continue;
                    count++;
                    if (brush.Mode == 1)
                    {
                        int c = 0, g = 0;
                        Compute(tool, Radius, ref c, ref g);
                        cost += c; gain += g;
                    }
                    else { items += tool.cursorPointCount; for (int i = 0; i < tool.cursorPointCount; i++) snapped.Add(tool.cursorPoints[i]); }
                }
                if (snapped.Count > 0)
                {
                    // One native call per block: per-tile calls count every neighbour's fade ring again (~radius+3..12 m),
                    // which overstated soil many times. Levels only rise, so painting tile by tile costs the same as this.
                    Vector3 center = Center(block, out float radius);
                    int c = 0, g = 0;
                    tool.factory.ComputeFlattenTerrainReform(snapped.ToArray(), center, radius, snapped.Count, ref c, ref g);
                    cost += c; gain += g;
                }
            }
            return (string?)null;
        });
        tiles = count; sand = 0;
        if (error != null || count == 0) return error;
        if (brush.Mode == 1) sand = cost - gain;
        else
        {
            if (brush.Item == null) return Text.SelectFoundation;
            int id = brush.Item.ID;
            int available = ItemCount(player, id) + (player.inhandItemId == id ? player.inhandItemCount : 0);
            if (available < items) return Text.LackOfItems(items, available);
            sand = WithHandGuard(player, id, () =>
            {
                CopyPackage(tool);
                return (long)tool.GetNeedSandCountByInc(id, items, Clamp(cost), Clamp(gain));
            });
            if (GameMain.data.history.HasFeatureKey(1100001) && GameMain.sandboxToolsEnabled) sand = 0;
        }
        // Soil is estimated per block before neighbouring blocks change the terrain, so this is an upper bound.
        if (sand > player.sandCount) return Text.NotEnoughSoil(sand, player.sandCount);
        return null;
    }

    /// <summary>Reform a whole block with one native call, the way the vanilla brush reforms its square</summary>
    /// <remarks>Every native call rebuilds the terrain mesh, re-renders the heightmap, recounts the planet's land and syncs GPU buffers;
    /// Doing that per tile is what makes painting lag. Levels only rise, so one call over all points gives the same terrain.</remarks>
    /// <returns>False when the block cannot be painted (resources ran out); the reason is shown to the player</returns>
    internal static bool Apply(BuildTool_Reform tool, Brush brush, List<Vector3> block) => WithBrush(tool, brush, () =>
    {
        var points = new List<Vector3>(block.Count); var indices = new List<int>(block.Count);
        foreach (Vector3 point in block)
        {
            Snap(tool, point);
            if ((int)Pit.GetValue(tool) != 0) { UIRealtimeTip.Popup(Text.PitInSelection); return false; }
            if (tool.cursorIndices[0] < 0) continue;
            indices.Add(tool.cursorIndices[0]);
            for (int i = 0; i < tool.cursorPointCount; i++) points.Add(tool.cursorPoints[i]);
        }
        if (indices.Count == 0) return true;
        // Native code walks brushSize^2 indices; restore copies them into a buffer sized by its first caller (100).
        int side = (int)Math.Ceiling(Math.Sqrt(indices.Count)), size = Math.Max(100, Math.Max(side * side, points.Count));
        tool.cursorPoints = new Vector3[size]; points.CopyTo(tool.cursorPoints);
        tool.cursorIndices = new int[size];
        for (int i = 0; i < size; i++) tool.cursorIndices[i] = i < indices.Count ? indices[i] : -1;
        tool.cursorPointCount = points.Count; tool.brushSize = side;
        tool.reformCenterPoint = Center(block, out float radius);
        int cost = 0, gain = 0;
        Compute(tool, radius, ref cost, ref gain);
        if (brush.Mode == 1) return Execute(tool, Restore, radius, cost - gain);
        if (brush.Item == null) return false;
        Player player = tool.player;
        return WithHandGuard(player, brush.Item.ID, () =>
        {
            TopUp(player, brush.Item.ID, tool.cursorPointCount - player.inhandItemCount);
            if (NativeCount(player, brush.Item.ID) + player.inhandItemCount < tool.cursorPointCount)
            { UIRealtimeTip.Popup(Text.LackOfItem, true, 1); return false; }
            CopyPackage(tool);
            int sandNeed = tool.GetNeedSandCountByInc(brush.Item.ID, tool.cursorPointCount, cost, gain);
            if (GameMain.data.history.HasFeatureKey(1100001) && GameMain.sandboxToolsEnabled) sandNeed = 0;
            return Execute(tool, Flatten, radius, sandNeed);
        });
    });

    /// <summary>A delivery mod's replacement for the GetItemCount call in native <c>DoFlattenExecute</c>, or null</summary>
    /// <remarks>DeliverySlotsTweaks (UseLogisticSlots) transpiles that call to a static <c>GetItemCount(StorageComponent, int)</c>
    /// that adds the mecha's logistic slots, and consumes from them too.</remarks>
    private static MethodInfo? DeliveryCount() => Harmony.GetPatchInfo(Flatten)?.Transpilers
        .Select(p => p.PatchMethod.DeclaringType?.GetMethod("GetItemCount", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(StorageComponent), typeof(int) }, null))
        .FirstOrDefault(m => m != null && m.ReturnType == typeof(int));

    /// <summary>Item count as native <c>DoFlattenExecute</c> sees it</summary>
    private static int NativeCount(Player player, int itemId)
    {
        MethodInfo? count = DeliveryCount();
        return count == null ? player.package.GetItemCount(itemId) : (int)count.Invoke(null, new object[] { player.package, itemId });
    }

    /// <summary>Items available for painting: inventory plus the mecha's logistic slots, with or without a delivery mod</summary>
    internal static int ItemCount(Player player, int itemId) =>
        NativeCount(player, itemId) + (DeliveryCount() == null && player.deliveryPackage is { unlocked: true } delivery ? delivery.GetItemCount(itemId) : 0);

    /// <summary>Without a delivery mod native code only takes from the inventory: move the shortfall in from the logistic slots</summary>
    private static void TopUp(Player player, int itemId, int need)
    {
        DeliveryPackage? delivery = player.deliveryPackage;
        if (DeliveryCount() != null || delivery is not { unlocked: true }) return;
        int missing = need - player.package.GetItemCount(itemId);
        while (missing > 0)
        {
            int id = itemId, count = missing;
            delivery.TakeItems(ref id, ref count, out int inc);
            if (id != itemId || count <= 0) return;
            int added = player.package.AddItemStacked(itemId, count, inc, out int remainInc);
            // Inventory full: the rest goes back to its logistic slot, so nothing is lost.
            if (added < count) { delivery.AddItem(itemId, count - added, remainInc, out _); return; }
            missing -= added;
        }
    }

    private static bool Execute(BuildTool_Reform tool, MethodInfo action, float radius, int need)
    {
        if (tool.player.sandCount - need < 0) { UIRealtimeTip.Popup(Text.NotEnoughSoilPile); return false; }
        action.Invoke(tool, new object[] { radius, need, true });
        return true;
    }

    private static void Compute(BuildTool_Reform tool, float radius, ref int cost, ref int gain)
    {
        if (tool.reformMode == 1)
            tool.factory.ComputeRestoreTerrainReform(tool.cursorPoints, tool.cursorIndices, tool.brushSize * tool.brushSize, tool.reformCenterPoint, radius, tool.cursorPointCount, ref cost, ref gain);
        else
            tool.factory.ComputeFlattenTerrainReform(tool.cursorPoints, tool.reformCenterPoint, radius, tool.cursorPointCount, ref cost, ref gain);
    }

    /// <summary>Surface point under the centroid of <paramref name="points"/>, and a radius reaching all of them (vanilla brush: 0.99 x size)</summary>
    internal static Vector3 Center(List<Vector3> points, out float radius)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points) sum += p;
        Vector3 center = sum.normalized * points[0].magnitude;
        float reach = 0f;
        foreach (Vector3 p in points) reach = Math.Max(reach, (p - center).magnitude);
        radius = reach + Radius;
        return center;
    }

    private static void CopyPackage(BuildTool_Reform tool)
    {
        var scratch = (StorageComponent)Package.GetValue(tool);
        if (scratch == null || scratch.size != tool.player.package.size)
        { scratch = new StorageComponent(tool.player.package.size); Package.SetValue(tool, scratch); }
        Array.Copy(tool.player.package.grids, scratch.grids, scratch.size);
    }

    private static int Clamp(long value) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));

    /// <summary>Run <paramref name="body"/> with the tool set up as a native brush with the captured <paramref name="brush"/>; restores all tool state</summary>
    private static T WithBrush<T>(BuildTool_Reform tool, Brush brush, Func<T> body)
    {
        var points = tool.cursorPoints; var indices = tool.cursorIndices;
        int count = tool.cursorPointCount, size = tool.brushSize;
        var center = tool.reformCenterPoint; int oldLine = (int)Line.GetValue(tool);
        int mode = tool.reformMode, color = tool.brushColor, type = tool.brushType;
        bool bury = tool.buryVeins; ItemProto item = tool.handItem;
        try
        {
            tool.cursorPoints = new Vector3[100]; tool.cursorIndices = new int[100];
            for (int i = 0; i < 100; i++) tool.cursorIndices[i] = -1;
            tool.brushSize = 1; Line.SetValue(tool, 0);
            tool.reformMode = brush.Mode; tool.brushColor = brush.Color; tool.brushType = brush.Type;
            tool.buryVeins = brush.Bury; tool.handItem = brush.Item!;
            return body();
        }
        finally
        {
            tool.cursorPoints = points; tool.cursorIndices = indices;
            tool.cursorPointCount = count; tool.brushSize = size; tool.reformCenterPoint = center;
            Line.SetValue(tool, oldLine);
            tool.reformMode = mode; tool.brushColor = color; tool.brushType = type; tool.buryVeins = bury; tool.handItem = item;
        }
    }

    /// <summary>Snap the 1x1 native brush (set up by <see cref="WithBrush"/>) to the tile at <paramref name="point"/></summary>
    private static void Snap(BuildTool_Reform tool, Vector3 point)
    {
        tool.cursorIndices[0] = -1;
        Prepare.Invoke(tool, new object[] { point });
    }

    /// <summary>Hide a different item held in hand, so native code neither counts nor consumes it as foundation</summary>
    private static T WithHandGuard<T>(Player player, int itemId, Func<T> body)
    {
        if (player.inhandItemId == itemId || player.inhandItemCount == 0) return body();
        int count = player.inhandItemCount, inc = player.inhandItemInc;
        HandCount.Invoke(player, new object[] { 0 }); HandInc.Invoke(player, new object[] { 0 });
        try { return body(); }
        finally { HandCount.Invoke(player, new object[] { count }); HandInc.Invoke(player, new object[] { inc }); }
    }
}
