using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._Shiptest.SpaceBiomes;

[RegisterComponent]
public sealed partial class PortableBiomeSurveyorComponent : Component
{
    [DataField]
    public float ScanDuration = 10f;

    [DataField]
    public string DiskPrototype = "ResearchDisk";

    [DataField]
    public SoundSpecifier? ScanStartSound = new SoundPathSpecifier("/Audio/Machines/anomaly_sync_connect.ogg");

    [DataField]
    public SoundSpecifier? ScanCompleteSound = new SoundPathSpecifier("/Audio/Machines/diagnoser_printing.ogg");
}

[Serializable, NetSerializable]
public sealed partial class PortableBiomeSurveyDoAfterEvent : DoAfterEvent
{
    public NetEntity? SourceUid;

    public PortableBiomeSurveyDoAfterEvent(NetEntity? sourceUid = null)
    {
        SourceUid = sourceUid;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}
