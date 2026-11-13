using System;
using System.Collections.Generic;
using UnityEngine;

namespace BUG_TerraGrid;

/// <summary>The foundation tiles between two corners, on the game's own reform grid</summary>
/// <remarks>Tile centres sit at odd tenths of a grid segment: latitude in planet segments, longitude in the
/// segments of that latitude band. Band widths and tile indices come from the game's
/// <see cref="PlanetGrid.DetermineLongitudeSegmentCount"/> and <see cref="PlatformSystem.GetReformIndexForSegment"/>,
/// so planets of any size (Galactic Scale patches both) get the same tiles as the vanilla brush.</remarks>
internal sealed class AreaGrid
{
    /// <summary>One entry per tile, in row order: surface point, reform index, row and column within the area</summary>
    public readonly List<Vector3> Points = new();
    public readonly List<int> Indices = new(), Rows = new(), Columns = new();
    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }

    public void Select(PlanetData planet, Vector3 from, Vector3 to, bool shortWay)
    {
        PlatformSystem platform = planet.factory.platformSystem;
        platform.EnsureReformData();
        Select(planet.aux.mainGrid.segment, platform.latitudeCount, planet.realRadius + 0.2f, from, to, shortWay,
            (lat, lon) => platform.GetReformIndexForSegment(lat, lon), platform.reformData.Length);
    }

    internal void Select(int segment, int latitudeCount, float radius, Vector3 from, Vector3 to, bool shortWay, Func<float, float, int> index, int indexCount)
    {
        Points.Clear(); Indices.Clear(); Rows.Clear(); Columns.Clear();
        RowCount = 0; ColumnCount = 0;

        // Longitude as a fraction of a full turn; walk east from `start` over `span`.
        float a = Turn(from), b = Turn(to);
        float start = a, span = Mathf.Repeat(b - a, 1f);
        if (span > 0.5f == shortWay) { start = b; span = 1f - span; }

        int limit = latitudeCount / 10 * 10;   // the vanilla brush skips tiles at |latitude| >= latitudeCount / 10 segments
        int first = OddTenth(Latitude(from, segment)), last = OddTenth(Latitude(to, segment));
        for (int lat = Math.Min(first, last); lat <= Math.Max(first, last); lat += 2)
        {
            if (Math.Abs(lat) >= limit) continue;
            int bandSegments = PlanetGrid.DetermineLongitudeSegmentCount(Math.Abs(lat) / 10, segment);
            int ring = bandSegments * 5;   // tiles around the planet in this band
            int west = Column(start, ring), count = Mod(Column(start + span, ring) - west, ring) + 1;
            if (span * ring >= ring - 1) count = ring;   // all the way round
            int column = 0;
            for (int k = 0; k < count; k++)
            {
                int lon = 2 * Mod(west + k, ring) + 1 - ring;
                int reform = index(lat / 10f, lon / 10f);
                if (reform < 0 || reform >= indexCount) continue;
                Points.Add(Surface(lat / 10f / segment, lon / 10f / bandSegments, radius));
                Indices.Add(reform); Rows.Add(RowCount); Columns.Add(column++);
            }
            if (column == 0) continue;
            RowCount++; ColumnCount = Math.Max(ColumnCount, column);
        }
    }

    /// <summary>Block of tile <paramref name="i"/> when the area is cut into <paramref name="size"/> x <paramref name="size"/> blocks</summary>
    public long BlockKey(int i, int size) => ((long)(Rows[i] / size) << 32) | (uint)(Columns[i] / size);

    /// <summary>The tile under <paramref name="p"/>, to skip recomputing an area whose corners did not move</summary>
    public static long TileKey(int segment, Vector3 p)
    {
        int lat = OddTenth(Latitude(p, segment));
        int ring = PlanetGrid.DetermineLongitudeSegmentCount(Math.Abs(lat) / 10, segment) * 5;
        return ((long)lat << 32) | (uint)Column(Turn(p), ring);
    }

    private static float Turn(Vector3 p) => Mathf.Atan2(p.x, -p.z) / (2f * Mathf.PI);
    private static float Latitude(Vector3 p, int segment) => Mathf.Asin(Mathf.Clamp(p.normalized.y, -1f, 1f)) / (2f * Mathf.PI) * segment;
    /// <summary>Tile centre, in odd tenths of a segment, the way the vanilla 1x1 brush snaps: values just below zero go to +1</summary>
    private static int OddTenth(float segments)
    {
        float tenths = segments * 10f, rounded = Mathf.Round(tenths);
        int odd = (int)Mathf.Abs(rounded);
        if (odd % 2 != 1) { odd = Mathf.FloorToInt(Mathf.Abs(tenths)); if (odd % 2 != 1) odd++; }
        return rounded >= 0f ? odd : -odd;
    }

    /// <summary>Column 0..ring-1 counted east from the date line; a turn of longitude is 2 x ring tenths</summary>
    private static int Column(float turn, int ring) => Mod((OddTenth(Mathf.Repeat(turn + 0.5f, 1f) * ring / 5f - ring / 10f) + ring - 1) / 2, ring);
    private static int Mod(int x, int m) => ((x % m) + m) % m;

    /// <summary>Point at a latitude and longitude given in turns</summary>
    private static Vector3 Surface(float latTurns, float lonTurns, float radius)
    {
        float lat = latTurns * 2f * Mathf.PI, lon = lonTurns * 2f * Mathf.PI;
        return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), -Mathf.Cos(lat) * Mathf.Cos(lon)) * radius;
    }
}
