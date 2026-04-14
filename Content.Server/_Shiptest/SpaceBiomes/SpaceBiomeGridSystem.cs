using System.Numerics;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Manages a 19x19 grid of space biomes around the station.
/// Each cell is 1500x1500 meters square.
/// 
/// Layout:
/// - Center 5x5 (indices 7-11): Always empty (station location)
/// - First orbit (indices 3 and 15): 60% asteroids, 30% nebula, 10% empty
/// - Second orbit (indices 0 and 18): 60% asteroids, 30% nebula, 10% empty
/// - Between orbits and outer edges: carp/ion storms, less frequent than empty space
/// </summary>
public sealed class SpaceBiomeGridSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// Size of each grid cell in meters.
    /// </summary>
    private const int CellSize = SpaceBiomeGridCellComponent.CellSize;

    /// <summary>
    /// Grid dimensions (19x19).
    /// </summary>
    private const int GridSize = SpaceBiomeGridCellComponent.GridSize;

    /// <summary>
    /// Center zone: indices 7-11 (5x5 cells centered on the station).
    /// </summary>
    private const int CenterStart = SpaceBiomeGridCellComponent.CenterZoneStart;
    private const int CenterEnd = SpaceBiomeGridCellComponent.CenterZoneEnd;

    /// <summary>
    /// First orbit: indices 3 and 15 (2 cells away from center zone border through 1 empty cell).
    /// </summary>
    private const int FirstOrbitInner = 3;
    private const int FirstOrbitOuter = 15;

    /// <summary>
    /// Second orbit: indices 0 and 18 (edge of grid).
    /// </summary>
    private const int SecondOrbitInner = 0;
    private const int SecondOrbitOuter = 18;

    /// <summary>
    /// Spawn probabilities for first/second orbit.
    /// </summary>
    private const float OrbitAsteroidChance = 0.60f;
    private const float OrbitNebulaChance = 0.30f;
    // Remaining 10% is empty space

    /// <summary>
    /// Spawn probabilities for inter-orbit and outer regions.
    /// </summary>
    private const float OuterCarpStormChance = 0.20f; // 20% carp/ion storm
    private const float OuterEmptyChance = 0.80f;      // 80% empty space

    // Grid state: [x,y] -> EntityUid of biome source (or null if empty)
    private EntityUid?[,] _gridCells = new EntityUid?[GridSize, GridSize];

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("biome_grid");
        SubscribeLocalEvent<StationPostInitEvent>(OnStationPostInit);
    }

    private void OnStationPostInit(ref StationPostInitEvent ev)
    {
        var gridUid = _stationSystem.GetLargestGrid((ev.Station.Owner, ev.Station.Comp));
        if (!gridUid.HasValue)
            return;

        var gridXform = Transform(gridUid.Value);
        var gridCenter = _transform.GetWorldPosition(gridXform);
        var mapId = gridXform.MapID;

        InitializeGrid(gridCenter, mapId);
    }

    /// <summary>
    /// Initializes the 19x19 biome grid around the station.
    /// </summary>
    private void InitializeGrid(Vector2 stationCenter, MapId mapId)
    {
        _sawmill.Info($"Initializing {GridSize}x{GridSize} biome grid centered at {stationCenter}");

        // Clear previous grid
        _gridCells = new EntityUid?[GridSize, GridSize];

        var halfGrid = GridSize / 2; // 9
        var spawnedCount = 0;
        var emptyCount = 0;

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                // Calculate world position for this cell
                var worldX = stationCenter.X + (x - halfGrid) * CellSize + CellSize / 2f;
                var worldY = stationCenter.Y + (y - halfGrid) * CellSize + CellSize / 2f;
                var worldPos = new Vector2(worldX, worldY);

                // Determine what biome this cell should have
                var biomeId = DetermineBiomeForCell(x, y);

                if (string.IsNullOrEmpty(biomeId))
                {
                    // Empty cell (station or empty space)
                    _gridCells[x, y] = null;
                    emptyCount++;
                }
                else
                {
                    // Spawn biome source entity for this cell
                    var sourceUid = SpawnBiomeCell(biomeId, worldPos, mapId, x, y);
                    _gridCells[x, y] = sourceUid;
                    spawnedCount++;
                }
            }
        }

        _sawmill.Info($"Biome grid initialized: {spawnedCount} biomes, {emptyCount} empty cells");
    }

    /// <summary>
    /// Determines which biome (if any) should be placed at grid cell (x, y).
    /// </summary>
    private string? DetermineBiomeForCell(int x, int y)
    {
        // Center 5x5 zone: always empty (station location)
        // Indices 7-11 on both axes
        if (IsInCenterZone(x, y))
        {
            return null; // Empty - station here
        }

        // Calculate minimum Chebyshev distance from center zone border
        // Center zone border is at indices 6 (inner edge before 7) and 12 (outer edge after 11)
        var distFromCenter = CalculateDistanceFromCenterZoneBorder(x, y);

        // First orbit: through 1 empty cell from center zone
        // Center zone border at index 6, so 1 cell gap = index 5
        // Also index 13 on the other side (border at 12, gap at 13)
        if (distFromCenter == 1)
        {
            return RollOrbitBiome(); // 60% asteroids, 30% nebula, 10% empty
        }

        // Second orbit: through 2 cells from first orbit
        // First orbit at index 5, so 2 cells gap = indices 4,3 -> orbit at index 2
        // Also index 15 on the other side (first orbit at 13, gap at 14,15 -> orbit at 16)
        if (distFromCenter == 4)
        {
            return RollOrbitBiome(); // 60% asteroids, 30% nebula, 10% empty
        }

        // Between orbits (indices 3-4, 14-15) and outer regions (indices 0-1, 17-18)
        // Spawn carp/ion storms less frequently than empty space
        return RollOuterBiome(); // 20% storms, 80% empty
    }

    /// <summary>
    /// Calculates minimum Chebyshev distance from cell to center zone border.
    /// Center zone is indices 7-11, so border is at index 6 (inner) and 12 (outer).
    /// Returns:
    ///   0 = inside center zone
    ///   1 = first orbit position (index 5 or 13)
    ///   4 = second orbit position (index 2 or 16)
    /// </summary>
    private static int CalculateDistanceFromCenterZoneBorder(int x, int y)
    {
        // Calculate distance on each axis to nearest center zone edge
        // Center zone spans indices 7-11, edges at 6 and 12
        var dx = DistanceFromRange(x, CenterStart - 1, CenterEnd + 1);
        var dy = DistanceFromRange(y, CenterStart - 1, CenterEnd + 1);
        
        // Chebyshev distance (max of x and y distances for square rings)
        return Math.Max(dx, dy);
    }

    /// <summary>
    /// Calculates distance from a value to the nearest edge of a range.
    /// Returns 0 if value is within range, positive distance otherwise.
    /// </summary>
    private static int DistanceFromRange(int value, int rangeMin, int rangeMax)
    {
        if (value < rangeMin)
            return rangeMin - value;
        if (value > rangeMax)
            return value - rangeMax;
        return 0;
    }

    /// <summary>
    /// Checks if cell is in the center 5x5 zone (indices 7-11).
    /// </summary>
    private static bool IsInCenterZone(int x, int y)
    {
        return x >= CenterStart && x <= CenterEnd && y >= CenterStart && y <= CenterEnd;
    }

    /// <summary>
    /// Rolls biome for orbit cells: 60% asteroids, 30% nebula, 10% empty.
    /// </summary>
    private string? RollOrbitBiome()
    {
        var roll = _random.NextFloat();

        if (roll < OrbitAsteroidChance)
        {
            return "AsteroidBelt";
        }
        else if (roll < OrbitAsteroidChance + OrbitNebulaChance)
        {
            return "NebulaSpace"; // Nebula that blocks scanning
        }
        else
        {
            return null; // 10% empty
        }
    }

    /// <summary>
    /// Rolls biome for outer regions: 20% carp/ion storm, 80% empty.
    /// </summary>
    private string? RollOuterBiome()
    {
        var roll = _random.NextFloat();

        if (roll < OuterCarpStormChance)
        {
            // Mix of ion storm and electric storm
            return _random.NextFloat() < 0.5f ? "IonStorm" : "DebrisField";
        }
        else
        {
            return null; // 80% empty space
        }
    }

    /// <summary>
    /// Spawns a biome source entity for a grid cell.
    /// </summary>
    private EntityUid SpawnBiomeCell(string biomeId, Vector2 worldPos, MapId mapId, int gridX, int gridY)
    {
        var sourceId = biomeId switch
        {
            "AsteroidBelt" => "SpaceBiomeSourceAsteroidBelt",
            "DebrisField" => "SpaceBiomeSourceDebrisField",
            "AnomalousSpace" => "SpaceBiomeSourceAnomalousSpace",
            "NebulaSpace" => "SpaceBiomeSourceNebulaSpace",
            "IonStorm" => "SpaceBiomeSourceIonStorm",
            _ => "SpaceBiomeSourceDefault"
        };

        var uid = Spawn(sourceId, new MapCoordinates(worldPos, mapId));

        // Configure as square grid cell
        if (TryComp<SpaceBiomeSourceComponent>(uid, out var sourceComp))
        {
            // For grid system, use half cell size as swap distance (cells are square)
            sourceComp.SwapDistance = CellSize / 2; // 750m = half of 1500m
            sourceComp.Priority = 0;
            sourceComp.BoundaryResolution = 4; // Square has 4 corners
            sourceComp.BoundaryPoints = new float[] { 1f, 1f, 1f, 1f }; // Square boundary
            Dirty(uid, sourceComp);
        }

        // Add grid cell component
        var gridComp = EnsureComp<SpaceBiomeGridCellComponent>(uid);
        gridComp.GridX = gridX;
        gridComp.GridY = gridY;
        gridComp.BiomeId = biomeId;
        Dirty(uid, gridComp);

        _sawmill.Debug($"Spawned biome cell at ({gridX},{gridY}): {biomeId}");

        return uid;
    }

    /// <summary>
    /// Gets the biome ID at a given world position by checking which grid cell it falls into.
    /// </summary>
    public string GetBiomeAt(Vector2 worldPos, Vector2 stationCenter)
    {
        // Convert world position to grid coordinates
        // stationCenter is at the center of the grid (cell 9,9)
        var halfGrid = GridSize / 2; // 9
        
        var gridX = (int)MathF.Floor((worldPos.X - stationCenter.X) / CellSize) + halfGrid;
        var gridY = (int)MathF.Floor((worldPos.Y - stationCenter.Y) / CellSize) + halfGrid;

        // Clamp to grid bounds
        gridX = Math.Clamp(gridX, 0, GridSize - 1);
        gridY = Math.Clamp(gridY, 0, GridSize - 1);

        // Get biome from grid
        var cellUid = _gridCells[gridX, gridY];
        if (!cellUid.HasValue)
            return "DefaultSpace";

        if (TryComp<SpaceBiomeGridCellComponent>(cellUid.Value, out var gridComp))
        {
            return string.IsNullOrEmpty(gridComp.BiomeId) ? "DefaultSpace" : gridComp.BiomeId;
        }

        return "DefaultSpace";
    }

    /// <summary>
    /// Returns all biome source entities in the grid.
    /// </summary>
    public List<EntityUid> GetAllBiomeCells()
    {
        var cells = new List<EntityUid>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                var cell = _gridCells[x, y];
                if (cell.HasValue)
                {
                    cells.Add(cell.Value);
                }
            }
        }
        return cells;
    }
}
