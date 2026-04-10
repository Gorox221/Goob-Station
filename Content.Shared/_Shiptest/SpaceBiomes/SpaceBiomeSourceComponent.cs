using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Shiptest.SpaceBiomes;

[RegisterComponent, AutoGenerateComponentState]
public sealed partial class SpaceBiomeSourceComponent : Component
{
    /// <summary>
    /// Prototype ID of the <see cref="SpaceBiomePrototype"/> to apply.
    /// </summary>
    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdSerializer<SpaceBiomePrototype>)), AutoNetworkedField]
    public string Biome = "";

    /// <summary>
    /// Base distance (in meters) at which biome swap should begin.
    /// Actual boundary is deformed by <see cref="BoundaryPoints"/>.
    /// </summary>
    [DataField(required: true)]
    public int SwapDistance;

    /// <summary>
    /// When multiple biomes overlap, the one with the highest priority wins.
    /// </summary>
    [DataField]
    public int Priority;

    /// <summary>
    /// Server-only: boundary deformation points defining the irregular shape of this biome zone.
    /// Each value is a multiplier applied to SwapDistance at a given angle.
    /// Generated at spawn time, never changes.
    /// </summary>
    [ViewVariables]
    public float[] BoundaryPoints = Array.Empty<float>();

    /// <summary>
    /// Resolution of boundary points (how many angles are sampled).
    /// </summary>
    [ViewVariables]
    public int BoundaryResolution;

    /// <summary>
    /// Checks if a point (relative to this entity's position) is inside the biome's irregular boundary.
    /// </summary>
    public bool ContainsPoint(System.Numerics.Vector2 relativePos)
    {
        if (BoundaryPoints.Length == 0)
        {
            // Fallback: perfect circle
            return relativePos.LengthSquared() <= SwapDistance * SwapDistance;
        }

        var angle = MathF.Atan2(relativePos.Y, relativePos.X);
        var distance = relativePos.Length();

        // Interpolate boundary radius at this angle
        var boundaryRadius = GetBoundaryRadiusAtAngle(angle);

        return distance <= boundaryRadius;
    }

    /// <summary>
    /// Gets the boundary radius at a specific angle by interpolating between stored points.
    /// </summary>
    private float GetBoundaryRadiusAtAngle(float angle)
    {
        // Normalize angle to 0..2pi
        while (angle < 0) angle += MathF.Tau;
        while (angle >= MathF.Tau) angle -= MathF.Tau;

        var angleStep = MathF.Tau / BoundaryResolution;
        var indexFloat = angle / angleStep;
        var i0 = (int)MathF.Floor(indexFloat) % BoundaryResolution;
        var i1 = (i0 + 1) % BoundaryResolution;
        var t = indexFloat - MathF.Floor(indexFloat);

        // Smooth interpolation
        t = t * t * (3 - 2 * t); // smoothstep

        var r0 = BoundaryPoints[i0] * SwapDistance;
        var r1 = BoundaryPoints[i1] * SwapDistance;

        return r0 * (1 - t) + r1 * t;
    }
}
