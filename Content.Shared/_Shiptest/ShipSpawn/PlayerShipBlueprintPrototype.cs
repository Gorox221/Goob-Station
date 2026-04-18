using System.Collections.Generic;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Shiptest.ShipSpawn;

[Prototype("playerShipBlueprint")]
public sealed partial class PlayerShipBlueprintPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localization id for the ship name in the lobby UI.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; }

    /// <summary>
    /// Grid map file (single grid) merged onto the station map.
    /// </summary>
    [DataField(required: true)]
    public ResPath Map { get; private set; }

    /// <summary>
    /// Job the player receives (captain of this ship).
    /// </summary>
    [DataField(required: true)]
    public ProtoId<JobPrototype> CaptainJob { get; private set; }

    /// <summary>
    /// Job slots for this ship's station (round start / mid-round counts per job), same as map <c>StationJobs.availableJobs</c>.
    /// If empty, defaults to a single slot for <see cref="CaptainJob"/>.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<JobPrototype>, int[]> AvailableJobs { get; private set; } = new();
}
