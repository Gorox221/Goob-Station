using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server._Shiptest.ShipAccess;
using Content.Server._Shiptest.SpaceBiomes;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared._Shiptest.Access;
using Content.Shared._Shiptest.ShipSpawn;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station;
using Content.Shared.Station.Components;
using Content.Shared.Radio;
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
using Robust.Shared.Upload;
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
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly IGamePrototypeLoadManager _runtimePrototypes = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;

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

        ApplyPlayerShipCargoAndBank(shipStation, faction);

        ApplyPlayerShipHullAccess(shipStation, gridUid);

        var captainCoords = new EntityCoordinates(gridUid, loadedGrid.Value.Comp.LocalAABB.Center);
        _ticker.MakeJoinGame(session, shipStation, blueprint.CaptainJob.Id, silent: true, forceSpawn: captainCoords);

        _claimedShipBlueprints.Add(blueprint.ID);
        BroadcastClaimedBlueprints();
    }

    /// <summary>
    /// Sets cargo markets and starting funds for this vessel's station bank (one account entity per ship station).
    /// </summary>
    private void ApplyPlayerShipCargoAndBank(EntityUid shipStation, PlayerShipFactionPrototype faction)
    {
        if (TryComp<StationCargoOrderDatabaseComponent>(shipStation, out var cargoDb))
        {
            cargoDb.Markets.Clear();
            foreach (var market in faction.CargoMarkets)
                cargoDb.Markets.Add(market);

            Dirty(shipStation, cargoDb);
        }

        if (TryComp<StationBankAccountComponent>(shipStation, out var bank))
        {
            var stationBank = (shipStation, bank);
            foreach (var account in bank.Accounts.Keys.ToList())
            {
                var current = _cargo.GetBalanceFromAccount(stationBank, account);
                var target = account == bank.PrimaryAccount
                    ? faction.StartingCargoBalance
                    : 0;
                var delta = target - current;
                if (delta != 0)
                    _cargo.UpdateBankAccount(stationBank, delta, account);
            }
        }
    }

    /// <summary>
    /// Assign a unique hull token to this ship station and require it on all access readers on the loaded grid.
    /// </summary>
    private void ApplyPlayerShipHullAccess(EntityUid shipStation, EntityUid gridUid)
    {
        var hull = EnsureComp<PlayerShipHullAccessComponent>(shipStation);
        hull.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        hull.RadioChannelProtoId = null;

        TryRegisterPlayerShipRadioChannel(hull);

        var query = AllEntityQuery<AccessReaderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var reader, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            _accessReader.ClearAccesses((uid, reader));
            _accessReader.ClearAccessKeys((uid, reader));

            var hullReader = EnsureComp<ShipHullAccessReaderComponent>(uid);
            hullReader.RequiredHullToken = hull.Token;
            Dirty(uid, hullReader);
        }
    }

    /// <summary>
    /// Registers a unique long-range radio channel (random multi-char key, e.g. <c>:якуй</c>; frequency like a handheld).
    /// Uses <see cref="IGamePrototypeLoadManager"/> so the prototype is replicated to all clients (required for chat UI).
    /// </summary>
    private void TryRegisterPlayerShipRadioChannel(PlayerShipHullAccessComponent hull)
    {
        var channelId = $"PlayerShipRx{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
        var radioKey = GenerateUniqueShipRadioKey();
        var frequency = _random.Next(
            PlayerShipRadioConstants.DynamicFrequencyMin,
            PlayerShipRadioConstants.DynamicFrequencyMaxExclusive);

        var yaml = $"- type: radioChannel\n" +
                   $"  id: {channelId}\n" +
                   "  name: chat-radio-player-ship\n" +
                   $"  keycode: \"{radioKey}\"\n" +
                   $"  frequency: {frequency}\n" +
                   "  color: \"#6ec6ff\"\n" +
                   "  longRange: true\n";

        try
        {
            _runtimePrototypes.SendGamePrototype(yaml);
            hull.RadioChannelProtoId = channelId;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to register runtime player-ship radio channel prototype {channelId}: {e}");
        }
    }

    /// <summary>
    /// Builds a key that does not collide with any existing <see cref="Content.Shared.Radio.RadioChannelPrototype"/> keycode
    /// (dictionary is keyed by keycode string; duplicates throw).
    /// </summary>
    private string GenerateUniqueShipRadioKey()
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            Span<char> buf = stackalloc char[PlayerShipRadioConstants.ShipRadioKeyLength];
            for (var i = 0; i < buf.Length; i++)
            {
                buf[i] = PlayerShipRadioConstants.ShipRadioKeyAlphabet[
                    _random.Next(PlayerShipRadioConstants.ShipRadioKeyAlphabet.Length)];
            }

            var key = new string(buf);
            if (!_proto.EnumeratePrototypes<RadioChannelPrototype>()
                    .Any(p => !string.IsNullOrEmpty(p.KeyCode)
                              && string.Equals(p.KeyCode, key, StringComparison.OrdinalIgnoreCase)))
            {
                return key;
            }
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant();
    }
}
