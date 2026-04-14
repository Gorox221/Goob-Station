using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Shiptest.SpaceBiomes;

/// <summary>
/// Represents a single cell in the 19x19 space biome grid.
/// Each cell is a square zone of 1500x1500 meters.
/// </summary>
[RegisterComponent, AutoGenerateComponentState]
public sealed partial class SpaceBiomeGridCellComponent : Component
{
    /// <summary>
    /// Grid coordinate X (0-18).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int GridX;

    /// <summary>
    /// Grid coordinate Y (0-18).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int GridY;

    /// <summary>
    /// Prototype ID of the <see cref="SpaceBiomePrototype"/> for this cell.
    /// Empty string means no biome (empty space/station).
    /// </summary>
    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<SpaceBiomePrototype>)), AutoNetworkedField]
    public string BiomeId = "";

    /// <summary>
    /// Size of each grid cell in meters (1500m = 1500 square meters per side).
    /// </summary>
    public const int CellSize = 1500;

    /// <summary>
    /// Grid dimensions (19x19).
    /// </summary>
    public const int GridSize = 19;

    /// <summary>
    /// Center zone: 5x5 cells around center (indices 7-11).
    /// These cells are always empty (station location).
    /// </summary>
    public const int CenterZoneStart = 7;
    public const int CenterZoneEnd = 11; // inclusive

    /// <summary>
    /// First orbit: cells at distance 3 from center zone border (through 1 empty cell).
    /// Center zone border is at index 6 (one before start), so orbit is at index 3 and 15.
    /// </summary>
    public const int FirstOrbitDistance = 3; // cells from center zone border

    /// <summary>
    /// Second orbit: cells at distance 5 from center zone border (through 2 empty cells from first orbit).
    /// First orbit outer border is at index 3, so second orbit is at index 0 and 18.
    /// </summary>
    public const int SecondOrbitDistance = 6; // cells from center zone border
}
