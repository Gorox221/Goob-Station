using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared.GameTicking;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

internal sealed class MeteorStormSourceState
{
    public EntityUid SourceUid;
    public BiomeMeteorStormPrototype Storm = default!;
    public TimeSpan NextWave;
}

/// <summary>
/// Spawns meteor waves from asteroid belt (and other configured biomes) towards nearby grids.
/// Uses Kessler-style ring spawning but is data-driven via spaceFactionBiome.meteorStorm.
/// Ensures meteors do not spawn directly in players' immediate view radius.
/// </summary>
public sealed class SpaceBiomeMeteorStormSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly List<MeteorStormSourceState> _meteorSources = new();
    private readonly Dictionary<MapId, List<EntityUid>> _mapGrids = new();
    private readonly List<EntityUid> _candidateGrids = new();

    private TimeSpan _nextUpdate;
    private TimeSpan _nextGridCacheRefresh;
    private bool _sourcesDirty = true;
    private int _sourceIndex;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan GridCacheRefreshInterval = TimeSpan.FromSeconds(5.0);
    private const int MaxSourcesPerUpdate = 10;

    // Hard cap to avoid pathological configs.
    private const int MaxMeteorsPerWave = 20;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _sourcesDirty = true;
        _sourceIndex = 0;
        _nextUpdate = _timing.CurTime;
        _nextGridCacheRefresh = _timing.CurTime;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _meteorSources.Clear();
        _mapGrids.Clear();
        _sourcesDirty = true;
        _sourceIndex = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        if (_sourcesDirty)
            RebuildMeteorSources();

        if (now >= _nextGridCacheRefresh)
        {
            RefreshGridCache();
            _nextGridCacheRefresh = now + GridCacheRefreshInterval;
        }

        ProcessMeteorStorms(now);
    }

    private void ProcessMeteorStorms(TimeSpan now)
    {
        if (_meteorSources.Count == 0)
            return;

        var processed = 0;
        while (processed < MaxSourcesPerUpdate && _meteorSources.Count > 0)
        {
            if (_sourceIndex >= _meteorSources.Count)
                _sourceIndex = 0;

            var sourceState = _meteorSources[_sourceIndex];
            _sourceIndex++;
            processed++;

            if (now < sourceState.NextWave)
                continue;

            if (!TryComp<SpaceBiomeSourceComponent>(sourceState.SourceUid, out var source) ||
                !TryComp<TransformComponent>(sourceState.SourceUid, out var sourceXform))
            {
                _sourcesDirty = true;
                continue;
            }

            sourceState.NextWave = now + TimeSpan.FromSeconds(GetNextInterval(sourceState.Storm));

            if (!IsBiomeActive(sourceState.SourceUid, sourceXform.MapID, source.SwapDistance, sourceState.Storm.PlayerActivationRange))
                continue;

            SpawnWaveTowardsNearestGrid(sourceState.SourceUid, source, sourceXform, sourceState.Storm);
        }
    }

    private void RebuildMeteorSources()
    {
        _meteorSources.Clear();
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent>();

        while (query.MoveNext(out var uid, out var source))
        {
            if (!_prototype.TryIndex<SpaceBiomePrototype>(source.Biome, out var biomeProto))
                continue;

            if (biomeProto.MeteorStorm is not { Enabled: true } storm)
                continue;

            if (storm.MeteorPrototypes.Count == 0)
                continue;

            _meteorSources.Add(new MeteorStormSourceState
            {
                SourceUid = uid,
                Storm = storm,
                NextWave = _timing.CurTime + TimeSpan.FromSeconds(GetNextInterval(storm)),
            });
        }

        _sourceIndex = 0;
        _sourcesDirty = false;
    }

    private void RefreshGridCache()
    {
        _mapGrids.Clear();

        foreach (var map in _mapManager.GetAllMapIds())
        {
            var grids = new List<EntityUid>();
            foreach (var grid in _mapManager.GetAllGrids(map))
            {
                grids.Add(grid.Owner);
            }

            if (grids.Count > 0)
                _mapGrids[map] = grids;
        }
    }

    private bool IsBiomeActive(EntityUid sourceUid, MapId mapId, int sourceRadius, float playerActivationRange)
    {
        var sourcePos = _transform.GetWorldPosition(sourceUid);
        var effectiveRadius = SpaceBiomeHelpers.GetEffectiveRadius();
        var activationRadius = effectiveRadius + playerActivationRange;

        foreach (var session in _playerMan.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } playerUid)
                continue;

            if (!TryComp<TransformComponent>(playerUid, out var playerXform) ||
                playerXform.MapID != mapId)
                continue;

            var playerPos = _transform.GetWorldPosition(playerXform);
            if (Vector2.Distance(sourcePos, playerPos) <= activationRadius)
                return true;
        }

        return false;
    }

    private void SpawnWaveTowardsNearestGrid(
        EntityUid sourceUid,
        SpaceBiomeSourceComponent source,
        TransformComponent sourceXform,
        BiomeMeteorStormPrototype storm)
    {
        if (!_mapGrids.TryGetValue(sourceXform.MapID, out var mapGrids) || mapGrids.Count == 0)
            return;

        // Pick the grid whose playable area intersects this biome's influence and is closest to the biome source.
        var biomePos = _transform.GetWorldPosition(sourceXform);
        EntityUid? targetGridUid = null;
        Box2 targetAabb = default;
        var bestDist = float.MaxValue;

        foreach (var gridUid in mapGrids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var gridComp) ||
                !TryComp<TransformComponent>(gridUid, out var gridXform))
            {
                continue;
            }

            var worldAabb = _transform.GetWorldMatrix(gridXform).TransformBox(gridComp.LocalAABB);

            if (!SpaceBiomeHelpers.IntersectsBiomeInfluence(biomePos, worldAabb))
                continue;

            var dist = (worldAabb.Center - biomePos).Length();
            if (dist < bestDist)
            {
                bestDist = dist;
                targetGridUid = gridUid;
                targetAabb = worldAabb;
            }
        }

        if (targetGridUid == null)
            return;

        // Build player positions on this map for visibility culling.
        var playerPositions = new List<Vector2>();
        foreach (var session in _playerMan.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } playerUid)
                continue;

            if (!TryComp<TransformComponent>(playerUid, out var playerXform) ||
                playerXform.MapID != sourceXform.MapID)
                continue;

            playerPositions.Add(_transform.GetWorldPosition(playerXform));
        }

        // Ring around the grid, similar to MeteorSwarmSystem.
        var halfExtent = (targetAabb.TopRight - targetAabb.Center).Length();
        var minimumDistance = halfExtent + storm.SpawnRingPadding;
        var maximumDistance = minimumDistance + storm.SpawnRingPadding;
        var center = targetAabb.Center;

        var minCount = Math.Max(1, storm.MinMeteorsPerWave);
        var maxCount = Math.Max(minCount, storm.MaxMeteorsPerWave);
        maxCount = Math.Min(maxCount, MaxMeteorsPerWave);
        var meteorsToSpawn = _random.Next(minCount, maxCount + 1);

        if (!TryComp<TransformComponent>(targetGridUid!.Value, out var targetGridXform))
            return;

        for (var i = 0; i < meteorsToSpawn; i++)
        {
            var spawnProtoId = _random.Pick(storm.MeteorPrototypes);
            if (!_prototype.HasIndex<EntityPrototype>(spawnProtoId))
                continue;

            // Pick a random direction roughly pointing from biome to grid.
            var baseDir = (center - biomePos);
            var baseAngle = baseDir.LengthSquared() > 0 ? MathF.Atan2(baseDir.Y, baseDir.X) : _random.NextFloat(0, MathF.Tau);

            // Slight randomization around the base direction.
            var angle = baseAngle + _random.NextFloat(-0.35f, 0.35f);
            var distance = _random.NextFloat(minimumDistance, maximumDistance);
            var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

            // Perpendicular spread to avoid all meteors hitting the same line.
            var subAngle = angle + (_random.Prob(0.5f) ? MathF.PI / 2 : -MathF.PI / 2);
            var subOffset = new Vector2(MathF.Cos(subAngle), MathF.Sin(subAngle)) * (halfExtent / 3f * _random.NextFloat());

            var spawnPos = center + offset + subOffset;

            // Visibility check: do not spawn too close to any player.
            var visible = false;
            foreach (var playerPos in playerPositions)
            {
                if ((playerPos - spawnPos).Length() <= storm.PlayerVisibilityRadius)
                {
                    visible = true;
                    break;
                }
            }

            if (visible)
            {
                // Try a couple of alternate sub-offsets before giving up on this meteor.
                var retries = 2;
                var ok = false;
                while (retries-- > 0 && visible)
                {
                    subAngle = angle + (_random.Prob(0.5f) ? MathF.PI / 2 : -MathF.PI / 2);
                    subOffset = new Vector2(MathF.Cos(subAngle), MathF.Sin(subAngle)) * (halfExtent / 3f * _random.NextFloat());
                    spawnPos = center + offset + subOffset;

                    visible = false;
                    foreach (var playerPos in playerPositions)
                    {
                        if ((playerPos - spawnPos).Length() <= storm.PlayerVisibilityRadius)
                        {
                            visible = true;
                            break;
                        }
                    }

                    ok = !visible;
                }

                if (visible && !ok)
                    continue;
            }

            var meteor = Spawn(spawnProtoId, new MapCoordinates(spawnPos, targetGridXform.MapID));
            if (!TryComp<PhysicsComponent>(meteor, out var physics))
                continue;

            var impulseDir = (center - spawnPos);
            if (impulseDir.LengthSquared() <= 0)
                continue;

            impulseDir = Vector2.Normalize(impulseDir);
            _physics.ApplyLinearImpulse(meteor, impulseDir * storm.MeteorVelocity * physics.Mass, body: physics);
        }
    }

    private float GetNextInterval(BiomeMeteorStormPrototype storm)
    {
        var min = Math.Max(0.5f, storm.IntervalMin);
        var max = Math.Max(min, storm.IntervalMax);
        return _random.NextFloat(min, max);
    }

}

