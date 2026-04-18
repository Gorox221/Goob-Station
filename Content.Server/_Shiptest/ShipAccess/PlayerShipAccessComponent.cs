namespace Content.Server._Shiptest.ShipAccess;

/// <summary>
/// Server-only: unique hull token for a spawned player ship station. Used to configure doors and crew ID cards.
/// </summary>
[RegisterComponent]
public sealed partial class PlayerShipHullAccessComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public string Token = "";

    /// <summary>
    /// Pooled radio channel prototype id for this ship (long-range; no telecomms).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? RadioChannelProtoId;
}