using System.Collections.Generic;
using Content.Shared._Shiptest.ShipSpawn;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Client._Shiptest.ShipSpawn;

public sealed class ShipSpawnClientSystem : EntitySystem
{
    private readonly HashSet<string> _consumedBlueprints = new();
    private Action? _afterAvailabilityReceived;

    public IReadOnlyCollection<string> ConsumedBlueprints => _consumedBlueprints;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<PlayerShipConsumedBlueprintsSyncEvent>(OnConsumedSync);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _consumedBlueprints.Clear();
        _afterAvailabilityReceived = null;
    }

    private void OnConsumedSync(PlayerShipConsumedBlueprintsSyncEvent msg)
    {
        _consumedBlueprints.Clear();
        foreach (var id in msg.ConsumedBlueprintIds)
            _consumedBlueprints.Add(id);

        if (msg.RespondedToRequest)
        {
            _afterAvailabilityReceived?.Invoke();
            _afterAvailabilityReceived = null;
        }
    }

    /// <summary>
    /// Ask the server for the current list of already-spawned ship blueprints, then run <paramref name="continuation"/>.
    /// </summary>
    public void RequestAvailabilityAndThen(Action continuation)
    {
        _afterAvailabilityReceived = continuation;
        RaiseNetworkEvent(new RequestPlayerShipSpawnAvailabilityEvent());
    }

    public void RequestSpawn(ProtoId<PlayerShipFactionPrototype> faction, ProtoId<PlayerShipBlueprintPrototype> blueprint)
    {
        RaiseNetworkEvent(new RequestPlayerShipSpawnEvent(faction, blueprint));
    }
}
