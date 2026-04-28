using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Crescent.ShipShields;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipShieldEmitterComponent : Component
{
    [AutoNetworkedField]  
    public EntityUid? Shield;
    public EntityUid? Shielded;

    [DataField]
    public float Accumulator;

    [AutoNetworkedField, DataField]
    public float Damage = 0f;

    [DataField]
    public float DamageExp = 1.0f;

    [DataField]
    public float HealPerSecond = 250f;

    [DataField]
    public float UnpoweredBonus = 6f;

    // [DataField] //commented because we only have base draw now
    // public float MaxDraw = 150000f;

    /// <summary>
    /// At this received power (W) the shield has nominal stats from DataField (damageLimit, healPerSecond, etc).
    /// </summary>
    [DataField]
    public float BaselineWatts = 20f;

    /// <summary>
    /// Upper bound the operator requests from the MV network (W). Actual draw may be less if the net is underfed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxPowerDrawWatts = 20f;

    [AutoNetworkedField, DataField]
    public float ReceivedPower = 0f;

    [AutoNetworkedField, DataField]
    public bool Recharging = false;

    [AutoNetworkedField, DataField]
    public float DamageLimit = 3500;

    [DataField]
    public float DamageOverloadTimePunishment = 30;

    [AutoNetworkedField]
    public float OverloadAccumulator = 0f;

    /// <summary>
    /// Short grace timer after operator changes requested draw, to let power net recompute ReceivedPower.
    /// </summary>
    [DataField]
    public float DrawChangeGraceAccumulator = 0f;

    /// <summary>
    /// Texture for the shield bubble. Copied to the spawned <c>ShipShield</c> entity’s <see cref="ShipShieldVisualsComponent"/>.
    /// </summary>
    [DataField]
    public string ShieldTexture = "/Textures/_Crescent/ShipShields/shieldtex.png";

    /// <summary>
    /// On power up, players for all on vessel, pitched down.
    /// </summary>
    [DataField]
    public SoundSpecifier PowerUpSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    [DataField]
    public SoundSpecifier PowerDownSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");
}

[Serializable, NetSerializable]
public enum ShipShieldEmitterUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class ShipShieldEmitterSetDrawRateMessage : BoundUserInterfaceMessage
{
    public int DrawRateWatts;

    public ShipShieldEmitterSetDrawRateMessage(int drawRateWatts)
    {
        DrawRateWatts = drawRateWatts;
    }
}

[Serializable, NetSerializable]
public sealed class ShipShieldEmitterBuiState : BoundUserInterfaceState
{
    public int RequestedWatts;
    public int ReceivedWatts;
    public int ShieldHp;
    public float CurrentDamage;

    public ShipShieldEmitterBuiState(int requestedWatts, int receivedWatts, int shieldHp, float currentDamage)
    {
        RequestedWatts = requestedWatts;
        ReceivedWatts = receivedWatts;
        ShieldHp = shieldHp;
        CurrentDamage = currentDamage;
    }
}
