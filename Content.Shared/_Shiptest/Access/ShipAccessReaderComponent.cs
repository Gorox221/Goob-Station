using Robust.Shared.GameStates;

namespace Content.Shared._Shiptest.Access;

/// <summary>
/// When <see cref="RequiredHullToken"/> is non-empty, this reader only opens if the user presents a matching
/// <see cref="PlayerShipHullGrantComponent"/> (e.g. on their ID). Standard access lists are ignored in that case.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipHullAccessReaderComponent : Component
{
    [DataField, AutoNetworkedField]
    public string RequiredHullToken = "";
}
