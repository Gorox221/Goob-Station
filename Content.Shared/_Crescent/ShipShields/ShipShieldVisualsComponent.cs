using Robust.Shared.GameStates;

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Client draw settings for the energy shield bubble (see <c>ShipShieldOverlay</c>).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipShieldVisualsComponent : Component
{
    /// <summary>
    /// ResPath string to a PNG (or other supported image) used as the shield surface texture.
    /// Set from the emitter prototype via <see cref="ShipShieldEmitterComponent.ShieldTexture"/> when the shield spawns.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string ShieldTexture = "/Textures/_Crescent/ShipShields/shieldtex.png";
}
