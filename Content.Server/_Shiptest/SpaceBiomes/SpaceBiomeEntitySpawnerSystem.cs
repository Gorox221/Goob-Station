using System.Numerics;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

/// <summary>
/// Tracks per-spawn-entry state for periodic spawning.
/// </summary>
internal sealed class BiomeSpawnState
{
    public EntityUid BiomeUid;
    public BiomeSpawnEntryPrototype Entry = default!;
    public SpaceBiomeSourceComponent Source = default!;
    public TransformComponent Xform = default!;
    public Vector2 Center;
    public MapId MapId;
    public float EffectiveRadius;
    public float Timer;
}

/// <summary>
/// Periodically spawns entities inside biome zones.
/// Only spawns if a player is within range of the biome.
/// Spawns batches of entities spread randomly across the entire biome.
/// </summary>
public sealed class SpaceBiomeEntitySpawnerSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    private ISawmill _sawmill = default!;

    /// <summary>
    /// How far a player must be from a biome for spawning to activate (in meters).
    /// </summary>
    private const float PlayerActivationRange = 200f;

    /// <summary>
    /// Number of entities to spawn per batch per biome.
    /// All entities are spread randomly across the entire biome area.
    /// </summary>
    private const int BatchSpawnCount = 8;

    /// <summary>
    /// Maximum attempts per entity to find a valid position inside the biome boundary.
    /// </summary>
    private const int MaxSpawnAttempts = 30;

    private readonly List<BiomeSpawnState> _spawnStates = new();
    private bool _roundInitialized;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = IoCManager.Resolve<ILogManager>().GetSawmill("biome_spawner");

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    /// <summary>
    /// Called AFTER all players are spawned and all stations are initialized.
    /// This is the correct time to scan for biome sources.
    /// </summary>
    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _roundInitialized = false;
        _spawnStates.Clear();
        _sawmill.Info("Round started, will scan biomes on first update");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
        {
            _roundInitialized = false;
            return;
        }

        // Rebuild spawn states once per round, after everything is set up
        if (!_roundInitialized)
        {
            RebuildSpawnStates();
            _roundInitialized = true;
            _sawmill.Info($"Found {_spawnStates.Count} spawn entries from biome sources");
        }

        foreach (var state in _spawnStates)
        {
            if (!state.Entry.Enabled || state.Entry.SpawnInterval <= 0)
                continue;

            state.Timer += frameTime;
            if (state.Timer < state.Entry.SpawnInterval)
                continue;

            state.Timer = 0;

            // Check if any player is near this biome
            if (!IsBiomeActive(state.Center, state.MapId, state.EffectiveRadius))
                continue;

            TrySpawnBatch(state);
        }
    }

    private void OnRestart(RoundRestartCleanupEvent ev)
    {
        _spawnStates.Clear();
        _roundInitialized = false;
    }

    /// <summary>
    /// Scans all biome sources and builds spawn state entries.
    /// </summary>
    private void RebuildSpawnStates()
    {
        _spawnStates.Clear();

        var biomeCount = 0;
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            biomeCount++;
            _sawmill.Debug($"Biome source: {source.Biome} at {xform.Coordinates}");

            if (string.IsNullOrEmpty(source.Biome))
            {
                _sawmill.Debug("  Skipped: empty biome ID");
                continue;
            }

            if (!_protoMan.TryIndex<SpaceBiomePrototype>(source.Biome, out var biomeProto))
            {
                _sawmill.Debug($"  Skipped: unknown biome prototype '{source.Biome}'");
                continue;
            }

            if (biomeProto.Spawns.Count == 0)
            {
                _sawmill.Debug($"  Skipped: no spawn entries in prototype");
                continue;
            }

            var biomeCenter = _transform.GetWorldPosition(uid);
            var effectiveRadius = (float)source.SwapDistance;

            foreach (var entry in biomeProto.Spawns)
            {
                if (!entry.Enabled || string.IsNullOrEmpty(entry.EntityId))
                {
                    _sawmill.Debug($"  Spawn entry disabled or empty: {entry.EntityId}");
                    continue;
                }

                _sawmill.Info($"  Adding spawn: {entry.EntityId} interval={entry.SpawnInterval}");
                _spawnStates.Add(new BiomeSpawnState
                {
                    BiomeUid = uid,
                    Entry = entry,
                    Source = source,
                    Xform = xform,
                    Center = biomeCenter,
                    MapId = xform.MapID,
                    EffectiveRadius = effectiveRadius,
                    Timer = 0
                });
            }
        }

        _sawmill.Info($"Scanned {biomeCount} biome sources, {_spawnStates.Count} spawn entries total");
    }

    /// <summary>
    /// Checks if any active player is within activation range of the biome.
    /// Only counts players with InGame status (spawned, not disconnected).
    /// </summary>
    private bool IsBiomeActive(Vector2 biomeCenter, MapId mapId, float biomeRadius)
    {
        var checkRange = biomeRadius + PlayerActivationRange;

        foreach (var session in _playerMan.Sessions)
        {
            // Only count players who are fully in-game
            if (session.Status != SessionStatus.InGame)
                continue;

            if (session.AttachedEntity is not { } playerUid)
                continue;

            var xform = Transform(playerUid);
            if (xform.MapID != mapId)
                continue;

            var playerPos = _transform.GetWorldPosition(xform);
            var dist = Vector2.Distance(biomeCenter, playerPos);

            _sawmill.Debug($"Player {session.Name} at dist {dist:F0}m (range {checkRange:F0}m)");

            if (dist <= checkRange)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Spawns a batch of entities randomly across the biome's square area.
    /// Each entity is placed at a random position inside the biome's square boundary.
    /// </summary>
    private void TrySpawnBatch(BiomeSpawnState state)
    {
        if (!_protoMan.TryIndex<EntityPrototype>(state.Entry.EntityId, out var proto))
        {
            _sawmill.Warning($"Unknown entity prototype: {state.Entry.EntityId}");
            return;
        }

        var halfSize = state.EffectiveRadius; // For grid biomes, this is half the cell size
        var spawned = 0;
        
        // Check if this is a grid-based biome (square)
        var isGridBiome = HasComp<SpaceBiomeGridCellComponent>(state.BiomeUid);

        for (var i = 0; i < BatchSpawnCount; i++)
        {
            // Try to find a valid position
            var placed = false;
            for (var attempt = 0; attempt < MaxSpawnAttempts; attempt++)
            {
                Vector2 localPos;
                
                if (isGridBiome)
                {
                    // Random position within square boundary
                    localPos = new Vector2(
                        _random.NextFloat(-halfSize, halfSize),
                        _random.NextFloat(-halfSize, halfSize)
                    );
                }
                else
                {
                    // Random position within circular boundary
                    var angle = _random.NextFloat(0, MathF.Tau);
                    var distance = _random.NextFloat(0, halfSize);
                    localPos = new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance);

                    // Check if inside irregular boundary
                    if (!state.Source.ContainsPoint(localPos))
                        continue;
                }

                var worldPos = state.Center + localPos;

                // Spawn the entity
                Spawn(state.Entry.EntityId, new MapCoordinates(worldPos, state.MapId));
                spawned++;
                placed = true;
                break;
            }

            if (!placed)
                _sawmill.Debug($"Failed to place entity {i + 1}/{BatchSpawnCount} after {MaxSpawnAttempts} attempts");
        }

        if (spawned > 0)
            _sawmill.Info($"Spawned {spawned}/{BatchSpawnCount} {state.Entry.EntityId} in biome '{state.Source.Biome}' (size={halfSize * 2:F0}m)");
    }
}
