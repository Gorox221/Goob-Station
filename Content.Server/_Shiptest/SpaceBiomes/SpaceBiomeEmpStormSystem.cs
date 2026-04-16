using System.Numerics;
using Content.Server.Emp;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
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

internal sealed class EmpStormSourceState
{
    public EntityUid SourceUid;
    public BiomeEmpStormPrototype Storm = default!;
    public TimeSpan NextPulse;
}

/// <summary>
/// Applies EMP pulses for configured space biomes.
/// Data-driven through spaceFactionBiome.empStorm in prototypes.
/// </summary>
public sealed class SpaceBiomeEmpStormSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private readonly List<EmpStormSourceState> _empStormSources = new();
    private readonly Dictionary<MapId, List<EntityUid>> _mapGrids = new();
    private readonly List<EntityUid> _candidateGrids = new();

    private TimeSpan _nextUpdate;
    private TimeSpan _nextGridCacheRefresh;
    private bool _sourcesDirty = true;
    private int _empSourceIndex;

    // Storm updates are coarse and sampled to avoid full scans every frame.
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1.0);
    private static readonly TimeSpan GridCacheRefreshInterval = TimeSpan.FromSeconds(5.0);
    private const int MaxSourcesPerUpdate = 10;

    // Hard clamp to prevent a badly configured prototype from nuking performance.
    private const int MaxPulsesPerTrigger = 8;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _sourcesDirty = true;
        _empSourceIndex = 0;
        _nextUpdate = _timing.CurTime;
        _nextGridCacheRefresh = _timing.CurTime;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _empStormSources.Clear();
        _mapGrids.Clear();
        _sourcesDirty = true;
        _empSourceIndex = 0;
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
            RebuildEmpStormSources();

        if (now >= _nextGridCacheRefresh)
        {
            RefreshGridCache();
            _nextGridCacheRefresh = now + GridCacheRefreshInterval;
        }

        ProcessEmpStorms(now);
    }

    private void ProcessEmpStorms(TimeSpan now)
    {
        if (_empStormSources.Count == 0)
            return;

        var processed = 0;
        while (processed < MaxSourcesPerUpdate && _empStormSources.Count > 0)
        {
            if (_empSourceIndex >= _empStormSources.Count)
                _empSourceIndex = 0;

            var sourceState = _empStormSources[_empSourceIndex];
            _empSourceIndex++;
            processed++;

            if (now < sourceState.NextPulse)
                continue;

            if (!TryComp<SpaceBiomeSourceComponent>(sourceState.SourceUid, out var source) ||
                !TryComp<TransformComponent>(sourceState.SourceUid, out var sourceXform))
            {
                _sourcesDirty = true;
                continue;
            }

            sourceState.NextPulse = now + TimeSpan.FromSeconds(GetNextInterval(sourceState.Storm));

            if (!IsBiomeActive(sourceState.SourceUid, sourceXform.MapID, source.SwapDistance, sourceState.Storm.PlayerActivationRange))
                continue;

            PulseRandomGridFromBiome(sourceState.SourceUid, source, sourceXform, sourceState.Storm);
        }
    }

    private void RebuildEmpStormSources()
    {
        _empStormSources.Clear();
        var query = EntityQueryEnumerator<SpaceBiomeSourceComponent>();

        while (query.MoveNext(out var uid, out var source))
        {
            if (!_prototype.TryIndex<SpaceBiomePrototype>(source.Biome, out var biomeProto))
                continue;

            if (biomeProto.EmpStorm is { Enabled: true } empStorm)
            {
                _empStormSources.Add(new EmpStormSourceState
                {
                    SourceUid = uid,
                    Storm = empStorm,
                    NextPulse = _timing.CurTime + TimeSpan.FromSeconds(GetNextInterval(empStorm)),
                });
            }
        }

        _empSourceIndex = 0;
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

    private void PulseRandomGridFromBiome(
        EntityUid sourceUid,
        SpaceBiomeSourceComponent source,
        TransformComponent sourceXform,
        BiomeEmpStormPrototype storm)
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
            if (!SpaceBiomeHelpers.IntersectsBiomeInfluence(sourcePos, gridAabb))
                continue;

            _candidateGrids.Add(gridUid);
        }

        if (_candidateGrids.Count == 0)
            return;

        var targetGrid = _random.Pick(_candidateGrids);
        if (!TryComp<MapGridComponent>(targetGrid, out var targetGridComp) ||
            !TryComp<TransformComponent>(targetGrid, out var targetGridXform))
        {
            return;
        }

        var minPulses = Math.Max(1, storm.MinPulses);
        var maxPulses = Math.Max(minPulses, storm.MaxPulses);
        maxPulses = Math.Min(maxPulses, MaxPulsesPerTrigger);
        var pulses = _random.Next(minPulses, maxPulses + 1);

        var pulseRadius = Math.Max(0.5f, storm.PulseRadius);
        var energy = Math.Max(0f, storm.EnergyConsumption);
        var duration = Math.Max(0f, storm.DisableDuration);

        var worldMatrix = _transform.GetWorldMatrix(targetGridXform);

        for (var i = 0; i < pulses; i++)
        {
            var localPoint = new Vector2(
                _random.NextFloat(targetGridComp.LocalAABB.Left, targetGridComp.LocalAABB.Right),
                _random.NextFloat(targetGridComp.LocalAABB.Bottom, targetGridComp.LocalAABB.Top));

            var worldPoint = Vector2.Transform(localPoint, worldMatrix);
            _emp.EmpPulse(new MapCoordinates(worldPoint, targetGridXform.MapID), pulseRadius, energy, duration);
        }
    }

    private float GetNextInterval(BiomeEmpStormPrototype storm)
    {
        var min = Math.Max(0.1f, storm.IntervalMin);
        var max = Math.Max(min, storm.IntervalMax);
        return _random.NextFloat(min, max);
    }

}

