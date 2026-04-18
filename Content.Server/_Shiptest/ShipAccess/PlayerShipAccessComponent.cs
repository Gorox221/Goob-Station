namespace Content.Server._Shiptest.ShipAccess;

/// <summary>
/// Server-only: unique hull token for a spawned player ship station. Used to configure doors and crew ID cards.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerShipHullAccessComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public string Token = "";
}
