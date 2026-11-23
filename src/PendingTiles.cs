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
    // One mesh per block, built once: rebuilding one mesh of every remaining tile several times a second cost seconds of
    // frame time on large areas. Keyed by the block's list (reference equality).
    private static readonly Dictionary<List<Vector3>, Mesh> Meshes = new();
    private const int BuildsPerFrame = 32;
    private static Material? _material;
    private static bool _noShader;
    /// <summary>Config: draw the pending blocks at all</summary>
    internal static bool Enabled { get; set; } = true;

    /// <summary>Draw <paramref name="blocks"/> this frame, building at most <see cref="BuildsPerFrame"/> new block meshes</summary>
    // shortcut: one draw call per remaining block; merge blocks into larger meshes if planet-sized jobs show draw-call cost.
    internal static void Draw(PlanetData planet, List<List<Vector3>> blocks)
    {
        if (!Enabled || blocks.Count == 0 || planet != GameMain.localPlanet || planet.data == null || !Ready()) return;
        _material!.color = new Color(Tint.r, Tint.g, Tint.b, 0.3f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f));
        int built = 0;
        foreach (List<Vector3> block in blocks)
        {
            if (!Meshes.TryGetValue(block, out Mesh? mesh))
            {
                if (block.Count == 0 || built++ >= BuildsPerFrame) continue;
                mesh = BuildMesh(planet, block);
                Meshes[block] = mesh;
            }
            Graphics.DrawMesh(mesh, Matrix4x4.identity, _material, 0, null, 0, null, ShadowCastingMode.Off, false);
        }
    }

    private static Mesh BuildMesh(PlanetData planet, List<Vector3> block)
    {
        var vertices = new List<Vector3>(block.Count * 4); var triangles = new List<int>(block.Count * 6);
        float floor = planet.realRadius + 0.2f, spacing = 2f * Mathf.PI * planet.realRadius / (planet.factory.platformSystem.segment * 5f);
        // On the future foundation surface, or just above higher terrain so it is never buried.
        Build(new List<List<Vector3>> { block }, spacing * 0.42f, p => Math.Max(planet.data.QueryModifiedHeight(p), floor) + 0.15f, vertices, triangles);
        var mesh = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        var colors = new Color[vertices.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        mesh.colors = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Drop the mesh of a block that was painted or lost tiles</summary>
    internal static void Forget(List<Vector3> block)
    {
        if (!Meshes.TryGetValue(block, out Mesh? mesh)) return;
        Meshes.Remove(block);
        UnityEngine.Object.Destroy(mesh);
    }

    /// <summary>Forget all meshes once the job ends</summary>
    internal static void Clear()
    {
        foreach (Mesh mesh in Meshes.Values) UnityEngine.Object.Destroy(mesh);
        Meshes.Clear();
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
