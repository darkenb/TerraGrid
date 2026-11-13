using System;
using System.Collections.Generic;
using UnityEngine;

namespace BUG_TerraGrid;

/// <summary>An area being painted, one block per frame, nearest block in build range first</summary>
/// <remarks>Runs from <see cref="Tick"/> every frame, apart from the tool: closing the tool, switching items or changing the
/// brush does not stop it. Running out of items or soil, leaving the planet or <see cref="Cancel"/> does.</remarks>
internal sealed class PaintJob
{
    public static PaintJob? Current { get; private set; }
    public static bool Paused { get; set; }
    /// <summary>Side of the blocks an area is painted in (restore: at most 10, the size of the game's restore buffer)</summary>
    public static int BlockSize { get; set; } = 12;

    public readonly PlanetFactory Factory;
    /// <summary>Blocks still to paint; the centre and brush of each are at the same index</summary>
    public readonly List<List<Vector3>> Blocks = new();
    public readonly List<Vector3> Centers = new();
    public readonly List<Brush> Brushes = new();
    public int Total, Done;
    private bool _waiting;

    public PaintJob(PlanetFactory factory) => Factory = factory;

    /// <summary>Add an area; its tiles leave older pending blocks, so where areas overlap the newest brush wins</summary>
    /// <remarks>Tile points come from integer grid positions, so the same tile always gives the same vector.</remarks>
    public void Add(Brush brush, List<List<Vector3>> blocks, int count)
    {
        var newer = new HashSet<Vector3>();
        foreach (List<Vector3> block in blocks) newer.UnionWith(block);
        for (int i = Blocks.Count - 1; i >= 0; i--)
        {
            int removed = Blocks[i].RemoveAll(newer.Contains);
            if (removed == 0) continue;
            Total -= removed;
            if (Blocks[i].Count == 0) { Blocks.RemoveAt(i); Centers.RemoveAt(i); Brushes.RemoveAt(i); }
            else Centers[i] = GameReform.Center(Blocks[i], out _);
        }
        foreach (List<Vector3> block in blocks) { Blocks.Add(block); Centers.Add(GameReform.Center(block, out _)); Brushes.Add(brush); }
        Total += count; _waiting = false;
    }

    /// <summary>Check the whole area, then paint it (or add it to the running job)</summary>
    /// <returns>False when the area was refused; the reason is shown to the player</returns>
    public static bool Start(BuildTool_Reform tool, AreaGrid area, Brush brush)
    {
        int blockSize = brush.Mode == 1 ? Math.Min(BlockSize, 10) : BlockSize;
        // Soil is estimated in 24x24 blocks: their border overlap overstates it by about 1.2x, against 1.4x for 12x12.
        int estimateSize = Math.Max(24, blockSize);
        var points = new List<Vector3>(); var keys = new List<long>(); var estimateKeys = new List<long>();
        var seen = new HashSet<int>();
        for (int i = 0; i < area.Points.Count; i++)
            if (seen.Add(area.Indices[i]))
            { points.Add(area.Points[i]); keys.Add(area.BlockKey(i, blockSize)); estimateKeys.Add(area.BlockKey(i, estimateSize)); }
        string? error = GameReform.Measure(tool, brush, Split(points, estimateKeys), out int tiles, out long sand);
        Plugin.Log?.LogInfo($"Area: {points.Count} tiles ({tiles} to paint), mode {brush.Mode}, item {brush.Item?.ID ?? 0}, soil ~{sand} of {tool.player.sandCount}"
            + (error != null ? ", refused: " + error : ""));
        if (error != null) { UIRealtimeTip.Popup(error); return false; }
        if (tiles == 0) return true;

        List<List<Vector3>> blocks = Split(points, keys);
        if (Current != null && ReferenceEquals(Current.Factory, tool.factory)) { Current.Add(brush, blocks, points.Count); Tick(); return true; }
        Current = new PaintJob(tool.factory);
        Current.Add(brush, blocks, points.Count);
        tool.player.ClearOrders();
        tool.controller.actionMine.miningType = EObjectType.None; tool.controller.actionMine.miningId = 0; tool.controller.actionMine.miningTick = 0;
        GameMain.gameScenario?.NotifyOnDoReformOpt(1);
        Tick();
        return true;
    }

    /// <summary>Group points into blocks, keeping first-seen block order</summary>
    /// <remarks>Always by block, even for few tiles: the game clears plants in a circle around each painted block,
    /// so a long thin area must not become one block.</remarks>
    internal static List<List<Vector3>> Split(IList<Vector3> points, IList<long> keys)
    {
        var blocks = new List<List<Vector3>>();
        var byKey = new Dictionary<long, List<Vector3>>();
        for (int i = 0; i < points.Count; i++)
        {
            if (!byKey.TryGetValue(keys[i], out var block)) { block = new List<Vector3>(); byKey[keys[i]] = block; blocks.Add(block); }
            block.Add(points[i]);
        }
        return blocks;
    }

    /// <summary>Paint the next block; called every frame</summary>
    public static void Tick()
    {
        PaintJob? job = Current;
        if (job == null) return;
        Player player = GameMain.mainPlayer;
        if (GameMain.data == null || player == null) { Stop(false, null); return; }
        PendingTiles.Draw(job.Factory.planet, job.Blocks);
        if (GameMain.isPaused || Paused) return;
        BuildTool_Reform? tool = player.controller?.actionBuild?.reformTool;
        if (tool == null || !ReferenceEquals(player.factory, job.Factory) || !ReferenceEquals(tool.factory, job.Factory))
        { Stop(true, Text.LeftPlanet); return; }
        try
        {
            // Nearest block whose centre is in build range (the vanilla brush checks its centre); none: wait for the player.
            int next = -1; float best = player.mecha.buildArea * player.mecha.buildArea;
            for (int i = 0; i < job.Centers.Count; i++)
            {
                float distance = (job.Centers[i] - player.position).sqrMagnitude;
                if (distance <= best) { best = distance; next = i; }
            }
            if (next < 0) { if (!job._waiting) UIRealtimeTip.Popup(Text.OutOfRange); job._waiting = true; return; }
            job._waiting = false;
            // The game may commit a block before throwing, so it leaves the list first and is never painted twice.
            List<Vector3> block = job.Blocks[next]; Brush brush = job.Brushes[next];
            job.Blocks.RemoveAt(next); job.Centers.RemoveAt(next); job.Brushes.RemoveAt(next);
            if (!GameReform.Apply(tool, brush, block)) { Stop(true, null); return; }
            job.Done += block.Count;
            if (job.Blocks.Count == 0) Stop(true, job.Done > block.Count ? Text.Complete(job.Done) : null);
        }
        catch (Exception error)
        { Stop(true, Text.Failed); Plugin.Log?.LogError(error); }
    }

    public static void Cancel() { if (Current != null) Stop(true, Text.Cancelled); }

    private static void Stop(bool notify, string? message)
    {
        Current = null; Paused = false;
        PendingTiles.Clear();
        if (notify) GameMain.gameScenario?.NotifyOnDoReformOpt(2);
        if (message != null) UIRealtimeTip.Popup(message);
    }
}
