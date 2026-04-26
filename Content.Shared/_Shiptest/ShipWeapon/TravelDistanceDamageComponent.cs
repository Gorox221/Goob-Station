using System.Numerics;
using Content.Shared.Projectiles;

namespace Content.Shared._Shiptest.ShipWeapon;

/// <summary>
/// Scales <see cref="ProjectileComponent"/> damage on hit by how far the projectile has traveled along its path.
/// </summary>
[RegisterComponent]
public sealed partial class TravelDistanceDamageComponent : Component
{
    /// <summary>
    /// World-space path length at which the projectile reaches full (prototype) damage.
    /// </summary>
    [DataField]
    public float MaxRampingDistance = 1230f;

    /// <summary>
    /// Damage multiplier when traveled distance is ~0. Linearly ramps to 1.0 at <see cref="MaxRampingDistance"/>.
    /// </summary>
    [DataField]
    public float MinDamageFactor = 0.1f;

    public float DistanceTraveled;

    public Vector2? LastWorldPos;
}
