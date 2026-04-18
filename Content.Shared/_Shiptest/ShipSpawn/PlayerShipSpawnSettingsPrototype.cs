using Robust.Shared.Prototypes;

namespace Content.Shared._Shiptest.ShipSpawn;

/// <summary>
/// Global tuning for player-placed ships (distance, retries). Single prototype id: <see cref="PlayerShipSpawnSettings"/>.
/// </summary>
[Prototype("playerShipSpawnSettings")]
public sealed partial class PlayerShipSpawnSettingsPrototype : IPrototype
{
    public const string DefaultId = "PlayerShipSpawn";

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float MaxDistanceFromStationCenter { get; private set; } = 24000f;

    /// <summary>
    /// If true, only positions where the space biome grid reports empty/default space are allowed.
    /// </summary>
    [DataField]
    public bool RequireDefaultSpaceBiome { get; private set; } = true;

    [DataField]
    public int PlacementAttempts { get; private set; } = 64;

    /// <summary>
    /// Minimum world distance from station center (avoids stacking on the station hull).
    /// </summary>
    [DataField]
    public float MinDistanceFromStationCenter { get; private set; } = 400f;
}
