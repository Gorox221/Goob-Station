using Robust.Shared.Serialization;

namespace Content.Shared._Shiptest.ShipSpawn;

/// <summary>
/// Client asks the server for the set of player ship blueprint ids already spawned this round.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestPlayerShipSpawnAvailabilityEvent : EntityEventArgs;

/// <summary>
/// Full list of blueprint ids that have already been spawned this round (at most one each).
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerShipConsumedBlueprintsSyncEvent : EntityEventArgs
{
    public string[] ConsumedBlueprintIds { get; }

    /// <summary>
    /// When true, this message is the reply to <see cref="RequestPlayerShipSpawnAvailabilityEvent"/> for one client.
    /// </summary>
    public bool RespondedToRequest { get; }

    public PlayerShipConsumedBlueprintsSyncEvent(string[] consumedBlueprintIds, bool respondedToRequest)
    {
        ConsumedBlueprintIds = consumedBlueprintIds;
        RespondedToRequest = respondedToRequest;
    }
}
