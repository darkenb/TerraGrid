using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace BUG_TerraGrid;

/// <summary>Pulsing translucent tiles over the blocks a paint job has not reached yet (e.g. out of mecha range)</summary>
/// <remarks>Drawn like the game's own gizmos: planet-local coordinates as world space, layer 0, every frame.</remarks>
internal static class PendingTiles
{
    private static readonly Color Tint = new(0.35f, 0.85f, 1f);
    private static Mesh? _mesh;
    private static Material? _material;
    private static bool _noShader;
    /// <summary>Config: draw the pending blocks at all</summary>
    internal static bool Enabled { get; set; } = true;
    private static int _shownBlocks = -1;
    private static float _builtAt;

    /// <summary>Draw <paramref name="blocks"/> this frame; the mesh is rebuilt at most 4 times a second while blocks are painted</summary>
    internal static void Draw(PlanetData planet, List<List<Vector3>> blocks)
    {
        if (!Enabled || blocks.Count == 0 || planet != GameMain.localPlanet || planet.data == null || !Ready()) return;
        if (_mesh == null || (_shownBlocks != blocks.Count && Time.unscaledTime - _builtAt > 0.25f))
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float floor = planet.realRadius + 0.2f, spacing = 2f * Mathf.PI * planet.realRadius / (planet.factory.platformSystem.segment * 5f);
            // On the future foundation surface, or just above higher terrain so it is never buried.
            Build(blocks, spacing * 0.42f, p => Math.Max(planet.data.QueryModifiedHeight(p), floor) + 0.15f, vertices, triangles);
            _mesh ??= new Mesh { indexFormat = IndexFormat.UInt32 };
            _mesh.Clear();
            _mesh.SetVertices(vertices);
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            _mesh.colors = colors;
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateBounds();
            _shownBlocks = blocks.Count; _builtAt = Time.unscaledTime;
        }
        _material!.color = new Color(Tint.r, Tint.g, Tint.b, 0.3f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f));
        Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, null, 0, null, ShadowCastingMode.Off, false);
    }

    /// <summary>Forget the mesh once the job ends</summary>
    internal static void Clear()
    {
        if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        _mesh = null; _shownBlocks = -1;
    }

    private static bool Ready()
    {
        if (_material != null) return true;
        if (_noShader) return false;
        // Always-included Unity shaders: transparent, vertex-coloured, depth-tested, no depth write.
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader == null) { _noShader = true; Plugin.Log?.LogWarning("No transparent shader found; pending blocks are not highlighted"); return false; }
        _material = new Material(shader) { renderQueue = 3000 };
        return true;
    }

    /// <summary>One square of half-size <paramref name="half"/> per tile, lying on the sphere at <paramref name="height"/></summary>
    internal static void Build(List<List<Vector3>> blocks, float half, Func<Vector3, float> height, List<Vector3> vertices, List<int> triangles)
    {
        foreach (List<Vector3> block in blocks)
            foreach (Vector3 point in block)
            {
                Vector3 up = point.normalized;
                Vector3 east = Vector3.Cross(Vector3.up, up);
                east = east.sqrMagnitude < 1e-6f ? Vector3.right : east.normalized;   // poles
                Vector3 north = Vector3.Cross(up, east);
                Vector3 center = up * height(point);
                int first = vertices.Count;
                vertices.Add(center + (-east - north) * half);
                vertices.Add(center + (east - north) * half);
                vertices.Add(center + (east + north) * half);
                vertices.Add(center + (-east + north) * half);
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }
    }
}
