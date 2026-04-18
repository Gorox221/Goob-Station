using Robust.Shared.GameStates;

namespace Content.Shared._Shiptest.Access;

/// <summary>
/// Placed on an ID card (or other access item). Grants hull access when any <see cref="Tokens"/> entry
/// matches a ship's hull reader requirement.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlayerShipHullGrantComponent : Component
{
    /// <summary>
    /// Hull tokens from player ships the holder is allowed to open (multiple ships / re-prints).
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> Tokens = new();
}
