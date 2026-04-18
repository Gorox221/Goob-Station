using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server._Shiptest.SpaceBiomes;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Shiptest.ShipSpawn;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using static Robust.Shared.Prototypes.EntityPrototype;
using TransformSystem = Robust.Server.GameObjects.TransformSystem;

namespace Content.Server._Shiptest.ShipSpawn;

public sealed class PlayerShipSpawnSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SpaceBiomeGridSystem _spaceBiomeGrid = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    /// <summary>
    /// Player ship blueprint ids that have already been spawned this round (one per id).
    /// </summary>
    private readonly HashSet<string> _claimedShipBlueprints = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestPlayerShipSpawnEvent>(OnRequestSpawn);
        SubscribeNetworkEvent<RequestPlayerShipSpawnAvailabilityEvent>(OnRequestAvailability);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _claimedShipBlueprints.Clear();
        RaiseNetworkEvent(
            new PlayerShipConsumedBlueprintsSyncEvent(System.Array.Empty<string>(), respondedToRequest: false),
            Filter.Broadcast());
    }

    private void OnRequestAvailability(RequestPlayerShipSpawnAvailabilityEvent ev, EntitySessionEventArgs args)
    {
        var payload = new PlayerShipConsumedBlueprintsSyncEvent(
            _claimedShipBlueprints.ToArray(),
            respondedToRequest: true);
        RaiseNetworkEvent(payload, Filter.SinglePlayer(args.SenderSession));
    }

    private void BroadcastClaimedBlueprints()
    {
        RaiseNetworkEvent(
            new PlayerShipConsumedBlueprintsSyncEvent(_claimedShipBlueprints.ToArray(), respondedToRequest: false),
            Filter.Broadcast());
    }

    private void OnRequestSpawn(RequestPlayerShipSpawnEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-not-in-round"));
            return;
        }

        if (_ticker.UserHasJoinedGame(session))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-already-playing"));
            return;
        }

        if (_ticker.DisallowLateJoin)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-latejoin-disabled"));
            return;
        }

        if (!_proto.TryIndex(msg.Faction, out var faction) || !_proto.TryIndex(msg.Blueprint, out var blueprint))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-invalid-proto"));
            return;
        }

        if (!faction.Ships.Contains(msg.Blueprint.Id))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-ship-not-in-faction"));
            return;
        }

        if (_claimedShipBlueprints.Contains(msg.Blueprint.Id))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-blueprint-already-spawned"));
            return;
        }

        var stations = _ticker.GetSpawnableStations();
        if (stations.Count == 0)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-no-station"));
            return;
        }

        var station = stations[0];
        if (!TryComp<StationDataComponent>(station, out var stationData))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-no-station"));
            return;
        }

        var largest = _station.GetLargestGrid((station, stationData));
        if (largest == null || !TryComp<MapGridComponent>(largest, out var stationGrid))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-no-station"));
            return;
        }

        var stationGridXform = Transform(largest.Value);
        var stationCenter = _transform.GetWorldPosition(stationGridXform);
        var mapId = stationGridXform.MapID;

        _proto.TryIndex(PlayerShipSpawnSettingsPrototype.DefaultId, out PlayerShipSpawnSettingsPrototype? settings);

        var maxDist = settings?.MaxDistanceFromStationCenter ?? 24000f;
        var minDist = settings?.MinDistanceFromStationCenter ?? 400f;
        var attempts = settings?.PlacementAttempts ?? 64;
        var requireBiome = settings?.RequireDefaultSpaceBiome ?? true;

        Vector2? chosenOffset = null;
        for (var i = 0; i < attempts; i++)
        {
            var dir = _random.NextAngle().ToVec();
            var dist = _random.NextFloat(minDist, maxDist);
            var worldPos = stationCenter + dir * dist;

            if (requireBiome && _spaceBiomeGrid.GetBiomeAt(worldPos, stationCenter) != "DefaultSpace")
                continue;

            chosenOffset = worldPos;
            break;
        }

        if (chosenOffset == null)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-placement"));
            return;
        }

        if (!_mapLoader.TryLoadGrid(mapId, blueprint.Map, out var loadedGrid, offset: chosenOffset.Value))
        {
            _chat.DispatchServerMessage(session, Loc.GetString("player-ship-spawn-fail-load-map"));
            return;
        }

        var gridUid = loadedGrid!.Value.Owner;

        var jobs = new Dictionary<ProtoId<JobPrototype>, int[]>();
        foreach (var (jobId, counts) in blueprint.AvailableJobs)
            jobs[jobId] = counts;

        if (!jobs.ContainsKey(blueprint.CaptainJob))
            jobs[blueprint.CaptainJob] = new[] { 1, 1 };

        var availableNode = new MappingDataNode();
        foreach (var (jobId, counts) in jobs)
        {
            availableNode.Add(jobId, new SequenceDataNode(
                new ValueDataNode(counts[0].ToString()),
                new ValueDataNode(counts[1].ToString())));
        }

        var jobsMapping = new MappingDataNode { { "availableJobs", availableNode } };
        var stationJobs = _serialization.Read<StationJobsComponent>(jobsMapping, notNullableOverride: true);
        var overrides = new ComponentRegistry
        {
            ["StationJobs"] = new ComponentRegistryEntry(stationJobs, new MappingDataNode())
        };

        var stationConfig = new StationConfig
        {
            StationPrototype = SpaceBiomeGridSystem.PlayerShipStationPrototypeId,
            StationComponentOverrides = overrides
        };

        var shipStation = _station.InitializeNewStation(
            stationConfig,
            new[] { gridUid },
            name: Loc.GetString(blueprint.Name));

        var captainCoords = new EntityCoordinates(gridUid, loadedGrid.Value.Comp.LocalAABB.Center);
        _ticker.MakeJoinGame(session, shipStation, blueprint.CaptainJob.Id, silent: true, forceSpawn: captainCoords);

        _claimedShipBlueprints.Add(blueprint.ID);
        BroadcastClaimedBlueprints();
    }
}
