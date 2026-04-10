using System.Numerics;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Shiptest.SpaceBiomes;

/// <summary>
/// Network-serializable representation of a biome zone for map display.
/// Contains boundary line segments for rendering irregular shapes.
/// </summary>
[Serializable, NetSerializable]
public sealed class BiomeZoneObject : IMapObject
{
    /// <summary>
    /// NetCoordinates of the biome source entity center.
    /// </summary>
    public NetCoordinates Coordinates;

    /// <summary>
    /// Boundary line segments as a flat array of Vector2 pairs.
    /// Each pair [start, end] is relative to the biome center.
    /// Format: [x0,y0, x1,y1, x2,y2, x3,y3, ...]
    /// </summary>
    public Vector2[] BoundaryLines;

    /// <summary>
    /// Average radius for viewport culling.
    /// </summary>
    public float AverageRadius;

    /// <summary>
    /// Biome prototype ID.
    /// </summary>
    public string BiomeId;

    /// <summary>
    /// Display name of the biome.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Color used to render this biome zone on maps.
    /// </summary>
    public Color Color;

    /// <summary>
    /// Whether to hide the button representation on the map screen.
    /// Biome zones are drawn as line boundaries, not buttons.
    /// </summary>
    public bool HideButton => true;

    public BiomeZoneObject(
        NetCoordinates coordinates,
        Vector2[] boundaryLines,
        float averageRadius,
        string biomeId,
        string name,
        Color color)
    {
        Coordinates = coordinates;
        BoundaryLines = boundaryLines;
        AverageRadius = averageRadius;
        BiomeId = biomeId;
        Name = name;
        Color = color;
    }
}
