namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Projectiles (or other entities) with this tag skip crescent deflector shield collision handling.
/// </summary>
[RegisterComponent]
public sealed partial class IgnoresHullrotShieldsComponent : Component;
