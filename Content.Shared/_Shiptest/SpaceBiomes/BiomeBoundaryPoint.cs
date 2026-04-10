using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Shiptest.SpaceBiomes;

/// <summary>
/// A single point on a biome boundary: angle (radians) and distance from center.
/// Used by the spawner to generate organic, non-circular shapes.
/// </summary>
[Serializable, NetSerializable]
public struct BiomeBoundaryPoint
{
    public float Angle;
    public float Radius;

    public BiomeBoundaryPoint(float angle, float radius)
    {
        Angle = angle;
        Radius = radius;
    }

    /// <summary>
    /// Converts this boundary point to a 2D offset from the biome center.
    /// </summary>
    public readonly Vector2 ToOffset()
    {
        return new Vector2(MathF.Cos(Angle), MathF.Sin(Angle)) * Radius;
    }
}
