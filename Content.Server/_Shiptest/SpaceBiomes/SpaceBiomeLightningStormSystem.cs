using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Lightning;
using Content.Shared.GameTicking;
using Content.Shared._Shiptest.SpaceBiomes;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.SpaceBiomes;

internal sealed class StormSourceState
{
    public EntityUid SourceUid;
    public BiomeLightningStormPrototype Storm = default!;
    public TimeSpan NextStrike;
}

/// <summary>
/// Handles electric storm biome behavior by striking nearby grids with lightning.
/// Configured in spaceFactionBiome.lightningStorm.
/// </summary>
public sealed class SpaceBiomeLightningStormSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly List<StormSourceState> _stormSources = new();
    private readonly Dictionary<MapId, List<EntityUid>> _mapGrids = new();
    private readonly List<EntityUid> _candidateGrids = new();

    private TimeSpan _nextUpdate;
    private TimeSpan _nextGridCacheRefresh;
    private bool _sourcesDirty = true;
    private int _sourceIndex;

    // Storm updates are coarse and sampled to avoid full scans every frame.
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan GridCacheRefreshInterval = TimeSpan.FromSeconds(5.0);
    private const int MaxSourcesPerUpdate = 10;

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
        _stormSources.Clear();
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
            RebuildStormSources();

        if (now >= _nextGridCacheRefresh)
        {
            RefreshGridCache();
            _nextGridCacheRefresh = now + GridCacheRefreshInterval;
        }

        if (_stormSources.Count == 0)
            return;

        var processed = 0;
        while (processed < MaxSourcesPerUpdate && _stormSources.Count > 0)
        {
            if (_sourceIndex >= _stormSources.Count)
                _sourceIndex = 0;

            var sourceState = _stormSources[_sourceIndex];
            _sourceIndex++;
            processed++;

            if (now < sourceState.NextStrike)
                continue;

            if (!TryComp<SpaceBiomeSourceComponent>(sourceState.SourceUid, out var source) ||
                !TryComp<TransformComponent>(sourceState.SourceUid, out var sourceXform))
            {
                _sourcesDirty = true;
                continue;
            }

            sourceState.NextStrike = now + TimeSpan.FromSeconds(GetNextInterval(sourceState.Storm));

            if (!IsBiomeActive(sourceState.SourceUid, sourceXform.MapID, source.SwapDistance, sourceState.Storm.PlayerActivationRange))
                continue;

            StrikeRandomGridFromBiome(sourceState.SourceUid, source, sourceXform, sourceState.Storm);
        }
    }

    private void RebuildStormSources()
    {
        _stormSources.Clear();
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent>();
        while (query.MoveNext(out var uid, out var source))
        {
            if (!_prototype.TryIndex<SpaceBiomePrototype>(source.Biome, out var biomeProto))
                continue;

            if (biomeProto.LightningStorm is not { Enabled: true } storm)
                continue;

            _stormSources.Add(new StormSourceState
            {
                SourceUid = uid,
                Storm = storm,
                NextStrike = _timing.CurTime + TimeSpan.FromSeconds(GetNextInterval(storm)),
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
        var activationRadius = sourceRadius + playerActivationRange;

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

    private void StrikeRandomGridFromBiome(
        EntityUid sourceUid,
        SpaceBiomeSourceComponent source,
        TransformComponent sourceXform,
        BiomeLightningStormPrototype storm)
    {
        if (!_mapGrids.TryGetValue(sourceXform.MapID, out var mapGrids) || mapGrids.Count == 0)
            return;

        var sourcePos = _transform.GetWorldPosition(sourceXform);
        _candidateGrids.Clear();

        foreach (var gridUid in mapGrids)
        {
            if (!TryComp<MapGridComponent>(gridUid, out var gridComp) ||
                !TryComp<TransformComponent>(gridUid, out var gridXform))
            {
                continue;
            }

            var gridAabb = _transform.GetWorldMatrix(gridXform).TransformBox(gridComp.LocalAABB);
            if (!SpaceBiomeHelpers.RectCircleIntersect(gridAabb, sourcePos, source.SwapDistance))
                continue;

            _candidateGrids.Add(gridUid);
        }

        if (_candidateGrids.Count == 0)
            return;

        var targetGrid = _random.Pick(_candidateGrids);

        // Visual strike from storm source to selected grid.
        _lightning.ShootLightning(sourceUid, targetGrid, storm.LightningPrototype);

        // Then arc inside the grid area to actual lightning targets.
        var minBolts = Math.Max(1, storm.MinBolts);
        var maxBolts = Math.Max(minBolts, storm.MaxBolts);
        var bolts = _random.Next(minBolts, maxBolts + 1);
        _lightning.ShootRandomLightnings(
            targetGrid,
            Math.Max(0.5f, storm.ArcRangeOnGrid),
            bolts,
            storm.LightningPrototype,
            arcDepth: Math.Max(0, storm.ArcDepth),
            ignoredEntity: sourceUid);
    }

    private float GetNextInterval(BiomeLightningStormPrototype storm)
    {
        var min = Math.Max(0.1f, storm.IntervalMin);
        var max = Math.Max(min, storm.IntervalMax);
        return _random.NextFloat(min, max);
    }
}

