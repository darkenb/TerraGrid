using UnityEngine;

namespace BUG_TerraGrid;

/// <summary>Terrain stuck at another foundation level, released before a block is painted (setting, off by default)</summary>
/// <remarks>On planets that build by height (<see cref="PlanetData.levelized"/>) every terrain point keeps the level (plane,
/// 1.33 m each) of the brush that first flattened it fully. Native flatten skips points that are already fully flattened, so
/// such a point never comes down again: a patch once painted on a hilltop stays a step above the foundation around it.
/// Restoring the terrain first fixes it by hand; this does the same for just those points under the block.</remarks>
internal static class LockedLevels
{
    internal static bool Enabled { get; set; }
    /// <summary>Points released in the running job, for the log</summary>
    internal static int Released;
    /// <summary>Native flatten's "full" core: squared distance to the nearest brush point</summary>
    private const float Core2 = 8f;

    /// <summary>Level a brush centred at <paramref name="centerMagnitude"/> flattens to, as native flatten computes it</summary>
    public static int Plane(float centerMagnitude, float realRadius) =>
        Mathf.Clamp(Mathf.RoundToInt((centerMagnitude - 0.2f - realRadius) / 1.3333333f), 0, 3);

    /// <summary>Stored level of terrain point <paramref name="index"/> (PlanetRawData.SetModPlane's bits)</summary>
    public static int PlaneOf(byte[] modData, int index) => (modData[index >> 1] >> (((index & 1) << 2) + 2)) & 3;

    /// <summary>Set fully flattened points at another level under <paramref name="points"/> back to unmodified terrain</summary>
    /// <returns>Points released; the flatten that follows then lays them at the brush's level</returns>
    internal static int Release(PlanetData planet, Vector3[] points, int count, Vector3 center)
    {
        if (!Enabled || !planet.levelized || planet.data?.modData == null) return 0;
        PlanetRawData data = planet.data;
        Vector3[] vertices = data.vertices;
        float radius = planet.realRadius;
        int target = Plane(center.magnitude, radius), stride = data.stride, length = data.dataLength, released = 0;
        int reach = Mathf.CeilToInt(Mathf.Sqrt(Core2) / (radius * Mathf.PI / (planet.precision * 2f))) + 1;
        for (int p = 0; p < count; p++)
        {
            Vector3 point = points[p];
            int middle = data.QueryIndex(point);
            for (int j = -reach; j <= reach; j++)
                for (int k = -reach; k <= reach; k++)
                {
                    int v = middle + j * stride + k;
                    if ((uint)v >= (uint)length || (vertices[v] * radius - point).sqrMagnitude > Core2) continue;
                    if (data.GetModLevel(v) != 3 || PlaneOf(data.modData, v) == target) continue;
                    planet.SetHeightMapModLevel(v, 0);   // marks the mesh dirty, so the patch redraws even if flatten leaves it
                    released++;
                }
        }
        Released += released;
        return released;
    }
}
