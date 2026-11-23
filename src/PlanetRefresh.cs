using System;
using HarmonyLib;
using UnityEngine;

namespace BUG_TerraGrid;

/// <summary>Planet-wide refreshes the game runs after every terrain reform call, run at most twice a second while a job paints</summary>
/// <remarks>Each native flatten/restore call re-renders the planet's whole heightmap cubemap and recounts the land of the whole
/// planet. Painting one block per frame did both every frame, which cost most of the frame rate on large areas. Only calls made
/// by <see cref="Defer{T}"/> on the main thread are held back; everything else (other mods, planet generation threads) runs as usual.</remarks>
[HarmonyPatch]
internal static class PlanetRefresh
{
    [ThreadStatic] private static bool _deferring;
    private static PlanetFactory? _heightmap;
    private static PlanetData? _land;
    private static float _flushedAt;

    internal static T Defer<T>(Func<T> body)
    {
        _deferring = true;
        try { return body(); }
        finally { _deferring = false; }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RenderLocalPlanetHeightmap))]
    private static bool Heightmap(PlanetFactory __instance)
    {
        if (!_deferring) return true;
        _heightmap = __instance;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlanetAlgorithm), nameof(PlanetAlgorithm.CalcLandPercent))]
    private static bool Land(PlanetData _planet)
    {
        if (!_deferring) return true;
        _land = _planet;
        return false;
    }

    internal static bool Pending => _heightmap != null || _land != null;

    /// <summary>Run the held-back refreshes; unless <paramref name="now"/>, at most twice a second</summary>
    internal static void Flush(bool now)
    {
        if (!Pending || (!now && Time.unscaledTime - _flushedAt < 0.5f)) return;
        _flushedAt = Time.unscaledTime;
        PlanetFactory? factory = _heightmap; PlanetData? planet = _land;
        _heightmap = null; _land = null;
        if (factory != null && GameMain.isRunning) factory.RenderLocalPlanetHeightmap();
        if (planet != null) PlanetAlgorithm.CalcLandPercent(planet);
    }
}
