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
/// - Center circle: Always empty (station location)
/// - First orbit ring: 60% asteroids, 30% nebula, 10% empty
/// - Second orbit ring: 60% asteroids, 30% nebula, 10% empty
/// - Between/around rings: carp/ion storms, less frequent than empty space
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
    /// Circular band thresholds measured in "cell units" from station center.
    /// Using cell centers avoids square-looking orbital rings.
    /// </summary>
    private const float CenterEmptyRadiusCells = 2.5f;
    private const float FirstOrbitInnerRadiusCells = 3.5f;
    private const float FirstOrbitOuterRadiusCells = 4.5f;
    private const float SecondOrbitInnerRadiusCells = 6.5f;
    private const float SecondOrbitOuterRadiusCells = 7.5f;

    /// <summary>
    /// Spawn probabilities for first/second orbit.
    /// </summary>
    private const float OrbitAsteroidChance = 0.40f;
    private const float OrbitNebulaChance = 0.40f;
    // Remaining 20% is empty space

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
        var radialDistance = CalculateRadialDistanceFromCenter(x, y);

        // Circular empty center around station.
        if (radialDistance <= CenterEmptyRadiusCells)
        {
            return null;
        }

        // First circular orbital band.
        if (radialDistance >= FirstOrbitInnerRadiusCells && radialDistance < FirstOrbitOuterRadiusCells)
        {
            return RollOrbitBiome();
        }

        // Second circular orbital band.
        if (radialDistance >= SecondOrbitInnerRadiusCells && radialDistance < SecondOrbitOuterRadiusCells)
        {
            return RollOrbitBiome();
        }

        // Between rings and in the far outer region.
        return RollOuterBiome();
    }

    /// <summary>
    /// Calculates radial distance from station center in cell units.
    /// Uses cell-center coordinates, so rings stay visually circular.
    /// </summary>
    private static float CalculateRadialDistanceFromCenter(int x, int y)
    {
        var center = GridSize / 2f;
        var cellCenterX = x + 0.5f;
        var cellCenterY = y + 0.5f;
        var dx = cellCenterX - center;
        var dy = cellCenterY - center;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Rolls biome for orbit cells.
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
            return null; // Remaining chance is empty
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
            return _random.NextFloat() < 0.5f ? "IonStorm" : "ElectricStorm";
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
        // Data-driven source prototype lookup:
        // for biome "Foo" expects entity prototype "SpaceBiomeSourceFoo".
        var sourceId = $"SpaceBiomeSource{biomeId}";
        if (!_protoMan.HasIndex<EntityPrototype>(sourceId))
        {
            _sawmill.Warning($"Biome source prototype '{sourceId}' not found for biome '{biomeId}', using default source.");
            sourceId = "SpaceBiomeSourceDefault";
        }

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
