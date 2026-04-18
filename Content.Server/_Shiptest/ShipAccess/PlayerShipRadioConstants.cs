namespace Content.Server._Shiptest.ShipAccess;

/// <summary>
/// Player ship radio uses runtime <c>radioChannel</c> prototypes (see <c>IGamePrototypeLoadManager</c>)
/// with a random multi-character key (e.g. <c>:якуй</c>) and random frequency per vessel.
/// </summary>
public static class PlayerShipRadioConstants
{
    public const string EncryptionKeyPrototypeId = "EncryptionKeyPlayerShip";

    /// <summary>Length of the random UTF-8 key (after <c>:</c>).</summary>
    public const int ShipRadioKeyLength = 4;

    /// <summary>Cyrillic letters used to build random ship keys.</summary>
    public const string ShipRadioKeyAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";

    /// <summary>Minimum frequency (inclusive); same scale as other radio prototypes (e.g. handheld 1330).</summary>
    public const int DynamicFrequencyMin = 2000;

    /// <summary>Upper bound (exclusive) for random frequency selection.</summary>
    public const int DynamicFrequencyMaxExclusive = 9999;
}
