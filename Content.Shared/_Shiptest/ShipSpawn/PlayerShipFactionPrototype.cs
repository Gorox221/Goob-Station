using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Shared._Shiptest.ShipSpawn;

[Prototype("playerShipFaction")]
public sealed partial class PlayerShipFactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localization id for the faction name shown in the lobby UI.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; }

    /// <summary>
    /// Ship blueprints offered when this faction is selected (serialized as prototype ids; use <see cref="ProtoId{T}"/> at call sites).
    /// </summary>
    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdListSerializer<PlayerShipBlueprintPrototype>))]
    public List<string> Ships { get; private set; } = new();

    /// <summary>
    /// Cargo markets available to this faction's vessel (station order database).
    /// </summary>
    [DataField]
    public List<ProtoId<CargoMarketPrototype>> CargoMarkets { get; private set; } = new();

    /// <summary>
    /// Starting balance on the primary cargo account when the player ship station spawns.
    /// </summary>
    [DataField]
    public int StartingCargoBalance { get; private set; } = 7500;
}
